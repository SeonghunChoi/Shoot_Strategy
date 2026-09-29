using System.Collections.Generic;
using UnityEngine;

namespace Prototype.Combat
{
    /// <summary>
    /// 원경 루프 레이어 (검증계획 3.1 Background). 가로는 진행 거리 × 계수로 흐르며 루프하고,
    /// 세로는 카메라 상승량 × 계수만큼 내려간다. 계수가 작을수록 멀리 있는 레이어.
    /// </summary>
    public sealed class ParallaxBackground
    {
        sealed class Layer
        {
            public float Factor;
            public float Span;
            public bool FullHeight; // true: 맵 높이 전체에 분포 (구름), false: 바닥에 고정 (산)
            public readonly List<(Transform t, float baseX, float normY, float bottomOffset)> Items = new();
        }

        readonly Transform _root;
        readonly List<Layer> _layers = new();

        public ParallaxBackground(float maxHalfWidth)
        {
            _root = new GameObject("Background").transform;
            var rng = new System.Random(1234);

            var mountains = AddLayer(0.1f, maxHalfWidth, 12f, false);
            for (var i = 0; i < 9; i++)
            {
                var size = Range(rng, 8f, 16f);
                var t = Shapes.Create(PrimitiveType.Cube, new Color(0.36f, 0.45f, 0.58f), _root,
                    new Vector3(0f, 0f, 60f), new Vector3(size, size, 1f), lit: false);
                t.localRotation = Quaternion.Euler(0f, 0f, 45f);
                mountains.Items.Add((t, Range(rng, 0f, mountains.Span), 0f, size * 0.2f));
            }

            AddClouds(rng, 0.3f, maxHalfWidth, 14, new Color(0.78f, 0.85f, 0.93f), 45f, 2.5f);
            AddClouds(rng, 0.6f, maxHalfWidth, 10, new Color(0.95f, 0.97f, 1f), 30f, 4f);
        }

        Layer AddLayer(float factor, float maxHalfWidth, float itemWidth, bool fullHeight)
        {
            var layer = new Layer { Factor = factor, Span = (maxHalfWidth + itemWidth) * 2f, FullHeight = fullHeight };
            _layers.Add(layer);
            return layer;
        }

        void AddClouds(System.Random rng, float factor, float maxHalfWidth, int count, Color color, float z, float scale)
        {
            var layer = AddLayer(factor, maxHalfWidth, scale * 3f, true);
            for (var i = 0; i < count; i++)
            {
                var t = Shapes.Create(PrimitiveType.Sphere, color, _root, new Vector3(0f, 0f, z),
                    new Vector3(scale * Range(rng, 2f, 3.5f), scale * Range(rng, 0.6f, 1f), 1f), lit: false);
                layer.Items.Add((t, Range(rng, 0f, layer.Span), (float)rng.NextDouble(), 0f));
            }
        }

        public void Tick(float distance, float cameraY, float halfHeight, float mapHeight)
        {
            var rise = Mathf.Max(0f, cameraY - halfHeight); // 바닥 기준 카메라 상승량
            var maxRise = Mathf.Max(0f, mapHeight - halfHeight * 2f);

            foreach (var layer in _layers)
            {
                var sink = rise * layer.Factor;
                foreach (var (t, baseX, normY, bottomOffset) in layer.Items)
                {
                    var screenX = Mathf.Repeat(baseX - distance * layer.Factor, layer.Span) - layer.Span * 0.5f;
                    var screenY = layer.FullHeight
                        ? Mathf.Lerp(-halfHeight, halfHeight + maxRise * layer.Factor, normY) - sink
                        : -halfHeight + bottomOffset - sink;
                    t.position = new Vector3(screenX, cameraY + screenY, t.position.z);
                }
            }
        }

        static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);
    }
}
