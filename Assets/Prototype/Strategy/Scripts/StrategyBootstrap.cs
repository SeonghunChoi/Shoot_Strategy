using Prototype.Strategy.Rules;
using UnityEngine;

namespace Prototype.Strategy
{
    /// <summary>
    /// 전략 프로토타입 진입점. 씬에는 이 오브젝트와 카메라만 두고 나머지는 코드로 만든다.
    /// 규칙 상태(World)는 Prototype.Strategy.Rules에 있고, 여기서는 입력·표시만 다룬다.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class StrategyBootstrap : MonoBehaviour
    {
        [SerializeField] ulong seed = 20260930;

        public ulong Seed => seed;
        public World World { get; private set; }
        public MapCursor Cursor { get; private set; }
        public Hex? Selected { get; private set; }

        StrategyControls _controls;
        MapCamera _camera;
        HexMapView _view;
        StrategyHud _hud;

        void Awake()
        {
            World = World.Create(seed);
            _controls = new StrategyControls();

            var bounds = HexLayout.Bounds(World.Map);
            _camera = new MapCamera(Camera.main, bounds);
            _view = new HexMapView(World.Map, transform);
            Cursor = new MapCursor(World.Map, _camera, bounds);

            _hud = gameObject.AddComponent<StrategyHud>();
            _hud.Init(this);
            AutoScreenshot.AttachIfRequested(gameObject);
        }

        void Update()
        {
            var dt = Time.unscaledDeltaTime;
            var mouse = _controls.Point.ReadValue<Vector2>();
            var overUi = _hud.IsOverUi(mouse);

            if (_controls.DebugToggle.WasPressedThisFrame()) _hud.ToggleHelp();

            // 입력칸 타이핑 중에는 키보드 이동 무시 (이후 디버그 메뉴 대비)
            if (GUIUtility.keyboardControl == 0) _camera.Pan(_controls.Pan.ReadValue<Vector2>(), dt);
            if (_controls.DragButton.IsPressed()) _camera.Drag(_controls.DragDelta.ReadValue<Vector2>());
            if (!overUi) _camera.ZoomWheel(_controls.Scroll.ReadValue<float>(), mouse);

            Cursor.Tick(mouse, _controls.PadCursor.ReadValue<Vector2>(), dt, overUi);
            var padAnchor = _camera.WorldToScreen(Cursor.World);
            _camera.ZoomPad(_controls.PadZoom.ReadValue<float>(), dt, padAnchor);

            if (_controls.MouseSelect.WasPressedThisFrame() && !overUi || _controls.PadSelect.WasPressedThisFrame())
                Selected = Cursor.Hover;
            if (_controls.Cancel.WasPressedThisFrame()) Selected = null;

            _view.UpdateMarkers(Cursor.Hover, Selected);
        }

        void OnDestroy() => _controls?.Dispose();
    }
}
