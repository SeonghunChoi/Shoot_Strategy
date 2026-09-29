using UnityEngine;

namespace Prototype.Combat
{
    /// <summary>간이 HUD: 구간·진행률, 모드, 속도, 거리, 시간, 현재 기체, 무장, 격추·충돌 수, 결과 창.</summary>
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
            var stage = ctx.Stage;
            var player = ctx.Player;
            var time = _boot.ElapsedTime;

            var text =
                $"구간  {(stage.Active ? stage.Current.Name : "무한 (스테이지 끔)")}\n" +
                "\n" + // 진행 막대 자리
                $"모드  {t.horizontalMode}  {(s.Turning ? "선회 중" : s.Direction > 0 ? "→" : "←")}\n" +
                $"속도  {s.Speed,6:0.0}\n" +
                (t.horizontalMode == HorizontalMode.PositionOffset
                    ? $"오프셋 {s.Offset,6:0.0} / ±{t.offsetRange:0}\n"
                    : $"스로틀 {s.Throttle,6:+0.0;-0.0} / -{s.DecelRange:0}~+{s.AccelRange:0}\n") +
                $"거리  {s.Distance,6:0}m\n" +
                $"시간  {FormatTime(time)}\n" +
                $"기체  {player.Member.Def.Name}  {player.Hp:0} / {player.Member.MaxHp:0}{(t.playerInvincible ? " (무적)" : "")}\n" +
                $"무장  {WeaponText(ctx.Weapons)}\n" +
                $"격추  공중 {ctx.AirKills} · 지상 {ctx.GroundKills}\n" +
                $"충돌  지형 {ctx.TerrainHits} · 기체 손실 {ctx.PlayerDowns}\n" +
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
            DrawBoss(ctx);
            if (s.Turning)
                GUI.Label(new Rect(0, Screen.height * 0.2f, Screen.width, 60), "선회", _center);
            if (player.Down)
            {
                GUI.Label(new Rect(0, Screen.height * 0.35f, Screen.width, 60),
                    t.restoreSquadronOnWipe ? "편대 전멸 — 2초 뒤 복구" : "편대 전멸", _center);
            }

            if (stage.Cleared) DrawResult(ctx, time, "스테이지 종료");
            else if (ctx.Squadron.Wiped && !t.restoreSquadronOnWipe) DrawResult(ctx, time, "편대 전멸");
        }

        /// <summary>선회 구역에 들어오면 화면 위 가운데에 보스 부위별 체력 막대.</summary>
        void DrawBoss(CombatContext ctx)
        {
            var boss = ctx.Boss;
            if (!boss.Active || !ctx.Stage.InTurnZone) return;

            const float barWidth = 180f;
            var parts = boss.Parts;
            var x = Screen.width * 0.5f - (parts.Count * (barWidth + 10f) - 10f) * 0.5f;
            if (boss.Defeated)
            {
                GUI.Label(new Rect(0, 10, Screen.width, 50), "보스 격파 — 오른쪽 끝으로", _center);
                return;
            }

            for (var i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                var rect = new Rect(x + i * (barWidth + 10f), 12f, barWidth, 40f);
                GUI.Box(rect, GUIContent.none);
                var state = !p.Alive ? " (파괴)" : p.Invulnerable ? " (무적)" : "";
                GUI.Label(new Rect(rect.x + 6f, rect.y + 2f, barWidth - 12f, 20f), p.Label + state);
                var bar = new Rect(rect.x + 6f, rect.y + 24f, barWidth - 12f, 8f);
                GUI.color = new Color(0f, 0f, 0f, 0.6f);
                GUI.DrawTexture(bar, Texture2D.whiteTexture);
                GUI.color = p.Invulnerable ? new Color(0.6f, 0.6f, 0.65f) : new Color(1f, 0.35f, 0.3f);
                GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Max(0f, p.Hp) / p.MaxHp, bar.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
        }

        void DrawResult(CombatContext ctx, float time, string title)
        {
            _result ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 20 };
            var rect = new Rect(Screen.width * 0.5f - 180f, Screen.height * 0.5f - 120f, 360f, 240f);
            GUI.Box(rect, GUIContent.none);
            GUI.Box(rect, GUIContent.none); // 두 번 겹쳐 덜 투명하게
            GUI.Label(new Rect(rect.x, rect.y + 16f, rect.width, 40f), title, _center);
            GUI.Label(new Rect(rect.x, rect.y + 64f, rect.width, 160f),
                $"시간  {FormatTime(time)}\n" +
                $"격추  공중 {ctx.AirKills} · 지상 {ctx.GroundKills}\n" +
                $"지형 충돌  {ctx.TerrainHits}\n" +
                $"기체 손실  {ctx.PlayerDowns}\n" +
                $"보스  {(ctx.Boss.Defeated ? "격파" : "미격파")} (부위 {ctx.BossPartKills})\n" +
                "R 다시 시작", _result);
        }

        static string FormatTime(float time) => $"{(int)(time / 60)}:{time % 60:00.0}";

        static string WeaponText(PlayerWeapons w)
        {
            var selected = w.Selected;
            var other = PlayerWeapons.Other(selected);
            if (!w.Has(selected)) return "없음";
            var text = $"{Name(selected)} {w.Ammo(selected)}";
            return w.Has(other) ? $"{text}  ({Name(other)} {w.Ammo(other)})" : text;
        }

        static string Name(HardpointWeapon w) => w == HardpointWeapon.Missile ? "미사일" : "폭탄";
    }
}
