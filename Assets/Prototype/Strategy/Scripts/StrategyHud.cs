using Prototype.Strategy.Rules;
using UnityEngine;

namespace Prototype.Strategy
{
    /// <summary>
    /// 상단 바(기획서 11.1)와 헥스 정보 패널. 자금·수지·신뢰도는 2단계부터 채운다.
    /// </summary>
    public sealed class StrategyHud : MonoBehaviour
    {
        const float BarHeight = 32f;
        const float InfoWidth = 230f;
        const float InfoHeight = 290f;
        const float HelpWidth = 360f;
        const float HelpHeight = 200f;

        StrategyBootstrap _boot;
        bool _showHelp = true;

        public void Init(StrategyBootstrap boot) => _boot = boot;

        public void ToggleHelp() => _showHelp = !_showHelp;

        Rect InfoRect => new(Screen.width - InfoWidth - 8f, BarHeight + 8f, InfoWidth, InfoHeight);
        Rect HelpRect => new(8f, Screen.height - HelpHeight - 8f, HelpWidth, HelpHeight);

        /// <summary>화면 좌표(아래가 0)가 패널 위인지. 패널 위 클릭은 맵 선택으로 보내지 않는다.</summary>
        public bool IsOverUi(Vector2 screen)
        {
            var gui = new Vector2(screen.x, Screen.height - screen.y);
            return gui.y < BarHeight || InfoRect.Contains(gui) || _showHelp && HelpRect.Contains(gui);
        }

        void OnGUI()
        {
            if (_boot == null || _boot.World == null) return;
            ProtoGui.Begin();
            DrawTopBar();
            DrawInfo();
            if (_showHelp) DrawHelp();
        }

        void DrawTopBar()
        {
            var world = _boot.World;
            var r = new Rect(0f, 0f, Screen.width, BarHeight);
            ProtoGui.Panel(r);
            GUILayout.BeginArea(new Rect(12f, 6f, Screen.width - 24f, BarHeight - 6f));
            GUILayout.BeginHorizontal();
            GUILayout.Label("자금  —", GUILayout.Width(150));
            GUILayout.Label("예상 수지  —", GUILayout.Width(170));
            GUILayout.Label("신뢰도  —", GUILayout.Width(130));
            GUILayout.Label($"턴  {world.Turn}", ProtoGui.Bold, GUILayout.Width(90));
            GUILayout.FlexibleSpace();
            GUILayout.Label($"시드 {_boot.Seed}", ProtoGui.Small);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void DrawInfo()
        {
            var r = InfoRect;
            ProtoGui.Panel(r);
            GUILayout.BeginArea(new Rect(r.x + 10f, r.y + 8f, r.width - 20f, r.height - 16f));

            var map = _boot.World.Map;
            var hover = _boot.Cursor.Hover;
            var selected = _boot.Selected;

            GUILayout.Label("헥스 정보", ProtoGui.Bold);
            var shown = selected ?? hover;
            if (shown is { } h)
            {
                var cell = map[h];
                var (col, row) = h.ToOffset();
                GUILayout.Label($"{(selected != null ? "선택" : "커서")}  열 {col}, 행 {row}   축 {h}");
                GUILayout.Label($"지형  {TerrainName(cell.Terrain)}{(cell.Oil ? "  · 유전" : "")}");
            }
            else
            {
                GUILayout.Label("칸을 가리키거나 클릭하세요", ProtoGui.Small);
            }

            if (selected is { } s && hover is { } hv && hv != s)
            {
                var dir = Hex.StraightDirection(s, hv);
                GUILayout.Label($"커서까지 거리  {Hex.Distance(s, hv)}");
                GUILayout.Label(dir >= 0 ? $"직선 ({DirectionName(dir)})" : "직선 아님", ProtoGui.Small);
            }

            GUILayout.Space(6);
            GUILayout.Label("지형", ProtoGui.Bold);
            foreach (TerrainType t in System.Enum.GetValues(typeof(TerrainType)))
                ProtoGui.Swatch(HexMapView.TerrainColor(t), TerrainName(t));
            ProtoGui.Swatch(HexMapView.OilColor, "유전 표식");
            GUILayout.EndArea();
        }

        void DrawHelp()
        {
            var r = HelpRect;
            ProtoGui.Panel(r);
            GUILayout.BeginArea(new Rect(r.x + 10f, r.y + 8f, r.width - 20f, r.height - 16f));
            GUILayout.Label("조작  (F1 / View로 숨기기)", ProtoGui.Bold);
            GUILayout.Label("선택: 왼쪽 클릭 / 패드 South", ProtoGui.Small);
            GUILayout.Label("취소: 오른쪽 클릭 · Esc / 패드 East", ProtoGui.Small);
            GUILayout.Label("맵 이동: WASD · 방향키 · 가운데 드래그 / 오른쪽 스틱", ProtoGui.Small);
            GUILayout.Label("확대·축소: 휠 / RT · LT", ProtoGui.Small);
            GUILayout.Label("패드 커서: 왼쪽 스틱 (가장자리에서 맵 이동)", ProtoGui.Small);
            GUILayout.Label("칸을 고른 뒤 다른 칸을 가리키면 거리와 직선 여부 표시", ProtoGui.Small);
            GUILayout.EndArea();
        }

        public static string TerrainName(TerrainType t) => t switch
        {
            TerrainType.Plain => "평지",
            TerrainType.Mountain => "산악",
            TerrainType.Sea => "해상",
            TerrainType.Coast => "해안",
            TerrainType.Desert => "사막",
            _ => t.ToString(),
        };

        static string DirectionName(int dir) => dir switch
        {
            0 => "동",
            1 => "북동",
            2 => "북서",
            3 => "서",
            4 => "남서",
            5 => "남동",
            _ => "?",
        };
    }
}
