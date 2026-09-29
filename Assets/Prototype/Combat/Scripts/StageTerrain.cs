using System.Collections.Generic;
using UnityEngine;

namespace Prototype.Combat
{
    /// <summary>
    /// 지면 좌표 x에 따른 바닥·천장 높이 곡선. 판정과 렌더링이 같은 곡선을 쓴다 (물리 엔진 없음).
    /// 스테이지가 없으면 바닥 0, 천장 = 맵 높이의 평면.
    /// </summary>
    public sealed class StageTerrain
    {
        const float Step = 2f;
        const float StartX = -120f;  // 시작 화면 + 모드 A 뒤로 오프셋까지 덮는 여유
        const float EndMargin = 150f;
        const float MinGap = 8f;     // 바닥과 천장 사이 최소 통로 높이
        const float FadeLength = 30f;
        const float MeshZ = 2f;      // 기체·적보다 뒤

        struct Bump
        {
            public float Center;
            public float HalfWidth;
            public float Height;
        }

        float[] _floor;
        float[] _ceil;
        float _mapHeight;
        GameObject _meshObject;

        public bool Active => _floor != null;

        public float Floor(float x) => Active ? Sample(_floor, x) : 0f;
        public float Ceil(float x) => Active ? Sample(_ceil, x) : _mapHeight;
        public bool IsSolid(float x, float y) => y < Floor(x) || y > Ceil(x);

        public void Clear(float mapHeight)
        {
            _mapHeight = mapHeight;
            _floor = null;
            _ceil = null;
            if (_meshObject != null) Object.Destroy(_meshObject);
        }

        public void Build(List<StageSection> sections, float mapHeight, Transform scrollX)
        {
            Clear(mapHeight);
            var rng = new System.Random(7);
            var peaks = new List<Bump>();
            var overhangs = new List<Bump>();

            foreach (var s in sections)
            {
                if (s.Style != TerrainStyle.Mountains) continue;
                for (var x = s.Start + 40f; x < s.End - 40f; x += Range(rng, 35f, 70f))
                {
                    peaks.Add(new Bump { Center = x, HalfWidth = Range(rng, 15f, 30f), Height = Range(rng, 0.2f, 0.6f) * mapHeight });
                    if (rng.NextDouble() < 0.5)
                        overhangs.Add(new Bump { Center = x + Range(rng, 15f, 30f), HalfWidth = Range(rng, 10f, 20f), Height = Range(rng, 0.15f, 0.35f) * mapHeight });
                }
            }

            var end = sections[^1].End + EndMargin;
            var count = Mathf.CeilToInt((end - StartX) / Step) + 1;
            _floor = new float[count];
            _ceil = new float[count];

            for (var i = 0; i < count; i++)
            {
                var x = StartX + i * Step;
                var section = SectionAt(sections, x);
                var hills = Hills(x);

                var floor = section.Style switch
                {
                    TerrainStyle.Flat => hills * Mathf.Clamp01(1f - (x - section.Start) / FadeLength),
                    TerrainStyle.Mountains => Mathf.Max(hills, BumpHeight(peaks, x)),
                    _ => hills,
                };
                var ceil = mapHeight - BumpHeight(overhangs, x);

                floor = Mathf.Min(floor, mapHeight - MinGap);
                ceil = Mathf.Clamp(ceil, floor + MinGap, mapHeight);
                _floor[i] = floor;
                _ceil[i] = ceil;
            }

            BuildMesh(sections, scrollX);
        }

        static StageSection SectionAt(List<StageSection> sections, float x)
        {
            foreach (var s in sections)
                if (x < s.End) return s;
            return sections[^1];
        }

        static float Hills(float x) =>
            Mathf.Max(0f, 1.2f + 0.9f * Mathf.Sin(x * 0.05f) + 0.6f * Mathf.Sin(x * 0.13f + 1f));

        static float BumpHeight(List<Bump> bumps, float x)
        {
            var h = 0f;
            foreach (var b in bumps)
            {
                var d = Mathf.Abs(x - b.Center);
                if (d < b.HalfWidth) h = Mathf.Max(h, b.Height * (1f - d / b.HalfWidth));
            }
            return h;
        }

        float Sample(float[] values, float x)
        {
            var f = (x - StartX) / Step;
            if (f <= 0f) return values[0];
            var i = (int)f;
            if (i >= values.Length - 1) return values[^1];
            return Mathf.Lerp(values[i], values[i + 1], f - i);
        }

        void BuildMesh(List<StageSection> sections, Transform scrollX)
        {
            var count = _floor.Length;
            var vertices = new Vector3[count * 4];
            for (var i = 0; i < count; i++)
            {
                var x = StartX + i * Step;
                vertices[i * 4 + 0] = new Vector3(x, _floor[i], 0f);
                vertices[i * 4 + 1] = new Vector3(x, -30f, 0f);
                vertices[i * 4 + 2] = new Vector3(x, _ceil[i], 0f);
                vertices[i * 4 + 3] = new Vector3(x, _mapHeight + 30f, 0f);
            }

            // 구간별 바닥·천장 서브메시 (천장은 어둡게)
            var floorTris = new List<int>[sections.Count];
            var ceilTris = new List<int>[sections.Count];
            for (var s = 0; s < sections.Count; s++)
            {
                floorTris[s] = new List<int>();
                ceilTris[s] = new List<int>();
            }

            for (var i = 0; i < count - 1; i++)
            {
                var s = sections.IndexOf(SectionAt(sections, StartX + (i + 0.5f) * Step));
                int a = i * 4, b = (i + 1) * 4;
                // 카메라(-z)에서 보아 시계 방향 = 앞면
                floorTris[s].AddRange(new[] { a + 1, a + 0, b + 0, a + 1, b + 0, b + 1 });
                ceilTris[s].AddRange(new[] { a + 2, a + 3, b + 3, a + 2, b + 3, b + 2 });
            }

            var mesh = new Mesh { name = "Terrain", vertices = vertices, subMeshCount = sections.Count * 2 };
            var materials = new Material[sections.Count * 2];
            for (var s = 0; s < sections.Count; s++)
            {
                mesh.SetTriangles(floorTris[s], s * 2);
                mesh.SetTriangles(ceilTris[s], s * 2 + 1);
                materials[s * 2] = Shapes.Material(sections[s].Color, false);
                materials[s * 2 + 1] = Shapes.Material(sections[s].Color * 0.7f, false);
            }
            mesh.RecalculateBounds();

            _meshObject = new GameObject("Terrain");
            _meshObject.transform.SetParent(scrollX, false);
            _meshObject.transform.localPosition = new Vector3(0f, 0f, MeshZ);
            _meshObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            _meshObject.AddComponent<MeshRenderer>().sharedMaterials = materials;
        }

        static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);
    }
}
