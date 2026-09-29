using UnityEngine;

namespace Prototype.Combat
{
    public enum EnemyKind
    {
        Straight,
        Curve,
        Chase,
        AntiAir,
        Target,
    }

    /// <summary>
    /// 잡몹·지상 목표 1기. 루트는 ScrollX 아래에 있고 localPosition이 지면 기준 좌표다.
    /// 판정은 월드 좌표의 원(공중) 또는 박스(지상).
    /// </summary>
    public sealed class Enemy
    {
        const float FlashTime = 0.06f;

        public EnemyKind Kind;
        public Transform Root;
        public Transform Barrel;
        public float Hp;
        public float Radius;
        public bool IsBox;
        public Vector2 HalfSize;
        public Vector2 CenterOffset;
        public float Age;
        public float BaseY;
        public float FireTimer;
        public Vector2 Velocity; // 지면 좌표 기준

        Renderer[] _renderers;
        Material[] _originals;
        float _flash;

        public bool IsGround => Kind is EnemyKind.AntiAir or EnemyKind.Target;
        public bool Alive => Hp > 0f;
        public Vector2 Center => (Vector2)Root.position + CenterOffset;
        public float Extent => IsBox ? Mathf.Max(HalfSize.x, HalfSize.y) : Radius;

        public void CacheRenderers()
        {
            _renderers = Root.GetComponentsInChildren<Renderer>();
            _originals = new Material[_renderers.Length];
            for (var i = 0; i < _renderers.Length; i++) _originals[i] = _renderers[i].sharedMaterial;
        }

        public bool Overlaps(Vector2 p, float r) =>
            IsBox ? Hit.CircleBox(p, r, Center, HalfSize) : Hit.Circles(p, r, Center, Radius);

        public void StartFlash()
        {
            _flash = FlashTime;
            var white = Shapes.Material(Color.white, false);
            foreach (var r in _renderers) r.sharedMaterial = white;
        }

        public void TickFlash(float dt)
        {
            if (_flash <= 0f) return;
            _flash -= dt;
            if (_flash > 0f) return;
            for (var i = 0; i < _renderers.Length; i++) _renderers[i].sharedMaterial = _originals[i];
        }
    }
}
