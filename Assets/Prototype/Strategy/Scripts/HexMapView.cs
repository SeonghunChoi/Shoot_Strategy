using System.Collections.Generic;
using Prototype.Strategy.Rules;
using UnityEngine;

namespace Prototype.Strategy
{
    /// <summary>
    /// 지형 메시와 영역·커서·선택 표시. 지형은 한 번 굽고, 나머지 층은 매 프레임 다시 만든다 (칸 수가 적어 충분히 싸다).
    /// 글자(거점 이름, 시설 약자)는 StrategyHud가 IMGUI로 그린다.
    /// </summary>
    public sealed class HexMapView
    {
        const float CellScale = 0.94f; // 칸 사이 틈이 격자선 역할

        public static Color TerrainColor(TerrainType t) => t switch
        {
            TerrainType.Plain => new Color(0.55f, 0.72f, 0.42f),
            TerrainType.Mountain => new Color(0.52f, 0.42f, 0.33f),
            TerrainType.Sea => new Color(0.2f, 0.38f, 0.62f),
            TerrainType.Coast => new Color(0.55f, 0.74f, 0.72f),
            TerrainType.Desert => new Color(0.88f, 0.77f, 0.5f),
            _ => Color.magenta,
        };

        public static readonly Color OilColor = new(0.12f, 0.1f, 0.1f);
        public static readonly Color PlayerColor = new(0.25f, 0.6f, 1f);

        readonly HexMap _map;
        readonly HexMesh _terrain;
        readonly HexMesh _territory;
        readonly HexMesh _overlay;
        readonly HexMesh _markers;
        readonly HexMesh _prediction;
        readonly HexMesh _playback;

        public HexMapView(HexMap map, Transform parent)
        {
            _map = map;
            _terrain = new HexMesh("Terrain", parent, 0f, 0);
            _territory = new HexMesh("Territory", parent, -0.05f, 1);
            _overlay = new HexMesh("Overlay", parent, -0.1f, 2);
            _markers = new HexMesh("Markers", parent, -0.2f, 3);
            _prediction = new HexMesh("Prediction", parent, -0.15f, 2);
            _playback = new HexMesh("Playback", parent, -0.18f, 3);
            BuildTerrain();
        }

        void BuildTerrain()
        {
            _terrain.Clear();
            foreach (var h in _map.AllHexes())
            {
                var cell = _map[h];
                _terrain.AddFilled(h, CellScale, TerrainColor(cell.Terrain));
                if (cell.Oil) _terrain.AddFilled(h, 0.28f, OilColor);
            }
            _terrain.Apply();
        }

        public static readonly Color EnemyColor = new(0.9f, 0.3f, 0.25f);
        public static readonly Color PlanColor = new(1f, 0.6f, 0.15f);

        public static readonly Color Faction2Color = new(0.75f, 0.4f, 0.95f);
        public static readonly Color GhostColor = new(0.6f, 0.6f, 0.6f, 0.8f);
        public static readonly Color GroundColor = new(0.45f, 0.95f, 0.35f);

        public static Color OwnerColor(int owner) => owner switch
        {
            World.Player => PlayerColor,
            2 => Faction2Color,
            _ => EnemyColor,
        };

        /// <summary>영역 색·경계, 거점 본부, 활주로 선, 적 부대(보이는 것만)와 마지막 목격 유령.</summary>
        public void UpdateTerritory(World world, Balance bal, HashSet<Hex> visible, IEnumerable<Sighting> ghosts, MovePlayback playback)
        {
            _territory.Clear();
            foreach (var (h, baseId) in world.Territory)
            {
                var b = world.BaseById(baseId);
                var color = OwnerColor(b.Owner);
                _territory.AddFilled(h, CellScale, new Color(color.r, color.g, color.b, 0.22f));
                for (var dir = 0; dir < 6; dir++)
                {
                    var n = h.Neighbor(dir);
                    var other = world.Territory.TryGetValue(n, out var nid) ? nid : -1;
                    if (other == baseId) continue;
                    // 같은 세력의 다른 거점과 맞닿은 변은 가늘게
                    var sameOwner = other >= 0 && world.BaseById(other).Owner == b.Owner;
                    _territory.AddEdge(h, dir, sameOwner ? 0.04f : 0.1f, new Color(color.r, color.g, color.b, sameOwner ? 0.5f : 1f));
                }
            }

            foreach (var b in world.Bases)
            {
                _territory.AddFilled(b.Hex, 0.62f, new Color(0.1f, 0.12f, 0.16f, 0.9f));
                _territory.AddOutline(b.Hex, 0.62f, 0.52f, OwnerColor(b.Owner));
            }

            // 자원 타일: 종류 색 작은 헥스 + 소유 세력 테두리 (중립은 흰색, 폐허는 어둡게)
            foreach (var s in world.Sites)
            {
                var fill = s.Type switch
                {
                    SiteType.Refinery => new Color(0.12f, 0.1f, 0.08f, 0.95f),
                    SiteType.Mine => new Color(0.38f, 0.27f, 0.18f, 0.95f),
                    _ => new Color(0.32f, 0.32f, 0.4f, 0.95f),
                };
                if (s.Grade == 0) fill = new Color(0.18f, 0.18f, 0.18f, 0.7f);
                var edge = s.Owner == ResourceSite.Neutral ? new Color(0.9f, 0.9f, 0.9f, 0.9f) : OwnerColor(s.Owner);
                _territory.AddFilled(s.Hex, 0.5f, fill);
                _territory.AddOutline(s.Hex, 0.52f, 0.4f, edge);
            }
            // 시야 밖은 어둡게
            foreach (var h in _map.AllHexes())
                if (!visible.Contains(h)) _territory.AddFilled(h, CellScale, new Color(0f, 0f, 0f, 0.35f));

            // 적 부대 (보이는 것만): 세력 색 작은 헥스 + 테두리, 이번 턴 목표 방향 선
            foreach (var u in world.EnemyUnits)
            {
                if (!visible.Contains(u.Hex) || playback.Hides(TraceKind.Enemy, u.Id)) continue;
                var color = OwnerColor(u.Owner);
                _territory.AddFilled(u.Hex, 0.55f, new Color(color.r * 0.4f, color.g * 0.3f, color.b * 0.3f, 0.95f));
                _territory.AddOutline(u.Hex, 0.55f, 0.45f, color);
                if (u.Goal is { } goal && goal != u.Hex)
                {
                    var from = HexLayout.ToWorld(u.Hex);
                    var dir = (HexLayout.ToWorld(goal) - from).normalized;
                    _territory.AddSegment(from + dir * 0.55f, from + dir * 1.1f, 0.1f, new Color(color.r, color.g, color.b, 0.8f));
                }
            }

            // 마지막 목격 유령 (회색)
            foreach (var g in ghosts)
            {
                _territory.AddFilled(g.Hex, 0.5f, new Color(0.2f, 0.2f, 0.2f, 0.7f));
                _territory.AddOutline(g.Hex, 0.5f, 0.42f, GhostColor);
            }

            // 출격 계획: 작전 지역(목표 + 인접 6칸) 바깥 경계를 주황 테두리로, 목표 칸은 진하게
            foreach (var plan in world.SortiePlan)
            {
                var area = new HashSet<Hex>(SortieRules.OperationArea(plan.Target));
                foreach (var h in area)
                {
                    if (!_map.Contains(h)) continue;
                    _territory.AddFilled(h, CellScale, new Color(PlanColor.r, PlanColor.g, PlanColor.b, 0.12f));
                    for (var dir = 0; dir < 6; dir++)
                        if (!area.Contains(h.Neighbor(dir))) _territory.AddEdge(h, dir, 0.1f, PlanColor);
                }
                _territory.AddOutline(plan.Target, 0.9f, 0.78f, PlanColor);
            }

            // 출동 중 아군 지상군: 녹색 작은 헥스 + 목표 방향 선
            foreach (var g in world.GroundUnits)
            {
                if (!g.Deployed || playback.Hides(TraceKind.Ground, g.Id)) continue;
                _territory.AddFilled(g.Hex, 0.45f, new Color(0.12f, 0.3f, 0.1f, 0.95f));
                _territory.AddOutline(g.Hex, 0.45f, 0.35f, GroundColor);
                if (g.Target is { } t && t != g.Hex)
                    _territory.AddSegment(HexLayout.ToWorld(g.Hex), HexLayout.ToWorld(t), 0.08f, new Color(GroundColor.r, GroundColor.g, GroundColor.b, 0.6f));
            }

            // 비행 중 편대 표식 (재생으로 아직 도착하지 않았으면 재생 쪽에서 그린다)
            foreach (var s in world.Squadrons)
            {
                if (s.AirHex is not { } ah || playback.Hides(TraceKind.Squadron, s.Id)) continue;
                DrawSquadronToken(_territory, HexLayout.ToWorld(ah));
            }

            _territory.Apply();
        }

        static readonly Color SquadronFill = new(0.06f, 0.16f, 0.32f, 0.95f);

        static void DrawSquadronToken(HexMesh mesh, Vector2 pos)
        {
            mesh.AddFilledAt(pos, 0.4f, SquadronFill);
            mesh.AddOutlineAt(pos, 0.46f, 0.36f, PlayerColor);
            mesh.AddFilledAt(pos, 0.12f, Color.white);
        }

        /// <summary>이동 재생 표식: 움직이는 편대·적 부대·지상군, 교전 표시(퍼지는 붉은 고리).</summary>
        public void UpdatePlayback(MovePlayback playback, Balance bal)
        {
            _playback.Clear();
            if (playback.Playing)
            {
                foreach (var t in playback.Tokens())
                {
                    var trace = t.Trace;
                    switch (trace.Kind)
                    {
                        case TraceKind.Squadron:
                            if (t.Current) _playback.AddSegment(HexLayout.ToWorld(trace.Path[0]), t.Position, 0.06f, new Color(PlayerColor.r, PlayerColor.g, PlayerColor.b, 0.6f));
                            DrawSquadronToken(_playback, t.Position);
                            break;
                        case TraceKind.Enemy:
                        {
                            var color = OwnerColor(trace.Owner);
                            _playback.AddFilledAt(t.Position, 0.55f, new Color(color.r * 0.4f, color.g * 0.3f, color.b * 0.3f, 0.95f));
                            _playback.AddOutlineAt(t.Position, 0.55f, 0.45f, color);
                            break;
                        }
                        case TraceKind.Ground:
                            _playback.AddFilledAt(t.Position, 0.45f, new Color(0.12f, 0.3f, 0.1f, 0.95f));
                            _playback.AddOutlineAt(t.Position, 0.45f, 0.35f, GroundColor);
                            break;
                        case TraceKind.Event:
                        {
                            var p = Mathf.Clamp01(t.EventProgress);
                            var outer = 0.5f + 0.6f * p;
                            _playback.AddOutlineAt(t.Position, outer, outer - 0.14f, new Color(1f, 0.35f, 0.2f, 1f - p * 0.8f));
                            break;
                        }
                    }
                }
            }
            _playback.Apply();
        }

        /// <summary>
        /// 강조 칸(출격·출동 후보 등)과 커서·선택 테두리.
        /// 강조가 없으면 선택 칸→커서 칸 직선을 보여 준다 (헥스 수학 확인용).
        /// </summary>
        /// <summary>
        /// 고른 적 부대의 다음 턴 예상: 이번 턴에 지나갈 칸(진한 선 + 칸 강조), 도착 칸 테두리, 그 뒤 남은 경로(옅은 선), 공격 예상 거점(빨간 테두리).
        /// </summary>
        public void UpdatePrediction(EnemyUnit unit, EnemyTurn.Prediction p)
        {
            _prediction.Clear();
            if (unit != null && p != null)
            {
                var color = OwnerColor(unit.Owner);
                var prev = HexLayout.ToWorld(unit.Hex);
                foreach (var h in p.ThisTurn)
                {
                    _prediction.AddFilled(h, CellScale, new Color(color.r, color.g, color.b, 0.28f));
                    var c = HexLayout.ToWorld(h);
                    _prediction.AddSegment(prev, c, 0.16f, new Color(color.r, color.g, color.b, 0.95f));
                    prev = c;
                }
                if (p.ThisTurn.Count > 0) _prediction.AddOutline(p.ThisTurn[^1], 0.98f, 0.8f, color);
                foreach (var h in p.Later)
                {
                    var c = HexLayout.ToWorld(h);
                    _prediction.AddSegment(prev, c, 0.07f, new Color(color.r, color.g, color.b, 0.45f));
                    prev = c;
                }
                if (p.AttackTarget != null) _prediction.AddOutline(p.AttackTarget.Hex, 1.0f, 0.75f, new Color(1f, 0.2f, 0.15f));
                if (p.EngageGround != null) _prediction.AddOutline(p.EngageGround.Hex, 1.0f, 0.75f, new Color(1f, 0.2f, 0.15f));
            }
            _prediction.Apply();
        }

        public void UpdateMarkers(Hex? hover, Hex? selected, IReadOnlyCollection<Hex> highlight, Color highlightColor)
        {
            _overlay.Clear();
            if (highlight != null && highlight.Count > 0)
            {
                var set = new HashSet<Hex>(highlight);
                var edge = new Color(highlightColor.r, highlightColor.g, highlightColor.b, 1f);
                foreach (var h in highlight)
                {
                    _overlay.AddFilled(h, CellScale, highlightColor);
                    // 바깥 경계 변만 굵게 그려 영역이 한눈에 보이게
                    for (var dir = 0; dir < 6; dir++)
                        if (!set.Contains(h.Neighbor(dir))) _overlay.AddEdge(h, dir, 0.12f, edge);
                }
            }
            else if (hover is { } hv && selected is { } sel && hv != sel && _map.Contains(hv))
            {
                var straight = Hex.StraightDirection(sel, hv) >= 0;
                var lineColor = straight ? new Color(1f, 0.85f, 0.2f, 0.45f) : new Color(1f, 1f, 1f, 0.3f);
                foreach (var h in Hex.Line(sel, hv))
                    if (_map.Contains(h)) _overlay.AddFilled(h, CellScale, lineColor);
            }
            _overlay.Apply();

            _markers.Clear();
            if (selected is { } s) _markers.AddOutline(s, 1.0f, 0.8f, new Color(1f, 0.85f, 0.2f));
            if (hover is { } h2 && _map.Contains(h2)) _markers.AddOutline(h2, 0.98f, 0.86f, new Color(1f, 1f, 1f, 0.9f));
            _markers.Apply();
        }
    }
}
