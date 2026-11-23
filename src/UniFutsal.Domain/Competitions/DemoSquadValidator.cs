using System;
using System.Collections.Generic;
using System.Linq;

namespace UniFutsal.Domain.Competitions
{
    public enum SquadIssueCode
    {
        ClubNotInFixture,
        InvalidStarterCount,
        InvalidStartingGoalkeeperCount,
        DuplicatePlayer,
        UnknownPlayer,
        PlayerOutsideClub,
        SuspendedPlayer,
        InjuredPlayer
    }

    public sealed class SquadIssue
    {
        public SquadIssueCode Code { get; }
        public string? PlayerId { get; }

        internal SquadIssue(SquadIssueCode code, string? playerId = null)
        {
            Code = code;
            PlayerId = playerId;
        }
    }

    public static class DemoSquadValidator
    {
        public static IReadOnlyList<SquadIssue> Validate(
            DemoFixture fixture,
            string club,
            IReadOnlyList<string> starters,
            IReadOnlyList<string> bench,
            IEnumerable<string>? suspendedPlayerIds = null,
            IEnumerable<string>? unfitPlayerIds = null)
        {
            if (club != fixture.HomeClub && club != fixture.AwayClub)
                return Array.AsReadOnly(new[] { new SquadIssue(SquadIssueCode.ClubNotInFixture) });

            var issues = new List<SquadIssue>();
            if (starters.Count != 5)
                issues.Add(new SquadIssue(SquadIssueCode.InvalidStarterCount));

            var players = DemoRoster.Create().ToDictionary(player => player.Id, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var suspended = new HashSet<string>(suspendedPlayerIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            var unfit = new HashSet<string>(unfitPlayerIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            var knownStarters = 0;
            var startingGoalkeepers = 0;
            foreach (var id in starters)
            {
                var player = CheckPlayer(id);
                if (player == null)
                    continue;

                knownStarters++;
                if (player.Role == DemoPlayerRole.Goalkeeper)
                    startingGoalkeepers++;
            }

            if (starters.Count == 5 && knownStarters == starters.Count && startingGoalkeepers != 1)
                issues.Add(new SquadIssue(SquadIssueCode.InvalidStartingGoalkeeperCount));

            foreach (var id in bench)
                CheckPlayer(id);

            return issues.AsReadOnly();

            DemoPlayer? CheckPlayer(string id)
            {
                if (!seen.Add(id))
                {
                    issues.Add(new SquadIssue(SquadIssueCode.DuplicatePlayer, id));
                    return null;
                }

                if (string.IsNullOrEmpty(id) || !players.TryGetValue(id, out var player))
                {
                    issues.Add(new SquadIssue(SquadIssueCode.UnknownPlayer, id));
                    return null;
                }

                if (player.Club != club)
                {
                    issues.Add(new SquadIssue(SquadIssueCode.PlayerOutsideClub, id));
                    return null;
                }

                if (suspended.Contains(id))
                    issues.Add(new SquadIssue(SquadIssueCode.SuspendedPlayer, id));
                if (unfit.Contains(id))
                    issues.Add(new SquadIssue(SquadIssueCode.InjuredPlayer, id));

                return player;
            }
        }
    }
}
