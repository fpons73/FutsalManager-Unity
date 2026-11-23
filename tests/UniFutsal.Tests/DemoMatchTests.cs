using UniFutsal.Domain.Competitions;

namespace UniFutsal.Tests;

public sealed class DemoMatchTests
{
    [Fact]
    public void ReplaysTheSameFixtureAndSeedWithAnIdenticalTrace()
    {
        var fixture = DemoCalendar.Create()[0];
        var home = Squad(fixture.HomeClub);
        var away = Squad(fixture.AwayClub);
        var first = DemoMatch.Simulate(fixture, 42, home, away);
        var replay = DemoMatch.Simulate(fixture, 42, home, away);
        var changed = DemoMatch.Simulate(fixture, 43, home, away);

        Assert.True(first.Completed);
        Assert.Equal(Trace(first), Trace(replay));
        Assert.False(Trace(first).SequenceEqual(Trace(changed)));
    }

    [Fact]
    public void CompletesTwoTimedPeriodsAndReconcilesGoalsWithShotsAndTheScore()
    {
        var fixture = DemoCalendar.Create()[0];
        var home = Squad(fixture.HomeClub);
        var away = Squad(fixture.AwayClub);
        var result = DemoMatch.Simulate(fixture, 42, home, away);

        Assert.Equal(Enumerable.Range(1, result.Events.Count), result.Events.Select(item => item.Sequence));
        Assert.Equal(result.Events.Select(item => item.ElapsedEffectiveMs).Order(),
            result.Events.Select(item => item.ElapsedEffectiveMs));
        Assert.Equal(new[] { (1, 0), (2, 1_200_000) }, Times(DemoMatchEventKind.PeriodStarted));
        Assert.Equal(new[] { (1, 1_200_000), (2, 2_400_000) }, Times(DemoMatchEventKind.PeriodEnded));
        Assert.Equal((DemoMatchEventKind.MatchFinished, 2, 2_400_000),
            (result.Events[^1].Kind, result.Events[^1].Period, result.Events[^1].ElapsedEffectiveMs));
        Assert.Equal(result.HomeGoals + result.AwayGoals, result.Events.Count(item => item.Kind == DemoMatchEventKind.Goal));
        Assert.Contains(result.Events, item => item.Kind == DemoMatchEventKind.Shot);
        foreach (var item in result.Events.Where(item => item.Kind == DemoMatchEventKind.Goal))
        {
            var shot = result.Events[item.Sequence - 2];
            Assert.Equal((DemoMatchEventKind.Shot, item.Club, item.PlayerId, item.ElapsedEffectiveMs),
                (shot.Kind, shot.Club, shot.PlayerId, shot.ElapsedEffectiveMs));
        }
        foreach (var item in result.Events.Where(item => item.Kind == DemoMatchEventKind.Shot))
        {
            var squad = item.Club == fixture.HomeClub ? home : away;
            Assert.Contains(item.PlayerId, squad.Starters);
            Assert.NotEqual(squad.Starters[0], item.PlayerId);
        }

        IEnumerable<(int, int)> Times(DemoMatchEventKind kind) =>
            result.Events.Where(item => item.Kind == kind).Select(item => (item.Period, item.ElapsedEffectiveMs));
    }

    [Fact]
    public void RejectsUnavailableStartersOnEitherSideWithoutPlayingAnyEvents()
    {
        var fixture = DemoCalendar.Create()[0];
        var home = Squad(fixture.HomeClub);
        var away = Squad(fixture.AwayClub);
        var homeId = home.Starters[1];
        var awayId = away.Starters[1];
        var result = DemoMatch.Simulate(fixture, 42,
            new DemoMatchSquad(home.Starters, home.Bench, suspendedPlayerIds: [homeId]),
            new DemoMatchSquad(away.Starters, away.Bench, unfitPlayerIds: [awayId]));

        Assert.False(result.Completed);
        Assert.Empty(result.Events);
        Assert.Equal((0, 0), (result.HomeGoals, result.AwayGoals));
        Assert.Equal((SquadIssueCode.SuspendedPlayer, homeId), Issue(Assert.Single(result.HomeIssues)));
        Assert.Equal((SquadIssueCode.InjuredPlayer, awayId), Issue(Assert.Single(result.AwayIssues)));
    }

    private static (SquadIssueCode, string?) Issue(SquadIssue issue) => (issue.Code, issue.PlayerId);

    private static DemoMatchSquad Squad(string club)
    {
        var roster = DemoRoster.Create().Where(player => player.Club == club).ToArray();
        var starters = roster.Take(1).Concat(roster.Skip(2).Take(4)).Select(player => player.Id).ToArray();
        return new DemoMatchSquad(starters, roster.Select(player => player.Id).Except(starters).ToArray());
    }

    private static IEnumerable<object> Trace(DemoMatchResult result) => result.Events.Select(item =>
        (object)(item.Sequence, item.Period, item.ElapsedEffectiveMs, item.Kind, item.Club, item.PlayerId));
}
