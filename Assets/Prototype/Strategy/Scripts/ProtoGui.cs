using UnityEngine;

namespace Prototype.Strategy
{
    /// <summary>IMGUI 공용 설정. 기본 폰트의 한글 누락 대비로 OS 폰트를 쓴다.</summary>
    public static class ProtoGui
    {
        static Font _font;
        static GUIStyle _bold;
        static GUIStyle _small;
        static Texture2D _panel;

        public static GUIStyle Bold => _bold ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
        public static GUIStyle Small => _small ??= new GUIStyle(GUI.skin.label) { fontSize = 12 };

        /// <summary>OnGUI 시작 시 호출.</summary>
        public static void Begin()
        {
            if (_font == null)
            {
                _font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Arial" }, 14);
                _bold = null;
                _small = null;
            }
            GUI.skin.font = _font;
        }

        /// <summary>반투명 어두운 패널 배경.</summary>
        public static void Panel(Rect r)
        {
            if (_panel == null)
            {
                _panel = new Texture2D(1, 1);
                _panel.SetPixel(0, 0, new Color(0.08f, 0.1f, 0.13f, 0.82f));
                _panel.Apply();
            }
            GUI.DrawTexture(r, _panel);
        }

        /// <summary>색 견본 + 이름.</summary>
        public static void Swatch(Color color, string label)
        {
            GUILayout.BeginHorizontal();
            var r = GUILayoutUtility.GetRect(14, 14, GUILayout.Width(14), GUILayout.Height(18));
            r.y += 2;
            r.height = 14;
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
            GUILayout.Label(label, Small);
            GUILayout.EndHorizontal();
        }
    }
}
