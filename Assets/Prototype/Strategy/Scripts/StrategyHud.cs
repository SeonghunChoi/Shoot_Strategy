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
        const float SideWidth = 400f;
        const float LogWidth = 380f;
        const float LogHeight = 150f;

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
        int _pilotPickFor;    // 파일럿 배정 목록을 펼친 기체 Id
        int _aircraftPickFor; // 기체 추가 목록을 펼친 편대 Id

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
            if (_boot.ShowDebrief) DrawDebrief();
            if (_boot.ShowEnemyReport) DrawEnemyReport();
            if (_boot.ShowHq) DrawHq();
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
            if (GUILayout.Button($"신뢰도 {w.Trust}{(w.Directive != null ? " · 지시!" : "")}", GUILayout.Width(130)))
                Do(() => _boot.ShowHq = !_boot.ShowHq);
            GUILayout.Label($"턴  {w.Turn}", ProtoGui.Bold, GUILayout.Width(70));
            GUI.enabled = w.GameOver == null;
            if (GUILayout.Button("턴 종료 (Enter)", GUILayout.Width(130))) Do(_boot.EndTurn);
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            GUILayout.Label($"F1 도움말 · F2 튜닝   시드 {_boot.Seed}", ProtoGui.Small);
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
                var squadrons = w.SquadronsAt(b).Count();
                var ground = GroundRules.StationedAt(w, b).Count();
                var garrison = (squadrons > 0 ? $"편대 {squadrons}" : "") + (squadrons > 0 && ground > 0 ? " · " : "") + (ground > 0 ? $"지상 {ground}" : "");
                if (garrison != "") MapLabel(c + new Vector2(0f, -0.62f), garrison, _mapLabelSmall, new Color(0.7f, 0.9f, 1f));
                if (b.Id == w.GoalBaseId && b.Owner != World.Player) MapLabel(c + new Vector2(0f, 1.05f), "★ 장기 목표", _mapLabelSmall, new Color(1f, 0.9f, 0.4f));
            }

            foreach (var u in w.EnemyUnits)
            {
                if (!_boot.Visible.Contains(u.Hex)) continue;
                var c = HexLayout.ToWorld(u.Hex);
                MapLabel(c + new Vector2(0f, 0.1f), u.Name, _mapLabelSmall, new Color(1f, 0.75f, 0.7f));
                MapLabel(c + new Vector2(0f, -0.3f), $"공 {u.Air} 지 {u.Ground}", _mapLabelSmall, new Color(1f, 0.75f, 0.7f));
            }
            foreach (var gu in w.GroundUnits.Where(x => x.Deployed))
                MapLabel(HexLayout.ToWorld(gu.Hex) + new Vector2(0f, -0.35f), $"{gu.Name} {gu.Strength}", _mapLabelSmall, HexMapView.GroundColor);
            foreach (var g in Vision.StaleSightings(w, _boot.Balance))
            {
                var c = HexLayout.ToWorld(g.Hex);
                MapLabel(c + new Vector2(0f, 0.1f), g.Name, _mapLabelSmall, HexMapView.GhostColor);
                MapLabel(c + new Vector2(0f, -0.3f), $"{w.Turn - g.Turn}턴 전 목격", _mapLabelSmall, HexMapView.GhostColor);
            }
            foreach (var f in w.Factions)
            {
                var stance = EnemyTurn.StanceName(f.Stance);
                foreach (var b in w.Bases.Where(x => x.Owner == f.Id))
                    MapLabel(HexLayout.ToWorld(b.Hex) + new Vector2(0f, -0.95f), $"{f.Name} · {stance}", _mapLabelSmall, HexMapView.OwnerColor(f.Id));
            }
            foreach (var b in w.Bases.Where(x => x.Owner != World.Player))
                MapLabel(HexLayout.ToWorld(b.Hex) + new Vector2(0f, -0.62f), $"대공 {b.AntiAir} 시설 {b.Structures}", _mapLabelSmall, new Color(1f, 0.75f, 0.7f));

            for (var i = 0; i < w.SortiePlan.Count; i++)
                MapLabel(HexLayout.ToWorld(w.SortiePlan[i].Target) + new Vector2(0f, 0.55f), $"출격 {i + 1}", _mapLabel, HexMapView.PlanColor);

            if (_boot.TargetPickSquadron != null)
                foreach (var h in _boot.TargetCandidates)
                    MapLabel(HexLayout.ToWorld(h) + new Vector2(0f, 0.55f), "목표?", _mapLabel, Color.white);

            if (_boot.OutpostMode)
                foreach (var h in _boot.OutpostCandidates)
                    MapLabel(HexLayout.ToWorld(h), BaseRules.OutpostCost(w, _boot.Balance, h).ToString(), _mapLabelSmall, Color.white);

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
            // 세로만 스크롤 (가로 넘침은 잘라서 가로 막대가 생기지 않게)
            _sideScroll = GUILayout.BeginScrollView(_sideScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);

            var w = _boot.World;
            DrawTabs();

            switch (_boot.Tab)
            {
                case PanelTab.Hangar: DrawHangarTab(); break;
                case PanelTab.Sortie: DrawSortieTab(); break;
                case PanelTab.Ground: DrawGroundTab(); break;
                case PanelTab.Shop: DrawShopTab(); break;
                case PanelTab.Pilots: DrawPilotsTab(); break;
                default:
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
                    if (_boot.SelectedEnemy != null)
                        DrawEnemyInfo(_boot.SelectedEnemy, _boot.SelectedEnemyPrediction);
                    else if (_boot.Selected is { } sel && w.BaseOf(sel) is { Owner: World.Player } b)
                        DrawBasePanel(b, sel);
                    else
                        DrawBaseSummary();
                    break;
            }

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
            GUILayout.Space(6);
            DrawOutpostButton();
        }

        void DrawBasePanel(Base b, Hex sel)
        {
            var w = _boot.World;
            var bal = _boot.Balance;
            GUILayout.Space(8);
            GUILayout.Label($"{b.Name}  ·  {b.Grade}등급 {GradeName(b.Grade)}", ProtoGui.Bold);
            var hexes = w.TerritoryOf(b).Count();
            var count = BaseRules.FacilityCount(w, b);
            var cap = BaseRules.FacilityCap(bal, b);
            GUILayout.Label($"시설 {count}/{cap}{(count > cap ? $" (상한 초과 {count - cap})" : "")}   영역 {hexes}칸   유지비 −{hexes * bal.UpkeepPerHex}", count > cap ? _red : ProtoGui.Small);
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
                var over = BaseRules.NextOverCap(w, bal, b);
                if (over > 0 && !bal.HardFacilityCap)
                    GUILayout.Label($"상한 초과 {over}번째 — 건설비 +{bal.FacilityOverCapCostRatio * over:P0}. 거점을 빼앗기면 상한을 넘는 시설부터 파괴됨", _red);
                foreach (var def in bal.Facilities.Values)
                {
                    var cost = BaseRules.BuildCost(w, bal, b, def.Type);
                    ActionButton(cost != def.Cost ? $"{def.Name}  {cost} (기본 {def.Cost})" : $"{def.Name}  {cost}",
                        BaseRules.CanBuild(w, bal, sel, def.Type), () => BaseRules.Build(w, bal, sel, def.Type), def.Effect);
                }
            }

            GUILayout.Space(6);
            DrawOutpostButton();
        }

        /// <summary>전초기지 건설 모드 버튼 (거점 패널·거점 요약 공용).</summary>
        void DrawOutpostButton()
        {
            var w = _boot.World;
            var bal = _boot.Balance;
            if (_boot.OutpostMode)
            {
                GUILayout.Label($"전초기지 모드: 노란 칸을 클릭 / 패드 South. 아군 거점에서 {bal.OutpostMinDistance}칸 이상, 편대가 (급유 포함) 닿는 곳. 비용 {bal.OutpostCost} + 거리 × {bal.OutpostCostPerDistance}", _wrap);
                // 거점별 도달 범위: 편대가 없는 거점은 후보가 없다는 것을 바로 알 수 있게
                foreach (var ob in w.Bases.Where(x => x.Owner == World.Player))
                {
                    var squadrons = w.SquadronsAt(ob).ToList();
                    var reach = squadrons.Count == 0 ? 0 : squadrons.Max(sq => SquadronRules.OperationRadius(w, bal, sq, refuel: true));
                    GUILayout.Label(squadrons.Count == 0
                        ? $"  {ob.Name}: 편대 없음 → 후보 없음 (편대를 옮기면 열림)"
                        : $"  {ob.Name}: 편대 {squadrons.Count} · 급유 포함 최대 도달 {reach}", squadrons.Count == 0 ? _red : ProtoGui.Small);
                }
                PanelButton("전초기지 모드 끝내기", _boot.StopOutpost);
            }
            else
            {
                var any = BaseRules.OutpostCandidates(w, bal).Any();
                PanelButton($"전초기지 건설…  ({bal.OutpostCost} + 거리 × {bal.OutpostCostPerDistance})", _boot.StartOutpost, enabled: any);
                if (!any) GUILayout.Label("지을 수 있는 칸 없음 (자금·거리·도달 범위 확인)", _red);
            }
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

        // ---- 탭 ----

        static readonly string[] TabNames = { "거점", "격납고", "출격", "지상군", "상점", "명부" };

        void DrawTabs()
        {
            var current = (int)_boot.Tab;
            var next = GUILayout.Toolbar(current, TabNames);
            if (next != current) Do(() => _boot.Tab = (PanelTab)next);
            GUILayout.Label("Q / E · LB / RB 로 탭 전환", ProtoGui.Small);
            GUILayout.Space(4);
        }

        // ---- 격납고 ----

        void DrawHangarTab()
        {
            var w = _boot.World;
            var bal = _boot.Balance;
            var selectedBase = _boot.Selected is { } sel ? w.BaseOf(sel) : null;
            if (selectedBase is { Owner: World.Player })
            {
                GUILayout.Label($"{selectedBase.Name}  ·  격납고 {w.SquadronsAt(selectedBase).Count()}/{SquadronRules.HangarCapacity(w, bal, selectedBase)}", ProtoGui.Bold);
                GUILayout.Label($"설비 반경 보너스 +{SquadronRules.FacilityRadiusBonus(w, bal, selectedBase)}   정비소 {SquadronRules.MaintenanceGrade(w, selectedBase)}등급   유흥가 {SquadronRules.EntertainmentGrade(w, selectedBase)}등급", ProtoGui.Small);
                ActionButton("새 편대 편성", SquadronRules.CanCreateSquadron(w, bal, selectedBase), () =>
                {
                    var ns = SquadronRules.CreateSquadron(w, bal, selectedBase);
                    if (ns != null) _boot.SelectedSquadronId = ns.Id;
                });
                foreach (var s in w.SquadronsAt(selectedBase).ToList()) DrawSquadron(s);
            }
            else
            {
                GUILayout.Label("편대 전체 (거점을 고르면 그 거점만)", ProtoGui.Bold);
                foreach (var s in w.Squadrons.ToList()) DrawSquadron(s);
            }

            GUILayout.Space(8);
            GUILayout.Label($"예비 기체 {w.ReserveAircraft.Count()}기 · 대기 파일럿 {w.IdlePilots.Count()}명  (상점·명부 탭)", ProtoGui.Small);
        }

        void DrawSquadron(Squadron s)
        {
            var w = _boot.World;
            var bal = _boot.Balance;
            var b = w.BaseById(s.BaseId);
            var selected = _boot.SelectedSquadronId == s.Id;

            GUILayout.Space(8);
            GUILayout.Label($"{(selected ? "● " : "")}{s.Name}  ·  {b.Name}  ·  {s.AircraftIds.Count}/{bal.SquadronMaxSize}기", ProtoGui.Bold);
            var maint = SquadronRules.MaintenanceGrade(w, b);
            var repair = maint > 0 ? maint * bal.RepairPerMaintenanceGrade : bal.RepairWithoutMaintenance;
            var rest = bal.FatigueRecovery + SquadronRules.EntertainmentGrade(w, b) * bal.FatigueRecoveryPerEntertainmentGrade;
            GUILayout.Label($"이 거점에서 턴마다 수리 {repair}%{(maint == 0 ? " (정비소 없음)" : "")} · 피로 −{rest}", ProtoGui.Small);
            GUILayout.Label($"작전 반경 {SquadronRules.OperationRadius(w, bal, s, refuel: false)} (가장 짧은 기체 기준 + 설비)  ·  공중급유 시 {SquadronRules.OperationRadius(w, bal, s, refuel: true)}", ProtoGui.Small);
            if (s.MovedThisTurn) GUILayout.Label("이번 턴 재배치함 — 출격 불가, 적 턴에 이 거점에서 요격", _green);
            else if (s.SortiedThisTurn) GUILayout.Label("이번 턴 출격함 — 재배치 불가, 적 턴 요격 불참", _red);

            foreach (var id in s.AircraftIds.ToList())
            {
                var a = w.AircraftById(id);
                var def = bal.Def(a.Type);
                var p = w.PilotById(a.PilotId);
                var cannot = SquadronRules.CannotSortie(w, bal, a);
                var status = p == null ? "파일럿 없음" : $"{p.Name}  숙련 {p.Skill}{(p.Aptitude == a.Type ? " ★" : "")}  피로 {p.Fatigue}";
                GUILayout.Label($"{s.AircraftIds.IndexOf(a.Id) + 1}. {def.Name} ({RoleName(def.Role)})  공중 {def.Air} 지상 {def.Ground} 폭격 {def.Bombing} 반경 {def.Range}  손상 {a.Damage}%", a.Damage >= 50 ? _red : GUI.skin.label);
                GUILayout.Label(status + (cannot != null ? $"   [{cannot}]" : ""), cannot != null ? _red : ProtoGui.Small);

                GUILayout.BeginHorizontal();
                var aircraft = a;
                var pos = s.AircraftIds.IndexOf(a.Id);
                PanelButton("▲", () => SquadronRules.ReorderAircraft(s, aircraft, -1), enabled: pos > 0);
                PanelButton("▼", () => SquadronRules.ReorderAircraft(s, aircraft, 1), enabled: pos < s.AircraftIds.Count - 1);
                PanelButton(_pilotPickFor == a.Id ? "파일럿 목록 닫기" : "파일럿 배정", () => _pilotPickFor = _pilotPickFor == aircraft.Id ? 0 : aircraft.Id);
                if (p != null) PanelButton("파일럿 해제", () => SquadronRules.UnassignPilot(w, aircraft));
                var sq = s;
                PanelButton("편대에서 빼기", () => SquadronRules.DetachAircraft(w, sq, aircraft));
                GUILayout.EndHorizontal();

                if (_pilotPickFor == a.Id)
                {
                    var idle = w.IdlePilots.ToList();
                    if (idle.Count == 0) GUILayout.Label("대기 파일럿 없음 (상점 탭에서 고용)", _red);
                    foreach (var cand in idle)
                    {
                        var pilot = cand;
                        PanelButton($"  {cand.Name}  숙련 {cand.Skill}  숙련 기종 {bal.Def(cand.Aptitude).Name}{(cand.Aptitude == a.Type ? " ★" : "")}  피로 {cand.Fatigue}",
                            () => { SquadronRules.AssignPilot(w, aircraft, pilot); _pilotPickFor = 0; }, style: GUI.skin.label);
                    }
                }
            }

            GUILayout.BeginHorizontal();
            var sq2 = s;
            PanelButton(selected ? "반경 숨기기" : "작전 반경 보기", () => _boot.SelectedSquadronId = selected ? 0 : sq2.Id);
            if (_boot.MovingSquadron == s) PanelButton("재배치 취소", _boot.StopMove);
            else PanelButton("재배치… (즉시 도착, 이번 턴 출격 불가)", () => _boot.StartMove(sq2),
                enabled: !s.MovedThisTurn && !s.SortiedThisTurn && s.AircraftIds.Count > 0 && w.SortiePlan.All(p => p.SquadronId != s.Id));
            GUILayout.EndHorizontal();
            if (_boot.MovingSquadron == s) GUILayout.Label("초록 거점을 클릭 / 패드 South (East로 취소)", _wrap);

            GUILayout.BeginHorizontal();
            PanelButton(_aircraftPickFor == s.Id ? "기체 목록 닫기" : "기체 추가", () => _aircraftPickFor = _aircraftPickFor == sq2.Id ? 0 : sq2.Id,
                enabled: s.AircraftIds.Count < bal.SquadronMaxSize);
            PanelButton("디버그: 손상 50·피로 80", () => SquadronRules.SimulateWear(w, sq2, 50, 80), enabled: s.AircraftIds.Count > 0);
            ActionButton("해산", SquadronRules.CanDisband(w, s), () => { SquadronRules.Disband(w, sq2); if (_boot.SelectedSquadronId == sq2.Id) _boot.SelectedSquadronId = 0; });
            GUILayout.EndHorizontal();

            if (_aircraftPickFor == s.Id)
            {
                var reserve = w.ReserveAircraft.ToList();
                if (reserve.Count == 0) GUILayout.Label("예비 기체 없음 (상점 탭에서 구매)", _red);
                foreach (var cand in reserve)
                {
                    var aircraft = cand;
                    var reason = SquadronRules.CanAttachAircraft(w, bal, s, cand);
                    ActionButton($"  {bal.Def(cand.Type).Name}  손상 {cand.Damage}%", reason,
                        () => { SquadronRules.AttachAircraft(w, sq2, aircraft); _aircraftPickFor = 0; });
                }
            }
        }

        // ---- 출격 ----

        void DrawSortieTab()
        {
            var w = _boot.World;
            var bal = _boot.Balance;

            if (_boot.TargetPickSquadron is { } picking)
            {
                GUILayout.Label($"{picking.Name}: 목표 칸을 클릭 / 패드 South", ProtoGui.Bold);
                GUILayout.Label("흰 '목표?' 칸 = 작전 반경 안의 적. 오른쪽 클릭·Esc·East로 취소", _wrap);
                PanelButton("목표 선택 취소", _boot.StopTargetPick);
                GUILayout.Space(8);
            }

            if (_boot.EndTurnWarned) GUILayout.Label("미실행 출격 계획이 있습니다. 턴 종료를 다시 누르면 계획을 버리고 턴을 마칩니다.", _red);
            GUILayout.Label($"출격 계획  {w.SortiePlan.Count}건  ·  비용 합계 {SortieRules.PlannedCost(w, bal)}", ProtoGui.Bold);
            if (w.SortiePlan.Count == 0) GUILayout.Label("아래에서 편대를 골라 목표를 지정하세요", ProtoGui.Small);
            for (var i = 0; i < w.SortiePlan.Count; i++)
            {
                var plan = w.SortiePlan[i];
                var s = w.SquadronById(plan.SquadronId);
                GUILayout.Label($"{i + 1}. {s.Name} → {SortieRules.TargetName(w, plan.Target)}  ·  {SortieRules.MissionName(plan.Mission)}  ·  {SortieRules.Cost(w, bal, s)}{(s.Refuel ? "  ·  공중급유" : "")}",
                    ProtoGui.Small);
                GUILayout.BeginHorizontal();
                var p = plan;
                PanelButton("▲", () => SortieRules.Reorder(w, p, -1), enabled: i > 0);
                PanelButton("▼", () => SortieRules.Reorder(w, p, 1), enabled: i < w.SortiePlan.Count - 1);
                PanelButton("제거", () => SortieRules.Unplan(w, p));
                GUILayout.EndHorizontal();
            }

            var gaps = SortieRules.DefenseGaps(w).ToList();
            if (gaps.Count > 0) GUILayout.Label($"방어 공백: {string.Join(", ", gaps.Select(b => b.Name))} — 편대가 모두 출격 (그 턴 적이 와도 요격 없음)", _red);

            GUILayout.Space(4);
            ActionButton($"출격 실행  ({w.SortiePlan.Count}건, 순서대로 자동 정산)", w.SortiePlan.Count == 0 ? "계획 없음" : null, _boot.ExecuteSorties);

            GUILayout.Space(10);
            GUILayout.Label("편대 선택 → 목표 지정", ProtoGui.Bold);
            foreach (var s in w.Squadrons.ToList())
            {
                // 편대마다 한 묶음: 제목 → 상태 → [목표 지정] [공중급유] 버튼 한 줄 → 안내
                var b = w.BaseById(s.BaseId);
                var reason = SortieRules.CanSortie(w, bal, s);
                var targetList = reason == null ? SortieRules.Targets(w, bal, s).ToList() : new List<Hex>();
                var targets = targetList.Count;
                // 반경 안의 목표 중 실제로 계획할 수 있는 곳 (자금·같은 목표 중복으로 막히면 그 사유를 보여 준다)
                var blocked = targetList.Select(h => SortieRules.CanPlan(w, bal, s, h)).ToList();
                var pickable = blocked.Count(x => x == null);
                var planned = w.SortiePlan.Any(p => p.SquadronId == s.Id);
                var sq = s;

                GUILayout.Space(6);
                GUILayout.Label($"■ {s.Name}  ·  {b.Name}{(s.Refuel ? "  ·  급유 켜짐" : "")}", ProtoGui.Bold);
                GUILayout.Label($"출격 {SortieRules.SortieAircraft(w, bal, s).Count()}/{s.AircraftIds.Count}기  ·  비용 {SortieRules.Cost(w, bal, s)}  ·  반경 {SquadronRules.OperationRadius(w, bal, s)}" +
                                (targets > 0 ? $"  ·  목표 후보 {targets}곳" : ""), ProtoGui.Small);

                GUILayout.BeginHorizontal();
                PanelButton($"{s.Name} 목표 지정…", () => _boot.StartTargetPick(sq), enabled: reason == null && pickable > 0);
                if (reason == null || reason == "이미 계획됨")
                    PanelButton(s.Refuel
                            ? $"급유 끄기 (반경 {SquadronRules.OperationRadius(w, bal, s, refuel: false)})"
                            : $"급유 켜기 (반경 {SquadronRules.OperationRadius(w, bal, s, refuel: true)}, +{bal.RefuelCostRatio:P0})",
                        () => sq.Refuel = !sq.Refuel, enabled: !planned);
                GUILayout.EndHorizontal();

                if (reason != null) GUILayout.Label(reason, _red);
                else if (targets == 0) GUILayout.Label(NoTargetHint(s), _red);
                else if (pickable == 0) GUILayout.Label(BlockedHint(w, bal, s, blocked.First()), _red);
                if (planned) GUILayout.Label("계획을 제거해야 급유 설정을 바꿀 수 있음", ProtoGui.Small);
            }

            GUILayout.Space(8);
            if (w.Debriefs.Count > 0) PanelButton($"최근 디브리핑 다시 보기 ({w.Debriefs.Count}건)", () => _boot.OpenDebrief(0));
            if (w.EnemyEvents.Count > 0 || w.Intercepts.Count > 0) PanelButton("최근 적 턴 요약 다시 보기", () => _boot.ShowEnemyReport = true);
            PanelButton(bal.VisionEnabled ? "디버그: 시야 끄기 (적 전부 표시)" : "디버그: 시야 켜기", () => bal.VisionEnabled = !bal.VisionEnabled);
            GUILayout.Label($"기체 크기별 비용 소형 30 · 중형 50 · 대형 80, 보급창고 −20%. 공중급유 = 반경 × {bal.RefuelRadiusMultiplier:0.#}, 비용 +{bal.RefuelCostRatio:P0}. 교대 기준 손상 {bal.SwapDamage}%, 불리 계수 {bal.AutoResolvePenalty}", _wrap);
        }

        string NoTargetHint(Squadron s)
        {
            var w = _boot.World;
            var b = w.BaseById(s.BaseId);
            var (d, name) = SortieRules.NearestTarget(w, s);
            var radius = SquadronRules.OperationRadius(w, _boot.Balance, s);
            var refuel = SquadronRules.OperationRadius(w, _boot.Balance, s, refuel: true);
            return name == null ? "맵에 목표 없음"
                : $"작전 반경 {radius} 안에 목표 없음 — 가장 가까운 {name}까지 거리 {d}" + (!s.Refuel && refuel >= d ? " (공중급유로 닿음)" : "");
        }

        /// <summary>목표는 있지만 모두 계획할 수 없을 때의 안내.</summary>
        static string BlockedHint(World w, Balance bal, Squadron s, string reason) => reason == "자금 부족"
            ? $"자금 부족 — 계획 합계 {SortieRules.PlannedCost(w, bal)} + 이 편대 {SortieRules.Cost(w, bal, s)} > 자금 {w.Funds}"
            : reason == null ? null : $"목표 지정 불가 — {reason}";

        Vector2 _debriefScroll;

        void DrawDebrief()
        {
            var w = _boot.World;
            if (w.Debriefs.Count == 0) { _boot.ShowDebrief = false; return; }
            var idx = Mathf.Clamp(_boot.DebriefIndex, 0, w.Debriefs.Count - 1);
            var rep = w.Debriefs[idx];
            var width = Mathf.Min(620f, Screen.width - 40f);
            var height = Mathf.Min(560f, Screen.height - 80f);
            var r = Panel(new Rect((Screen.width - SideWidth - width) * 0.5f, (Screen.height - height) * 0.5f, width, height));
            GUILayout.BeginArea(new Rect(r.x + 16f, r.y + 12f, r.width - 32f, r.height - 24f));

            GUILayout.Label($"디브리핑 {idx + 1}/{w.Debriefs.Count}  ·  출격 {rep.Order}: {rep.SquadronName} → {rep.TargetName}  ({SortieRules.MissionName(rep.Mission)})", ProtoGui.Bold);
            if (rep.Effect != null && rep.Rounds == 0 && rep.Aircraft.Count == 0)
            {
                GUILayout.Label(rep.Effect, _wrap);
            }
            else
            {
                GUILayout.Label($"파괴율 {rep.DestructionRate:P0}  ·  {(rep.Exit == ExitReason.Retreat ? "철수" : "완료")}  ·  {rep.Rounds}라운드  ·  비용 −{rep.Cost}  보상 +{rep.Reward}" +
                                (rep.OverEnemyTerritory ? "  ·  적 영역 상공" : "  ·  아군·중립 상공") + (rep.OverlapBonus ? "  ·  겹침 보정" : ""), ProtoGui.Small);
                GUILayout.Label($"전략 효과: {rep.Effect}", _green);

                GUILayout.Space(6);
                GUILayout.Label("기체별 결과", ProtoGui.Bold);
                foreach (var o in rep.Aircraft)
                {
                    var line = $"{o.AircraftName} ({o.PilotName})  전경 {o.Rounds}R  손상 {o.DamageBefore}% → {o.DamageAfter}%" +
                               (o.ShotDown ? $"  격추: {o.Fate}, {o.PilotFate}" : "") +
                               $"  경험치 +{o.XpGain} (기본 {o.XpBase} · 교전 {o.XpCombat} · 공유 {o.XpShare}){(o.SkillUp ? " 숙련도 상승!" : "")}";
                    GUILayout.Label(line, o.ShotDown ? _red : ProtoGui.Small);
                }

                GUILayout.Space(6);
                GUILayout.Label("라운드 기록", ProtoGui.Bold);
                _debriefScroll = GUILayout.BeginScrollView(_debriefScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
                foreach (var l in rep.Log) GUILayout.Label(l, _wrap);
                GUILayout.EndScrollView();
            }

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUI.enabled = idx > 0;
            if (GUILayout.Button("◀ 이전")) Do(() => _boot.OpenDebrief(idx - 1));
            GUI.enabled = idx < w.Debriefs.Count - 1;
            if (GUILayout.Button("다음 ▶")) Do(() => _boot.OpenDebrief(idx + 1));
            GUI.enabled = true;
            if (GUILayout.Button("닫기 (Esc / East)")) Do(() => _boot.ShowDebrief = false);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ---- 적 부대 정보 ----

        void DrawEnemyInfo(EnemyUnit u, EnemyTurn.Prediction p)
        {
            var w = _boot.World;
            var f = EnemyTurn.FactionOf(w, u.Owner);
            GUILayout.Space(8);
            GUILayout.Label($"{u.Name}  ·  {f.Name}", ProtoGui.Bold);
            GUILayout.Label($"전력 {u.Strength} (공중 {u.Air} · 지상 {u.Ground})", ProtoGui.Small);
            if (p == null) return;
            GUILayout.Space(4);
            GUILayout.Label("다음 적 턴 예상", ProtoGui.Bold);
            GUILayout.Label($"태세: {EnemyTurn.StanceName(p.Stance)}{(p.Stance != f.Stance ? $" (지금 {EnemyTurn.StanceName(f.Stance)}에서 바뀜)" : "")}", ProtoGui.Small);
            if (p.EngageGround != null)
            {
                GUILayout.Label($"이동하지 않고 인접한 {p.EngageGround.Name}과 교전", _red);
            }
            else if (p.Goal == null)
            {
                GUILayout.Label("제자리 (재정비·방어 중)", ProtoGui.Small);
            }
            else
            {
                GUILayout.Label($"목표: {p.GoalName}", ProtoGui.Small);
                GUILayout.Label(p.ThisTurn.Count > 0 ? $"이번에 {p.ThisTurn.Count}칸 이동 → {p.ThisTurn[^1].ToOffset()} (진한 선)" : "이동 없음", ProtoGui.Small);
                if (p.Later.Count > 0) GUILayout.Label($"이후 목표까지 {p.Later.Count}칸 더 (옅은 선) — 약 {Mathf.CeilToInt(p.Later.Count / (float)_boot.Balance.EnemyMovePoints)}턴", ProtoGui.Small);
                if (p.AttackTarget != null)
                {
                    var b = p.AttackTarget;
                    var guards = w.SquadronsAt(b).Count(s => !s.SortiedThisTurn && SortieRules.SortieAircraft(w, _boot.Balance, s).Any());
                    GUILayout.Label($"다음 적 턴에 {b.Name} 공격 예상 (빨간 테두리)", _red);
                    GUILayout.Label($"지금 그 거점의 요격 가능 편대 {guards}개 · 대공 시설 {EnemyTurn.FacilityDefense(w, _boot.Balance, b)} · 주둔 지상군 {GroundRules.StationedStrength(w, b)}", _wrap);
                }
            }
            GUILayout.Label("예상은 지금 상태 기준입니다. 다른 부대의 움직임·교전, 이번 턴 내 행동(출격·재배치·건설)에 따라 달라집니다.", _wrap);
        }

        // ---- 지상군 ----

        void DrawGroundTab()
        {
            var w = _boot.World;
            var bal = _boot.Balance;

            if (_boot.GroundPick is { } picking)
            {
                GUILayout.Label($"{picking.Name}: 목표 칸을 클릭 / 패드 South", ProtoGui.Bold);
                GUILayout.Label("녹색 = 거점(적 거점은 점령, 아군 거점은 주둔). 그 밖의 육지 칸도 이동 목표로 지정 가능. 오른쪽 클릭·Esc·East로 취소", _wrap);
                PanelButton("목표 선택 취소", _boot.StopGroundPick);
                GUILayout.Space(8);
            }

            GUILayout.Label($"지상군 {w.GroundUnits.Count}개  ·  유지비 {GroundRules.Upkeep(w, bal)}/턴", ProtoGui.Bold);
            GUILayout.Label($"고용 {bal.GroundHireCost} (병영 필요, 전력 {bal.GroundBaseStrength} + 병영 등급 × {bal.GroundStrengthPerBarracksGrade}), 출동 {bal.GroundDeployCost}, 이동 {bal.GroundMovePoints}칸/턴", _wrap);
            GUILayout.Label("점령: 적 거점 인접에서 전력 > 대공 + 시설. 폭격으로 시설을 깎아 두면 쉬워짐. 거점의 적 부대와 먼저 교전", _wrap);

            foreach (var b in w.Bases.Where(x => x.Owner == World.Player))
            {
                GUILayout.Space(6);
                var barracks = GroundRules.BarracksGrade(w, b);
                GUILayout.Label($"{b.Name}  ·  병영 {(barracks > 0 ? $"{barracks}등급" : "없음")}  ·  주둔 지상군 방어 {GroundRules.StationedStrength(w, b)}", ProtoGui.Bold);
                var bb = b;
                ActionButton($"지상군 고용  {bal.GroundHireCost}", GroundRules.CanHire(w, bal, b), () => GroundRules.Hire(w, bal, bb));
            }

            GUILayout.Space(8);
            GUILayout.Label("부대", ProtoGui.Bold);
            if (w.GroundUnits.Count == 0) GUILayout.Label("지상군 없음", ProtoGui.Small);
            foreach (var g in w.GroundUnits.ToList())
            {
                var gu = g;
                var where = g.Deployed
                    ? $"출동 중 {g.Hex.ToOffset()} → {(g.Target is { } t ? (w.BaseAt(t)?.Name ?? t.ToOffset().ToString()) : "대기")}"
                    : $"{w.BaseById(g.BaseId).Name} 주둔";
                GUILayout.Label($"{g.Name}  전력 {g.Strength}  ·  {where}", g.Deployed ? _green : ProtoGui.Small);
                GUILayout.BeginHorizontal();
                PanelButton(g.Deployed ? "목표 변경…" : $"출동…  ({bal.GroundDeployCost})", () => _boot.StartGroundPick(gu),
                    enabled: g.Deployed || w.Funds >= bal.GroundDeployCost);
                if (g.Deployed) PanelButton("가까운 거점으로 복귀", () => GroundRules.Recall(w, gu));
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(6);
            GUILayout.Label("턴 순서: 턴 종료 → 아군 지상군 이동·공격 → 적 턴. 출동 중 적 부대와 맞닿으면 교전", _wrap);
        }

        // ---- 상부 통신 ----

        Vector2 _hqScroll;

        void DrawHq()
        {
            var w = _boot.World;
            var bal = _boot.Balance;
            var width = 460f;
            var height = Mathf.Min(420f, Screen.height - BarHeight - 40f);
            var r = Panel(new Rect(Mathf.Max(8f, Screen.width * 0.5f - width), BarHeight + 6f, width, height));
            GUILayout.BeginArea(new Rect(r.x + 14f, r.y + 10f, r.width - 28f, r.height - 20f));
            GUILayout.Label($"상부 통신  ·  신뢰도 {w.Trust}/100", ProtoGui.Bold);
            GUILayout.Label($"예산 {HqRules.Budget(w, bal)}/턴 = {bal.HqBudget} × ({bal.BudgetBaseRatio} + 신뢰도/100). 신뢰도 0이면 해임", _wrap);
            GUILayout.Space(4);
            GUILayout.Label($"장기 목표: {HqRules.GoalText(w)}  (달성 시 +{bal.TrustGoal}, 승리)", _green);

            GUILayout.Space(6);
            GUILayout.Label("긴급 지시", ProtoGui.Bold);
            if (w.Directive is { } d)
            {
                var left = d.Deadline - w.Turn;
                GUILayout.Label($"{d.Text}  ·  남은 턴 {left}", left <= 1 ? _red : GUI.skin.label);
                GUILayout.Label($"완수 +{bal.TrustDirectiveSuccess} · 실패 −{bal.TrustDirectiveFail} · 거부 −{bal.TrustRefuse}", ProtoGui.Small);
                if (GUILayout.Button($"지시 거부 (−{bal.TrustRefuse})")) Do(() => HqRules.Refuse(w, bal));
            }
            else
            {
                GUILayout.Label($"없음 — {bal.DirectiveInterval - w.DirectiveTimer}턴 뒤 새 지시", ProtoGui.Small);
            }

            GUILayout.Space(6);
            GUILayout.Label("신뢰도 변동", ProtoGui.Bold);
            _hqScroll = GUILayout.BeginScrollView(_hqScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            if (w.TrustLog.Count == 0) GUILayout.Label("아직 없음", ProtoGui.Small);
            for (var i = w.TrustLog.Count - 1; i >= 0; i--) GUILayout.Label(w.TrustLog[i], _wrap);
            GUILayout.EndScrollView();
            if (GUILayout.Button("닫기 (Esc / East)")) Do(() => _boot.ShowHq = false);
            GUILayout.EndArea();
        }

        // ---- 적 턴 요약 ----

        Vector2 _enemyScroll;

        void DrawEnemyReport()
        {
            var w = _boot.World;
            var width = Mathf.Min(640f, Screen.width - 40f);
            var height = Mathf.Min(520f, Screen.height - 80f);
            var r = Panel(new Rect((Screen.width - SideWidth - width) * 0.5f, (Screen.height - height) * 0.5f, width, height));
            GUILayout.BeginArea(new Rect(r.x + 16f, r.y + 12f, r.width - 32f, r.height - 24f));
            GUILayout.Label($"적 턴 요약  ·  턴 {w.Turn - 1} → {w.Turn}", ProtoGui.Bold);
            foreach (var f in w.Factions)
                GUILayout.Label($"{f.Name}: 태세 {EnemyTurn.StanceName(f.Stance)} · 부대 {w.EnemyUnits.Count(u => u.Owner == f.Id)} · 전력 {EnemyTurn.FactionStrength(w, f)}", ProtoGui.Small);

            _enemyScroll = GUILayout.BeginScrollView(_enemyScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            GUILayout.Space(4);
            GUILayout.Label("사건", ProtoGui.Bold);
            if (w.EnemyEvents.Count == 0) GUILayout.Label("탐지된 사건 없음", ProtoGui.Small);
            foreach (var e in w.EnemyEvents) GUILayout.Label(e, e.Contains("상실") ? _red : _wrap);

            foreach (var rep in w.Intercepts)
            {
                GUILayout.Space(6);
                GUILayout.Label($"{rep.BaseName} 피습 — {rep.UnitName} (전력 {rep.EnemyBefore})", rep.Captured ? _red : ProtoGui.Bold);
                GUILayout.Label($"요격 편대 {rep.SquadronsEngaged}개 · {rep.Rounds}라운드 → 적 {rep.EnemyAfterAir}  ·  대공 시설 {rep.FacilityDefense} → 적 {rep.EnemyAfterFacility}  ·  {(rep.Captured ? "거점 상실" : "방어 성공")}", ProtoGui.Small);
                foreach (var l in rep.Log) GUILayout.Label(l, _wrap);
            }
            GUILayout.EndScrollView();

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("닫기 (Esc / East)")) Do(() => _boot.ShowEnemyReport = false);
            GUILayout.EndArea();
        }

        // ---- 상점 ----

        void DrawShopTab()
        {
            var w = _boot.World;
            var bal = _boot.Balance;
            GUILayout.Label("기체 구매", ProtoGui.Bold);
            foreach (var def in bal.Aircraft.Values)
            {
                var type = def.Type;
                var reason = SquadronRules.CanBuy(w, bal, type);
                ActionButton($"{def.Name}  {RoleName(def.Role)}  {def.Price}", reason, () => SquadronRules.Buy(w, bal, type),
                    $"{BaseRules.SizeName(def.Size)} · {def.Tier}티어 · 체력 {def.Hp} · 공중 {def.Air} 지상 {def.Ground} 폭격 {def.Bombing}");
            }

            GUILayout.Space(8);
            GUILayout.Label("파일럿 고용", ProtoGui.Bold);
            GUILayout.Label($"급여는 모두 {bal.SalaryRegular}/턴. 고용비 = {bal.HirePilotCost} + (숙련 − 1) × {bal.HireCostPerSkill}. 후보는 {SquadronRules.TurnsUntilRefresh(w, bal)}턴 뒤 교체", _wrap);
            if (w.HireCandidates.Count == 0) GUILayout.Label("남은 후보 없음 — 다음 교체를 기다리세요", ProtoGui.Small);
            foreach (var cand in w.HireCandidates.ToList())
            {
                var c = cand;
                ActionButton($"{c.Name}  ·  숙련 {c.Skill}  ·  숙련 기종 {bal.Def(c.Aptitude).Name}  ·  {SquadronRules.HireCost(bal, c)}",
                    SquadronRules.CanHire(w, bal, c), () => SquadronRules.Hire(w, bal, c));
            }

            GUILayout.Space(8);
            GUILayout.Label($"예비 기체 판매 (구매가의 {bal.SellRatio:P0}, 손상만큼 감액)", ProtoGui.Bold);
            var reserve = w.ReserveAircraft.ToList();
            if (reserve.Count == 0) GUILayout.Label("예비 기체 없음", ProtoGui.Small);
            foreach (var a in reserve)
            {
                var aircraft = a;
                PanelButton($"{bal.Def(a.Type).Name}  손상 {a.Damage}%  → 판매 {SquadronRules.SellPrice(bal, a)}", () => SquadronRules.Sell(w, bal, aircraft));
            }
            GUILayout.Label("편대에 든 기체는 격납고 탭에서 먼저 빼야 합니다", ProtoGui.Small);
        }

        // ---- 명부 ----

        void DrawPilotsTab()
        {
            var w = _boot.World;
            var bal = _boot.Balance;
            GUILayout.Label($"파일럿 {w.Pilots.Count}명  ·  급여 합계 {SquadronRules.Salaries(w, bal)}/턴", ProtoGui.Bold);
            foreach (var p in w.Pilots)
            {
                var a = w.AircraftById(p.AircraftId);
                var where = a == null ? "대기" : $"{bal.Def(a.Type).Name} · {w.SquadronById(a.SquadronId)?.Name ?? "예비"}";
                var exhausted = SquadronRules.IsExhausted(bal, p);
                GUILayout.Label($"{(p.Named ? "★ " : "")}{p.Name}  숙련 {p.Skill}  숙련 기종 {bal.Def(p.Aptitude).Name}  급여 {bal.Salary(p)}", ProtoGui.Small);
                var state = p.Captured ? "포로" : p.ReturnTurns > 0 ? $"복귀 대기 {p.ReturnTurns}턴" : exhausted ? "탈진" : "";
                GUILayout.Label($"   피로 {p.Fatigue}  경험치 {p.Xp}/{p.Skill * bal.XpPerSkill}  {state}   {where}", state != "" ? _red : ProtoGui.Small);
                if (p.Captured)
                {
                    var pilot = p;
                    ActionButton($"몸값 지불  {bal.RansomCost} (다음 턴 복귀)", SquadronRules.CanRansom(w, bal, p), () => SquadronRules.Ransom(w, bal, pilot));
                }
            }
            var captured = w.Pilots.Count(x => x.Captured);
            if (captured > 0) GUILayout.Label($"포로 {captured}명: 급여의 {bal.CapturedSalaryRatio:P0}를 계속 지급", ProtoGui.Small);
            GUILayout.Space(6);
            GUILayout.Label($"피로: 출격하지 않은 턴마다 −{bal.FatigueRecovery}, 유흥가 등급당 −{bal.FatigueRecoveryPerEntertainmentGrade} 추가. {bal.FatigueExhausted} 이상 탈진", _wrap);
        }

        static string RoleName(AircraftRole r) => r switch
        {
            AircraftRole.Fighter => "제공",
            AircraftRole.Attacker => "공격",
            AircraftRole.Multirole => "다용도",
            AircraftRole.Bomber => "폭격",
            _ => "",
        };

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

        static readonly string[] HelpLines =
        {
            "선택: 왼쪽 클릭 / 패드 South    취소: 오른쪽 클릭 · Esc / East",
            "맵 이동: WASD · 방향키 · 가운데 드래그 / 오른쪽 스틱",
            "확대·축소: 휠 / RT · LT    패드 커서: 왼쪽 스틱",
            "턴 종료: Enter / Start    다시 시작: R",
            "아군 영역 칸을 고르면 거점 패널: 확장·등급·시설 건설",
            "패드 패널 조작: 방향키 위·아래로 초점, South 실행, East 해제",
            "탭 전환: Q / E · LB / RB    (거점 · 격납고 · 출격 · 지상군 · 상점 · 명부)",
            "출격 탭: 편대 선택 → 맵에서 목표 클릭 → 출격 실행 → 디브리핑",
            "어두운 칸 = 시야 밖. 회색 = 마지막 목격. 턴 종료 후 적 턴 요약",
            "예상 수지에 마우스를 올리면 항목별 내역",
            "지상군 탭: 병영에서 고용 → 출동(목표 클릭) → 적 거점 인접에서 점령",
            "상단 바 '신뢰도'를 누르면 상부 통신: 장기 목표·긴급 지시·거부",
            "튜닝 메뉴: F2 (수치 슬라이더·테스터 이름·튜닝 값 내보내기·로그 폴더)",
            "디버그: 출격 탭 맨 아래 '시야 끄기', 격납고 탭 '손상 50·피로 80'",
        };

        Vector2 _helpScroll;

        void DrawHelp()
        {
            // 줄 수에 맞춰 높이를 정하고, 화면이 낮으면 스크롤
            const float lineHeight = 22f;
            var wanted = 16f + 26f + HelpLines.Length * lineHeight + 8f;
            var height = Mathf.Min(wanted, Screen.height - LogHeight - BarHeight - 32f);
            var r = Panel(new Rect(8f, Screen.height - LogHeight - height - 16f, LogWidth, height));
            GUILayout.BeginArea(new Rect(r.x + 10f, r.y + 8f, r.width - 20f, r.height - 16f));
            GUILayout.Label("조작  (F1 / View로 닫기)", ProtoGui.Bold);
            _helpScroll = GUILayout.BeginScrollView(_helpScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            foreach (var line in HelpLines) GUILayout.Label(line, _wrap);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawGameOver()
        {
            var w = _boot.World;
            var st = w.Stats;
            var r = Panel(new Rect(Screen.width * 0.5f - 200f, Screen.height * 0.5f - 130f, 400f, 260f));
            GUILayout.BeginArea(new Rect(r.x + 16f, r.y + 12f, r.width - 32f, r.height - 24f));
            GUILayout.Label(w.Victory ? "승리" : "게임 오버", w.Victory ? _green : ProtoGui.Bold);
            GUILayout.Label(w.GameOver, _wrap);
            GUILayout.Label($"턴 {w.Turn}  ·  자금 {w.Funds:N0}  ·  신뢰도 {w.Trust}", ProtoGui.Small);
            GUILayout.Label($"출격 {st.Sorties}회  ·  피습 {st.BasesAttacked}회  ·  거점 상실 {st.BasesLost}  ·  점령 {st.BasesCaptured}", ProtoGui.Small);
            GUILayout.Label($"지시 완수 {st.DirectivesDone} · 실패 {st.DirectivesFailed} · 거부 {st.DirectivesRefused}", ProtoGui.Small);
            var log = _boot.Log;
            GUILayout.Label($"플레이 {log.RunSeconds / 60f:0.0}분 · 턴당 평균 {log.RunSeconds / Mathf.Max(1, log.TurnsLogged):0}초 — 기록: PlaytestLogs/strategy", ProtoGui.Small);
            GUILayout.FlexibleSpace();
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
