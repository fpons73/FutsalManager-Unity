using Microsoft.Data.Sqlite;
using UniFutsal.Domain.Competitions;
using UniFutsal.Infrastructure.Careers;

namespace UniFutsal.Tests;

public sealed class SqliteDemoCareerStoreTests
{
    [Fact]
    public void CreatesAndRestoresACareerThatCanContinueFromItsNextSeason()
    {
        using var scratch = new ScratchDirectory();
        var store = new SqliteDemoCareerStore();
        var career = new DemoCareer(ulong.MaxValue).CompleteSeason(1).CompleteSeason(2);
        store.SaveNew(scratch.SavePath, career);
        var original = File.ReadAllBytes(scratch.SavePath);

        var loaded = new SqliteDemoCareerStore().Load(scratch.SavePath);
        Assert.Equal(career.Seed, loaded.Seed);
        Assert.Equal(3, loaded.NextSeasonNumber);
        Assert.Equal(Scores(career), Scores(loaded));
        Assert.Equal(career.CompletedSeasons.Select(item => item.Champion), loaded.CompletedSeasons.Select(item => item.Champion));
        Assert.Equal(Scores(career.CompleteSeason(3)), Scores(loaded.CompleteSeason(3)));
        Assert.Equal(original, File.ReadAllBytes(scratch.SavePath));
    }

    [Fact]
    public void RefusesToReplaceAnExistingSaveAndLeavesNoTemporaryFile()
    {
        using var scratch = new ScratchDirectory();
        var store = new SqliteDemoCareerStore();
        store.SaveNew(scratch.SavePath, new DemoCareer(42).CompleteSeason(1));
        var original = File.ReadAllBytes(scratch.SavePath);

        Assert.Throws<IOException>(() => store.SaveNew(scratch.SavePath, new DemoCareer(77)));
        Assert.Equal(original, File.ReadAllBytes(scratch.SavePath));
        Assert.Equal(42UL, store.Load(scratch.SavePath).Seed);
        Assert.Single(Directory.GetFiles(scratch.Root));
    }

    [Theory]
    [InlineData("schema_version = 99")]
    [InlineData("engine_version = 'different-engine'")]
    [InlineData("seed = '77'")]
    [InlineData("closed_seasons = 1.5")]
    public void RejectsIncompatibleOrAlteredMetadataWithoutChangingTheFile(string update)
    {
        using var scratch = new ScratchDirectory();
        var store = new SqliteDemoCareerStore();
        store.SaveNew(scratch.SavePath, new DemoCareer(42).CompleteSeason(1));
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
               { DataSource = scratch.SavePath, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"UPDATE career_save SET {update}";
            Assert.Equal(1, command.ExecuteNonQuery());
        }
        var original = File.ReadAllBytes(scratch.SavePath);
        Assert.Throws<InvalidDataException>(() => store.Load(scratch.SavePath));
        Assert.Equal(original, File.ReadAllBytes(scratch.SavePath));
    }

    [Fact]
    public void RejectsMissingAndNonDatabaseFilesWithoutCreatingOrChangingThem()
    {
        using var scratch = new ScratchDirectory();
        var store = new SqliteDemoCareerStore();
        Assert.Throws<FileNotFoundException>(() => store.Load(scratch.SavePath));
        Assert.False(File.Exists(scratch.SavePath));
        File.WriteAllText(scratch.SavePath, "not a database");
        Assert.Throws<InvalidDataException>(() => store.Load(scratch.SavePath));
        Assert.Equal("not a database", File.ReadAllText(scratch.SavePath));
    }

    [Fact]
    public void AdvancesOneSeasonInANewSaveWithoutChangingTheSource()
    {
        using var scratch = new ScratchDirectory();
        var store = new SqliteDemoCareerStore();
        var career = new DemoCareer(42).CompleteSeason(1).CompleteSeason(2);
        store.SaveNew(scratch.SavePath, career);
        var original = File.ReadAllBytes(scratch.SavePath);
        var nextPath = Path.Combine(scratch.Root, "advanced.db");

        var completed = Assert.IsType<DemoCompletedSeason>(store.AdvanceToNewSave(scratch.SavePath, nextPath));
        var loaded = store.Load(nextPath);
        Assert.Equal(3, completed.Number);
        Assert.Equal(loaded.CompletedSeasons[2].Champion, completed.Champion);
        Assert.Equal(4, loaded.NextSeasonNumber);
        Assert.Equal(Scores(career.CompleteSeason(3)), Scores(loaded));
        Assert.Equal(original, File.ReadAllBytes(scratch.SavePath));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void KeepsExistingFilesWhenTheDestinationAlreadyExists(bool useSourceAsDestination)
    {
        using var scratch = new ScratchDirectory();
        var store = new SqliteDemoCareerStore();
        store.SaveNew(scratch.SavePath, new DemoCareer(42).CompleteSeason(1));
        var destination = useSourceAsDestination ? scratch.SavePath : Path.Combine(scratch.Root, "occupied.db");
        if (!useSourceAsDestination)
            store.SaveNew(destination, new DemoCareer(77));
        var sourceBytes = File.ReadAllBytes(scratch.SavePath);
        var destinationBytes = File.ReadAllBytes(destination);

        Assert.Throws<IOException>(() => store.AdvanceToNewSave(scratch.SavePath, destination));
        Assert.Equal(sourceBytes, File.ReadAllBytes(scratch.SavePath));
        Assert.Equal(destinationBytes, File.ReadAllBytes(destination));
        Assert.Equal(useSourceAsDestination ? 1 : 2, Directory.GetFiles(scratch.Root).Length);
    }

    [Fact]
    public void RefusesToAdvancePastTheDemoLimitWithoutCreatingAFile()
    {
        using var scratch = new ScratchDirectory();
        var store = new SqliteDemoCareerStore();
        var career = new DemoCareer(42);
        for (var number = 1; number <= SqliteDemoCareerStore.MaxClosedSeasons; number++)
            career = career.CompleteSeason(number);
        store.SaveNew(scratch.SavePath, career);
        var original = File.ReadAllBytes(scratch.SavePath);
        var destination = Path.Combine(scratch.Root, "advanced.db");

        Assert.Null(store.AdvanceToNewSave(scratch.SavePath, destination));
        Assert.Equal(original, File.ReadAllBytes(scratch.SavePath));
        Assert.False(File.Exists(destination));
        Assert.Single(Directory.GetFiles(scratch.Root));
    }

    [Fact]
    public void RefusesACorruptSourceWithoutCreatingADestination()
    {
        using var scratch = new ScratchDirectory();
        File.WriteAllText(scratch.SavePath, "invalid");
        var destination = Path.Combine(scratch.Root, "advanced.db");

        var store = new SqliteDemoCareerStore();
        Assert.Throws<InvalidDataException>(() => store.AdvanceToNewSave(scratch.SavePath, destination));
        Assert.Equal("invalid", File.ReadAllText(scratch.SavePath));
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public void RecoversTheSelectedBackupWithoutAdvancingOrChangingEitherOriginal()
    {
        using var scratch = new ScratchDirectory();
        var backup = Path.Combine(scratch.Root, "backup.db");
        var recovered = Path.Combine(scratch.Root, "recovered.db");
        var store = new SqliteDemoCareerStore();
        var career = new DemoCareer(42).CompleteSeason(1).CompleteSeason(2);
        File.WriteAllText(scratch.SavePath, "damaged primary");
        store.SaveNew(backup, career);
        var backupBytes = File.ReadAllBytes(backup);

        var restored = store.RecoverToNewSave(backup, recovered);

        Assert.Equal(3, restored.NextSeasonNumber);
        Assert.Equal(Scores(career), Scores(restored));
        Assert.Equal(Scores(career), Scores(store.Load(recovered)));
        Assert.Equal(backupBytes, File.ReadAllBytes(backup));
        Assert.Equal("damaged primary", File.ReadAllText(scratch.SavePath));
        Assert.Equal(3, Directory.GetFiles(scratch.Root).Length);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RecoveryRefusesAnExistingDestinationWithoutChangingFiles(bool destinationIsBackup)
    {
        using var scratch = new ScratchDirectory();
        var backup = Path.Combine(scratch.Root, "backup.db");
        var store = new SqliteDemoCareerStore();
        store.SaveNew(backup, new DemoCareer(42));
        var backupBytes = File.ReadAllBytes(backup);
        File.WriteAllText(scratch.SavePath, "damaged primary");

        Assert.Throws<IOException>(() =>
            store.RecoverToNewSave(backup, destinationIsBackup ? backup : scratch.SavePath));

        Assert.Equal(backupBytes, File.ReadAllBytes(backup));
        Assert.Equal("damaged primary", File.ReadAllText(scratch.SavePath));
        Assert.Equal(2, Directory.GetFiles(scratch.Root).Length);
    }

    [Fact]
    public void RecoveryRefusesAnInvalidBackupWithoutPublishingAFile()
    {
        using var scratch = new ScratchDirectory();
        File.WriteAllText(scratch.SavePath, "invalid backup");
        var recovered = Path.Combine(scratch.Root, "recovered.db");
        var store = new SqliteDemoCareerStore();

        Assert.Throws<InvalidDataException>(() => store.RecoverToNewSave(scratch.SavePath, recovered));
        Assert.Equal("invalid backup", File.ReadAllText(scratch.SavePath));
        Assert.False(File.Exists(recovered));
        Assert.Single(Directory.GetFiles(scratch.Root));
    }

    [Fact]
    public void RoundCheckpointRestoresTheTableAndContinuesFromTheNextRound()
    {
        using var scratch = new ScratchDirectory();
        var store = new SqliteDemoCareerStore();
        var progress = DemoRoundSeason.Resume(ulong.MaxValue, 2);
        store.SaveRoundCheckpointNew(scratch.SavePath, progress);
        var original = File.ReadAllBytes(scratch.SavePath);

        var loaded = store.LoadRoundCheckpoint(scratch.SavePath);

        Assert.Equal(2, loaded.CompletedRounds);
        Assert.Equal(progress.Standings.Select(row => (row.Club, row.Points, row.GoalsFor, row.GoalsAgainst)),
            loaded.Standings.Select(row => (row.Club, row.Points, row.GoalsFor, row.GoalsAgainst)));
        var round = loaded.PlayNextRound();
        Assert.All(round, match => Assert.Equal(3, match.Fixture.Round));
        Assert.Equal(RoundScores(progress.PlayNextRound()), RoundScores(round));
        Assert.Equal(original, File.ReadAllBytes(scratch.SavePath));
    }

    [Fact]
    public void RoundCheckpointCannotReplaceAnExistingCareerSaveOrBeLoadedAsOne()
    {
        using var scratch = new ScratchDirectory();
        var store = new SqliteDemoCareerStore();
        store.SaveNew(scratch.SavePath, new DemoCareer(42).CompleteSeason(1));
        var original = File.ReadAllBytes(scratch.SavePath);
        var progress = DemoRoundSeason.Resume(42, 2);

        Assert.Throws<IOException>(() => store.SaveRoundCheckpointNew(scratch.SavePath, progress));
        Assert.Throws<InvalidDataException>(() => store.LoadRoundCheckpoint(scratch.SavePath));
        Assert.Equal(original, File.ReadAllBytes(scratch.SavePath));
        Assert.Equal(2, store.Load(scratch.SavePath).NextSeasonNumber);
        Assert.Single(Directory.GetFiles(scratch.Root));

        var checkpoint = Path.Combine(scratch.Root, "round.db");
        store.SaveRoundCheckpointNew(checkpoint, progress);
        var checkpointBytes = File.ReadAllBytes(checkpoint);
        Assert.Throws<InvalidDataException>(() => store.Load(checkpoint));
        Assert.Equal(checkpointBytes, File.ReadAllBytes(checkpoint));
    }

    [Theory]
    [InlineData("schema_version = 99")]
    [InlineData("engine_version = 'different-engine'")]
    [InlineData("completed_rounds = 3")]
    [InlineData("completed_rounds = 15")]
    public void RefusesInvalidOrAlteredRoundCheckpointMetadata(string update)
    {
        using var scratch = new ScratchDirectory();
        var store = new SqliteDemoCareerStore();
        store.SaveRoundCheckpointNew(scratch.SavePath, DemoRoundSeason.Resume(42, 2));
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
               { DataSource = scratch.SavePath, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"UPDATE round_checkpoint SET {update}";
            Assert.Equal(1, command.ExecuteNonQuery());
        }
        var original = File.ReadAllBytes(scratch.SavePath);
        Assert.Throws<InvalidDataException>(() => store.LoadRoundCheckpoint(scratch.SavePath));
        Assert.Equal(original, File.ReadAllBytes(scratch.SavePath));
    }

    private static IEnumerable<(string, string, int, int)> RoundScores(IEnumerable<DemoFixtureResult> matches) =>
        matches.Select(match => (match.Fixture.HomeClub, match.Fixture.AwayClub, match.HomeGoals, match.AwayGoals));

    private static IEnumerable<(int, string, string, int, int)> Scores(DemoCareer career) =>
        career.CompletedSeasons.SelectMany(season => season.Result.Results.Select(match =>
            (season.Number, match.Fixture.HomeClub, match.Fixture.AwayClub, match.HomeGoals, match.AwayGoals)));

    private sealed class ScratchDirectory : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("unifutsal-save-").FullName;
        public string SavePath => Path.Combine(Root, "career.db");
        public void Dispose()
        {
            var target = Path.GetFullPath(Root);
            var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            if (string.Equals(Path.GetDirectoryName(target), temp, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(target).StartsWith("unifutsal-save-", StringComparison.Ordinal))
                Directory.Delete(target, recursive: true);
        }
    }
}
