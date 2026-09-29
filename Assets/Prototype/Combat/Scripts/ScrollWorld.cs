using System;
using System.Collections.Generic;
using UnityEngine;

namespace Prototype.Combat
{
    /// <summary>
    /// ScrollX 루트와 근경(평지 줄무늬·거리 표식·천장선). 줄무늬와 표식은 화면 밖으로 나가면 반대편으로 재배치한다.
    /// 스테이지 지형(Terrain)이 있으면 평지 줄무늬는 숨기고 표식만 지면 높이를 따라간다.
    /// </summary>
    public sealed class ScrollWorld
    {
        const float TileWidth = 4f;
        const float MarkerSpacing = 20f;
        const float Margin = 12f;

        readonly Transform _fixedRoot;
        readonly Transform _groundBase;
        readonly Transform _ceiling;
        readonly Transform _tileRoot;
        readonly List<Transform> _tiles = new();
        readonly List<(Transform pole, TextMesh label)> _markers = new();

        public Transform ScrollX { get; }

        public ScrollWorld(float maxHalfWidth)
        {
            ScrollX = new GameObject("ScrollX").transform;
            _fixedRoot = new GameObject("FixedWorld").transform;
            _tileRoot = new GameObject("FlatTiles").transform;
            _tileRoot.SetParent(ScrollX, false);

            var span = (maxHalfWidth + Margin) * 2f;
            _groundBase = Shapes.Create(PrimitiveType.Cube, new Color(0.32f, 0.3f, 0.24f), _fixedRoot,
                new Vector3(0f, -5f, 1f), new Vector3(span, 10f, 4f), name: "GroundBase");
            _ceiling = Shapes.Create(PrimitiveType.Cube, new Color(1f, 0.4f, 0.3f), _fixedRoot,
                Vector3.zero, new Vector3(span, 0.1f, 0.1f), lit: false, name: "CeilingLine");

            var tileCount = Mathf.CeilToInt(span / TileWidth);
            if (tileCount % 2 == 1) tileCount++; // 재배치 후에도 줄무늬 색 교대 유지
            var tileColors = new[] { new Color(0.45f, 0.55f, 0.3f), new Color(0.38f, 0.48f, 0.26f) };
            for (var i = 0; i < tileCount; i++)
            {
                _tiles.Add(Shapes.Create(PrimitiveType.Cube, tileColors[i % 2], _tileRoot,
                    new Vector3((i + 0.5f) * TileWidth - span * 0.5f, -0.25f, 0f), new Vector3(TileWidth, 0.5f, 3f)));
            }

            var markerCount = Mathf.CeilToInt(span / MarkerSpacing) + 1;
            for (var i = 0; i < markerCount; i++)
            {
                var pole = Shapes.Create(PrimitiveType.Cube, new Color(0.9f, 0.9f, 0.85f), ScrollX,
                    new Vector3(i * MarkerSpacing, 1.5f, 1f), new Vector3(0.15f, 3f, 0.15f), lit: false);
                var label = Shapes.Label(ScrollX, new Vector3(i * MarkerSpacing, 3.2f, 1f), 0.12f, Color.white);
                _markers.Add((pole, label));
            }
        }

        /// <summary>스테이지 지형이 있으면 평지 줄무늬와 바닥판을 숨긴다.</summary>
        public void SetFlatGroundVisible(bool visible)
        {
            _tileRoot.gameObject.SetActive(visible);
            _groundBase.gameObject.SetActive(visible);
        }

        public void Tick(float distance, float mapHeight, Func<float, float> floorAt)
        {
            ScrollX.position = new Vector3(-distance, 0f, 0f);
            _ceiling.position = new Vector3(0f, mapHeight, 0f);

            var span = _tiles.Count * TileWidth;
            foreach (var tile in _tiles) Wrap(tile, distance, span);

            var markerSpan = _markers.Count * MarkerSpacing;
            foreach (var (pole, label) in _markers)
            {
                var moved = Wrap(pole, distance, markerSpan);
                var x = pole.localPosition.x;
                var floor = floorAt(x);
                pole.localPosition = new Vector3(x, floor + 1.5f, 1f);
                label.transform.localPosition = new Vector3(x, floor + 3.2f, 1f);
                if (moved || label.text.Length == 0) label.text = $"{Mathf.RoundToInt(x)}m";
            }
        }

        /// <summary>화면 기준 [-span/2, span/2] 밖이면 span 단위로 옮긴다. 옮겼으면 true.</summary>
        static bool Wrap(Transform t, float distance, float span)
        {
            var p = t.localPosition;
            var screenX = p.x - distance;
            var limit = span * 0.5f;
            var moved = false;
            while (screenX < -limit) { p.x += span; screenX += span; moved = true; }
            while (screenX > limit) { p.x -= span; screenX -= span; moved = true; }
            if (moved) t.localPosition = p;
            return moved;
        }
    }
}
