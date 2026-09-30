using System;

namespace Prototype.Strategy.Rules
{
    /// <summary>
    /// 규칙 전용 시드 난수 (SplitMix64). 상태가 ulong 하나라 그대로 기록·복원할 수 있다.
    /// 규칙 코드에서는 UnityEngine.Random / System.Random을 쓰지 않는다 (기술 설계 2.6).
    /// </summary>
    public sealed class Rng
    {
        public ulong State { get; set; }

        public Rng(ulong seed) => State = seed;

        public ulong NextULong()
        {
            var z = State += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>[0, 1)</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

        /// <summary>[0, maxExclusive)</summary>
        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            return (int)(NextULong() % (ulong)maxExclusive);
        }

        /// <summary>[min, maxExclusive)</summary>
        public int Next(int min, int maxExclusive) => min + Next(maxExclusive - min);

        public bool Chance(double p) => NextDouble() < p;
    }
}
