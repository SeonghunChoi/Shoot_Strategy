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
        Ground,
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
        static readonly Color GroundColor = new(0.45f, 0.9f, 0.35f, 0.45f);

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
        /// <summary>출격·이동 목적지를 고르는 중인 편대 (null이면 아님). C안: 출격 = 이륙해서 그 칸까지 비행.</summary>
        public Squadron TargetPickSquadron { get; private set; }
        public IReadOnlyList<Hex> TargetCandidates => _targetCandidates;
        /// <summary>전초기지 위치를 고르는 중.</summary>
        public bool OutpostMode { get; private set; }
        public IReadOnlyList<Hex> OutpostCandidates => _outpostCandidates;
        public bool ShowDebrief { get; set; }
        /// <summary>적 턴 요약 창.</summary>
        public bool ShowEnemyReport { get; set; }
        /// <summary>상부 통신 창.</summary>
        public bool ShowHq { get; set; }
        /// <summary>출동 목표를 고르는 중인 지상군 (null이면 아님).</summary>
        public GroundUnit GroundPick { get; private set; }
        public HashSet<Hex> Visible { get; private set; } = new();
        /// <summary>고른 칸의 보이는 적 부대와 그 다음 턴 예상 (없으면 null).</summary>
        public EnemyUnit SelectedEnemy { get; private set; }
        public EnemyTurn.Prediction SelectedEnemyPrediction { get; private set; }
        public int DebriefIndex { get; private set; }
        /// <summary>출격·적 턴 이동 재생 (재생 중에는 맵·패널 조작을 막는다).</summary>
        public MovePlayback Playback { get; } = new();
        /// <summary>미실행 출격 계획이 있는 채로 턴 종료를 한 번 눌렀는지 (두 번째 누르면 취소하고 종료).</summary>
        public bool EndTurnWarned { get; private set; }
        /// <summary>연료가 바닥나 이번 턴을 하늘에서 마치면 불시착할 편대가 있는 채로 턴 종료를 한 번 눌렀는지.</summary>
        public bool FuelWarned { get; private set; }

        StrategyControls _controls;
        HexMapView _view;
        StrategyHud _hud;
        readonly List<Hex> _expandCandidates = new();
        readonly List<Hex> _targetCandidates = new();
        readonly List<Hex> _outpostCandidates = new();
        readonly List<Hex> _highlight = new();

        const string TesterPrefKey = "StrategyProto.Tester";

        /// <summary>플레이 로그 (PlaytestLogs/strategy, git 제외).</summary>
        public PlaytestLog Log { get; private set; }
        /// <summary>이번 턴에 플레이어가 실제로 쓴 시간 (창이 비활성이면 멈춤, S8).</summary>
        public float TurnSeconds { get; private set; }
        StrategyTuningMenu _tuning;

        public static string LogFolder => System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "PlaytestLogs", "strategy"));

        void Awake()
        {
            Balance = new Balance();
            World = World.Create(seed, Balance);
            _controls = new StrategyControls();
            Log = new PlaytestLog(LogFolder) { Tester = PlayerPrefs.GetString(TesterPrefKey, "") };
            Log.BeginRun(World, seed);

            var bounds = HexLayout.Bounds(World.Map);
            MapCamera = new MapCamera(Camera.main, bounds);
            _view = new HexMapView(World.Map, transform);
            Cursor = new MapCursor(World.Map, MapCamera, bounds);

            _hud = gameObject.AddComponent<StrategyHud>();
            _hud.Init(this);
            _tuning = gameObject.AddComponent<StrategyTuningMenu>();
            _tuning.Init(this);
            AutoScreenshot.AttachIfRequested(gameObject);
        }

        public void SetTester(string name)
        {
            Log.Tester = name;
            PlayerPrefs.SetString(TesterPrefKey, name);
        }

        public void OpenLogFolder()
        {
            System.IO.Directory.CreateDirectory(LogFolder);
            Application.OpenURL("file:///" + LogFolder.Replace('\\', '/'));
        }

        public void ToggleTuning() => _tuning.Toggle();

        public void Restart()
        {
            // 진행 중이던 판은 "중단"으로 기록
            if (Log.Running) Log.EndRun(World, Balance, "중단", TurnSeconds);
            World = World.Create(seed, Balance);
            Log.BeginRun(World, seed);
            TurnSeconds = 0f;
            Playback.Start(System.Array.Empty<MoveTrace>(), null);
            ShowEnemyReport = false;
            ShowHq = false;
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
            GroundPick = null;
            EndTurnWarned = false;
            FuelWarned = false;
        }

        public void StartOutpost()
        {
            ClearModes();
            OutpostMode = true;
            Tab = PanelTab.Base;
        }

        public void StopOutpost() => OutpostMode = false;

        public void StartGroundPick(GroundUnit g)
        {
            ClearModes();
            GroundPick = g;
            Tab = PanelTab.Ground;
        }

        public void StopGroundPick() => GroundPick = null;

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
            var crashing = World.Squadrons.Where(s => s.AirHex != null && s.Fuel < Balance.IdleFuelPerTurn).ToList();
            if (crashing.Count > 0 && !FuelWarned)
            {
                FuelWarned = true;
                Tab = PanelTab.Sortie;
                return;
            }
            ClearModes();
            var gaps = World.Bases.Count(b => b.Owner == World.Player && World.SquadronsAt(b).Any() && World.SquadronsAt(b).All(s => s.AirHex != null)); // 적 턴 직전 방어 공백 (S1)
            var report = Economy.EndTurn(World, Balance);
            Log.LogTurn(World, report, TurnSeconds, gaps);
            TurnSeconds = 0f;
            if (World.GameOver != null) Log.EndRun(World, Balance, ResultName(World));
            // 적 턴 이동을 맵에서 재생한 뒤 요약 창
            var showReport = World.EnemyEvents.Count > 0 || World.Intercepts.Count > 0;
            ShowEnemyReport = false;
            Playback.Start(World.Traces, () => ShowEnemyReport = showReport);
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

        /// <summary>비행 명령 (이륙·이동·착륙·교전은 바로 정산) → 맵 재생 → 교전이 있었으면 디브리핑.</summary>
        public void OrderFlight(Squadron s, Hex dest)
        {
            ClearModes();
            var rep = FlightRules.Order(World, Balance, s, dest);
            SelectedSquadronId = s.Id;
            Playback.Start(World.Traces, () =>
            {
                if (rep != null) OpenDebrief(World.Debriefs.IndexOf(rep));
            });
        }

        public void ExecuteSorties()
        {
            if (World.SortiePlan.Count == 0) return;
            ClearModes();
            SortieRules.ExecuteAll(World, Balance);
            // 편대 비행·교전을 맵에서 재생한 뒤 디브리핑
            Playback.Start(World.Traces, () => OpenDebrief(0));
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
            var overUi = _hud.IsOverUi(mouse) || _tuning.IsOver(mouse) || ShowDebrief || ShowEnemyReport;
            if (Application.isFocused && World.GameOver == null) TurnSeconds += Time.unscaledDeltaTime;
            if (_controls.TuningToggle.WasPressedThisFrame()) _tuning.Toggle();
            var typing = GUIUtility.keyboardControl != 0;

            // 이동 재생 중: 클릭·Space·South·Esc로 건너뛰기, 그 밖의 명령은 막는다 (카메라는 움직일 수 있음)
            var playing = Playback.Playing;
            if (playing)
            {
                Playback.Tick(dt);
                if (_controls.Skip.WasPressedThisFrame() || _controls.MouseSelect.WasPressedThisFrame() ||
                    _controls.PadSelect.WasPressedThisFrame() || _controls.Cancel.WasPressedThisFrame())
                    Playback.Skip();
            }

            if (_controls.DebugToggle.WasPressedThisFrame()) _hud.ToggleHelp();
            if (!typing && !playing && _controls.Restart.WasPressedThisFrame()) Restart();
            if (!typing && !playing && _controls.EndTurn.WasPressedThisFrame() && !ShowDebrief && !ShowEnemyReport) EndTurn();
            if (!typing && !playing && _controls.TabPrev.WasPressedThisFrame()) ShiftTab(-1);
            if (!typing && !playing && _controls.TabNext.WasPressedThisFrame()) ShiftTab(1);

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
                _targetCandidates.AddRange(FlightRules.Destinations(World, Balance, TargetPickSquadron));

            // 패드 패널 초점: 방향키 위·아래로 이동, 스틱으로 커서를 움직이면 맵으로 돌아온다
            if (_controls.PanelUp.WasPressedThisFrame()) _hud.PadFocusMove(-1);
            if (_controls.PanelDown.WasPressedThisFrame()) _hud.PadFocusMove(1);
            if (_controls.PadCursor.ReadValue<Vector2>().sqrMagnitude > 0.04f) _hud.PadFocusClear();

            var mouseSelect = !playing && _controls.MouseSelect.WasPressedThisFrame() && !overUi;
            var padSelect = !playing && _controls.PadSelect.WasPressedThisFrame();
            if (padSelect && _hud.HasPadFocus) _hud.PadActivate();
            else if ((mouseSelect || padSelect) && World.GameOver == null && !ShowDebrief && !ShowEnemyReport) OnSelect(Cursor.Hover);

            if (!playing && _controls.Cancel.WasPressedThisFrame())
            {
                if (ShowEnemyReport) ShowEnemyReport = false;
                else if (ShowDebrief) ShowDebrief = false;
                else if (_hud.HasPadFocus) _hud.PadFocusClear();
                else if (ShowHq) ShowHq = false;
                else if (ExpandBase != null || MovingSquadron != null || TargetPickSquadron != null || OutpostMode || GroundPick != null) ClearModes();
                else Selected = null;
            }

            if (World.SquadronById(SelectedSquadronId) == null) SelectedSquadronId = 0;
            if (MovingSquadron != null && !World.Squadrons.Contains(MovingSquadron)) MovingSquadron = null;
            if (TargetPickSquadron != null && !World.Squadrons.Contains(TargetPickSquadron)) TargetPickSquadron = null;
            if (GroundPick != null && !World.GroundUnits.Contains(GroundPick)) GroundPick = null;
            if (EndTurnWarned && World.SortiePlan.Count == 0) EndTurnWarned = false;

            Visible = Vision.VisibleHexes(World, Balance);
            _view.UpdateTerritory(World, Balance, Visible, Vision.StaleSightings(World, Balance), Playback);
            _view.UpdatePlayback(Playback, Balance);
            BuildHighlight(out var color);
            _view.UpdateMarkers(Cursor.Hover, Selected, _highlight, color);

            // 고른 칸에 보이는 적 부대가 있으면 다음 턴 예상 경로
            SelectedEnemy = Selected is { } sh && Visible.Contains(sh) ? World.EnemyUnitAt(sh) : null;
            SelectedEnemyPrediction = SelectedEnemy != null && World.GameOver == null ? EnemyTurn.Predict(World, Balance, SelectedEnemy) : null;
            _view.UpdatePrediction(SelectedEnemy, SelectedEnemyPrediction);
        }

        /// <summary>맵 강조: 전초기지 후보 > 출격 목표 후보 > 재배치 후보 거점 > 확장 후보 > 선택 편대의 작전 반경.</summary>
        void BuildHighlight(out Color color)
        {
            _highlight.Clear();
            if (GroundPick != null)
            {
                // 출동 목표 후보: 적 거점(점령)과 아군 거점(주둔). 그 밖의 육지 칸도 클릭하면 그 자리로 이동
                color = GroundColor;
                foreach (var b in World.Bases)
                    if (GroundRules.CanDeploy(World, Balance, GroundPick, b.Hex) == null) _highlight.Add(b.Hex);
                return;
            }
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
            if (s != null && s.AirHex is { } air && (Tab == PanelTab.Hangar || Tab == PanelTab.Sortie))
            {
                // 비행 중: 이번 턴 갈 수 있는 범위
                foreach (var h in Hex.Range(air, FlightRules.Reach(World, Balance, s)))
                    if (World.Map.Contains(h)) _highlight.Add(h);
            }
            else if (s != null && (Tab == PanelTab.Hangar || Tab == PanelTab.Sortie))
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
            if (GroundPick != null)
            {
                // 지상군 출동 모드: 갈 수 있는 칸을 누르면 출동(또는 목표 변경), 그 밖을 누르면 모드 종료
                if (hover is { } h && GroundRules.CanDeploy(World, Balance, GroundPick, h) == null) GroundRules.Deploy(World, Balance, GroundPick, h);
                GroundPick = null;
                return;
            }
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
                // 비행 명령 모드: 후보 칸을 누르면 바로 비행(교전·착륙 포함), 그 밖을 누르면 모드 종료
                var picking = TargetPickSquadron;
                TargetPickSquadron = null;
                if (hover is { } h && _targetCandidates.Contains(h)) OrderFlight(picking, h);
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
            // 비행 중 편대 칸을 고르면 그 편대를 출격 탭에서
            if (hover is { } fh && World.Squadrons.FirstOrDefault(x => x.AirHex == fh) is { } flying)
            {
                SelectedSquadronId = flying.Id;
                Tab = PanelTab.Sortie;
                return;
            }
            // 거점 칸을 고르면 거점 탭으로 (격납고·출격을 보던 중이면 유지)
            if (hover is { } hx && World.BaseOf(hx) is { Owner: World.Player } && Tab != PanelTab.Hangar && Tab != PanelTab.Sortie) Tab = PanelTab.Base;
        }

        /// <summary>확장 후보 칸 (HUD가 비용 표시에 사용).</summary>
        public IReadOnlyList<Hex> ExpandCandidates => _expandCandidates;

        static string ResultName(World w)
        {
            if (w.Victory) return "승리";
            if (w.GameOver == null) return "중단";
            if (w.GameOver.StartsWith("해임")) return "해임";
            if (w.GameOver.StartsWith("파산")) return "파산";
            if (w.GameOver.StartsWith("모든 거점")) return "상실";
            return w.GameOver;
        }

        void OnDestroy()
        {
            if (Log != null && Log.Running) Log.EndRun(World, Balance, "중단", TurnSeconds);
            _controls?.Dispose();
        }
    }
}
