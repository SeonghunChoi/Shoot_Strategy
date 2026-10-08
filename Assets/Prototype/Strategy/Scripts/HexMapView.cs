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
        static readonly Color RunwayColor = new(0.15f, 0.15f, 0.17f, 0.9f);

        readonly HexMap _map;
        readonly HexMesh _terrain;
        readonly HexMesh _territory;
        readonly HexMesh _overlay;
        readonly HexMesh _markers;

        public HexMapView(HexMap map, Transform parent)
        {
            _map = map;
            _terrain = new HexMesh("Terrain", parent, 0f, 0);
            _territory = new HexMesh("Territory", parent, -0.05f, 1);
            _overlay = new HexMesh("Overlay", parent, -0.1f, 2);
            _markers = new HexMesh("Markers", parent, -0.2f, 3);
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

        public static Color OwnerColor(int owner) => owner switch
        {
            World.Player => PlayerColor,
            2 => Faction2Color,
            _ => EnemyColor,
        };

        /// <summary>영역 색·경계, 거점 본부, 활주로 선, 적 부대(보이는 것만)와 마지막 목격 유령.</summary>
        public void UpdateTerritory(World world, HashSet<Hex> visible, IEnumerable<Sighting> ghosts)
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

            // 활주로: 칸 중앙에 작은 표식, 같은 거점의 이웃 활주로와는 선으로 잇는다
            foreach (var (h, f) in world.Facilities)
            {
                if (f.Type != FacilityType.Runway) continue;
                var c = HexLayout.ToWorld(h);
                _territory.AddFilled(h, 0.22f, RunwayColor);
                for (var dir = 0; dir < 3; dir++)
                {
                    var n = h.Neighbor(dir);
                    if (world.Facilities.TryGetValue(n, out var nf) && nf.Type == FacilityType.Runway &&
                        world.Territory.TryGetValue(n, out var a) && world.Territory.TryGetValue(h, out var bId) && a == bId)
                        _territory.AddSegment(c, HexLayout.ToWorld(n), 0.3f, RunwayColor);
                }
            }
            // 시야 밖은 어둡게
            foreach (var h in _map.AllHexes())
                if (!visible.Contains(h)) _territory.AddFilled(h, CellScale, new Color(0f, 0f, 0f, 0.35f));

            // 적 부대 (보이는 것만): 세력 색 작은 헥스 + 테두리, 이번 턴 목표 방향 선
            foreach (var u in world.EnemyUnits)
            {
                if (!visible.Contains(u.Hex)) continue;
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

            _territory.Apply();
        }

        /// <summary>
        /// 강조 칸(영역 확장 후보 등)과 커서·선택 테두리.
        /// 강조가 없으면 선택 칸→커서 칸 직선을 보여 준다 (헥스 수학 확인용).
        /// </summary>
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
