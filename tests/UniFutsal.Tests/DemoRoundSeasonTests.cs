using UniFutsal.Domain.Competitions;

namespace UniFutsal.Tests;

public sealed class DemoRoundSeasonTests
{
    [Fact]
    public void PlaysOnlyTheNextRoundEachTime()
    {
        var season = new DemoRoundSeason(42);
        Assert.Equal(14, season.RoundCount);
        Assert.Equal(0, season.CompletedRounds);
        Assert.False(season.IsComplete);

        var first = season.PlayNextRound();
        Assert.Equal(4, first.Count);
        Assert.All(first, match => Assert.Equal(1, match.Fixture.Round));
        Assert.Equal(1, season.CompletedRounds);
        Assert.False(season.IsComplete);

        var second = season.PlayNextRound();
        Assert.Equal(4, second.Count);
        Assert.All(second, match => Assert.Equal(2, match.Fixture.Round));
        Assert.Equal(2, season.CompletedRounds);
        Assert.All(first, match => Assert.Equal(1, match.Fixture.Round));
    }

    [Fact]
    public void StopsAfterFourteenRoundsAndMatchesTheSeededFullSeason()
    {
        var season = new DemoRoundSeason(42);
        var played = new List<DemoFixtureResult>();
        while (!season.IsComplete)
            played.AddRange(season.PlayNextRound());

        Assert.Equal(14, season.CompletedRounds);
        Assert.Equal(56, played.Count);
        Assert.Equal(56, played.Select(match =>
            (match.Fixture.Round, match.Fixture.HomeClub, match.Fixture.AwayClub)).Distinct().Count());
        Assert.Equal(Scores(DemoSeason.Simulate(42).Results), Scores(played));
        Assert.Empty(season.PlayNextRound());
        Assert.Equal(14, season.CompletedRounds);
    }

    [Fact]
    public void StandingsCountOnlyPlayedRoundsAndUseTheSameRulesAsTheFullSeason()
    {
        var season = new DemoRoundSeason(42);
        var empty = season.Standings;
        Assert.Equal(8, empty.Count);
        Assert.All(empty, row => Assert.Equal((0, 0, 0, 0), (row.Played, row.Points, row.GoalsFor, row.GoalsAgainst)));

        var played = new List<DemoFixtureResult>();
        for (var round = 1; round <= 2; round++)
        {
            played.AddRange(season.PlayNextRound());
            var table = season.Standings;
            Assert.Equal(8, table.Count);
            Assert.All(table, row => Assert.Equal(round, row.Played));
            Assert.Equal(played.Sum(item => item.HomeGoals + item.AwayGoals), table.Sum(row => row.GoalsFor));
            Assert.Equal(table.Sum(row => row.GoalsFor), table.Sum(row => row.GoalsAgainst));
            Assert.Equal(played.Sum(item => item.HomeGoals == item.AwayGoals ? 2 : 3), table.Sum(row => row.Points));
            Assert.Equal(table.OrderByDescending(row => row.Points).ThenByDescending(row => row.GoalDifference)
                .ThenByDescending(row => row.GoalsFor).ThenBy(row => row.Club, StringComparer.Ordinal)
                .Select(row => row.Club), table.Select(row => row.Club));
        }

        Assert.All(empty, row => Assert.Equal(0, row.Played));
        while (!season.IsComplete)
            season.PlayNextRound();

        Assert.Equal(Table(DemoSeason.Simulate(42).Standings), Table(season.Standings));
    }

    private static IEnumerable<(string, int, int, int, int, int, int, int)> Table(IEnumerable<DemoStanding> rows) =>
        rows.Select(row => (row.Club, row.Played, row.Wins, row.Draws, row.Losses, row.GoalsFor, row.GoalsAgainst, row.Points));

    private static IEnumerable<(int, string, string, int, int)> Scores(IEnumerable<DemoFixtureResult> results) =>
        results.Select(match => (match.Fixture.Round, match.Fixture.HomeClub, match.Fixture.AwayClub,
            match.HomeGoals, match.AwayGoals));
}
