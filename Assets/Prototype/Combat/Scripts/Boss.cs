using System.Collections.Generic;
using UnityEngine;

namespace Prototype.Combat
{
    /// <summary>
    /// 선회 구역 가운데의 고정형 보스 (검증계획 6장: 부위 2~3개, 3.5 선회 구역).
    /// 본체(지상 요새)는 지형으로 처리하고(StageTerrain), 부위 3개는 적 판정 목록에 넣는다.
    /// 포탑 2개: 예측 조준 부채꼴. 하나가 부서지면 남은 포탑이 빨라지고 5연.
    /// 코어: 포탑이 모두 부서지기 전에는 무적, 열리면 전방위 링 탄 + 조준 3연. 코어 파괴 = 격파.
    /// </summary>
    public sealed class Boss
    {
        public const float BodyHalfWidth = 8f;
        public const float BodyHeight = 7f;

        const float TurretOffset = 5.5f;
        const float FanSpread = 12f;
        const int RingCount = 12;
        const float FireRange = 10f; // 보스가 화면 밖 이 거리 안이면 사격

        static readonly Color BodyColor = new(0.32f, 0.34f, 0.38f);
        static readonly Color TurretColor = new(0.45f, 0.42f, 0.36f);
        static readonly Color CoreShieldColor = new(0.6f, 0.6f, 0.65f);
        static readonly Color CoreOpenColor = new(0.95f, 0.2f, 0.2f);

        readonly CombatContext _ctx;
        readonly List<Enemy> _parts = new();
        Transform _body;
        Enemy _left;
        Enemy _right;
        Enemy _core;
        float _leftTimer;
        float _rightTimer;
        float _coreTimer;
        bool _coreOpen;

        public bool Active => _core != null;
        public bool Defeated { get; private set; }
        public float CenterX { get; private set; }
        public IReadOnlyList<Enemy> Parts => _parts;

        public Boss(CombatContext ctx) => _ctx = ctx;

        /// <summary>부위 오브젝트는 EnemySystem.Clear가 지우므로 여기서는 본체와 참조만 정리한다.</summary>
        public void Clear()
        {
            if (_body != null) Object.Destroy(_body.gameObject);
            _body = null;
            _parts.Clear();
            _left = _right = _core = null;
            Defeated = false;
            _coreOpen = false;
        }

        public void Build(StageSection zone)
        {
            Clear();
            CenterX = zone.Center;
            var scrollX = _ctx.World.ScrollX;

            _body = Shapes.Create(PrimitiveType.Cube, BodyColor, scrollX,
                new Vector3(CenterX, BodyHeight * 0.5f, 1f), new Vector3(BodyHalfWidth * 2f, BodyHeight, 2f), name: "BossBody");

            _left = CreateTurret("왼쪽 포탑", -TurretOffset);
            _right = CreateTurret("오른쪽 포탑", TurretOffset);
            _core = CreateCore();

            var t = _ctx.Tuning;
            _leftTimer = t.bossFireInterval * 0.5f;
            _rightTimer = t.bossFireInterval;
            _coreTimer = t.bossFireInterval;
        }

        Enemy CreateTurret(string label, float offsetX)
        {
            var root = CreateRoot(label, offsetX);
            Shapes.Create(PrimitiveType.Cube, TurretColor, root, new Vector3(0f, 0.6f, 0f), new Vector3(2.4f, 1.2f, 1.6f));
            var barrel = new GameObject("Barrel").transform;
            barrel.SetParent(root, false);
            barrel.localPosition = new Vector3(0f, 1.2f, -0.3f);
            Shapes.Create(PrimitiveType.Cube, new Color(0.18f, 0.18f, 0.18f), barrel, new Vector3(0.8f, 0f, 0f), new Vector3(1.6f, 0.35f, 0.35f));

            var hp = _ctx.Tuning.bossTurretHp;
            return AddPart(new Enemy
            {
                Kind = EnemyKind.BossPart, Root = root, Barrel = barrel, Label = label, Hp = hp, MaxHp = hp,
                IsBox = true, HalfSize = new Vector2(1.2f, 0.9f), CenterOffset = new Vector2(0f, 0.9f),
            });
        }

        Enemy CreateCore()
        {
            var root = CreateRoot("코어", 0f);
            Shapes.Create(PrimitiveType.Sphere, CoreShieldColor, root, new Vector3(0f, 0.6f, 0f), new Vector3(3.6f, 3f, 3f));

            var hp = _ctx.Tuning.bossCoreHp;
            return AddPart(new Enemy
            {
                Kind = EnemyKind.BossPart, Root = root, Label = "코어", Hp = hp, MaxHp = hp,
                Radius = 1.6f, CenterOffset = new Vector2(0f, 0.8f), Invulnerable = true,
            });
        }

        Transform CreateRoot(string name, float offsetX)
        {
            var root = new GameObject(name).transform;
            root.SetParent(_ctx.World.ScrollX, false);
            root.localPosition = new Vector3(CenterX + offsetX, BodyHeight, 0f);
            return root;
        }

        Enemy AddPart(Enemy e)
        {
            e.Persistent = true;
            _ctx.Enemies.AddExternal(e);
            _parts.Add(e);
            return e;
        }

        public void Tick(float dt)
        {
            if (!Active || Defeated || dt <= 0f) return;

            if (!_core.Alive)
            {
                Defeated = true;
                // 코어 루트는 EnemySystem이 이미 제거했을 수 있으므로 지면 좌표에서 위치를 구한다
                var c = (Vector2)_ctx.World.ScrollX.TransformPoint(new Vector3(CenterX, BodyHeight, 0f));
                for (var i = 0; i < 5; i++)
                    _ctx.Effects.Explode(c + new Vector2(_ctx.Range(-7f, 7f), _ctx.Range(-5f, 2f)), _ctx.Range(4f, 8f), _ctx.World.ScrollX);
                return;
            }

            var turrets = (_left.Alive ? 1 : 0) + (_right.Alive ? 1 : 0);
            if (turrets == 0 && !_coreOpen)
            {
                _coreOpen = true;
                _core.Invulnerable = false;
                _core.SetBaseMaterial(Shapes.Material(CoreOpenColor, true));
            }

            if (_ctx.Player.Down || !InFireRange()) return;

            var t = _ctx.Tuning;
            var turretInterval = t.bossFireInterval * (turrets == 1 ? 0.6f : 1f);
            var fanCount = turrets == 1 ? 5 : 3;
            TickTurret(_left, ref _leftTimer, turretInterval, fanCount, dt);
            TickTurret(_right, ref _rightTimer, turretInterval, fanCount, dt);

            if (_coreOpen && (_coreTimer -= dt) <= 0f)
            {
                _coreTimer = t.bossFireInterval * 1.25f;
                var origin = _core.Center;
                var speed = t.enemyBulletSpeed * 0.8f;
                for (var i = 0; i < RingCount; i++)
                {
                    var a = (i + 0.5f) * Mathf.PI * 2f / RingCount;
                    _ctx.Enemies.FireBullet(origin, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * speed);
                }
                Fan(origin, _ctx.Enemies.LeadDirection(origin, Vector2.zero), 3);
            }
        }

        bool InFireRange()
        {
            var view = _ctx.Camera.View;
            var x = CenterX - _ctx.Scroll.Distance; // 보스 중심의 화면 x
            return x > view.xMin - FireRange && x < view.xMax + FireRange;
        }

        void TickTurret(Enemy turret, ref float timer, float interval, int fanCount, float dt)
        {
            if (!turret.Alive) return;
            _ctx.Enemies.AimBarrel(turret);
            if ((timer -= dt) > 0f) return;
            timer = interval;
            var dir = (Vector2)turret.Barrel.right;
            Fan((Vector2)turret.Barrel.position + dir * 1.6f, dir, fanCount);
        }

        void Fan(Vector2 origin, Vector2 dir, int count)
        {
            var baseAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            var speed = _ctx.Tuning.enemyBulletSpeed;
            for (var i = 0; i < count; i++)
            {
                var a = (baseAngle + (i - (count - 1) * 0.5f) * FanSpread) * Mathf.Deg2Rad;
                _ctx.Enemies.FireBullet(origin, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * speed);
            }
        }
    }
}
