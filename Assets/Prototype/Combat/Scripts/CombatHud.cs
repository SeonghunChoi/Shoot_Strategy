using UnityEngine;

namespace Prototype.Combat
{
    /// <summary>간이 HUD: 모드, 스크롤 속도, 진행 거리, 경과 시간.</summary>
    public sealed class CombatHud : MonoBehaviour
    {
        CombatBootstrap _boot;

        public void Init(CombatBootstrap boot) => _boot = boot;

        void OnGUI()
        {
            if (_boot == null) return;
            ProtoGui.Begin();

            var t = _boot.Tuning;
            var s = _boot.Scroll;
            var time = _boot.ElapsedTime;
            var text =
                $"모드  {t.horizontalMode}\n" +
                $"속도  {s.Speed,6:0.0}\n" +
                (t.horizontalMode == HorizontalMode.PositionOffset ? $"오프셋 {s.Offset,6:0.0} / ±{t.offsetRange:0}\n" : "") +
                $"거리  {s.Distance,6:0}m\n" +
                $"시간  {(int)(time / 60)}:{time % 60:00.0}\n" +
                "F1 디버그 · R 리셋";

            const float width = 200f;
            GUI.Box(new Rect(Screen.width - width - 10, 10, width, 118), GUIContent.none);
            GUI.Label(new Rect(Screen.width - width, 14, width - 10, 110), text);
        }
    }
}
