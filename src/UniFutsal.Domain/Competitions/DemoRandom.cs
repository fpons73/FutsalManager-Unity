namespace UniFutsal.Domain.Competitions
{
    internal sealed class DemoRandom
    {
        private ulong _a, _b, _c, _d;

        public DemoRandom(ref ulong seed)
        {
            _a = SplitMix64(ref seed);
            _b = SplitMix64(ref seed);
            _c = SplitMix64(ref seed);
            _d = SplitMix64(ref seed);
        }

        public int Next(int upperBound)
        {
            // Xoshiro256**. This demo only draws small positive integer ranges.
            var value = unchecked(RotateLeft(_b * 5, 7) * 9);
            var shifted = _b << 17;
            _c ^= _a;
            _d ^= _b;
            _b ^= _c;
            _a ^= _d;
            _c ^= shifted;
            _d = RotateLeft(_d, 45);
            return (int)(value % (ulong)upperBound);
        }

        private static ulong RotateLeft(ulong value, int count) => (value << count) | (value >> (64 - count));

        private static ulong SplitMix64(ref ulong seed)
        {
            unchecked
            {
                seed += 0x9e3779b97f4a7c15UL;
                var value = seed;
                value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL;
                value = (value ^ (value >> 27)) * 0x94d049bb133111ebUL;
                return value ^ (value >> 31);
            }
        }
    }
}
