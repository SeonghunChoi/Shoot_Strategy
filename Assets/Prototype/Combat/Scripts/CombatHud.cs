using UnityEngine;

namespace Prototype.Combat
{
    /// <summary>간이 HUD: 구간·진행률, 모드, 속도, 거리, 시간, 체력, 무장, 격추·충돌 수, 스테이지 결과.</summary>
    public sealed class CombatHud : MonoBehaviour
    {
        const float Width = 230f;
        const float Height = 262f;

        CombatBootstrap _boot;
        GUIStyle _center;
        GUIStyle _result;

        public void Init(CombatBootstrap boot) => _boot = boot;

        void OnGUI()
        {
            if (_boot == null) return;
            ProtoGui.Begin();

            var ctx = _boot.Ctx;
            var t = ctx.Tuning;
            var s = ctx.Scroll;
            var w = ctx.Weapons;
            var stage = ctx.Stage;
            var time = _boot.ElapsedTime;

            var text =
                $"구간  {(stage.Active ? stage.Current.Name : "무한 (스테이지 끔)")}\n" +
                "\n" + // 진행 막대 자리
                $"모드  {t.horizontalMode}\n" +
                $"속도  {s.Speed,6:0.0}\n" +
                (t.horizontalMode == HorizontalMode.PositionOffset
                    ? $"오프셋 {s.Offset,6:0.0} / ±{t.offsetRange:0}\n"
                    : $"스로틀 {s.Throttle,6:+0.0;-0.0} / -{t.decelRange:0}~+{t.accelRange:0}\n") +
                $"거리  {s.Distance,6:0}m\n" +
                $"시간  {FormatTime(time)}\n" +
                $"체력  {ctx.Player.Hp:0} / {t.playerMaxHp:0}{(t.playerInvincible ? " (무적)" : "")}\n" +
                $"무장  {Name(w.Selected)} {w.Ammo(w.Selected)}  ({Name(Other(w.Selected))} {w.Ammo(Other(w.Selected))})\n" +
                $"격추  공중 {ctx.AirKills} · 지상 {ctx.GroundKills}\n" +
                $"충돌  지형 {ctx.TerrainHits} · 피격추 {ctx.PlayerDowns}\n" +
                "F1 디버그 · R 리셋";

            var box = new Rect(Screen.width - Width - 10, 10, Width, Height);
            GUI.Box(box, GUIContent.none);
            GUI.Label(new Rect(box.x + 10, 14, Width - 20, Height - 8), text);

            if (stage.Active)
            {
                var bar = new Rect(box.x + 10, 40, Width - 20, 8);
                GUI.color = new Color(0f, 0f, 0f, 0.6f);
                GUI.DrawTexture(bar, Texture2D.whiteTexture);
                GUI.color = new Color(0.4f, 0.9f, 0.5f);
                GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * stage.Progress, bar.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }

            _center ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 32, fontStyle = FontStyle.Bold };
            if (ctx.Player.Down)
                GUI.Label(new Rect(0, Screen.height * 0.35f, Screen.width, 60), "격추됨", _center);

            if (stage.Cleared) DrawResult(ctx, time);
        }

        void DrawResult(CombatContext ctx, float time)
        {
            _result ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 20 };
            var rect = new Rect(Screen.width * 0.5f - 180f, Screen.height * 0.5f - 120f, 360f, 240f);
            GUI.Box(rect, GUIContent.none);
            GUI.Box(rect, GUIContent.none); // 두 번 겹쳐 덜 투명하게
            GUI.Label(new Rect(rect.x, rect.y + 16f, rect.width, 40f), "스테이지 종료", _center);
            GUI.Label(new Rect(rect.x, rect.y + 64f, rect.width, 160f),
                $"시간  {FormatTime(time)}\n" +
                $"격추  공중 {ctx.AirKills} · 지상 {ctx.GroundKills}\n" +
                $"지형 충돌  {ctx.TerrainHits}\n" +
                $"피격추  {ctx.PlayerDowns}\n" +
                "(선회 구역·보스는 6단계)  R 다시 시작", _result);
        }

        static string FormatTime(float time) => $"{(int)(time / 60)}:{time % 60:00.0}";

        static HardpointWeapon Other(HardpointWeapon w) =>
            w == HardpointWeapon.Missile ? HardpointWeapon.Bomb : HardpointWeapon.Missile;

        static string Name(HardpointWeapon w) => w == HardpointWeapon.Missile ? "미사일" : "폭탄";
    }
}
