using UniFutsal.Domain.Competitions;

namespace UniFutsal.Tests;

public sealed class DemoRosterTests
{
    [Fact]
    public void GivesEveryScheduledClubFourteenDistinctPlayersWithAllRoles()
    {
        var players = DemoRoster.Create();
        var clubs = DemoCalendar.Create()
            .SelectMany(fixture => new[] { fixture.HomeClub, fixture.AwayClub })
            .Distinct().Order();
        var rosters = players.GroupBy(player => player.Club).ToArray();

        Assert.Equal(112, players.Count);
        Assert.Equal(112, players.Select(player => player.Id).Distinct().Count());
        Assert.Equal(112, players.Select(player => player.Name).Distinct().Count());
        Assert.Equal(clubs, rosters.Select(roster => roster.Key).Order());

        foreach (var roster in rosters)
        {
            Assert.Equal(14, roster.Count());
            Assert.Equal(2, roster.Count(player => player.Role == DemoPlayerRole.Goalkeeper));
            Assert.Equal(Enum.GetValues<DemoPlayerRole>(), roster.Select(player => player.Role).Distinct().Order());
        }

        Assert.Equal(players.Select(player => (player.Id, player.Name, player.Club, player.Role)),
            DemoRoster.Create().Select(player => (player.Id, player.Name, player.Club, player.Role)));
    }
}
