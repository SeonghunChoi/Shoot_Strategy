using System.Collections.Generic;
using UnityEngine;

namespace Prototype.Combat
{
    /// <summary>
    /// 기본 도형 생성 도우미. 물리를 쓰지 않으므로 콜라이더는 제거한다.
    /// </summary>
    public static class Shapes
    {
        static readonly Dictionary<(Color, bool), Material> Materials = new();

        public static Transform Create(PrimitiveType type, Color color, Transform parent,
            Vector3 localPos, Vector3 localScale, bool lit = true, string name = null)
        {
            var go = GameObject.CreatePrimitive(type);
            if (name != null) go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = Material(color, lit);

            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            t.localScale = localScale;
            return t;
        }

        public static Material Material(Color color, bool lit)
        {
            if (Materials.TryGetValue((color, lit), out var mat) && mat != null) return mat;

            var shader = Shader.Find(lit ? "Universal Render Pipeline/Lit" : "Universal Render Pipeline/Unlit");
            mat = new Material(shader);
            mat.SetColor("_BaseColor", color);
            Materials[(color, lit)] = mat;
            return mat;
        }

        public static TextMesh Label(Transform parent, Vector3 localPos, float size, Color color)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;

            var text = go.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
            text.characterSize = size;
            text.fontSize = 64;
            text.anchor = TextAnchor.LowerCenter;
            text.color = color;
            return text;
        }
    }
}
