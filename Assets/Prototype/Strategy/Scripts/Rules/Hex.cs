using System;
using System.Collections.Generic;

namespace Prototype.Strategy.Rules
{
    /// <summary>
    /// pointy-top 헥스 좌표 (기술 설계 4.1). 저장은 축 좌표(q, r), 연산은 큐브 좌표(s = -q - r).
    /// 맵은 사각형 odd-r 오프셋(홀수 행이 반 칸 오른쪽)으로 저장한다.
    /// </summary>
    public readonly struct Hex : IEquatable<Hex>
    {
        public readonly int Q;
        public readonly int R;
        public int S => -Q - R;

        public Hex(int q, int r)
        {
            Q = q;
            R = r;
        }

        /// <summary>동(0)부터 반시계 방향: 동, 북동, 북서, 서, 남서, 남동. 행(r)은 아래로 증가.</summary>
        public static readonly Hex[] Directions =
        {
            new(1, 0), new(1, -1), new(0, -1), new(-1, 0), new(-1, 1), new(0, 1),
        };

        public static Hex operator +(Hex a, Hex b) => new(a.Q + b.Q, a.R + b.R);
        public static Hex operator -(Hex a, Hex b) => new(a.Q - b.Q, a.R - b.R);
        public static Hex operator *(Hex a, int k) => new(a.Q * k, a.R * k);
        public static bool operator ==(Hex a, Hex b) => a.Q == b.Q && a.R == b.R;
        public static bool operator !=(Hex a, Hex b) => !(a == b);

        public bool Equals(Hex other) => this == other;
        public override bool Equals(object obj) => obj is Hex h && this == h;
        public override int GetHashCode() => (Q * 397) ^ R;
        public override string ToString() => $"({Q}, {R})";

        public int Length => (Math.Abs(Q) + Math.Abs(R) + Math.Abs(S)) / 2;

        public static int Distance(Hex a, Hex b) => (a - b).Length;

        public Hex Neighbor(int direction) => this + Directions[direction];

        public IEnumerable<Hex> Neighbors()
        {
            for (var i = 0; i < 6; i++) yield return Neighbor(i);
        }

        /// <summary>중심에서 radius 이내의 모든 헥스 (작전 반경·탐지·작전 지역). 개수 = 1 + 3r(r+1).</summary>
        public static IEnumerable<Hex> Range(Hex center, int radius)
        {
            for (var q = -radius; q <= radius; q++)
            {
                var rMin = Math.Max(-radius, -q - radius);
                var rMax = Math.Min(radius, -q + radius);
                for (var r = rMin; r <= rMax; r++) yield return center + new Hex(q, r);
            }
        }

        /// <summary>중심에서 정확히 radius 떨어진 고리. 개수 = 6r (r = 0이면 중심 1개).</summary>
        public static IEnumerable<Hex> Ring(Hex center, int radius)
        {
            if (radius == 0)
            {
                yield return center;
                yield break;
            }
            var h = center + Directions[4] * radius;
            for (var side = 0; side < 6; side++)
            for (var step = 0; step < radius; step++)
            {
                yield return h;
                h = h.Neighbor(side);
            }
        }

        /// <summary>
        /// b가 a에서 한 방향으로 곧게 뻗은 칸이면 그 방향(0~5), 아니면 -1 (같은 칸도 -1).
        /// 활주로 직선 요건 판정용 (정수 연산).
        /// </summary>
        public static int StraightDirection(Hex a, Hex b)
        {
            var d = b - a;
            var n = d.Length;
            if (n == 0) return -1;
            for (var i = 0; i < 6; i++)
                if (Directions[i] * n == d) return i;
            return -1;
        }

        /// <summary>a에서 b까지 이어지는 헥스 직선 (양 끝 포함, 개수 = 거리 + 1). 경계에서는 한쪽으로 치우치게 반올림.</summary>
        public static List<Hex> Line(Hex a, Hex b)
        {
            var n = Distance(a, b);
            var result = new List<Hex>(n + 1);
            // 꼭짓점 위를 지날 때 반올림이 흔들리지 않도록 살짝 밀어 준다
            const double nudge = 1e-6;
            double aq = a.Q + nudge, ar = a.R + nudge, bq = b.Q + nudge, br = b.R + nudge;
            for (var i = 0; i <= n; i++)
            {
                var t = n == 0 ? 0.0 : (double)i / n;
                result.Add(Round(aq + (bq - aq) * t, ar + (br - ar) * t));
            }
            return result;
        }

        /// <summary>실수 축 좌표를 가장 가까운 헥스로 반올림 (큐브 반올림).</summary>
        public static Hex Round(double q, double r)
        {
            var s = -q - r;
            var rq = Math.Round(q);
            var rr = Math.Round(r);
            var rs = Math.Round(s);
            var dq = Math.Abs(rq - q);
            var dr = Math.Abs(rr - r);
            var ds = Math.Abs(rs - s);
            if (dq > dr && dq > ds) rq = -rr - rs;
            else if (dr > ds) rr = -rq - rs;
            return new Hex((int)rq, (int)rr);
        }

        public static Hex FromOffset(int col, int row) => new(col - (row - (row & 1)) / 2, row);

        public (int col, int row) ToOffset() => (Q + (R - (R & 1)) / 2, R);
    }
}
