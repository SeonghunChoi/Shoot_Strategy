using System.Collections.Generic;
using System.Linq;
using Prototype.Strategy.Rules;
using UnityEngine;

namespace Prototype.Strategy
{
    public enum PanelTab
    {
        Base,
        Hangar,
        Sortie,
        Shop,
        Pilots,
    }

    /// <summary>
    /// 전략 프로토타입 진입점. 씬에는 이 오브젝트와 카메라만 두고 나머지는 코드로 만든다.
    /// 규칙 상태(World)와 명령(BaseRules·SquadronRules·SortieRules·Economy)은 Prototype.Strategy.Rules에 있고, 여기서는 입력·표시만 다룬다.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class StrategyBootstrap : MonoBehaviour
    {
        static readonly Color ExpandColor = new(1f, 1f, 1f, 0.35f);
        static readonly Color RadiusColor = new(0.3f, 0.75f, 1f, 0.35f);
        static readonly Color MoveColor = new(0.4f, 1f, 0.5f, 0.4f);
        static readonly Color TargetColor = new(1f, 0.6f, 0.15f, 0.45f);
        static readonly Color OutpostColor = new(0.9f, 0.9f, 0.5f, 0.4f);

        [SerializeField] ulong seed = 20260930;

        public ulong Seed => seed;
        public Balance Balance { get; private set; }
        public World World { get; private set; }
        public MapCursor Cursor { get; private set; }
        public MapCamera MapCamera { get; private set; }
        public Hex? Selected { get; set; }
        public PanelTab Tab { get; set; }

        /// <summary>영역 확장 모드인 거점 (null이면 일반 모드).</summary>
        public Base ExpandBase { get; private set; }
        /// <summary>격납고 패널에서 고른 편대 (작전 반경 표시).</summary>
        public int SelectedSquadronId { get; set; }
        /// <summary>재배치 목적지를 고르는 중인 편대 (null이면 아님).</summary>
        public Squadron MovingSquadron { get; private set; }
        /// <summary>출격 목표를 고르는 중인 편대 (null이면 아님).</summary>
        public Squadron TargetPickSquadron { get; private set; }
        public IReadOnlyList<Hex> TargetCandidates => _targetCandidates;
        /// <summary>전초기지 위치를 고르는 중.</summary>
        public bool OutpostMode { get; private set; }
        public IReadOnlyList<Hex> OutpostCandidates => _outpostCandidates;
        public bool ShowDebrief { get; set; }
        /// <summary>적 턴 요약 창.</summary>
        public bool ShowEnemyReport { get; set; }
        public HashSet<Hex> Visible { get; private set; } = new();
        public int DebriefIndex { get; private set; }
        /// <summary>미실행 출격 계획이 있는 채로 턴 종료를 한 번 눌렀는지 (두 번째 누르면 취소하고 종료).</summary>
        public bool EndTurnWarned { get; private set; }

        StrategyControls _controls;
        HexMapView _view;
        StrategyHud _hud;
        readonly List<Hex> _expandCandidates = new();
        readonly List<Hex> _targetCandidates = new();
        readonly List<Hex> _outpostCandidates = new();
        readonly List<Hex> _highlight = new();

        void Awake()
        {
            Balance = new Balance();
            World = World.Create(seed, Balance);
            _controls = new StrategyControls();

            var bounds = HexLayout.Bounds(World.Map);
            MapCamera = new MapCamera(Camera.main, bounds);
            _view = new HexMapView(World.Map, transform);
            Cursor = new MapCursor(World.Map, MapCamera, bounds);

            _hud = gameObject.AddComponent<StrategyHud>();
            _hud.Init(this);
            AutoScreenshot.AttachIfRequested(gameObject);
        }

        public void Restart()
        {
            World = World.Create(seed, Balance);
            ShowEnemyReport = false;
            Selected = null;
            ClearModes();
            SelectedSquadronId = 0;
            ShowDebrief = false;
        }

        void ClearModes()
        {
            ExpandBase = null;
            MovingSquadron = null;
            TargetPickSquadron = null;
            OutpostMode = false;
            EndTurnWarned = false;
        }

        public void StartOutpost()
        {
            ClearModes();
            OutpostMode = true;
            Tab = PanelTab.Base;
        }

        public void StopOutpost() => OutpostMode = false;

        /// <summary>턴 종료. 미실행 출격 계획이 있으면 한 번은 경고만 하고, 다시 누르면 계획을 버리고 종료한다.</summary>
        public void EndTurn()
        {
            if (World.GameOver != null) return;
            if (World.SortiePlan.Count > 0 && !EndTurnWarned)
            {
                EndTurnWarned = true;
                Tab = PanelTab.Sortie;
                return;
            }
            ClearModes();
            Economy.EndTurn(World, Balance);
            ShowEnemyReport = World.EnemyEvents.Count > 0 || World.Intercepts.Count > 0;
        }

        public void StartExpand(Base b)
        {
            ClearModes();
            ExpandBase = b;
        }

        public void StopExpand() => ExpandBase = null;

        public void StartMove(Squadron s)
        {
            ClearModes();
            MovingSquadron = s;
            SelectedSquadronId = s.Id;
        }

        public void StopMove() => MovingSquadron = null;

        public void StartTargetPick(Squadron s)
        {
            ClearModes();
            TargetPickSquadron = s;
            SelectedSquadronId = s.Id;
            Tab = PanelTab.Sortie;
        }

        public void StopTargetPick() => TargetPickSquadron = null;

        public void ExecuteSorties()
        {
            if (World.SortiePlan.Count == 0) return;
            ClearModes();
            SortieRules.ExecuteAll(World, Balance);
            OpenDebrief(0);
        }

        public void OpenDebrief(int index)
        {
            DebriefIndex = index;
            ShowDebrief = World.Debriefs.Count > 0;
        }

        void Update()
        {
            var dt = Time.unscaledDeltaTime;
            var mouse = _controls.Point.ReadValue<Vector2>();
            var overUi = _hud.IsOverUi(mouse) || ShowDebrief || ShowEnemyReport;
            var typing = GUIUtility.keyboardControl != 0;

            if (_controls.DebugToggle.WasPressedThisFrame()) _hud.ToggleHelp();
            if (!typing && _controls.Restart.WasPressedThisFrame()) Restart();
            if (!typing && _controls.EndTurn.WasPressedThisFrame() && !ShowDebrief && !ShowEnemyReport) EndTurn();
            if (!typing && _controls.TabPrev.WasPressedThisFrame()) ShiftTab(-1);
            if (!typing && _controls.TabNext.WasPressedThisFrame()) ShiftTab(1);

            if (!typing) MapCamera.Pan(_controls.Pan.ReadValue<Vector2>(), dt);
            if (_controls.DragButton.IsPressed()) MapCamera.Drag(_controls.DragDelta.ReadValue<Vector2>());
            if (!overUi) MapCamera.ZoomWheel(_controls.Scroll.ReadValue<float>(), mouse);

            Cursor.Tick(mouse, _controls.PadCursor.ReadValue<Vector2>(), dt, overUi);
            MapCamera.ZoomPad(_controls.PadZoom.ReadValue<float>(), dt, MapCamera.WorldToScreen(Cursor.World));

            _expandCandidates.Clear();
            if (ExpandBase != null && World.GameOver == null)
                _expandCandidates.AddRange(BaseRules.ExpandCandidates(World, ExpandBase));
            _outpostCandidates.Clear();
            if (OutpostMode && World.GameOver == null)
                _outpostCandidates.AddRange(BaseRules.OutpostCandidates(World, Balance));
            _targetCandidates.Clear();
            if (TargetPickSquadron != null && World.GameOver == null)
                _targetCandidates.AddRange(SortieRules.Targets(World, Balance, TargetPickSquadron)
                    .Where(h => SortieRules.CanPlan(World, Balance, TargetPickSquadron, h) == null));

            // 패드 패널 초점: 방향키 위·아래로 이동, 스틱으로 커서를 움직이면 맵으로 돌아온다
            if (_controls.PanelUp.WasPressedThisFrame()) _hud.PadFocusMove(-1);
            if (_controls.PanelDown.WasPressedThisFrame()) _hud.PadFocusMove(1);
            if (_controls.PadCursor.ReadValue<Vector2>().sqrMagnitude > 0.04f) _hud.PadFocusClear();

            var mouseSelect = _controls.MouseSelect.WasPressedThisFrame() && !overUi;
            var padSelect = _controls.PadSelect.WasPressedThisFrame();
            if (padSelect && _hud.HasPadFocus) _hud.PadActivate();
            else if ((mouseSelect || padSelect) && World.GameOver == null && !ShowDebrief && !ShowEnemyReport) OnSelect(Cursor.Hover);

            if (_controls.Cancel.WasPressedThisFrame())
            {
                if (ShowEnemyReport) ShowEnemyReport = false;
                else if (ShowDebrief) ShowDebrief = false;
                else if (_hud.HasPadFocus) _hud.PadFocusClear();
                else if (ExpandBase != null || MovingSquadron != null || TargetPickSquadron != null || OutpostMode) ClearModes();
                else Selected = null;
            }

            if (World.SquadronById(SelectedSquadronId) == null) SelectedSquadronId = 0;
            if (MovingSquadron != null && !World.Squadrons.Contains(MovingSquadron)) MovingSquadron = null;
            if (TargetPickSquadron != null && !World.Squadrons.Contains(TargetPickSquadron)) TargetPickSquadron = null;
            if (EndTurnWarned && World.SortiePlan.Count == 0) EndTurnWarned = false;

            Visible = Vision.VisibleHexes(World, Balance);
            _view.UpdateTerritory(World, Visible, Vision.StaleSightings(World, Balance));
            BuildHighlight(out var color);
            _view.UpdateMarkers(Cursor.Hover, Selected, _highlight, color);
        }

        /// <summary>맵 강조: 전초기지 후보 > 출격 목표 후보 > 재배치 후보 거점 > 확장 후보 > 선택 편대의 작전 반경.</summary>
        void BuildHighlight(out Color color)
        {
            _highlight.Clear();
            if (OutpostMode)
            {
                color = OutpostColor;
                _highlight.AddRange(_outpostCandidates);
                return;
            }
            if (TargetPickSquadron != null)
            {
                color = TargetColor;
                _highlight.AddRange(_targetCandidates);
                return;
            }
            if (MovingSquadron != null)
            {
                color = MoveColor;
                foreach (var b in World.Bases)
                    if (SquadronRules.CanMove(World, Balance, MovingSquadron, b) == null) _highlight.Add(b.Hex);
                return;
            }
            if (_expandCandidates.Count > 0)
            {
                color = ExpandColor;
                _highlight.AddRange(_expandCandidates);
                return;
            }
            color = RadiusColor;
            var s = World.SquadronById(SelectedSquadronId);
            if (s != null && (Tab == PanelTab.Hangar || Tab == PanelTab.Sortie))
            {
                var b = World.BaseById(s.BaseId);
                foreach (var h in Hex.Range(b.Hex, SquadronRules.OperationRadius(World, Balance, s)))
                    if (World.Map.Contains(h)) _highlight.Add(h);
            }
        }

        void ShiftTab(int delta)
        {
            var n = System.Enum.GetValues(typeof(PanelTab)).Length;
            Tab = (PanelTab)(((int)Tab + delta + n) % n);
            _hud.PadFocusClear();
        }

        void OnSelect(Hex? hover)
        {
            if (OutpostMode)
            {
                // 전초기지 모드: 후보 칸을 누르면 건설하고 그 거점을 선택, 그 밖을 누르면 모드 종료
                if (hover is { } h && _outpostCandidates.Contains(h))
                {
                    var nb = BaseRules.BuildOutpost(World, Balance, h);
                    if (nb != null) Selected = nb.Hex;
                }
                OutpostMode = false;
                return;
            }
            if (TargetPickSquadron != null)
            {
                // 목표 선택 모드: 후보 칸을 누르면 계획에 추가, 그 밖을 누르면 모드 종료
                if (hover is { } h && _targetCandidates.Contains(h)) SortieRules.Plan(World, Balance, TargetPickSquadron, h);
                TargetPickSquadron = null;
                return;
            }
            if (MovingSquadron != null)
            {
                // 재배치 모드: 후보 거점 본부 칸을 누르면 이동 명령, 그 밖을 누르면 모드 종료
                if (hover is { } h && World.BaseAt(h) is { } to && SquadronRules.CanMove(World, Balance, MovingSquadron, to) == null)
                    SquadronRules.Move(World, Balance, MovingSquadron, to);
                MovingSquadron = null;
                return;
            }
            if (ExpandBase != null)
            {
                // 확장 모드: 후보 칸을 누르면 확장하고 모드를 유지, 그 밖을 누르면 모드 종료
                if (hover is { } h && _expandCandidates.Contains(h)) BaseRules.Expand(World, Balance, ExpandBase, h);
                else ExpandBase = null;
                return;
            }
            Selected = hover;
            // 거점 칸을 고르면 거점 탭으로 (격납고·출격을 보던 중이면 유지)
            if (hover is { } hx && World.BaseOf(hx) is { Owner: World.Player } && Tab != PanelTab.Hangar && Tab != PanelTab.Sortie) Tab = PanelTab.Base;
        }

        /// <summary>확장 후보 칸 (HUD가 비용 표시에 사용).</summary>
        public IReadOnlyList<Hex> ExpandCandidates => _expandCandidates;

        void OnDestroy() => _controls?.Dispose();
    }
}
