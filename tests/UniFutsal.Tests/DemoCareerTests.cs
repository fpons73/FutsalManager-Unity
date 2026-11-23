using UniFutsal.Domain.Competitions;

namespace UniFutsal.Tests;

public sealed class DemoCareerTests
{
    [Fact]
    public void ClosesOnlyTheNextSeasonAndDoesNotDuplicateAnExistingTitle()
    {
        var career = new DemoCareer(42);
        Assert.Empty(career.CompletedSeasons);
        Assert.Equal(1, career.NextSeasonNumber);
        Assert.Throws<ArgumentOutOfRangeException>(() => career.CompleteSeason(2));

        var advanced = career.CompleteSeason(1);
        Assert.Empty(career.CompletedSeasons);
        Assert.Equal(2, advanced.NextSeasonNumber);
        var first = Assert.Single(advanced.CompletedSeasons);
        Assert.Equal(1, first.Number);
        Assert.Equal(42UL, first.Seed);
        Assert.Equal("Costa Alba", first.Champion);
        Assert.Equal(Scores(DemoSeason.Simulate(42)), Scores(first.Result));

        var second = advanced.CompleteSeason(2);
        Assert.Equal(3, second.NextSeasonNumber);
        Assert.Equal(2, second.CompletedSeasons.Count);
        Assert.Same(second, second.CompleteSeason(1));
        Assert.Same(second, second.CompleteSeason(2));
        Assert.Same(first, second.CompletedSeasons[0]);
        Assert.False(Scores(first.Result).SequenceEqual(Scores(second.CompletedSeasons[1].Result)));
    }

    [Fact]
    public void ReproducesTenSeparateSeasonsWithoutMissingOrRepeatingFixtures()
    {
        var career = RunTen(42);
        var replay = RunTen(42);
        Assert.Equal(11, career.NextSeasonNumber);
        Assert.Equal(Enumerable.Range(1, 10), career.CompletedSeasons.Select(season => season.Number));
        Assert.Equal(10, career.CompletedSeasons.Select(season => season.Seed).Distinct().Count());
        Assert.Equal(560, career.CompletedSeasons.Sum(season => season.Result.Results.Count));

        foreach (var (season, repeated) in career.CompletedSeasons.Zip(replay.CompletedSeasons))
        {
            Assert.Equal(season.Seed, repeated.Seed);
            Assert.Equal(season.Champion, repeated.Champion);
            Assert.Equal(Scores(season.Result), Scores(repeated.Result));
            Assert.Equal(56, season.Result.Results.Count);
            Assert.Equal(56, season.Result.Results.Select(match =>
                (match.Fixture.Round, match.Fixture.HomeClub, match.Fixture.AwayClub)).Distinct().Count());
            Assert.Equal(8, season.Result.Standings.Count);
            Assert.All(season.Result.Standings, row => Assert.Equal(14, row.Played));
            Assert.Equal(season.Result.Standings.Sum(row => row.GoalsFor), season.Result.Standings.Sum(row => row.GoalsAgainst));
            Assert.Equal(season.Result.Results.Sum(match => match.HomeGoals == match.AwayGoals ? 2 : 3),
                season.Result.Standings.Sum(row => row.Points));
        }
    }

    private static DemoCareer RunTen(ulong seed)
    {
        var career = new DemoCareer(seed);
        for (var season = 1; season <= 10; season++)
            career = career.CompleteSeason(season);
        return career;
    }

    private static IEnumerable<(int, string, string, int, int)> Scores(DemoSeasonResult season) =>
        season.Results.Select(match => (match.Fixture.Round, match.Fixture.HomeClub, match.Fixture.AwayClub,
            match.HomeGoals, match.AwayGoals));
}
