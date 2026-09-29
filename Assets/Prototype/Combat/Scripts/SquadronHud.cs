using UnityEngine;

namespace Prototype.Combat
{
    /// <summary>
    /// 편대 패널(왼쪽 아래: 기체별 이름·체력·장탄·현재·격추·무방비)과 링 메뉴(기체 주위 3칸).
    /// </summary>
    public sealed class SquadronHud : MonoBehaviour
    {
        const float CardWidth = 150f;
        const float CardHeight = 62f;
        const float RingRadius = 110f;
        const float RingCard = 96f;

        static readonly Color CurrentColor = new(0.4f, 0.9f, 1f);
        static readonly Color DownColor = new(0.5f, 0.5f, 0.5f);
        static readonly Color HpColor = new(0.4f, 0.9f, 0.5f);
        static readonly Color VulnerableColor = new(1f, 0.8f, 0.2f);

        CombatBootstrap _boot;
        GUIStyle _small;
        GUIStyle _ringLabel;

        public void Init(CombatBootstrap boot) => _boot = boot;

        void OnGUI()
        {
            if (_boot == null) return;
            ProtoGui.Begin();
            _small ??= new GUIStyle(GUI.skin.label) { fontSize = 13 };
            _ringLabel ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 14, fontStyle = FontStyle.Bold };

            DrawPanel();
            DrawRing();
            GUI.color = Color.white;
        }

        void DrawPanel()
        {
            var squadron = _boot.Ctx.Squadron;
            var members = squadron.Members;
            var y = Screen.height - CardHeight - 12f;

            for (var i = 0; i < members.Count; i++)
            {
                var m = members[i];
                var rect = new Rect(12f + i * (CardWidth + 8f), y, CardWidth, CardHeight);
                var current = i == squadron.CurrentIndex && !squadron.Wiped;

                GUI.color = Color.white;
                GUI.Box(rect, GUIContent.none);
                if (current) Outline(rect, CurrentColor, 2f);

                GUI.color = m.Down ? DownColor : Color.white;
                var status = m.Down ? "  격추" : current && squadron.VulnerableTimer > 0f ? "  교대 중" : "";
                GUI.Label(new Rect(rect.x + 6f, rect.y + 2f, rect.width - 12f, 20f), $"{i + 1}. {m.Def.Name}{status}", _small);

                var bar = new Rect(rect.x + 6f, rect.y + 24f, rect.width - 12f, 8f);
                Fill(bar, new Color(0f, 0f, 0f, 0.6f));
                Fill(new Rect(bar.x, bar.y, bar.width * (m.MaxHp > 0f ? m.Hp / m.MaxHp : 0f), bar.height),
                    m.Down ? DownColor : current && squadron.VulnerableTimer > 0f ? VulnerableColor : HpColor);

                GUI.color = m.Down ? DownColor : Color.white;
                GUI.Label(new Rect(rect.x + 6f, rect.y + 36f, rect.width - 12f, 20f), Ammo(m), _small);
            }
        }

        void DrawRing()
        {
            var squadron = _boot.Ctx.Squadron;
            if (!squadron.RingOpen) return;

            var cam = _boot.Ctx.Camera.Camera;
            var sp = cam.WorldToScreenPoint(_boot.Ctx.Player.Position);
            var center = new Vector2(sp.x, Screen.height - sp.y);
            var members = squadron.Members;

            for (var i = 0; i < members.Count; i++)
            {
                var m = members[i];
                var angle = Squadron.SlotAngle(i, members.Count) * Mathf.Deg2Rad;
                var pos = center + new Vector2(Mathf.Cos(angle), -Mathf.Sin(angle)) * RingRadius;
                var rect = new Rect(pos.x - RingCard * 0.5f, pos.y - RingCard * 0.3f, RingCard, RingCard * 0.6f);

                GUI.color = Color.white;
                GUI.Box(rect, GUIContent.none);
                GUI.Box(rect, GUIContent.none);
                if (i == squadron.RingSelection) Outline(rect, m.Down ? DownColor : VulnerableColor, 3f);
                else if (i == squadron.CurrentIndex) Outline(rect, CurrentColor, 1f);

                GUI.color = m.Down ? DownColor : Color.white;
                GUI.Label(rect, m.Down ? $"{m.Def.Name}\n격추" : $"{m.Def.Name}\n{m.Hp:0}/{m.MaxHp:0}", _ringLabel);
            }
        }

        static string Ammo(SquadronMember m)
        {
            var text = "";
            if (m.Def.Missiles > 0) text += $"미사일 {m.Missiles}  ";
            if (m.Def.Bombs > 0) text += $"폭탄 {m.Bombs}";
            return text.Length > 0 ? text : "기관포만";
        }

        static void Fill(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
        }

        static void Outline(Rect r, Color c, float w)
        {
            Fill(new Rect(r.xMin, r.yMin, r.width, w), c);
            Fill(new Rect(r.xMin, r.yMax - w, r.width, w), c);
            Fill(new Rect(r.xMin, r.yMin, w, r.height), c);
            Fill(new Rect(r.xMax - w, r.yMin, w, r.height), c);
        }
    }
}
