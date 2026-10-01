using System.Collections.Generic;
using System.Linq;
using Prototype.Strategy.Rules;
using UnityEngine;

namespace Prototype.Strategy
{
    /// <summary>
    /// 전략 프로토타입 진입점. 씬에는 이 오브젝트와 카메라만 두고 나머지는 코드로 만든다.
    /// 규칙 상태(World)와 명령(BaseRules·Economy)은 Prototype.Strategy.Rules에 있고, 여기서는 입력·표시만 다룬다.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class StrategyBootstrap : MonoBehaviour
    {
        static readonly Color ExpandColor = new(1f, 1f, 1f, 0.35f);

        [SerializeField] ulong seed = 20260930;

        public ulong Seed => seed;
        public Balance Balance { get; private set; }
        public World World { get; private set; }
        public MapCursor Cursor { get; private set; }
        public MapCamera MapCamera { get; private set; }
        public Hex? Selected { get; set; }

        /// <summary>영역 확장 모드인 거점 (null이면 일반 모드).</summary>
        public Base ExpandBase { get; private set; }

        StrategyControls _controls;
        HexMapView _view;
        StrategyHud _hud;
        readonly List<Hex> _expandCandidates = new();

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
            Selected = null;
            ExpandBase = null;
        }

        public void EndTurn()
        {
            ExpandBase = null;
            Economy.EndTurn(World, Balance);
        }

        public void StartExpand(Base b) => ExpandBase = b;

        public void StopExpand() => ExpandBase = null;

        void Update()
        {
            var dt = Time.unscaledDeltaTime;
            var mouse = _controls.Point.ReadValue<Vector2>();
            var overUi = _hud.IsOverUi(mouse);
            var typing = GUIUtility.keyboardControl != 0;

            if (_controls.DebugToggle.WasPressedThisFrame()) _hud.ToggleHelp();
            if (!typing && _controls.Restart.WasPressedThisFrame()) Restart();
            if (!typing && _controls.EndTurn.WasPressedThisFrame()) EndTurn();

            if (!typing) MapCamera.Pan(_controls.Pan.ReadValue<Vector2>(), dt);
            if (_controls.DragButton.IsPressed()) MapCamera.Drag(_controls.DragDelta.ReadValue<Vector2>());
            if (!overUi) MapCamera.ZoomWheel(_controls.Scroll.ReadValue<float>(), mouse);

            Cursor.Tick(mouse, _controls.PadCursor.ReadValue<Vector2>(), dt, overUi);
            MapCamera.ZoomPad(_controls.PadZoom.ReadValue<float>(), dt, MapCamera.WorldToScreen(Cursor.World));

            _expandCandidates.Clear();
            if (ExpandBase != null && World.GameOver == null)
                _expandCandidates.AddRange(BaseRules.ExpandCandidates(World, ExpandBase));

            // 패드 패널 초점: 방향키 위·아래로 이동, 스틱으로 커서를 움직이면 맵으로 돌아온다
            if (_controls.PanelUp.WasPressedThisFrame()) _hud.PadFocusMove(-1);
            if (_controls.PanelDown.WasPressedThisFrame()) _hud.PadFocusMove(1);
            if (_controls.PadCursor.ReadValue<Vector2>().sqrMagnitude > 0.04f) _hud.PadFocusClear();

            var mouseSelect = _controls.MouseSelect.WasPressedThisFrame() && !overUi;
            var padSelect = _controls.PadSelect.WasPressedThisFrame();
            if (padSelect && _hud.HasPadFocus) _hud.PadActivate();
            else if ((mouseSelect || padSelect) && World.GameOver == null) OnSelect(Cursor.Hover);

            if (_controls.Cancel.WasPressedThisFrame())
            {
                if (_hud.HasPadFocus) _hud.PadFocusClear();
                else if (ExpandBase != null) ExpandBase = null; // 확장 모드부터 빠져나온다
                else Selected = null;
            }

            _view.UpdateTerritory(World);
            _view.UpdateMarkers(Cursor.Hover, Selected, _expandCandidates, ExpandColor);
        }

        void OnSelect(Hex? hover)
        {
            if (ExpandBase != null)
            {
                // 확장 모드: 후보 칸을 누르면 확장하고 모드를 유지, 그 밖을 누르면 모드 종료
                if (hover is { } h && _expandCandidates.Contains(h)) BaseRules.Expand(World, Balance, ExpandBase, h);
                else ExpandBase = null;
                return;
            }
            Selected = hover;
        }

        /// <summary>확장 후보 칸 (HUD가 비용 표시에 사용).</summary>
        public IReadOnlyList<Hex> ExpandCandidates => _expandCandidates;

        public bool IsExpandCandidate(Hex h) => _expandCandidates.Contains(h);

        void OnDestroy() => _controls?.Dispose();
    }
}
