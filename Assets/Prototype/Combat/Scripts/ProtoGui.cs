using UnityEngine;

namespace Prototype.Combat
{
    /// <summary>IMGUI 공용 설정. 기본 폰트의 한글 누락 대비로 OS 폰트를 쓴다.</summary>
    public static class ProtoGui
    {
        static Font _font;
        static GUIStyle _bold;

        public static GUIStyle Bold => _bold ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };

        /// <summary>OnGUI 시작 시 호출.</summary>
        public static void Begin()
        {
            if (_font == null)
            {
                _font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Arial" }, 14);
                _bold = null;
            }
            GUI.skin.font = _font;
        }
    }
}
