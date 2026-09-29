using UnityEngine;

namespace Prototype.Combat
{
    /// <summary>물리 엔진 없는 원·박스 판정 (기술설계 6.2와 같은 방식).</summary>
    public static class Hit
    {
        public static bool Circles(Vector2 a, float ra, Vector2 b, float rb)
        {
            var r = ra + rb;
            return (a - b).sqrMagnitude <= r * r;
        }

        public static bool CircleBox(Vector2 c, float r, Vector2 boxCenter, Vector2 half)
        {
            var d = c - boxCenter;
            var closest = new Vector2(Mathf.Clamp(d.x, -half.x, half.x), Mathf.Clamp(d.y, -half.y, half.y));
            return (d - closest).sqrMagnitude <= r * r;
        }
    }
}
