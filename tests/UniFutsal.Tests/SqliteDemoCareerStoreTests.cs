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
