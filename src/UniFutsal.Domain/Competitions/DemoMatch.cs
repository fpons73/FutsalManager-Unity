using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace UniFutsal.Domain.Competitions
{
    public sealed class DemoMatchSquad
    {
        public IReadOnlyList<string> Starters { get; }
        public IReadOnlyList<string> Bench { get; }
        internal IReadOnlyList<string> SuspendedPlayerIds { get; }
        internal IReadOnlyList<string> UnfitPlayerIds { get; }

        public DemoMatchSquad(IEnumerable<string> starters, IEnumerable<string> bench,
            IEnumerable<string>? suspendedPlayerIds = null, IEnumerable<string>? unfitPlayerIds = null)
        {
            Starters = Array.AsReadOnly(starters.ToArray());
            Bench = Array.AsReadOnly(bench.ToArray());
            SuspendedPlayerIds = Array.AsReadOnly((suspendedPlayerIds ?? Array.Empty<string>()).ToArray());
            UnfitPlayerIds = Array.AsReadOnly((unfitPlayerIds ?? Array.Empty<string>()).ToArray());
        }

        public static DemoMatchSquad SelectDefault(IEnumerable<DemoPlayer> roster)
        {
            var players = roster.ToArray();
            var starters = players.Where(player => player.Role == DemoPlayerRole.Goalkeeper).Take(1)
                .Concat(players.Where(player => player.Role != DemoPlayerRole.Goalkeeper).Take(4))
                .Select(player => player.Id).ToArray();
            return new DemoMatchSquad(starters, players.Select(player => player.Id).Except(starters, StringComparer.Ordinal));
        }
    }

    public enum DemoMatchEventKind { PeriodStarted, Shot, Goal, PeriodEnded, MatchFinished }

    public sealed class DemoMatchEvent
    {
        public int Sequence { get; }
        public int Period { get; }
        public int ElapsedEffectiveMs { get; }
        public DemoMatchEventKind Kind { get; }
        public string? Club { get; }
        public string? PlayerId { get; }

        internal DemoMatchEvent(int sequence, int period, int elapsedEffectiveMs, DemoMatchEventKind kind,
            string? club, string? playerId)
        {
            Sequence = sequence;
            Period = period;
            ElapsedEffectiveMs = elapsedEffectiveMs;
            Kind = kind;
            Club = club;
            PlayerId = playerId;
        }
    }

    public sealed class DemoMatchResult
    {
        public IReadOnlyList<DemoMatchEvent> Events { get; }
        public IReadOnlyList<SquadIssue> HomeIssues { get; }
        public IReadOnlyList<SquadIssue> AwayIssues { get; }
        public int HomeGoals { get; }
        public int AwayGoals { get; }
        public bool Completed => Events.Count > 0 && Events[Events.Count - 1].Kind == DemoMatchEventKind.MatchFinished;

        internal DemoMatchResult(IReadOnlyList<DemoMatchEvent> events, IReadOnlyList<SquadIssue> homeIssues,
            IReadOnlyList<SquadIssue> awayIssues, int homeGoals = 0, int awayGoals = 0)
        {
            Events = events;
            HomeIssues = homeIssues;
            AwayIssues = awayIssues;
            HomeGoals = homeGoals;
            AwayGoals = awayGoals;
        }
    }

    public static class DemoMatch
    {
        private const int PeriodDurationMs = 20 * 60 * 1000;

        public static DemoMatchResult Simulate(DemoFixture fixture, ulong careerSeed,
            DemoMatchSquad home, DemoMatchSquad away)
        {
            var homeIssues = Validate(fixture.HomeClub, home);
            var awayIssues = Validate(fixture.AwayClub, away);
            if (homeIssues.Count != 0 || awayIssues.Count != 0)
                return new DemoMatchResult(Array.Empty<DemoMatchEvent>(), homeIssues, awayIssues);

            var players = DemoRoster.Create().ToDictionary(player => player.Id, StringComparer.Ordinal);
            var homeShooters = Outfield(home);
            var awayShooters = Outfield(away);
            var streamSeed = careerSeed ^ FixtureHash(fixture);
            var actions = new DemoRandom(ref streamSeed);
            var resolution = new DemoRandom(ref streamSeed);
            var events = new List<DemoMatchEvent>();
            var homeGoals = 0;
            var awayGoals = 0;
            var elapsed = 0;

            for (var period = 1; period <= 2; period++)
            {
                var end = period * PeriodDurationMs;
                Emit(DemoMatchEventKind.PeriodStarted, period);
                while (elapsed < end)
                {
                    elapsed = Math.Min(end, elapsed + (1 + actions.Next(4)) * 1000);
                    if (elapsed == end || actions.Next(100) >= 3)
                        continue;

                    var isHome = actions.Next(2) == 0;
                    var shooters = isHome ? homeShooters : awayShooters;
                    var club = isHome ? fixture.HomeClub : fixture.AwayClub;
                    var shooter = shooters[actions.Next(shooters.Length)];
                    Emit(DemoMatchEventKind.Shot, period, club, shooter);
                    if (resolution.Next(100) >= 18)
                        continue;

                    Emit(DemoMatchEventKind.Goal, period, club, shooter);
                    if (isHome)
                        homeGoals++;
                    else
                        awayGoals++;
                }
                Emit(DemoMatchEventKind.PeriodEnded, period);
            }

            Emit(DemoMatchEventKind.MatchFinished, 2);
            return new DemoMatchResult(events.AsReadOnly(), homeIssues, awayIssues, homeGoals, awayGoals);

            IReadOnlyList<SquadIssue> Validate(string club, DemoMatchSquad squad) =>
                DemoSquadValidator.Validate(fixture, club, squad.Starters, squad.Bench,
                    squad.SuspendedPlayerIds, squad.UnfitPlayerIds);

            string[] Outfield(DemoMatchSquad squad) => squad.Starters
                .Where(id => players[id].Role != DemoPlayerRole.Goalkeeper)
                .OrderBy(id => id, StringComparer.Ordinal).ToArray();

            void Emit(DemoMatchEventKind kind, int period, string? club = null, string? playerId = null) =>
                events.Add(new DemoMatchEvent(events.Count + 1, period, elapsed, kind, club, playerId));
        }

        private static ulong FixtureHash(DemoFixture fixture)
        {
            var key = FormattableString.Invariant($"{fixture.Round}\0{fixture.HomeClub}\0{fixture.AwayClub}");
            var hash = 14695981039346656037UL;
            foreach (var value in Encoding.UTF8.GetBytes(key))
                hash = unchecked((hash ^ value) * 1099511628211UL);
            return hash;
        }
    }
}
