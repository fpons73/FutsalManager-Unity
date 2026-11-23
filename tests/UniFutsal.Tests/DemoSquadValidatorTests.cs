using UniFutsal.Domain.Competitions;

namespace UniFutsal.Tests;

public sealed class DemoSquadValidatorTests
{
    private static readonly DemoFixture Fixture = DemoCalendar.Create()[0];
    private static readonly string Club = Fixture.HomeClub;
    private static readonly DemoPlayer[] Roster = DemoRoster.Create().Where(player => player.Club == Club).ToArray();
    private static readonly string[] Starters = [Roster[0].Id, Roster[2].Id, Roster[3].Id, Roster[5].Id, Roster[9].Id];
    private static readonly string[] Bench = Roster.Select(player => player.Id).Except(Starters).ToArray();

    [Fact]
    public void AcceptsOneGoalkeeperAndFourOutfieldStartersWithDistinctEligibleReserves()
    {
        Assert.Empty(DemoSquadValidator.Validate(Fixture, Club, Starters, Bench));
    }

    [Fact]
    public void RejectsAClubNotPlayingTheFixture()
    {
        var otherClub = DemoRoster.Create().First(player => player.Club != Club && player.Club != Fixture.AwayClub).Club;
        var issue = Assert.Single(DemoSquadValidator.Validate(Fixture, otherClub, Starters, Bench));
        Assert.Equal(SquadIssueCode.ClubNotInFixture, issue.Code);
        Assert.Null(issue.PlayerId);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    public void RejectsAStartingGroupOtherThanFive(int count)
    {
        var starters = Starters.Concat(Bench).Take(count).ToArray();
        var issue = Assert.Single(DemoSquadValidator.Validate(Fixture, Club, starters, []));
        Assert.Equal(SquadIssueCode.InvalidStarterCount, issue.Code);
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(1, 1)]
    public void RejectsZeroOrTwoStartingGoalkeepers(int replacedIndex, int rosterIndex)
    {
        var starters = Starters.ToArray();
        starters[replacedIndex] = Roster[rosterIndex].Id;
        var issue = Assert.Single(DemoSquadValidator.Validate(Fixture, Club, starters, []));
        Assert.Equal(SquadIssueCode.InvalidStartingGoalkeeperCount, issue.Code);
    }

    [Fact]
    public void RejectsDuplicatesAcrossTheStartingGroupAndBench()
    {
        var playerId = Starters[0];
        var issue = Assert.Single(DemoSquadValidator.Validate(Fixture, Club, Starters, [playerId]));
        Assert.Equal((SquadIssueCode.DuplicatePlayer, playerId), (issue.Code, issue.PlayerId));
    }

    [Fact]
    public void RejectsUnknownAndOpposingPlayers()
    {
        var opposingPlayer = DemoRoster.Create().First(player => player.Club == Fixture.AwayClub).Id;
        const string unknown = "unknown";
        var issues = DemoSquadValidator.Validate(Fixture, Club, Starters, [opposingPlayer, unknown]);
        Assert.Collection(issues,
            issue => Assert.Equal((SquadIssueCode.PlayerOutsideClub, opposingPlayer), (issue.Code, issue.PlayerId)),
            issue => Assert.Equal((SquadIssueCode.UnknownPlayer, unknown), (issue.Code, issue.PlayerId)));
    }

    [Fact]
    public void ReportsWhyAStarterAndAReserveAreUnavailable()
    {
        var suspended = Starters[1];
        var injured = Bench[0];
        var issues = DemoSquadValidator.Validate(Fixture, Club, Starters, Bench, [suspended], [injured]);
        Assert.Collection(issues,
            issue => Assert.Equal((SquadIssueCode.SuspendedPlayer, suspended), (issue.Code, issue.PlayerId)),
            issue => Assert.Equal((SquadIssueCode.InjuredPlayer, injured), (issue.Code, issue.PlayerId)));
    }
}
