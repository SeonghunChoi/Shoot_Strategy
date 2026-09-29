using UnityEngine;

namespace Prototype.Combat
{
    /// <summary>
    /// 레이더 HUD (검증계획 3.3). 가장자리: 화면 위·아래 밖 적의 가로 위치에 방향 표시와 높이 차.
    /// 세로 띠: 맵 전체 높이 × 전방 구간을 축소해 지형·화면 범위·기체·적을 표시.
    /// </summary>
    public sealed class RadarHud : MonoBehaviour
    {
        const float StripWidth = 64f;
        const float StripTop = 290f;
        const float StripBehind = 6f;   // 띠에 담는 화면 왼쪽 밖 거리
        const float StripAhead = 40f;   // 띠에 담는 화면 오른쪽 밖 거리
        const float EdgeAhead = 8f;     // 가장자리 표시 대상의 화면 오른쪽 밖 거리

        static readonly Color AirColor = new(1f, 0.35f, 0.3f);
        static readonly Color GroundColor = new(1f, 0.75f, 0.3f);

        CombatBootstrap _boot;
        GUIStyle _marker;

        public void Init(CombatBootstrap boot) => _boot = boot;

        void OnGUI()
        {
            if (_boot == null) return;
            var mode = _boot.Tuning.radarMode;
            if (mode == RadarMode.Off) return;

            ProtoGui.Begin();
            _marker ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 16 };

            if (mode is RadarMode.Edge or RadarMode.Both) DrawEdgeMarkers();
            if (mode is RadarMode.Strip or RadarMode.Both) DrawStrip();
            GUI.color = Color.white;
        }

        void DrawEdgeMarkers()
        {
            var ctx = _boot.Ctx;
            var cam = ctx.Camera.Camera;
            var view = ctx.Camera.View;

            foreach (var e in ctx.Enemies.All)
            {
                if (!e.Alive) continue;
                var c = e.Center;
                var above = c.y > view.yMax;
                if (!above && c.y >= view.yMin) continue;
                var dir = ctx.Scroll.Direction; // 진행 방향 앞쪽 화면 밖까지 표시
                if (dir > 0 ? c.x < view.xMin || c.x > view.xMax + EdgeAhead : c.x < view.xMin - EdgeAhead || c.x > view.xMax) continue;

                var sx = cam.WorldToScreenPoint(new Vector3(Mathf.Clamp(c.x, view.xMin + 0.5f, view.xMax - 0.5f), view.center.y, 0f)).x;
                var dy = above ? c.y - view.yMax : view.yMin - c.y;
                var rect = above
                    ? new Rect(sx - 30f, 2f, 60f, 40f)
                    : new Rect(sx - 30f, Screen.height - 42f, 60f, 40f);

                GUI.color = e.IsGround ? GroundColor : AirColor;
                GUI.Label(rect, above ? $"▲\n{dy:0}" : $"{dy:0}\n▼", _marker);
            }
        }

        void DrawStrip()
        {
            var ctx = _boot.Ctx;
            var view = ctx.Camera.View;
            var terrain = ctx.Stage.Terrain;
            var distance = ctx.Scroll.Distance;
            var mapHeight = ctx.MapHeight;

            var height = Mathf.Max(120f, Screen.height - StripTop - 20f);
            var area = new Rect(Screen.width - StripWidth - 20f, StripTop, StripWidth, height);
            var ahead = ctx.Scroll.Direction > 0;
            var xMin = distance + view.xMin - (ahead ? StripBehind : StripAhead);
            var xMax = distance + view.xMax + (ahead ? StripAhead : StripBehind);

            Fill(area, new Color(0f, 0f, 0f, 0.45f));

            // 지형 (1px 열 단위)
            var terrainColor = new Color(0.6f, 0.5f, 0.4f, 0.9f);
            for (var px = 0; px < StripWidth; px += 2)
            {
                var gx = Mathf.Lerp(xMin, xMax, (px + 1f) / StripWidth);
                var floorY = ToY(area, terrain.Floor(gx), mapHeight);
                var ceilY = ToY(area, terrain.Ceil(gx), mapHeight);
                Fill(new Rect(area.x + px, floorY, 2f, area.yMax - floorY), terrainColor);
                Fill(new Rect(area.x + px, area.y, 2f, ceilY - area.y), terrainColor);
            }

            // 현재 화면 범위
            var viewRect = Rect.MinMaxRect(ToX(area, distance + view.xMin, xMin, xMax), ToY(area, view.yMax, mapHeight),
                ToX(area, distance + view.xMax, xMin, xMax), ToY(area, view.yMin, mapHeight));
            Outline(viewRect, new Color(1f, 1f, 1f, 0.6f));

            foreach (var e in ctx.Enemies.All)
            {
                if (!e.Alive) continue;
                var gx = e.Center.x + distance;
                if (gx < xMin || gx > xMax) continue;
                Dot(ToX(area, gx, xMin, xMax), ToY(area, e.Center.y, mapHeight), 4f, e.IsGround ? GroundColor : AirColor);
            }

            if (!ctx.Player.Down)
                Dot(ToX(area, ctx.Player.Position.x + distance, xMin, xMax), ToY(area, ctx.Player.Y, mapHeight), 5f, Color.white);
        }

        static float ToX(Rect area, float gx, float xMin, float xMax) => Mathf.Lerp(area.xMin, area.xMax, Mathf.InverseLerp(xMin, xMax, gx));
        static float ToY(Rect area, float y, float mapHeight) => Mathf.Lerp(area.yMax, area.yMin, Mathf.Clamp01(y / mapHeight));

        static void Fill(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
        }

        static void Outline(Rect r, Color c)
        {
            Fill(new Rect(r.xMin, r.yMin, r.width, 1f), c);
            Fill(new Rect(r.xMin, r.yMax - 1f, r.width, 1f), c);
            Fill(new Rect(r.xMin, r.yMin, 1f, r.height), c);
            Fill(new Rect(r.xMax - 1f, r.yMin, 1f, r.height), c);
        }

        static void Dot(float x, float y, float size, Color c) => Fill(new Rect(x - size * 0.5f, y - size * 0.5f, size, size), c);
    }
}
