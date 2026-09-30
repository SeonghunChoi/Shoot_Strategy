using System.Collections.Generic;
using Prototype.Strategy.Rules;
using UnityEngine;

namespace Prototype.Strategy
{
    /// <summary>
    /// 정점 색 병합 메시 도우미. 헥스마다 오브젝트를 두지 않고 메시 하나에 모은다 (기술 설계 4.4).
    /// </summary>
    public sealed class HexMesh
    {
        readonly List<Vector3> _vertices = new();
        readonly List<Color> _colors = new();
        readonly List<int> _triangles = new();
        readonly Mesh _mesh;

        public Transform Transform { get; }

        static Material _material;

        static Material SharedMaterial =>
            _material != null ? _material : _material = new Material(Shader.Find("Prototype/VertexColorUnlit"));

        /// <param name="z">카메라 쪽이 음수. 겹치는 층은 z와 sortingOrder로 순서를 정한다.</param>
        public HexMesh(string name, Transform parent, float z, int sortingOrder)
        {
            var go = new GameObject(name);
            Transform = go.transform;
            Transform.SetParent(parent, false);
            Transform.localPosition = new Vector3(0f, 0f, z);
            _mesh = new Mesh { name = name };
            _mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = SharedMaterial;
            renderer.sortingOrder = sortingOrder;
        }

        public void Clear()
        {
            _vertices.Clear();
            _colors.Clear();
            _triangles.Clear();
        }

        /// <summary>채운 헥스. scale은 Size 대비 비율.</summary>
        public void AddFilled(Hex h, float scale, Color color)
        {
            var center = HexLayout.ToWorld(h);
            var start = _vertices.Count;
            _vertices.Add(center);
            _colors.Add(color);
            for (var i = 0; i < 6; i++)
            {
                _vertices.Add(center + HexLayout.Corner(i, HexLayout.Size * scale));
                _colors.Add(color);
            }
            for (var i = 0; i < 6; i++)
            {
                _triangles.Add(start);
                _triangles.Add(start + 1 + (i + 1) % 6);
                _triangles.Add(start + 1 + i);
            }
        }

        /// <summary>테두리 고리 (outer~inner 사이).</summary>
        public void AddOutline(Hex h, float outer, float inner, Color color)
        {
            var center = HexLayout.ToWorld(h);
            var start = _vertices.Count;
            for (var i = 0; i < 6; i++)
            {
                _vertices.Add(center + HexLayout.Corner(i, HexLayout.Size * outer));
                _vertices.Add(center + HexLayout.Corner(i, HexLayout.Size * inner));
                _colors.Add(color);
                _colors.Add(color);
            }
            for (var i = 0; i < 6; i++)
            {
                int o0 = start + i * 2, i0 = o0 + 1;
                int o1 = start + (i + 1) % 6 * 2, i1 = o1 + 1;
                _triangles.Add(o0); _triangles.Add(o1); _triangles.Add(i0);
                _triangles.Add(i0); _triangles.Add(o1); _triangles.Add(i1);
            }
        }

        public void Apply()
        {
            // 정점 색은 셰이더에서 변환되지 않으므로 Linear 색 공간이면 여기서 바꾼다 (IMGUI 견본과 같은 색)
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                for (var i = 0; i < _colors.Count; i++) _colors[i] = _colors[i].linear;
            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetColors(_colors);
            _mesh.SetTriangles(_triangles, 0);
            _mesh.RecalculateBounds();
        }
    }
}
