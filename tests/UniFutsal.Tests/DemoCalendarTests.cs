using UniFutsal.Domain.Competitions;

namespace UniFutsal.Tests;

public sealed class DemoCalendarTests
{
    [Fact]
    public void SchedulesEightClubsOncePerRoundAndReversesEachPair()
    {
        var fixtures = DemoCalendar.Create();
        Assert.Equal(56, fixtures.Count);

        var rounds = fixtures.GroupBy(fixture => fixture.Round).ToArray();
        Assert.Equal(Enumerable.Range(1, 14), rounds.Select(round => round.Key));

        var clubs = fixtures.Select(fixture => fixture.HomeClub).Distinct().Order().ToArray();
        Assert.Equal(8, clubs.Length);
        foreach (var round in rounds)
        {
            Assert.Equal(4, round.Count());
            Assert.Equal(clubs, round.SelectMany(fixture => new[] { fixture.HomeClub, fixture.AwayClub }).Order());
        }

        var firstLeg = fixtures.Where(fixture => fixture.Round <= 7).ToArray();
        Assert.Equal(28, firstLeg.Select(fixture => string.CompareOrdinal(fixture.HomeClub, fixture.AwayClub) < 0
            ? (fixture.HomeClub, fixture.AwayClub)
            : (fixture.AwayClub, fixture.HomeClub)).Distinct().Count());

        foreach (var fixture in firstLeg)
        {
            Assert.Single(fixtures, reverse => reverse.Round == fixture.Round + 7
                && reverse.HomeClub == fixture.AwayClub && reverse.AwayClub == fixture.HomeClub);
        }

        foreach (var club in clubs)
        {
            Assert.Equal(7, fixtures.Count(fixture => fixture.HomeClub == club));
            Assert.Equal(7, fixtures.Count(fixture => fixture.AwayClub == club));
        }

        Assert.Equal(fixtures.Select(fixture => (fixture.Round, fixture.HomeClub, fixture.AwayClub)),
            DemoCalendar.Create().Select(fixture => (fixture.Round, fixture.HomeClub, fixture.AwayClub)));
    }
}
