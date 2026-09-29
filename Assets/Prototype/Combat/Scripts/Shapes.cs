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

        static readonly Dictionary<PrimitiveType, Mesh> Meshes = new();

        public static Mesh Mesh(PrimitiveType type)
        {
            if (Meshes.TryGetValue(type, out var mesh) && mesh != null) return mesh;

            var go = GameObject.CreatePrimitive(type);
            mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(go);
            Meshes[type] = mesh;
            return mesh;
        }

        public static Material Material(Color color, bool lit)
        {
            if (Materials.TryGetValue((color, lit), out var mat) && mat != null) return mat;

            mat = new Material(Template(lit));
            mat.SetColor("_BaseColor", color);
            Materials[(color, lit)] = mat;
            return mat;
        }

        /// <summary>인스턴싱 렌더링용 무조명 재질 (탄).</summary>
        public static Material InstancedUnlit(Color color)
        {
            var mat = new Material(Template(false)) { enableInstancing = true };
            mat.SetColor("_BaseColor", color);
            return mat;
        }

        // 빌드에서 셰이더가 제거되지 않도록 Resources의 재질 에셋을 복제해 쓴다
        // (에셋은 메뉴 Prototype ▸ Build Windows 또는 Create Build Materials가 만든다).
        public const string LitTemplatePath = "ProtoLit";
        public const string UnlitTemplatePath = "ProtoUnlit";
        static Material _litTemplate;
        static Material _unlitTemplate;

        static Material Template(bool lit)
        {
            var cached = lit ? _litTemplate : _unlitTemplate;
            if (cached != null) return cached;

            var path = lit ? LitTemplatePath : UnlitTemplatePath;
            cached = Resources.Load<Material>(path);
            if (cached == null)
            {
                Debug.LogWarning($"[CombatProto] Resources/{path} 재질이 없어 셰이더를 이름으로 찾습니다 (빌드에서는 실패할 수 있음).");
                cached = new Material(Shader.Find(lit ? "Universal Render Pipeline/Lit" : "Universal Render Pipeline/Unlit"));
            }
            if (lit) _litTemplate = cached;
            else _unlitTemplate = cached;
            return cached;
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
