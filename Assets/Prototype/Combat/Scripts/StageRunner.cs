using System.Collections.Generic;
using UnityEngine;

namespace Prototype.Combat
{
    /// <summary>
    /// 스테이지 진행(현재 구간·진행률·종료)과 지형 판정(기체 충돌·튕김, 탄·미사일 소멸).
    /// 스테이지를 끄면 지형은 평면이 되고 적은 튜닝 값의 간격으로 등장한다.
    /// </summary>
    public sealed class StageRunner
    {
        // 기체 판정점: 화면 좌표 오프셋과 반지름 (기수·중앙·꼬리), 기체 크기 배율을 곱한다
        static readonly (Vector2 offset, float radius)[] ProbePoints =
        {
            (new Vector2(1.2f, 0f), 0.3f),
            (Vector2.zero, 0.5f),
            (new Vector2(-1f, 0.15f), 0.3f),
        };

        const float TurnMargin = 4f;      // 선회 구역 끝에서 이만큼 앞에서 선회 시작
        const float WallThreshold = 0.3f; // 기수가 중앙보다 이만큼 더 박히면 정면 충돌로 보고 뒤로 튕긴다

        readonly CombatContext _ctx;
        List<StageSection> _sections;
        bool _inContact;

        public StageTerrain Terrain { get; } = new();
        public bool Active => _sections != null;
        public StageSection Current { get; private set; }
        public float Progress { get; private set; }
        public bool Cleared { get; private set; }
        public StageSection TurnZone { get; private set; }
        public bool InTurnZone => TurnZone != null && PlayerGroundX >= TurnZone.Start && PlayerGroundX <= TurnZone.End;

        public StageRunner(CombatContext ctx) => _ctx = ctx;

        /// <summary>플레이어 기체의 지면 좌표 x.</summary>
        public float PlayerGroundX => _ctx.Player.Position.x + _ctx.Scroll.Distance;

        public void Build(bool enabled, float mapHeight)
        {
            _sections = enabled ? StageDefinition.CreateTestStage() : null;
            if (Active) Terrain.Build(_sections, mapHeight, _ctx.World.ScrollX);
            else Terrain.Clear(mapHeight);

            _inContact = false;
            Cleared = false;
            Current = Active ? _sections[0] : null;
            TurnZone = Active ? _sections.Find(s => s.IsTurnZone) : null;
            Progress = 0f;
        }

        public StageSection SectionAt(float groundX)
        {
            if (!Active) return null;
            foreach (var s in _sections)
                if (groundX < s.End) return s;
            return _sections[^1];
        }

        public void Tick()
        {
            if (!Active) return;
            var x = PlayerGroundX;
            Current = SectionAt(x);
            var goal = TurnZone?.End ?? _sections[^1].End;
            Progress = Mathf.Clamp01(x / goal);
            if (Cleared) return;
            if (TurnZone == null)
            {
                if (x >= goal) Cleared = true;
                return;
            }

            // 선회 구역: 보스 격파 전에는 양 끝에서 자동 선회, 격파 후 오른쪽 끝 = 스테이지 종료
            var scroll = _ctx.Scroll;
            if (scroll.Turning || _ctx.Player.Down) return;
            var bossDown = _ctx.Boss.Defeated;
            if (scroll.Direction > 0 && x >= TurnZone.End - TurnMargin)
            {
                if (bossDown) Cleared = true;
                else StartAutoTurn();
            }
            else if (scroll.Direction < 0 && x <= TurnZone.Start + TurnMargin && x > TurnZone.Start - 20f)
            {
                StartAutoTurn(); // 구역 밖에서 임의 선회로 되돌아가는 중에는 발동하지 않음
            }
        }

        void StartAutoTurn()
        {
            _ctx.Scroll.StartTurn();
            _ctx.Log.Turn(manual: false);
        }

        /// <summary>기체가 지형에 박히면 밖으로 밀어내고 튕기며, 접촉을 시작할 때 피해를 준다.</summary>
        public void ResolvePlayer()
        {
            var player = _ctx.Player;
            if (player.Down)
            {
                _inContact = false;
                return;
            }

            var distance = _ctx.Scroll.Distance;
            var pos = player.Position;
            float floorPen = 0f, ceilPen = 0f, nosePen = 0f, centerPen = 0f;
            for (var i = 0; i < ProbePoints.Length; i++)
            {
                var (offset, radius) = ProbePoints[i];
                var p = pos + new Vector2(offset.x * player.Facing, offset.y) * player.ModelScale;
                var r = radius * player.Member.Def.HitScale;
                var gx = p.x + distance;
                var f = Terrain.Floor(gx) - (p.y - r);
                var c = p.y + r - Terrain.Ceil(gx);
                floorPen = Mathf.Max(floorPen, f);
                ceilPen = Mathf.Max(ceilPen, c);
                if (i == 0) nosePen = Mathf.Max(f, c);
                if (i == 1) centerPen = Mathf.Max(f, c);
            }

            var touching = floorPen > 0f || ceilPen > 0f;
            if (touching)
            {
                var bounce = _ctx.Tuning.terrainBounce;
                if (floorPen >= ceilPen) player.Push(floorPen, bounce);
                else player.Push(-ceilPen, -bounce);
                if (nosePen > centerPen + WallThreshold) _ctx.Scroll.Knockback(bounce);

                if (!_inContact)
                {
                    _ctx.TerrainHits++;
                    _ctx.Log.TerrainHit(Current?.Name ?? "무한");
                    _ctx.Effects.Explode(pos + new Vector2(0.6f * player.Facing, floorPen >= ceilPen ? -0.4f : 0.4f), 1.2f);
                    var canHit = player.CanBeHit;
                    var lost = player.Damage(_ctx.Tuning.terrainDamage);
                    if (canHit) _ctx.Log.PlayerHit("지형", _ctx.Tuning.terrainDamage, lost);
                    if (lost)
                    {
                        _ctx.PlayerDowns++;
                        _ctx.Effects.Explode(pos, 4f);
                    }
                }
            }
            _inContact = touching;
        }

        /// <summary>지형 안으로 들어간 기관포 탄·적 탄 제거.</summary>
        public void ResolveBullets()
        {
            if (!Active) return;
            RemoveInside(_ctx.PlayerBullets);
            RemoveInside(_ctx.EnemyBullets);
        }

        void RemoveInside(BulletPool pool)
        {
            var distance = _ctx.Scroll.Distance;
            for (var i = pool.Count - 1; i >= 0; i--)
            {
                var p = pool.WorldPos(i);
                if (Terrain.IsSolid(p.x + distance, p.y)) pool.Kill(i);
            }
        }
    }
}
