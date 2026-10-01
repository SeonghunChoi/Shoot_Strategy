using System.Collections.Generic;
using System.Linq;
using Prototype.Strategy.Rules;
using UnityEngine;

namespace Prototype.Strategy
{
    /// <summary>
    /// 상단 바(기획서 11.1), 거점 패널(11.2 거점 관리), 턴 로그, 맵 위 글자.
    /// 패널 영역은 Layout 이벤트마다 기록해 두고, 그 위의 클릭은 맵 선택으로 보내지 않는다.
    /// </summary>
    public sealed class StrategyHud : MonoBehaviour
    {
        const float BarHeight = 34f;
        const float SideWidth = 310f;
        const float LogWidth = 380f;
        const float LogHeight = 150f;
        const float HelpHeight = 200f;

        StrategyBootstrap _boot;
        bool _showHelp;
        Vector2 _sideScroll;
        bool _showBudgetTip; // Layout에서만 판정 (Layout·Repaint 사이에 컨트롤 수가 달라지면 IMGUI 예외)
        Rect _focusRect;        // 초점 버튼의 스크롤 내용 좌표 (Repaint에서 갱신)
        int _focusRectFor = -1; // 어느 초점의 것인지
        readonly List<Rect> _uiRects = new();
        GUIStyle _mapLabel;
        GUIStyle _mapLabelSmall;
        GUIStyle _red;
        GUIStyle _green;
        GUIStyle _wrap;
        System.Action _pending;

        // 패드 패널 조작: 오른쪽 패널의 버튼을 그리는 순서대로 모아 두고 방향키 위·아래로 초점을 옮긴다
        // 목록은 Layout 이벤트에서만 채우고, 다른 이벤트에서는 그리는 순서(_padIndex)로만 초점을 맞춘다
        readonly List<(string label, System.Action action, bool enabled)> _padItems = new();
        int _padIndex;
        /// <summary>패널 초점 (−1이면 맵 커서).</summary>
        public int PadFocus { get; private set; } = -1;
        public bool HasPadFocus => PadFocus >= 0;

        public void Init(StrategyBootstrap boot) => _boot = boot;

        public void ToggleHelp() => _showHelp = !_showHelp;

        /// <summary>화면 좌표(아래가 0)가 패널 위인지.</summary>
        public bool IsOverUi(Vector2 screen)
        {
            var gui = new Vector2(screen.x, Screen.height - screen.y);
            return _uiRects.Any(r => r.Contains(gui));
        }

        void OnGUI()
        {
            if (_boot == null || _boot.World == null) return;
            ProtoGui.Begin();
            EnsureStyles();
            if (Event.current.type == EventType.Layout)
            {
                // 버튼 동작은 다음 Layout에서 실행: 같은 이벤트 안에서 컨트롤 수가 바뀌면 IMGUI 레이아웃이 어긋난다
                var pending = _pending;
                _pending = null;
                pending?.Invoke();
                _uiRects.Clear();
                _padItems.Clear();
            }
            _padIndex = 0;

            DrawMapLabels();
            DrawSidePanel();
            DrawLog();
            if (_showHelp) DrawHelp();
            DrawTopBar(); // 수지 내역 툴팁이 다른 패널 위에 오도록 마지막에
            if (_boot.World.GameOver != null) DrawGameOver();
        }

        void EnsureStyles()
        {
            if (_mapLabel != null) return;
            _mapLabel = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _mapLabelSmall = new GUIStyle(_mapLabel) { fontStyle = FontStyle.Normal };
            _red = new GUIStyle(GUI.skin.label) { normal = { textColor = new Color(1f, 0.45f, 0.4f) } };
            _green = new GUIStyle(GUI.skin.label) { normal = { textColor = new Color(0.5f, 0.95f, 0.55f) } };
            _wrap = new GUIStyle(ProtoGui.Small) { wordWrap = true };
        }

        void Do(System.Action action) => _pending = action;

        /// <summary>초점 이동. 끝에서는 멈춘다 (순환하지 않음).</summary>
        public void PadFocusMove(int delta)
        {
            var count = _padItems.Count;
            if (count == 0) return;
            PadFocus = PadFocus < 0
                ? (delta > 0 ? 0 : count - 1)
                : Mathf.Clamp(PadFocus + delta, 0, count - 1);
        }

        public void PadFocusClear() => PadFocus = -1;

        /// <summary>초점이 있는 패널 버튼을 실행. 비활성 버튼이면 아무 일도 하지 않는다.</summary>
        public void PadActivate()
        {
            if (PadFocus < 0 || PadFocus >= _padItems.Count) return;
            var item = _padItems[PadFocus];
            if (item.enabled) Do(item.action);
        }

        /// <summary>오른쪽 패널의 버튼. 패드 초점 목록에 등록되고, 초점이 있으면 노란색으로 표시한다.</summary>
        bool PanelButton(string label, System.Action action, bool enabled = true, GUIStyle style = null)
        {
            var index = _padIndex++;
            if (Event.current.type == EventType.Layout) _padItems.Add((label, action, enabled));
            var focused = index == PadFocus;
            var oldBg = GUI.backgroundColor;
            var oldEnabled = GUI.enabled;
            if (focused) GUI.backgroundColor = new Color(1f, 0.85f, 0.2f);
            GUI.enabled = enabled;
            var pressed = GUILayout.Button(focused ? "▶ " + label : label, style ?? GUI.skin.button);
            if (focused && Event.current.type == EventType.Repaint)
            {
                _focusRect = GUILayoutUtility.GetLastRect();
                _focusRectFor = index;
            }
            GUI.enabled = oldEnabled;
            GUI.backgroundColor = oldBg;
            if (pressed && enabled) Do(action);
            return pressed;
        }

        Rect Panel(Rect r)
        {
            ProtoGui.Panel(r);
            if (Event.current.type == EventType.Layout) _uiRects.Add(r);
            return r;
        }

        // ---- 상단 바 ----

        void DrawTopBar()
        {
            var w = _boot.World;
            var bal = _boot.Balance;
            Panel(new Rect(0f, 0f, Screen.width, BarHeight));
            var lines = Economy.Forecast(w, bal);
            var net = Economy.Net(lines);

            GUILayout.BeginArea(new Rect(12f, 6f, Screen.width - 24f, BarHeight - 6f));
            GUILayout.BeginHorizontal();
            GUILayout.Label($"자금  {w.Funds:N0}", w.Funds < 0 ? _red : GUI.skin.label, GUILayout.Width(140));
            GUILayout.Label($"예상 수지  {net:+#,0;-#,0;0}", net < 0 ? _red : _green, GUILayout.Width(150));
            var budgetRect = GUILayoutUtility.GetLastRect();
            GUILayout.Label($"연속 적자  {w.DeficitStreak}/{bal.BankruptTurns}", w.DeficitStreak > 0 ? _red : GUI.skin.label, GUILayout.Width(120));
            GUILayout.Label("신뢰도  —", GUILayout.Width(100));
            GUILayout.Label($"턴  {w.Turn}", ProtoGui.Bold, GUILayout.Width(70));
            GUI.enabled = w.GameOver == null;
            if (GUILayout.Button("턴 종료 (Enter)", GUILayout.Width(130))) Do(_boot.EndTurn);
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            GUILayout.Label($"F1 도움말   시드 {_boot.Seed}", ProtoGui.Small);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            // 예상 수지에 마우스를 올리면 항목별 내역
            var hoverRect = new Rect(budgetRect.x + 12f, 0f, budgetRect.width, BarHeight);
            if (Event.current.type == EventType.Layout) _showBudgetTip = hoverRect.Contains(Event.current.mousePosition);
            if (_showBudgetTip)
            {
                var tip = new Rect(hoverRect.x, BarHeight + 2f, 240f, 30f + 22f * lines.Count);
                ProtoGui.Panel(tip);
                GUILayout.BeginArea(new Rect(tip.x + 10f, tip.y + 4f, tip.width - 20f, tip.height - 8f));
                GUILayout.Label("이번 턴 예상 수지", ProtoGui.Bold);
                foreach (var l in lines) BudgetRow(l.Label, l.Amount);
                GUILayout.EndArea();
            }
        }

        void BudgetRow(string label, int amount)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, ProtoGui.Small);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{amount:+#,0;-#,0;0}", amount < 0 ? _red : _green);
            GUILayout.EndHorizontal();
        }

        // ---- 맵 위 글자 ----

        void DrawMapLabels()
        {
            var w = _boot.World;
            var cam = _boot.MapCamera;
            var scale = Mathf.Clamp(7f / cam.Size, 0.6f, 1.6f);
            _mapLabel.fontSize = Mathf.RoundToInt(15 * scale);
            _mapLabelSmall.fontSize = Mathf.RoundToInt(12 * scale);

            foreach (var (h, f) in w.Facilities)
            {
                var def = _boot.Balance.Def(f.Type);
                var text = def.Graded ? $"{def.Short}{f.Grade}" : def.Short;
                MapLabel(HexLayout.ToWorld(h) + new Vector2(0f, 0.4f), text, _mapLabel, Color.white);
            }

            foreach (var b in w.Bases)
            {
                var c = HexLayout.ToWorld(b.Hex);
                MapLabel(c, b.Grade.ToString(), _mapLabel, Color.white);
                MapLabel(c + new Vector2(0f, 0.72f), b.Name, _mapLabelSmall, Color.white);
            }

            var eb = _boot.ExpandBase;
            if (eb != null && _boot.ExpandCandidates.Count > 0)
            {
                var cost = BaseRules.ExpandCost(w, _boot.Balance, eb);
                var color = w.Funds >= cost ? Color.white : new Color(1f, 0.5f, 0.45f);
                foreach (var h in _boot.ExpandCandidates) MapLabel(HexLayout.ToWorld(h), cost.ToString(), _mapLabelSmall, color);
            }
        }

        void MapLabel(Vector2 world, string text, GUIStyle style, Color color)
        {
            var s = _boot.MapCamera.WorldToScreen(world);
            var r = new Rect(s.x - 60f, Screen.height - s.y - 12f, 120f, 24f);
            var shadow = r;
            shadow.x += 1f;
            shadow.y += 1f;
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(shadow, text, style);
            GUI.color = color;
            GUI.Label(r, text, style);
            GUI.color = old;
        }

        // ---- 오른쪽 패널 ----

        void DrawSidePanel()
        {
            var r = Panel(new Rect(Screen.width - SideWidth - 8f, BarHeight + 8f, SideWidth, Screen.height - BarHeight - 16f));
            var viewHeight = r.height - 16f;
            GUILayout.BeginArea(new Rect(r.x + 10f, r.y + 8f, r.width - 20f, viewHeight));
            // 패드 초점 버튼이 보이도록 스크롤을 따라 옮긴다
            if (HasPadFocus && _focusRectFor == PadFocus)
            {
                const float pad = 24f;
                if (_focusRect.yMax + pad > _sideScroll.y + viewHeight) _sideScroll.y = _focusRect.yMax + pad - viewHeight;
                if (_focusRect.y - pad < _sideScroll.y) _sideScroll.y = Mathf.Max(0f, _focusRect.y - pad);
            }
            _sideScroll = GUILayout.BeginScrollView(_sideScroll);

            var w = _boot.World;
            var shown = _boot.Selected ?? _boot.Cursor.Hover;
            if (shown is { } h)
            {
                var cell = w.Map[h];
                var (col, row) = h.ToOffset();
                GUILayout.Label(_boot.Selected != null ? "선택한 칸" : "커서 칸", ProtoGui.Bold);
                GUILayout.Label($"열 {col}, 행 {row}   지형 {TerrainName(cell.Terrain)}{(cell.Oil ? " · 유전" : "")}", ProtoGui.Small);
            }
            else
            {
                GUILayout.Label("칸을 클릭하면 거점 패널이 열립니다", ProtoGui.Small);
            }

            if (_boot.Selected is { } sel && w.BaseOf(sel) is { Owner: World.Player } b)
                DrawBasePanel(b, sel);
            else
                DrawBaseSummary();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
            // 패널 내용이 바뀌어 버튼이 줄었으면 초점을 마지막 버튼으로
            if (Event.current.type == EventType.Layout && PadFocus >= _padItems.Count) PadFocus = _padItems.Count - 1;
        }

        void DrawBaseSummary()
        {
            GUILayout.Space(8);
            GUILayout.Label("아군 거점", ProtoGui.Bold);
            var w = _boot.World;
            foreach (var b in w.Bases.Where(x => x.Owner == World.Player))
            {
                var target = b.Hex;
                PanelButton($"{b.Name}  ·  {b.Grade}등급  ·  시설 {BaseRules.FacilityCount(w, b)}/{BaseRules.FacilityCap(_boot.Balance, b)}",
                    () => _boot.Selected = target);
            }
        }

        void DrawBasePanel(Base b, Hex sel)
        {
            var w = _boot.World;
            var bal = _boot.Balance;
            GUILayout.Space(8);
            GUILayout.Label($"{b.Name}  ·  {b.Grade}등급 {GradeName(b.Grade)}", ProtoGui.Bold);
            var hexes = w.TerritoryOf(b).Count();
            GUILayout.Label($"시설 {BaseRules.FacilityCount(w, b)}/{BaseRules.FacilityCap(bal, b)}   영역 {hexes}칸   유지비 −{hexes * bal.UpkeepPerHex}", ProtoGui.Small);
            var runway = BaseRules.RunwayLength(w, b);
            GUILayout.Label($"활주로 직선 {runway}칸 → {BaseRules.SizeName(BaseRules.MaxAircraftSize(bal, runway))} 기체까지", ProtoGui.Small);

            // 영역 확장
            GUILayout.Space(4);
            if (_boot.ExpandBase == b)
            {
                GUILayout.Label("확장 모드: 흰 칸을 클릭 / 패드 South (오른쪽 클릭·Esc·East로 종료)", _wrap);
                PanelButton("확장 끝내기", _boot.StopExpand);
            }
            else
            {
                var expandCost = BaseRules.ExpandCost(w, bal, b);
                var canExpand = w.Funds >= expandCost && BaseRules.ExpandCandidates(w, b).Any();
                PanelButton($"영역 확장  (다음 칸 {expandCost})", () => _boot.StartExpand(b), enabled: canExpand);
                if (!canExpand) GUILayout.Label(w.Funds < expandCost ? "자금 부족" : "넓힐 칸 없음", _red);
            }

            // 거점 등급 상승
            GUILayout.Space(4);
            if (BaseRules.IsMaxGrade(bal, b))
            {
                GUILayout.Label("최고 등급", ProtoGui.Small);
            }
            else
            {
                var next = b.Grade + 1;
                var reqs = BaseRules.UpgradeRequirements(w, bal, b).Select(x => $"{(x.met ? "■" : "□")} {bal.Def(x.type).Name}");
                GUILayout.Label($"→ {next}등급 {GradeName(next)}: {bal.BaseUpgradeCost[next]}   {string.Join("  ", reqs)}", _wrap);
                ActionButton("거점 등급 상승", BaseRules.CanUpgradeBase(w, bal, b), () => BaseRules.UpgradeBase(w, bal, b));
            }

            // 선택한 칸
            GUILayout.Space(10);
            if (b.Hex == sel)
            {
                GUILayout.Label("본부 칸 (시설 불가)", ProtoGui.Bold);
                DrawFacilityList(b);
                return;
            }

            if (w.Facilities.TryGetValue(sel, out var f))
            {
                var def = bal.Def(f.Type);
                GUILayout.Label(def.Graded ? $"{def.Name}  {f.Grade}등급" : def.Name, ProtoGui.Bold);
                GUILayout.Label(def.Effect, _wrap);
                if (def.Graded)
                    ActionButton($"시설 등급 상승  ({BaseRules.FacilityUpgradeCost(bal, f)})",
                        BaseRules.CanUpgradeFacility(w, bal, sel), () => BaseRules.UpgradeFacility(w, bal, sel));
                PanelButton("철거 (환불 없음)", () => BaseRules.Demolish(w, sel));
                return;
            }

            GUILayout.Label("빈 칸 — 시설 건설", ProtoGui.Bold);
            var here = BaseRules.CanBuildHere(w, sel);
            if (here != null)
            {
                GUILayout.Label(here, _wrap);
            }
            else
            {
                foreach (var def in bal.Facilities.Values)
                    ActionButton($"{def.Name}  {def.Cost}", BaseRules.CanBuild(w, bal, sel, def.Type),
                        () => BaseRules.Build(w, bal, sel, def.Type), def.Effect);
            }

            GUILayout.Space(6);
            ActionButton($"이 칸에 전초기지 건설  ({bal.OutpostCost})", BaseRules.CanBuildOutpost(w, bal, sel), () =>
            {
                var nb = BaseRules.BuildOutpost(w, bal, sel);
                if (nb != null) _boot.Selected = nb.Hex;
            });
        }

        /// <summary>본부 칸을 골랐을 때: 이 거점의 시설 목록.</summary>
        void DrawFacilityList(Base b)
        {
            var bal = _boot.Balance;
            foreach (var (h, f) in _boot.World.FacilitiesOf(b).OrderBy(x => x.facility.Type).ToList())
            {
                var def = bal.Def(f.Type);
                var target = h;
                PanelButton(def.Graded ? $"{def.Name} {f.Grade}등급" : def.Name, () => _boot.Selected = target, style: GUI.skin.label);
            }
            GUILayout.Label("시설을 지으려면 영역 안의 빈 칸을 고르세요", _wrap);
        }

        /// <summary>불가 사유가 있으면 비활성 버튼 + 사유를 표시.</summary>
        void ActionButton(string label, string reason, System.Action action, string note = null)
        {
            PanelButton(label, action, enabled: reason == null);
            var detail = reason ?? note;
            if (!string.IsNullOrEmpty(detail)) GUILayout.Label(detail, reason != null ? _red : ProtoGui.Small);
        }

        // ---- 턴 로그 ----

        void DrawLog()
        {
            var r = Panel(new Rect(8f, Screen.height - LogHeight - 8f, LogWidth, LogHeight));
            GUILayout.BeginArea(new Rect(r.x + 10f, r.y + 6f, r.width - 20f, r.height - 12f));
            GUILayout.Label("턴 정산", ProtoGui.Bold);
            var reports = _boot.World.Reports;
            if (reports.Count == 0) GUILayout.Label("턴을 마치면 수입·지출 내역이 쌓입니다", ProtoGui.Small);
            for (var i = reports.Count - 1; i >= 0 && i >= reports.Count - 4; i--)
            {
                var rep = reports[i];
                var income = rep.Lines.Where(l => l.Amount > 0).Sum(l => l.Amount);
                var expense = rep.Lines.Where(l => l.Amount < 0).Sum(l => l.Amount);
                GUILayout.Label($"턴 {rep.Turn}: 수입 +{income}  지출 {expense}  → 순 {rep.Net:+#,0;-#,0;0}, 자금 {rep.FundsAfter:N0}" +
                                (rep.DeficitStreak > 0 ? $"  (적자 {rep.DeficitStreak})" : ""),
                    rep.FundsAfter < 0 ? _red : ProtoGui.Small);
            }
            GUILayout.EndArea();
        }

        void DrawHelp()
        {
            var r = Panel(new Rect(8f, Screen.height - LogHeight - HelpHeight - 16f, LogWidth, HelpHeight));
            GUILayout.BeginArea(new Rect(r.x + 10f, r.y + 8f, r.width - 20f, r.height - 16f));
            GUILayout.Label("조작  (F1 / View로 닫기)", ProtoGui.Bold);
            GUILayout.Label("선택: 왼쪽 클릭 / 패드 South    취소: 오른쪽 클릭 · Esc / East", ProtoGui.Small);
            GUILayout.Label("맵 이동: WASD · 방향키 · 가운데 드래그 / 오른쪽 스틱", ProtoGui.Small);
            GUILayout.Label("확대·축소: 휠 / RT · LT    패드 커서: 왼쪽 스틱", ProtoGui.Small);
            GUILayout.Label("턴 종료: Enter / Start    다시 시작: R", ProtoGui.Small);
            GUILayout.Label("아군 영역 칸을 고르면 거점 패널: 확장·등급·시설 건설", ProtoGui.Small);
            GUILayout.Label("패드 패널 조작: 방향키 위·아래로 초점, South 실행, East 해제", ProtoGui.Small);
            GUILayout.Label("예상 수지에 마우스를 올리면 항목별 내역", ProtoGui.Small);
            GUILayout.EndArea();
        }

        void DrawGameOver()
        {
            var r = Panel(new Rect(Screen.width * 0.5f - 180f, Screen.height * 0.5f - 70f, 360f, 140f));
            GUILayout.BeginArea(new Rect(r.x + 16f, r.y + 12f, r.width - 32f, r.height - 24f));
            GUILayout.Label("게임 오버", ProtoGui.Bold);
            GUILayout.Label(_boot.World.GameOver, _wrap);
            GUILayout.Label($"턴 {_boot.World.Turn}에서 종료", ProtoGui.Small);
            if (GUILayout.Button("다시 시작 (R)")) Do(_boot.Restart);
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

        static string GradeName(int grade) => grade switch
        {
            1 => "전초기지",
            2 => "기지",
            3 => "거점",
            4 => "사령부",
            _ => "",
        };
    }
}
