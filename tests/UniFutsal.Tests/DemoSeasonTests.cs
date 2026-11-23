using UniFutsal.Domain.Competitions;

namespace UniFutsal.Tests;

public sealed class DemoSeasonTests
{
    [Fact]
    public void PlaysEveryScheduledFixtureExactlyOnceAndReproducesItsScore()
    {
        var first = DemoSeason.Simulate(42);
        var replay = DemoSeason.Simulate(42);
        Assert.Equal(56, first.Results.Count);
        Assert.Equal(DemoCalendar.Create().Select(FixtureKey), first.Results.Select(result => FixtureKey(result.Fixture)));
        Assert.Equal(56, first.Results.Select(result => FixtureKey(result.Fixture)).Distinct().Count());
        Assert.Equal(Scores(first), Scores(replay));
        Assert.False(Scores(first).SequenceEqual(Scores(DemoSeason.Simulate(43))));
    }

    [Fact]
    public void ReconcilesEveryTableRowWithTheResultsAndAppliesDemoTieBreaks()
    {
        var season = DemoSeason.Simulate(42);
        Assert.Equal(8, season.Standings.Count);
        Assert.Equal(8, season.Standings.Select(row => row.Club).Distinct().Count());
        Assert.Equal(112, season.Standings.Sum(row => row.Played));
        Assert.Equal(season.Standings.Sum(row => row.GoalsFor), season.Standings.Sum(row => row.GoalsAgainst));
        Assert.Equal(season.Results.Sum(result => result.HomeGoals == result.AwayGoals ? 2 : 3),
            season.Standings.Sum(row => row.Points));

        foreach (var row in season.Standings)
        {
            var scores = season.Results
                .Where(result => result.Fixture.HomeClub == row.Club || result.Fixture.AwayClub == row.Club)
                .Select(result => result.Fixture.HomeClub == row.Club
                    ? (For: result.HomeGoals, Against: result.AwayGoals)
                    : (For: result.AwayGoals, Against: result.HomeGoals)).ToArray();

            Assert.Equal(14, row.Played);
            Assert.Equal(scores.Count(score => score.For > score.Against), row.Wins);
            Assert.Equal(scores.Count(score => score.For == score.Against), row.Draws);
            Assert.Equal(scores.Count(score => score.For < score.Against), row.Losses);
            Assert.Equal(scores.Sum(score => score.For), row.GoalsFor);
            Assert.Equal(scores.Sum(score => score.Against), row.GoalsAgainst);
            Assert.Equal(row.GoalsFor - row.GoalsAgainst, row.GoalDifference);
            Assert.Equal(3 * row.Wins + row.Draws, row.Points);
        }

        Assert.Equal(season.Standings.OrderByDescending(row => row.Points)
            .ThenByDescending(row => row.GoalDifference).ThenByDescending(row => row.GoalsFor)
            .ThenBy(row => row.Club, StringComparer.Ordinal), season.Standings);
    }

    [Theory]
    [InlineData(51, "Unión Ribera", "Costa Alba", false)]
    [InlineData(21, "Horizonte Futsal", "Unión Ribera", true)]
    public void BreaksEqualPointsAndGoalDifferenceByGoalsThenClubId(
        int seed, string firstClub, string secondClub, bool equalGoals)
    {
        var tied = DemoSeason.Simulate((ulong)seed).Standings
            .Where(row => row.Club == firstClub || row.Club == secondClub).ToArray();
        Assert.Equal(new[] { firstClub, secondClub }, tied.Select(row => row.Club));
        Assert.Equal(tied[0].Points, tied[1].Points);
        Assert.Equal(tied[0].GoalDifference, tied[1].GoalDifference);
        if (equalGoals)
            Assert.Equal(tied[0].GoalsFor, tied[1].GoalsFor);
        else
            Assert.True(tied[0].GoalsFor > tied[1].GoalsFor);
    }

    private static (int, string, string) FixtureKey(DemoFixture fixture) =>
        (fixture.Round, fixture.HomeClub, fixture.AwayClub);

    private static IEnumerable<((int, string, string), int, int)> Scores(DemoSeasonResult season) =>
        season.Results.Select(result => (FixtureKey(result.Fixture), result.HomeGoals, result.AwayGoals));
}
