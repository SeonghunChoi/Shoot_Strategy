using System.Collections.Generic;
using UnityEngine;

namespace Prototype.Combat
{
    /// <summary>
    /// 하드포인트 무장. 미사일은 화면 좌표에서 가까운 공중 적을 유도하고,
    /// 폭탄은 지면 좌표(ScrollX)에서 투하 순간의 대지 속도를 이어받아 낙하한다.
    /// </summary>
    public sealed class OrdnanceSystem
    {
        const float MissileLife = 4f;
        const float MissileRadius = 0.3f;
        const float MissileAccel = 30f;
        const float BombRadius = 0.35f;
        const float BombDrag = 1.5f;

        sealed class Missile
        {
            public Transform T;
            public Vector2 Vel;
            public float Life;
            public Enemy Target;
        }

        sealed class Bomb
        {
            public Transform T;
            public Vector2 LocalVel;
        }

        readonly CombatContext _ctx;
        readonly Transform _root = new GameObject("Ordnance").transform;
        readonly List<Missile> _missiles = new();
        readonly List<Bomb> _bombs = new();

        public OrdnanceSystem(CombatContext ctx) => _ctx = ctx;

        public void FireMissile(Vector2 pos)
        {
            var t = Shapes.Create(PrimitiveType.Capsule, new Color(0.9f, 0.9f, 0.95f), _root, pos, new Vector3(0.2f, 0.45f, 0.2f), name: "Missile");
            _missiles.Add(new Missile
            {
                T = t,
                Vel = new Vector2(_ctx.Tuning.missileSpeed * 0.4f, -2f), // 레일에서 떨어지듯 시작
                Life = MissileLife,
                Target = _ctx.Enemies.FindNearestAir(pos, pos.x - 1f),
            });
        }

        public void DropBomb(Vector2 worldPos)
        {
            var scrollX = _ctx.World.ScrollX;
            var t = Shapes.Create(PrimitiveType.Capsule, new Color(0.25f, 0.28f, 0.22f), scrollX,
                Vector3.zero, new Vector3(0.35f, 0.5f, 0.35f), name: "Bomb");
            t.position = worldPos;
            t.localRotation = Quaternion.Euler(0f, 0f, 90f);
            // 모드 A의 뒤로 입력은 화면 내 위치 이동일 뿐 후진이 아니므로 대지 속도를 0 이상으로 제한
            _bombs.Add(new Bomb { T = t, LocalVel = new Vector2(Mathf.Max(0f, _ctx.Scroll.Speed), 0f) });
        }

        public void Clear()
        {
            foreach (var m in _missiles) Object.Destroy(m.T.gameObject);
            foreach (var b in _bombs) Object.Destroy(b.T.gameObject);
            _missiles.Clear();
            _bombs.Clear();
        }

        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            TickMissiles(dt);
            TickBombs(dt);
        }

        void TickMissiles(float dt)
        {
            var t = _ctx.Tuning;
            var cull = Inflate(_ctx.Camera.View, 8f);

            for (var i = _missiles.Count - 1; i >= 0; i--)
            {
                var m = _missiles[i];
                Vector2 pos = m.T.position;

                if (m.Target == null || !m.Target.Alive) m.Target = _ctx.Enemies.FindNearestAir(pos, pos.x);

                var speed = Mathf.Min(m.Vel.magnitude + MissileAccel * dt, t.missileSpeed);
                var dir = m.Vel.normalized;
                if (m.Target != null)
                {
                    var want = (m.Target.Center - pos).normalized;
                    var angle = Mathf.MoveTowardsAngle(Angle(dir), Angle(want), t.missileTurnRate * dt) * Mathf.Deg2Rad;
                    dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                }
                else if (dir.x < 0.99f)
                {
                    // 목표가 없으면 수평으로 복귀
                    var angle = Mathf.MoveTowardsAngle(Angle(dir), 0f, t.missileTurnRate * dt) * Mathf.Deg2Rad;
                    dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                }
                m.Vel = dir * speed;
                pos += m.Vel * dt;
                m.T.position = pos;
                m.T.rotation = Quaternion.Euler(0f, 0f, Angle(dir) - 90f);
                m.Life -= dt;

                var hit = FindOverlap(pos, MissileRadius);
                var hitTerrain = _ctx.Stage.Terrain.IsSolid(pos.x + _ctx.Scroll.Distance, pos.y);
                if (hit != null) _ctx.Enemies.Damage(hit, t.missileDamage);
                if (hit != null || hitTerrain) _ctx.Effects.Explode(pos, 1.5f);
                if (hit != null || hitTerrain || m.Life <= 0f || !cull.Contains(pos))
                {
                    Object.Destroy(m.T.gameObject);
                    _missiles.RemoveAt(i);
                }
            }
        }

        void TickBombs(float dt)
        {
            var t = _ctx.Tuning;
            for (var i = _bombs.Count - 1; i >= 0; i--)
            {
                var b = _bombs[i];
                b.LocalVel.y -= t.bombGravity * dt;
                b.LocalVel.x = Mathf.MoveTowards(b.LocalVel.x, 0f, BombDrag * dt);

                var local = (Vector2)b.T.localPosition + b.LocalVel * dt;
                b.T.localPosition = local;
                b.T.localRotation = Quaternion.Euler(0f, 0f, Angle(b.LocalVel.normalized) - 90f);

                Vector2 pos = b.T.position;
                if (local.y > _ctx.Stage.Terrain.Floor(local.x) && FindOverlap(pos, BombRadius) == null) continue;

                // 지면 또는 목표에 닿으면 범위 폭발
                foreach (var e in _ctx.Enemies.All)
                {
                    if (e.Alive && (e.Center - pos).magnitude <= t.bombRadius + e.Extent)
                        _ctx.Enemies.Damage(e, t.bombDamage);
                }
                _ctx.Effects.Explode(pos, t.bombRadius * 2f, _ctx.World.ScrollX);
                Object.Destroy(b.T.gameObject);
                _bombs.RemoveAt(i);
            }
        }

        Enemy FindOverlap(Vector2 pos, float radius)
        {
            foreach (var e in _ctx.Enemies.All)
                if (e.Alive && e.Overlaps(pos, radius)) return e;
            return null;
        }

        static float Angle(Vector2 dir) => Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        static Rect Inflate(Rect r, float m) => new(r.xMin - m, r.yMin - m, r.width + m * 2f, r.height + m * 2f);
    }
}
