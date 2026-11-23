using System;
using System.Collections.Generic;
using System.Linq;

namespace UniFutsal.Domain.Competitions
{
    public sealed class DemoFixtureResult
    {
        public DemoFixture Fixture { get; }
        public int HomeGoals { get; }
        public int AwayGoals { get; }

        internal DemoFixtureResult(DemoFixture fixture, int homeGoals, int awayGoals)
        {
            Fixture = fixture;
            HomeGoals = homeGoals;
            AwayGoals = awayGoals;
        }
    }

    public sealed class DemoStanding
    {
        public string Club { get; }
        public int Played => Wins + Draws + Losses;
        public int Wins { get; }
        public int Draws { get; }
        public int Losses { get; }
        public int GoalsFor { get; }
        public int GoalsAgainst { get; }
        public int GoalDifference => GoalsFor - GoalsAgainst;
        public int Points => 3 * Wins + Draws;

        internal DemoStanding(string club, IReadOnlyList<(int For, int Against)> scores)
        {
            Club = club;
            foreach (var score in scores)
            {
                GoalsFor += score.For;
                GoalsAgainst += score.Against;
                if (score.For > score.Against)
                    Wins++;
                else if (score.For == score.Against)
                    Draws++;
                else
                    Losses++;
            }
        }

        internal static IReadOnlyList<DemoStanding> Build(IEnumerable<string> clubs,
            IEnumerable<DemoFixtureResult> results)
        {
            var scoresByClub = clubs.ToDictionary(club => club,
                _ => new List<(int For, int Against)>(), StringComparer.Ordinal);
            foreach (var match in results)
            {
                scoresByClub[match.Fixture.HomeClub].Add((match.HomeGoals, match.AwayGoals));
                scoresByClub[match.Fixture.AwayClub].Add((match.AwayGoals, match.HomeGoals));
            }

            // In the fixed demo, the canonical club name also serves as its ordinal ID.
            return Array.AsReadOnly(scoresByClub.Select(entry => new DemoStanding(entry.Key, entry.Value))
                .OrderByDescending(row => row.Points)
                .ThenByDescending(row => row.GoalDifference)
                .ThenByDescending(row => row.GoalsFor)
                .ThenBy(row => row.Club, StringComparer.Ordinal)
                .ToArray());
        }
    }

    public sealed class DemoSeasonResult
    {
        public IReadOnlyList<DemoFixtureResult> Results { get; }
        public IReadOnlyList<DemoStanding> Standings { get; }

        internal DemoSeasonResult(IReadOnlyList<DemoFixtureResult> results, IReadOnlyList<DemoStanding> standings)
        {
            Results = results;
            Standings = standings;
        }
    }

    public static class DemoSeason
    {
        public static DemoSeasonResult Simulate(ulong careerSeed)
        {
            var season = new DemoRoundSeason(careerSeed);
            while (!season.IsComplete)
                season.PlayNextRound();

            return new DemoSeasonResult(season.Results, season.Standings);
        }
    }
}
