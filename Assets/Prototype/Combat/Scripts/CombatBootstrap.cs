using UnityEngine;

namespace Prototype.Combat
{
    /// <summary>
    /// 전투 프로토타입 진입점. 씬에는 이 오브젝트와 카메라·조명만 두고 나머지는 코드로 생성한다.
    /// 갱신 순서: 스크롤 → 기체 → 카메라 → 근경 → 원경.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class CombatBootstrap : MonoBehaviour
    {
        const float MaxAspect = 2.4f; // 21:9까지 루프 오브젝트가 화면을 덮도록

        [SerializeField] CombatTuning tuning;

        public CombatTuning Tuning => tuning;
        public ProtoControls Controls { get; private set; }
        public ScrollController Scroll { get; private set; }
        public float ElapsedTime { get; private set; }
        public float MapHeight => tuning.mapHeightScreens * _cameraRig.HalfHeight * 2f;

        PlayerShip _player;
        CameraRig _cameraRig;
        ScrollWorld _world;
        ParallaxBackground _background;

        void Awake()
        {
            if (tuning == null)
            {
                Debug.LogWarning("[CombatProto] CombatTuning이 지정되지 않아 기본값 인스턴스를 사용합니다.");
                tuning = ScriptableObject.CreateInstance<CombatTuning>();
            }

            Controls = new ProtoControls(tuning.padLayout);
            Scroll = new ScrollController(tuning);
            _cameraRig = new CameraRig(tuning, Camera.main);

            var maxHalfWidth = _cameraRig.HalfHeight * MaxAspect;
            _world = new ScrollWorld(maxHalfWidth);
            _background = new ParallaxBackground(maxHalfWidth);
            _player = new PlayerShip(tuning, new Color(0.75f, 0.8f, 0.85f));

            gameObject.AddComponent<DebugMenu>().Init(this);
            gameObject.AddComponent<CombatHud>().Init(this);

            ResetRun();
        }

        public void ResetRun()
        {
            Scroll.Reset();
            ElapsedTime = 0f;
            _player.Reset(MapHeight * 0.3f);
            _cameraRig.Snap(_player.Y, MapHeight);
            Tick(0f);
        }

        void Update()
        {
            Controls.SetLayout(tuning.padLayout);
            if (Controls.Restart.WasPressedThisFrame()) ResetRun();

            var dt = Time.deltaTime;
            ElapsedTime += dt;
            Tick(dt);
        }

        void Tick(float dt)
        {
            var move = Controls.Move.ReadValue<Vector2>();
            var mapHeight = MapHeight;

            Scroll.Tick(Controls.Horizontal, dt);
            _player.Tick(move.y, dt, mapHeight, tuning.playerScreenX * _cameraRig.HalfWidth * 2f);
            _cameraRig.Tick(_player.Y, dt, mapHeight);
            _world.Tick(Scroll.Distance, mapHeight);
            _background.Tick(Scroll.Distance, _cameraRig.Y, _cameraRig.HalfHeight, mapHeight);
        }

        void OnDestroy()
        {
            Controls?.Dispose();
        }
    }
}
