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
        // 기체 판정점: 화면 좌표 오프셋과 반지름 (기수·중앙·꼬리)
        static readonly (Vector2 offset, float radius)[] ProbePoints =
        {
            (new Vector2(1.2f, 0f), 0.3f),
            (Vector2.zero, PlayerShip.HitRadius),
            (new Vector2(-1f, 0.15f), 0.3f),
        };

        const float WallThreshold = 0.3f; // 기수가 중앙보다 이만큼 더 박히면 정면 충돌로 보고 뒤로 튕긴다

        readonly CombatContext _ctx;
        List<StageSection> _sections;
        bool _inContact;

        public StageTerrain Terrain { get; } = new();
        public bool Active => _sections != null;
        public StageSection Current { get; private set; }
        public float Progress { get; private set; }
        public bool Cleared { get; private set; }

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
            var turnZone = _sections.Find(s => s.IsTurnZone);
            var goal = turnZone?.Start ?? _sections[^1].End;
            Progress = Mathf.Clamp01(x / goal);
            if (x >= goal) Cleared = true; // 선회 구역·보스는 6단계
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
                var (offset, r) = ProbePoints[i];
                var p = pos + offset;
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
                    _ctx.Effects.Explode(pos + new Vector2(0.6f, floorPen >= ceilPen ? -0.4f : 0.4f), 1.2f);
                    if (player.Damage(_ctx.Tuning.terrainDamage))
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
