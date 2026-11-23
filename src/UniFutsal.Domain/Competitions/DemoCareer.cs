using System;
using System.Collections.Generic;

namespace UniFutsal.Domain.Competitions
{
    public sealed class DemoCompletedSeason
    {
        public int Number { get; }
        public ulong Seed { get; }
        public DemoSeasonResult Result { get; }
        public string Champion => Result.Standings[0].Club;

        internal DemoCompletedSeason(int number, ulong seed, DemoSeasonResult result)
        {
            Number = number;
            Seed = seed;
            Result = result;
        }
    }

    public sealed class DemoCareer
    {
        public ulong Seed { get; }
        public IReadOnlyList<DemoCompletedSeason> CompletedSeasons { get; }
        public int NextSeasonNumber => CompletedSeasons.Count + 1;

        public DemoCareer(ulong seed) : this(seed, Array.Empty<DemoCompletedSeason>()) { }

        private DemoCareer(ulong seed, IReadOnlyList<DemoCompletedSeason> completedSeasons)
        {
            Seed = seed;
            CompletedSeasons = completedSeasons;
        }

        public DemoCareer CompleteSeason(int seasonNumber)
        {
            if (seasonNumber < 1 || seasonNumber > NextSeasonNumber)
                throw new ArgumentOutOfRangeException(nameof(seasonNumber));
            if (seasonNumber < NextSeasonNumber)
                return this;

            // Keep the first season compatible with the standalone season command.
            var seasonSeed = unchecked(Seed + (ulong)(seasonNumber - 1) * 0x9E3779B97F4A7C15UL);
            var result = DemoSeason.Simulate(seasonSeed);
            var history = new List<DemoCompletedSeason>(CompletedSeasons)
            {
                new DemoCompletedSeason(seasonNumber, seasonSeed, result)
            };
            return new DemoCareer(Seed, history.AsReadOnly());
        }
    }
}
