using UnityEngine;

namespace Prototype.Combat
{
    /// <summary>
    /// 전투 프로토타입 진입점. 씬에는 이 오브젝트와 카메라·조명만 두고 나머지는 코드로 생성한다.
    /// 갱신 순서: 스크롤 → 기체 → 카메라 → 근경·원경 → 발사 → 적 → 무장 → 탄 → 판정 → 효과 → 탄 렌더.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class CombatBootstrap : MonoBehaviour
    {
        const float MaxAspect = 2.4f; // 21:9까지 루프 오브젝트가 화면을 덮도록
        const float BulletCullMargin = 8f;

        [SerializeField] CombatTuning tuning;

        public CombatTuning Tuning => tuning;
        public CombatContext Ctx { get; private set; }
        public ProtoControls Controls => Ctx.Controls;
        public ScrollController Scroll => Ctx.Scroll;
        public float ElapsedTime { get; private set; }

        ParallaxBackground _background;

        void Awake()
        {
            if (tuning == null)
            {
                Debug.LogWarning("[CombatProto] CombatTuning이 지정되지 않아 기본값 인스턴스를 사용합니다.");
                tuning = ScriptableObject.CreateInstance<CombatTuning>();
            }

            var ctx = Ctx = new CombatContext { Tuning = tuning };
            ctx.Controls = new ProtoControls(tuning.padLayout);
            ctx.Scroll = new ScrollController(tuning);
            ctx.Camera = new CameraRig(tuning, Camera.main);

            var maxHalfWidth = ctx.Camera.HalfHeight * MaxAspect;
            ctx.World = new ScrollWorld(maxHalfWidth);
            _background = new ParallaxBackground(maxHalfWidth);
            ctx.Player = new PlayerShip(tuning, new Color(0.75f, 0.8f, 0.85f));
            ctx.Weapons = new PlayerWeapons(ctx);
            ctx.PlayerBullets = new BulletPool(new Color(1f, 0.95f, 0.4f), new Vector3(0.6f, 0.12f, 0.12f), stretch: true);
            ctx.EnemyBullets = new BulletPool(new Color(1f, 0.3f, 0.6f), Vector3.one * 0.5f, stretch: false);
            ctx.Enemies = new EnemySystem(ctx);
            ctx.Ordnance = new OrdnanceSystem(ctx);
            ctx.Effects = new Effects();

            gameObject.AddComponent<DebugMenu>().Init(this);
            gameObject.AddComponent<CombatHud>().Init(this);

            ResetRun();
        }

        public void ResetRun()
        {
            var ctx = Ctx;
            ctx.Scroll.Reset();
            ctx.MapHeight = MapHeight();
            ctx.Player.Reset(ctx.MapHeight * 0.3f);
            ctx.Camera.Snap(ctx.Player.Y, ctx.MapHeight);
            ctx.Weapons.Reset();
            ctx.Enemies.Clear();
            ctx.Ordnance.Clear();
            ctx.Effects.Clear();
            ctx.PlayerBullets.Clear();
            ctx.EnemyBullets.Clear();
            ctx.AirKills = 0;
            ctx.GroundKills = 0;
            ElapsedTime = 0f;
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

        float MapHeight() => tuning.mapHeightScreens * Ctx.Camera.HalfHeight * 2f;

        void Tick(float dt)
        {
            var ctx = Ctx;
            var move = Controls.Move.ReadValue<Vector2>();
            ctx.MapHeight = MapHeight();

            ctx.Scroll.Tick(Controls.Horizontal, dt);

            var wasDown = ctx.Player.Down;
            ctx.Player.Tick(move.y, dt, ctx.MapHeight, tuning.playerScreenX * ctx.Camera.HalfWidth * 2f);
            if (wasDown && !ctx.Player.Down) ctx.Weapons.Reset(); // 복귀 시 재무장

            ctx.Camera.Tick(ctx.Player.Y, dt, ctx.MapHeight);
            ctx.World.Tick(ctx.Scroll.Distance, ctx.MapHeight);
            _background.Tick(ctx.Scroll.Distance, ctx.Camera.Y, ctx.Camera.HalfHeight, ctx.MapHeight);
            ctx.EnemyBullets.FrameOffset = ctx.World.ScrollX.position; // 적 탄은 지면 좌표

            ctx.Weapons.Tick(dt);
            ctx.Enemies.Tick(dt);
            ctx.Ordnance.Tick(dt);

            var view = ctx.Camera.View;
            var cull = new Rect(view.xMin - BulletCullMargin, view.yMin - BulletCullMargin,
                view.width + BulletCullMargin * 2f, view.height + BulletCullMargin * 2f);
            ctx.PlayerBullets.Tick(dt, cull);
            ctx.EnemyBullets.Tick(dt, cull);

            ctx.Enemies.ResolveCollisions();
            ctx.Effects.Tick(dt);

            ctx.PlayerBullets.Render();
            ctx.EnemyBullets.Render();
        }

        void OnDestroy()
        {
            Ctx?.Controls?.Dispose();
        }
    }
}
