using Prototype.Strategy.Rules;
using UnityEngine;

namespace Prototype.Strategy
{
    /// <summary>지형 메시와 커서·선택 표시. 지형은 한 번 굽고, 표시 층만 매 프레임 다시 만든다.</summary>
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

        readonly HexMap _map;
        readonly HexMesh _terrain;
        readonly HexMesh _overlay;
        readonly HexMesh _markers;

        public HexMapView(HexMap map, Transform parent)
        {
            _map = map;
            _terrain = new HexMesh("Terrain", parent, 0f, 0);
            _overlay = new HexMesh("Overlay", parent, -0.1f, 1);
            _markers = new HexMesh("Markers", parent, -0.2f, 2);
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

        /// <summary>선택 칸에서 가리킨 칸까지의 직선(헥스 수학 확인용)과 커서·선택 테두리.</summary>
        public void UpdateMarkers(Hex? hover, Hex? selected)
        {
            _overlay.Clear();
            if (hover is { } hv && selected is { } sel && hv != sel && _map.Contains(hv))
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
