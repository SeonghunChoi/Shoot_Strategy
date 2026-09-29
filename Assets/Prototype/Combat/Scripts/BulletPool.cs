using UnityEngine;

namespace Prototype.Combat
{
    /// <summary>
    /// 탄 구조체 배열 + 인스턴싱 렌더링 (기술설계 6.2의 데이터 형태, Job·Burst 없음).
    /// 제거는 마지막 원소와 맞바꾸므로 순회하면서 제거할 때는 뒤에서부터 돈다.
    /// Pos는 FrameOffset 기준 좌표 (적 탄은 ScrollX 기준 지면 좌표, 기체 탄은 월드 좌표).
    /// </summary>
    public sealed class BulletPool
    {
        public struct Bullet
        {
            public Vector2 Pos;
            public Vector2 Vel;
            public float Radius;
            public float Damage;
            public float Life;
        }

        const int Capacity = 1023; // RenderMeshInstanced 1회 호출 한도

        readonly Bullet[] _items = new Bullet[Capacity];
        readonly Matrix4x4[] _matrices = new Matrix4x4[Capacity];
        readonly Mesh _mesh;
        readonly RenderParams _renderParams;
        readonly Vector3 _scale;
        readonly bool _stretch;

        public int Count { get; private set; }

        /// <summary>월드 좌표 = Pos + FrameOffset</summary>
        public Vector2 FrameOffset { get; set; }

        public Vector2 WorldPos(int index) => _items[index].Pos + FrameOffset;
        public ref Bullet this[int index] => ref _items[index];

        /// <param name="stretch">true면 진행 방향으로 늘린 모양 (scale.x가 길이)</param>
        public BulletPool(Color color, Vector3 scale, bool stretch)
        {
            _mesh = Shapes.Mesh(PrimitiveType.Sphere);
            var material = Shapes.InstancedUnlit(color);
            _renderParams = new RenderParams(material) { worldBounds = new Bounds(Vector3.zero, Vector3.one * 100000f) };
            _scale = scale;
            _stretch = stretch;
        }

        public void Spawn(Vector2 pos, Vector2 vel, float radius, float damage, float life)
        {
            if (Count >= Capacity) return;
            _items[Count++] = new Bullet { Pos = pos, Vel = vel, Radius = radius, Damage = damage, Life = life };
        }

        public void Kill(int index)
        {
            _items[index] = _items[--Count];
        }

        public void Clear() => Count = 0;

        public void Tick(float dt, Rect bounds)
        {
            for (var i = Count - 1; i >= 0; i--)
            {
                ref var b = ref _items[i];
                b.Pos += b.Vel * dt;
                b.Life -= dt;
                if (b.Life <= 0f || !bounds.Contains(b.Pos + FrameOffset)) Kill(i);
            }
        }

        public void Render()
        {
            if (Count == 0) return;
            for (var i = 0; i < Count; i++)
            {
                ref var b = ref _items[i];
                var rot = _stretch ? Quaternion.Euler(0f, 0f, Mathf.Atan2(b.Vel.y, b.Vel.x) * Mathf.Rad2Deg) : Quaternion.identity;
                _matrices[i] = Matrix4x4.TRS(b.Pos + FrameOffset, rot, _scale);
            }
            Graphics.RenderMeshInstanced(_renderParams, _mesh, 0, _matrices, Count);
        }
    }
}
