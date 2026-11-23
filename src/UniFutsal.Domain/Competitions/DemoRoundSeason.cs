using System;
using System.Collections.Generic;
using System.Linq;

namespace UniFutsal.Domain.Competitions
{
    public sealed class DemoRoundSeason
    {
        private readonly ulong _seed;
        private readonly DemoFixture[][] _rounds;
        private readonly Dictionary<string, DemoMatchSquad> _squads;
        private readonly List<DemoFixtureResult> _results = new List<DemoFixtureResult>();

        public ulong Seed => _seed;
        public int RoundCount => _rounds.Length;
        public int CompletedRounds { get; private set; }
        public bool IsComplete => CompletedRounds == RoundCount;
        public IReadOnlyList<DemoStanding> Standings => DemoStanding.Build(_squads.Keys, _results);
        public IReadOnlyList<DemoFixtureResult> Results => _results.AsReadOnly();

        public DemoRoundSeason(ulong seed)
        {
            _seed = seed;
            _rounds = DemoCalendar.Create().GroupBy(fixture => fixture.Round)
                .OrderBy(round => round.Key).Select(round => round.ToArray()).ToArray();
            _squads = DemoRoster.Create().GroupBy(player => player.Club, StringComparer.Ordinal)
                .ToDictionary(roster => roster.Key, roster => DemoMatchSquad.SelectDefault(roster), StringComparer.Ordinal);
        }

        public static DemoRoundSeason Resume(ulong seed, int completedRounds)
        {
            var season = new DemoRoundSeason(seed);
            if (completedRounds < 0 || completedRounds > season.RoundCount)
                throw new ArgumentOutOfRangeException(nameof(completedRounds));

            while (season.CompletedRounds < completedRounds)
                season.PlayNextRound();

            return season;
        }

        public IReadOnlyList<DemoFixtureResult> PlayNextRound()
        {
            if (IsComplete)
                return Array.Empty<DemoFixtureResult>();

            var fixtures = _rounds[CompletedRounds];
            var results = new DemoFixtureResult[fixtures.Length];
            for (var index = 0; index < fixtures.Length; index++)
            {
                var fixture = fixtures[index];
                var match = DemoMatch.Simulate(fixture, _seed, _squads[fixture.HomeClub], _squads[fixture.AwayClub]);
                if (!match.Completed)
                    throw new InvalidOperationException($"Demo fixture has an invalid squad: {fixture.HomeClub} vs {fixture.AwayClub}.");
                results[index] = new DemoFixtureResult(fixture, match.HomeGoals, match.AwayGoals);
            }

            _results.AddRange(results);
            CompletedRounds++;
            return Array.AsReadOnly(results);
        }
    }
}
