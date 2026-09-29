using System.Collections.Generic;
using UnityEngine;

namespace Prototype.Combat
{
    /// <summary>
    /// 공중 잡몹(직선·곡선·추적)과 지상 목표(대공포·고정 목표)의 등장·이동·발사·피격.
    /// 모두 ScrollX 아래 지면 좌표로 움직이므로, 기체가 가속하면 화면상 접근도 빨라진다.
    /// 등장은 4단계 스테이지 전까지 임시 타이머 방식.
    /// </summary>
    public sealed class EnemySystem
    {
        const float CurveAmplitude = 3f;
        const float CurveFrequency = 1.6f;
        const float ChaseVerticalSpeed = 4f;

        static readonly Color StraightColor = new(0.85f, 0.25f, 0.2f);
        static readonly Color CurveColor = new(0.95f, 0.55f, 0.15f);
        static readonly Color ChaseColor = new(0.6f, 0.3f, 0.8f);

        readonly CombatContext _ctx;
        readonly List<Enemy> _enemies = new();
        float _airTimer;
        float _groundTimer;

        public IReadOnlyList<Enemy> All => _enemies;

        public EnemySystem(CombatContext ctx) => _ctx = ctx;

        public void Clear()
        {
            foreach (var e in _enemies) Object.Destroy(e.Root.gameObject);
            _enemies.Clear();
            _airTimer = 0f;
            _groundTimer = 0f;
        }

        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            Spawn(dt);

            var view = _ctx.Camera.View;
            for (var i = _enemies.Count - 1; i >= 0; i--)
            {
                var e = _enemies[i];
                var screenX = e.Root.position.x;
                if (!e.Alive || screenX < view.xMin - 6f || screenX > view.xMax + 40f)
                {
                    Object.Destroy(e.Root.gameObject);
                    _enemies.RemoveAt(i);
                    continue;
                }

                e.Age += dt;
                if (!e.IsGround) Move(e, dt);
                if (e.Barrel != null) AimBarrel(e);
                Fire(e, dt, view);
                e.TickFlash(dt);
            }
        }

        public void Damage(Enemy e, float amount)
        {
            if (!e.Alive) return;
            e.Hp -= amount;
            e.StartFlash();
            if (e.Alive) return;

            _ctx.Effects.Explode(e.Center, e.IsGround ? 3.5f : 2.5f, e.IsGround ? _ctx.World.ScrollX : null);
            if (e.IsGround) _ctx.GroundKills++;
            else _ctx.AirKills++;
            e.Root.gameObject.SetActive(false); // 제거는 다음 Tick
        }

        public Enemy FindNearestAir(Vector2 from, float minX)
        {
            Enemy best = null;
            var bestDist = float.MaxValue;
            foreach (var e in _enemies)
            {
                if (!e.Alive || e.IsGround || e.Center.x < minX) continue;
                var d = (e.Center - from).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = e; }
            }
            return best;
        }

        /// <summary>기관포 탄 대 적, 적 탄 대 기체, 공중 적 몸체 대 기체.</summary>
        public void ResolveCollisions()
        {
            var bullets = _ctx.PlayerBullets;
            for (var i = bullets.Count - 1; i >= 0; i--)
            {
                ref var b = ref bullets[i];
                foreach (var e in _enemies)
                {
                    if (!e.Alive || !e.Overlaps(b.Pos, b.Radius)) continue;
                    Damage(e, b.Damage);
                    bullets.Kill(i);
                    break;
                }
            }

            var player = _ctx.Player;
            if (!player.CanBeHit) return;

            var enemyBullets = _ctx.EnemyBullets;
            for (var i = enemyBullets.Count - 1; i >= 0; i--)
            {
                ref var b = ref enemyBullets[i];
                if (!Hit.Circles(enemyBullets.WorldPos(i), b.Radius, player.Position, player.HitRadius)) continue;
                var damage = b.Damage;
                enemyBullets.Kill(i);
                if (HitPlayer(damage)) return;
            }

            foreach (var e in _enemies)
            {
                if (!e.Alive || e.IsGround || !e.Overlaps(player.Position, player.HitRadius)) continue;
                Damage(e, float.MaxValue);
                if (HitPlayer(_ctx.Tuning.enemyContactDamage)) return;
            }
        }

        bool HitPlayer(float damage)
        {
            var player = _ctx.Player;
            if (!player.Damage(damage)) return !player.CanBeHit;
            _ctx.PlayerDowns++;
            _ctx.Effects.Explode(player.Position, 4f);
            return true;
        }

        void Spawn(float dt)
        {
            var t = _ctx.Tuning;
            var stage = _ctx.Stage;
            if (stage.Cleared) return;

            // 스테이지 사용 시 화면 오른쪽 끝(등장 지점)이 속한 구간의 설정을 따른다
            var section = stage.SectionAt(_ctx.Scroll.Distance + _ctx.Camera.HalfWidth);
            var airInterval = section != null ? section.AirInterval * t.stageSpawnScale : t.airSpawnInterval;
            var groundInterval = section != null ? section.GroundInterval * t.stageSpawnScale : t.groundSpawnInterval;
            var airWeights = section?.AirWeights ?? EqualWeights;
            var antiAirRatio = section?.AntiAirRatio ?? 0.5f;

            if (t.airSpawnEnabled && airInterval > 0f && (_airTimer -= dt) <= 0f)
            {
                SpawnAir((EnemyKind)PickWeighted(airWeights));
                _airTimer = airInterval;
            }
            if (t.groundSpawnEnabled && groundInterval > 0f && (_groundTimer -= dt) <= 0f)
            {
                SpawnGround(_ctx.Rng.NextDouble() < antiAirRatio ? EnemyKind.AntiAir : EnemyKind.Target);
                _groundTimer = groundInterval;
            }
        }

        static readonly float[] EqualWeights = { 1f, 1f, 1f };

        int PickWeighted(float[] weights)
        {
            var total = 0f;
            foreach (var w in weights) total += w;
            var r = (float)_ctx.Rng.NextDouble() * total;
            for (var i = 0; i < weights.Length; i++)
            {
                if ((r -= weights[i]) < 0f) return i;
            }
            return weights.Length - 1;
        }

        (float min, float max) AirBand(float groundX)
        {
            var terrain = _ctx.Stage.Terrain;
            return (terrain.Floor(groundX) + 1.5f, terrain.Ceil(groundX) - 1.5f);
        }

        void SpawnAir(EnemyKind kind)
        {
            var cam = _ctx.Camera;
            // 화면 위아래 밖에서도 등장 (Q3: 세로 시야 확인)
            var groundX = _ctx.Scroll.Distance + cam.HalfWidth + 3f;
            var (minY, maxY) = AirBand(groundX);
            var y = Mathf.Clamp(cam.Y + _ctx.Range(-1.6f, 1.6f) * cam.HalfHeight, minY, maxY);
            var root = CreateRoot(kind, groundX, y);

            var color = kind switch { EnemyKind.Curve => CurveColor, EnemyKind.Chase => ChaseColor, _ => StraightColor };
            var body = Shapes.Create(PrimitiveType.Capsule, color, root, Vector3.zero, new Vector3(0.55f, 0.9f, 0.55f));
            body.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Shapes.Create(PrimitiveType.Cube, color * 0.7f, root, new Vector3(0.1f, 0f, 0f), new Vector3(0.5f, 0.07f, 1.8f));
            Shapes.Create(PrimitiveType.Cube, color * 0.7f, root, new Vector3(0.75f, 0.3f, 0f), new Vector3(0.3f, 0.5f, 0.07f));

            Add(new Enemy
            {
                Kind = kind, Root = root, Hp = _ctx.Tuning.airEnemyHp, Radius = 0.8f, BaseY = y,
                FireTimer = _ctx.Range(0.5f, _ctx.Tuning.airFireInterval),
            });
        }

        void SpawnGround(EnemyKind kind)
        {
            var groundX = _ctx.Scroll.Distance + _ctx.Camera.HalfWidth + 4f;
            var root = CreateRoot(kind, groundX, _ctx.Stage.Terrain.Floor(groundX));
            var e = new Enemy { Kind = kind, Root = root, IsBox = true };

            if (kind == EnemyKind.AntiAir)
            {
                Shapes.Create(PrimitiveType.Cube, new Color(0.3f, 0.32f, 0.3f), root, new Vector3(0f, 0.5f, 0f), new Vector3(1.6f, 1f, 1.2f));
                e.Barrel = new GameObject("Barrel").transform;
                e.Barrel.SetParent(root, false);
                e.Barrel.localPosition = new Vector3(0f, 1f, -0.2f);
                Shapes.Create(PrimitiveType.Cube, new Color(0.18f, 0.18f, 0.18f), e.Barrel, new Vector3(0.6f, 0f, 0f), new Vector3(1.2f, 0.25f, 0.25f));
                e.Hp = _ctx.Tuning.antiAirHp;
                e.HalfSize = new Vector2(0.8f, 0.6f);
                e.CenterOffset = new Vector2(0f, 0.6f);
                e.FireTimer = _ctx.Range(0.5f, _ctx.Tuning.antiAirFireInterval);
            }
            else
            {
                Shapes.Create(PrimitiveType.Cube, new Color(0.55f, 0.4f, 0.3f), root, new Vector3(0f, 1.25f, 0f), new Vector3(3f, 2.5f, 2f));
                Shapes.Create(PrimitiveType.Cube, new Color(0.4f, 0.3f, 0.25f), root, new Vector3(0f, 2.7f, 0f), new Vector3(3.2f, 0.4f, 2.2f));
                e.Hp = _ctx.Tuning.targetHp;
                e.HalfSize = new Vector2(1.5f, 1.45f);
                e.CenterOffset = new Vector2(0f, 1.45f);
            }
            Add(e);
        }

        Transform CreateRoot(EnemyKind kind, float localX, float y)
        {
            var root = new GameObject(kind.ToString()).transform;
            root.SetParent(_ctx.World.ScrollX, false);
            root.localPosition = new Vector3(localX, y, 0f);
            return root;
        }

        void Add(Enemy e)
        {
            e.CacheRenderers();
            _enemies.Add(e);
        }

        void Move(Enemy e, float dt)
        {
            var p = e.Root.localPosition;
            var previous = (Vector2)p;
            var speed = _ctx.Tuning.enemyAirSpeed;
            switch (e.Kind)
            {
                case EnemyKind.Straight:
                    p.x -= speed * dt;
                    break;
                case EnemyKind.Curve:
                    p.x -= speed * dt;
                    p.y = e.BaseY + Mathf.Sin(e.Age * CurveFrequency) * CurveAmplitude;
                    break;
                case EnemyKind.Chase:
                    p.x -= speed * 0.8f * dt;
                    p.y += Mathf.Clamp((_ctx.Player.Y - p.y) * 1.5f, -ChaseVerticalSpeed, ChaseVerticalSpeed) * dt;
                    break;
            }
            var (minY, maxY) = AirBand(p.x); // 지형을 뚫지 않는다
            p.y = Mathf.Clamp(p.y, minY, maxY);
            e.Root.localPosition = p;
            e.Velocity = ((Vector2)p - previous) / dt;
        }

        void AimBarrel(Enemy e)
        {
            var dir = LeadDirection(e.Barrel.position, Vector2.zero);
            var angle = Mathf.Clamp(Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg, 10f, 170f);
            e.Barrel.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        /// <summary>
        /// 지면 좌표에서 기체(대지 속도 = 스크롤 속도)의 도달 시점 위치를 겨누는 방향.
        /// 탄의 지면 속도 = shooterVelocity + 방향 × 탄속. 해가 없으면 현재 위치를 겨눈다.
        /// 예측량은 enemyLeadAccuracy 배율로 줄일 수 있다 (0이면 현재 위치).
        /// </summary>
        Vector2 LeadDirection(Vector2 originWorld, Vector2 shooterVelocity)
        {
            var player = _ctx.Player;
            var toTarget = player.Position - originWorld; // 월드와 지면 좌표의 차이는 평행 이동뿐
            var relVel = new Vector2(_ctx.Scroll.Speed, player.VelocityY) - shooterVelocity;
            var s = _ctx.Tuning.enemyBulletSpeed;

            // |toTarget + relVel·t| = s·t 의 가장 작은 양의 해
            var a = Vector2.Dot(relVel, relVel) - s * s;
            var b = 2f * Vector2.Dot(toTarget, relVel);
            var c = Vector2.Dot(toTarget, toTarget);
            var t = -1f;
            if (Mathf.Abs(a) < 1e-4f)
            {
                if (Mathf.Abs(b) > 1e-4f) t = -c / b;
            }
            else
            {
                var disc = b * b - 4f * a * c;
                if (disc >= 0f)
                {
                    var sq = Mathf.Sqrt(disc);
                    var t1 = (-b - sq) / (2f * a);
                    var t2 = (-b + sq) / (2f * a);
                    t = t1 > 0f && (t1 < t2 || t2 <= 0f) ? t1 : t2;
                }
            }

            var aim = t > 0f ? toTarget + relVel * (t * _ctx.Tuning.enemyLeadAccuracy) : toTarget;
            return aim.normalized;
        }

        void Fire(Enemy e, float dt, Rect view)
        {
            if (e.Kind == EnemyKind.Target || _ctx.Player.Down) return;
            if ((e.FireTimer -= dt) > 0f) return;

            var t = _ctx.Tuning;
            e.FireTimer = e.IsGround ? t.antiAirFireInterval : t.airFireInterval;
            if (!view.Contains(e.Center)) return; // 화면 밖에서는 쏘지 않는다

            // 탄은 지면 좌표에 두고 발사한 적의 속도를 이어받는다
            Vector2 origin;
            Vector2 vel;
            if (e.Barrel != null)
            {
                origin = e.Barrel.position + e.Barrel.right * 1.2f;
                vel = (Vector2)e.Barrel.right * t.enemyBulletSpeed;
            }
            else
            {
                origin = e.Center;
                vel = e.Velocity + LeadDirection(origin, e.Velocity) * t.enemyBulletSpeed;
            }
            var bullets = _ctx.EnemyBullets;
            bullets.Spawn(origin - bullets.FrameOffset, vel, 0.25f, t.enemyBulletDamage, 6f);
        }
    }
}
