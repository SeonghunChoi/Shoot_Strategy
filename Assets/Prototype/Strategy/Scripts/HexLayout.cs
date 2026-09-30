using Prototype.Strategy.Rules;
using UnityEngine;

namespace Prototype.Strategy
{
    /// <summary>
    /// 헥스 ↔ 월드 좌표 (XY 평면, 카메라는 +Z를 바라봄). 행(r)이 늘수록 아래(−Y)로 간다.
    /// Size는 중심에서 꼭짓점까지의 거리.
    /// </summary>
    public static class HexLayout
    {
        public const float Size = 1f;
        static readonly float Sqrt3 = Mathf.Sqrt(3f);

        public static Vector2 ToWorld(Hex h) =>
            new(Size * Sqrt3 * (h.Q + h.R * 0.5f), -Size * 1.5f * h.R);

        public static Hex FromWorld(Vector2 p)
        {
            var r = -p.y / (Size * 1.5f);
            var q = p.x / (Size * Sqrt3) - r * 0.5f;
            return Hex.Round(q, r);
        }

        /// <summary>pointy-top 꼭짓점 i (−30°부터 반시계로 60° 간격, 90°가 위쪽 꼭짓점).</summary>
        public static Vector2 Corner(int i, float size = Size)
        {
            var angle = Mathf.Deg2Rad * (60f * i - 30f);
            return new Vector2(size * Mathf.Cos(angle), size * Mathf.Sin(angle));
        }

        /// <summary>맵 전체를 덮는 월드 사각형.</summary>
        public static Rect Bounds(HexMap map)
        {
            var w = Size * Sqrt3;
            var right = w * (map.Width - 1 + 0.5f) + w * 0.5f;
            var bottom = -Size * 1.5f * (map.Height - 1) - Size;
            return Rect.MinMaxRect(-w * 0.5f, bottom, right, Size);
        }
    }
}
