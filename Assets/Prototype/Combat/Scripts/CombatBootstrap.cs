using UnityEngine;

namespace Prototype.Combat
{
    /// <summary>
    /// 전투 프로토타입 진입점. 씬에는 이 오브젝트와 카메라·조명만 두고 나머지는 코드로 생성한다.
    /// 갱신 순서: 스크롤 → 기체 → 지형 충돌 → 카메라 → 근경·원경 → 발사 → 적 → 무장 → 탄 → 판정 → 효과 → 진행 → 탄 렌더.
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
        public float ElapsedTime => Ctx.StageTime;

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
            var aircraft = AircraftDefinition.CreateSquadron();
            ctx.Player = new PlayerShip(tuning, aircraft);
            ctx.Weapons = new PlayerWeapons(ctx);
            ctx.Squadron = new Squadron(ctx, aircraft);
            ctx.PlayerBullets = new BulletPool(new Color(1f, 0.95f, 0.4f), new Vector3(0.6f, 0.12f, 0.12f), stretch: true);
            ctx.EnemyBullets = new BulletPool(new Color(1f, 0.3f, 0.6f), Vector3.one * 0.5f, stretch: false);
            ctx.Enemies = new EnemySystem(ctx);
            ctx.Ordnance = new OrdnanceSystem(ctx);
            ctx.Effects = new Effects();
            ctx.Stage = new StageRunner(ctx);
            ctx.Boss = new Boss(ctx);
            ctx.Log = new PlaytestLogger(ctx);

            gameObject.AddComponent<DebugMenu>().Init(this);
            gameObject.AddComponent<CombatHud>().Init(this);
            gameObject.AddComponent<RadarHud>().Init(this);
            gameObject.AddComponent<SquadronHud>().Init(this);

            ResetRun();
        }

        public void ResetRun()
        {
            var ctx = Ctx;
            ctx.Log.BeginRun(); // 진행 중이던 출격은 "중단"으로 기록 (카운터 초기화 전에)
            ctx.Scroll.Reset();
            ctx.MapHeight = MapHeight();
            ctx.Stage.Build(tuning.stageEnabled, ctx.MapHeight);
            ctx.World.SetFlatGroundVisible(!ctx.Stage.Active);
            ctx.Player.Reset(FreeY(PlayerScreenX, ctx.MapHeight * 0.3f, 3f));
            ctx.Squadron.Reset();
            ctx.Camera.Snap(ctx.Player.Y, ctx.MapHeight);
            ctx.Enemies.Clear();
            ctx.Boss.Clear();
            if (ctx.Stage.TurnZone != null) ctx.Boss.Build(ctx.Stage.TurnZone);
            ctx.Ordnance.Clear();
            ctx.Effects.Clear();
            ctx.PlayerBullets.Clear();
            ctx.EnemyBullets.Clear();
            ctx.AirKills = 0;
            ctx.GroundKills = 0;
            ctx.BossPartKills = 0;
            ctx.TerrainHits = 0;
            ctx.PlayerDowns = 0;
            ctx.StageTime = 0f;
            Tick(0f);
        }

        void Update()
        {
            Controls.SetLayout(tuning.padLayout);
            // 디버그 메뉴 입력칸에 타이핑 중에는 R 리셋 무시
            if (Controls.Restart.WasPressedThisFrame() && GUIUtility.keyboardControl == 0) ResetRun();

            var dt = Time.deltaTime;
            // 선회 중 스테이지 타이머 정지 (검증계획 3.5)
            if (!Ctx.Stage.Cleared && !Ctx.Squadron.Wiped && !Ctx.Scroll.Turning) Ctx.StageTime += dt;
            Tick(dt);
        }

        float MapHeight() => tuning.mapHeightScreens * Ctx.Camera.HalfHeight * 2f;

        float PlayerScreenX => tuning.playerScreenX * Ctx.Camera.HalfWidth * 2f;

        /// <summary>기체 화면 위치의 바닥·천장에서 margin 이상 떨어진 높이.</summary>
        float FreeY(float screenX, float y, float margin)
        {
            var groundX = screenX + Ctx.Scroll.Distance;
            var terrain = Ctx.Stage.Terrain;
            return Mathf.Clamp(y, terrain.Floor(groundX) + margin, terrain.Ceil(groundX) - margin);
        }

        void Tick(float dt)
        {
            var ctx = Ctx;
            var move = ctx.Squadron.SuppressMove ? Vector2.zero : Controls.Move.ReadValue<Vector2>();
            ctx.MapHeight = MapHeight();

            var scroll = ctx.Scroll;
            if (tuning.manualTurnEnabled && Controls.Turn.WasPressedThisFrame() && !scroll.Turning && !ctx.Player.Down)
            {
                scroll.StartTurn(); // 임의 선회 (Q7)
                ctx.Log.Turn(manual: true);
            }
            scroll.Tick(ctx.Squadron.SuppressMove ? 0f : Controls.Horizontal, dt);
            ctx.Player.Facing = scroll.Direction;
            ctx.Player.Turning = scroll.Turning;

            var wasDown = ctx.Player.Down;
            ctx.Squadron.Tick(dt, Time.unscaledDeltaTime);
            ctx.Player.Tick(move.y, dt, ctx.MapHeight, PlayerScreenX * scroll.VisualDirection, scroll.VisualDirection, scroll.TurnProgress);
            if (wasDown && !ctx.Player.Down)
                ctx.Player.Place(FreeY(PlayerScreenX * scroll.Direction, ctx.Player.Y, 2f)); // 전멸 복구: 그동안 지형이 지나갔을 수 있음
            ctx.Stage.ResolvePlayer();

            ctx.Camera.Tick(ctx.Player.Y, dt, ctx.MapHeight);
            ctx.World.Tick(ctx.Scroll.Distance, ctx.MapHeight, ctx.Stage.Terrain.Floor);
            _background.Tick(ctx.Scroll.Distance, ctx.Camera.Y, ctx.Camera.HalfHeight, ctx.MapHeight);
            ctx.EnemyBullets.FrameOffset = ctx.World.ScrollX.position; // 적 탄은 지면 좌표

            ctx.Weapons.Tick(dt);
            ctx.Boss.Tick(dt);
            ctx.Enemies.Tick(dt);
            ctx.Ordnance.Tick(dt);

            var view = ctx.Camera.View;
            var cull = new Rect(view.xMin - BulletCullMargin, view.yMin - BulletCullMargin,
                view.width + BulletCullMargin * 2f, view.height + BulletCullMargin * 2f);
            ctx.PlayerBullets.Tick(dt, cull);
            ctx.EnemyBullets.Tick(dt, cull);
            ctx.Stage.ResolveBullets();

            ctx.Enemies.ResolveCollisions();
            ctx.Effects.Tick(dt);
            ctx.Stage.Tick();
            EndRunIfFinished();

            ctx.PlayerBullets.Render();
            ctx.EnemyBullets.Render();
        }

        void EndRunIfFinished()
        {
            var log = Ctx.Log;
            if (!log.Running) return;
            if (Ctx.Stage.Cleared) log.EndRun("종료");
            else if (Ctx.Squadron.Wiped && !tuning.restoreSquadronOnWipe) log.EndRun("전멸");
        }

        void OnDestroy()
        {
            if (Ctx?.Log != null && Ctx.Log.Running) Ctx.Log.EndRun(Ctx.StageTime > 1f ? "중단" : null);
            Time.timeScale = 1f;
            Ctx?.Controls?.Dispose();
        }
    }
}
