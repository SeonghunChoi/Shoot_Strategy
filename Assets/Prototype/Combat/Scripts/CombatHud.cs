using UnityEngine;

namespace Prototype.Combat
{
    /// <summary>간이 HUD: 모드, 스크롤 속도, 진행 거리, 경과 시간, 체력, 무장, 격추 수.</summary>
    public sealed class CombatHud : MonoBehaviour
    {
        CombatBootstrap _boot;
        GUIStyle _center;

        public void Init(CombatBootstrap boot) => _boot = boot;

        void OnGUI()
        {
            if (_boot == null) return;
            ProtoGui.Begin();

            var ctx = _boot.Ctx;
            var t = ctx.Tuning;
            var s = ctx.Scroll;
            var w = ctx.Weapons;
            var time = _boot.ElapsedTime;

            var text =
                $"모드  {t.horizontalMode}\n" +
                $"속도  {s.Speed,6:0.0}\n" +
                (t.horizontalMode == HorizontalMode.PositionOffset ? $"오프셋 {s.Offset,6:0.0} / ±{t.offsetRange:0}\n" : "") +
                $"거리  {s.Distance,6:0}m\n" +
                $"시간  {(int)(time / 60)}:{time % 60:00.0}\n" +
                $"체력  {ctx.Player.Hp:0} / {t.playerMaxHp:0}{(t.playerInvincible ? " (무적)" : "")}\n" +
                $"무장  {Name(w.Selected)} {w.Ammo(w.Selected)}  ({Name(Other(w.Selected))} {w.Ammo(Other(w.Selected))})\n" +
                $"격추  공중 {ctx.AirKills} · 지상 {ctx.GroundKills}\n" +
                "F1 디버그 · R 리셋";

            const float width = 230f;
            const float height = 196f;
            GUI.Box(new Rect(Screen.width - width - 10, 10, width, height), GUIContent.none);
            GUI.Label(new Rect(Screen.width - width, 14, width - 10, height - 8), text);

            if (ctx.Player.Down)
            {
                _center ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 32, fontStyle = FontStyle.Bold };
                GUI.Label(new Rect(0, Screen.height * 0.35f, Screen.width, 60), "격추됨", _center);
            }
        }

        static HardpointWeapon Other(HardpointWeapon w) =>
            w == HardpointWeapon.Missile ? HardpointWeapon.Bomb : HardpointWeapon.Missile;

        static string Name(HardpointWeapon w) => w == HardpointWeapon.Missile ? "미사일" : "폭탄";
    }
}
