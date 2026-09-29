using System.Collections.Generic;
using UnityEngine;

namespace Prototype.Combat
{
    /// <summary>폭발: 빠르게 커졌다가 줄어드는 구.</summary>
    public sealed class Effects
    {
        struct Blast
        {
            public Transform T;
            public float Age;
            public float Duration;
            public float Size;
        }

        readonly List<Blast> _blasts = new();
        readonly Transform _root = new GameObject("Effects").transform;

        /// <param name="parent">지상 폭발은 ScrollX를 넘겨 지면과 함께 흐르게 한다</param>
        public void Explode(Vector3 position, float size, Transform parent = null)
        {
            var t = Shapes.Create(PrimitiveType.Sphere, new Color(1f, 0.6f, 0.15f), parent != null ? parent : _root,
                Vector3.zero, Vector3.zero, lit: false, name: "Blast");
            t.position = new Vector3(position.x, position.y, -1f);
            _blasts.Add(new Blast { T = t, Duration = 0.25f + size * 0.08f, Size = size });
        }

        public void Tick(float dt)
        {
            for (var i = _blasts.Count - 1; i >= 0; i--)
            {
                var b = _blasts[i];
                b.Age += dt;
                if (b.Age >= b.Duration)
                {
                    Object.Destroy(b.T.gameObject);
                    _blasts.RemoveAt(i);
                    continue;
                }
                var k = b.Age / b.Duration;
                var s = b.Size * (k < 0.3f ? k / 0.3f : 1f - (k - 0.3f) / 0.7f);
                b.T.localScale = Vector3.one * s;
                _blasts[i] = b;
            }
        }

        public void Clear()
        {
            foreach (var b in _blasts) if (b.T != null) Object.Destroy(b.T.gameObject);
            _blasts.Clear();
        }
    }
}
