using System;
using System.Collections.Generic;

namespace UniFutsal.Domain.Competitions
{
    public sealed class DemoFixture
    {
        public int Round { get; }
        public string HomeClub { get; }
        public string AwayClub { get; }

        internal DemoFixture(int round, string homeClub, string awayClub)
        {
            Round = round;
            HomeClub = homeClub;
            AwayClub = awayClub;
        }
    }

    public static class DemoCalendar
    {
        public static IReadOnlyList<DemoFixture> Create()
        {
            var clubs = new[]
            {
                "Atlético Bruma", "Bahía Norte", "Costa Alba", "Estrella del Valle",
                "Horizonte Futsal", "Monte Claro", "Puerto Viento", "Unión Ribera"
            };
            var roundsPerLeg = clubs.Length - 1;
            var matchesPerRound = clubs.Length / 2;
            var matchesPerLeg = roundsPerLeg * matchesPerRound;
            var fixtures = new DemoFixture[matchesPerLeg * 2];

            for (var round = 0; round < roundsPerLeg; round++)
            {
                for (var pair = 0; pair < matchesPerRound; pair++)
                {
                    var home = clubs[pair];
                    var away = clubs[clubs.Length - 1 - pair];
                    // Alternate the fixed club's venue instead of giving it an entire home leg.
                    if (pair == 0 && round % 2 != 0)
                        (home, away) = (away, home);

                    var index = round * matchesPerRound + pair;
                    fixtures[index] = new DemoFixture(round + 1, home, away);
                    fixtures[index + matchesPerLeg] = new DemoFixture(round + 1 + roundsPerLeg, away, home);
                }

                // Keep the first club fixed and rotate the remaining clubs by one place.
                var lastClub = clubs[clubs.Length - 1];
                Array.Copy(clubs, 1, clubs, 2, clubs.Length - 2);
                clubs[1] = lastClub;
            }

            return Array.AsReadOnly(fixtures);
        }
    }
}
