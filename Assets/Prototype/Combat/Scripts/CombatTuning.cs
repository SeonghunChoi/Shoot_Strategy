using UnityEngine;

namespace Prototype.Combat
{
    public enum HorizontalMode
    {
        [InspectorName("A: 위치 오프셋")] PositionOffset,
        [InspectorName("B: 속도 조절")] Speed,
        [InspectorName("B + 순항 복귀")] SpeedCruiseReturn,
    }

    public enum CameraFollowMode
    {
        [InspectorName("즉시")] Instant,
        [InspectorName("부드럽게")] Smooth,
        [InspectorName("데드존")] DeadZone,
    }

    public enum RadarMode
    {
        [InspectorName("끄기")] Off,
        [InspectorName("가장자리")] Edge,
        [InspectorName("세로 띠")] Strip,
        [InspectorName("둘 다")] Both,
    }

    public enum PadLayout
    {
        [InspectorName("배치 1 (LB)")] Layout1,
        [InspectorName("배치 2 (North)")] Layout2,
        [InspectorName("배치 3 (트리거 스로틀)")] Layout3,
    }

    /// <summary>
    /// 검증계획 5장의 조절 항목. 디버그 메뉴(F1)가 [Tooltip]을 라벨로, [Range]를 슬라이더 범위로 사용한다.
    /// 플레이 중 변경한 값은 에셋에 남는다 (File > Save Project로 디스크 저장).
    /// </summary>
    [CreateAssetMenu(menuName = "Prototype/Combat Tuning", fileName = "CombatTuning")]
    public sealed class CombatTuning : ScriptableObject
    {
        [Header("가로 입력")]
        [Tooltip("가로 입력 모드")] public HorizontalMode horizontalMode = HorizontalMode.PositionOffset;
        [Tooltip("기본 스크롤 속도 (B: 시작·순항 속도)"), Range(0f, 30f)] public float baseScrollSpeed = 8f;
        [Tooltip("A: 오프셋 범위 (±)"), Range(0f, 40f)] public float offsetRange = 16f;
        [Tooltip("A: 오프셋 이동 속도"), Range(0f, 40f)] public float offsetSpeed = 12f;
        [Tooltip("B: 감속 폭 (기본 스크롤 − 폭까지)"), Range(0f, 30f)] public float decelRange = 5f;
        [Tooltip("B: 가속 폭 (기본 스크롤 + 폭까지)"), Range(0f, 30f)] public float accelRange = 8f;
        [Tooltip("B: 가속도"), Range(0f, 60f)] public float acceleration = 10f;
        [Tooltip("B+: 순항 복귀 속도"), Range(0f, 30f)] public float cruiseReturnRate = 4f;

        [Header("맵 · 기체")]
        [Tooltip("맵 높이 (화면 배수)"), Range(1f, 4f)] public float mapHeightScreens = 2.5f;
        [Tooltip("기체 화면 가로 위치 (0=중앙)"), Range(-0.45f, 0.45f)] public float playerScreenX = -0.25f;
        [Tooltip("기체 기울기 (도)"), Range(0f, 45f)] public float shipTiltAngle = 15f;

        [Header("세로 이동 · 카메라")]
        [Tooltip("세로 이동 속도"), Range(0f, 30f)] public float verticalSpeed = 10f;
        [Tooltip("카메라 세로 추적")] public CameraFollowMode cameraFollow = CameraFollowMode.Smooth;
        [Tooltip("부드럽게: 추적 시간"), Range(0.01f, 1f)] public float cameraSmoothTime = 0.2f;
        [Tooltip("데드존: 높이 (화면 비율)"), Range(0f, 0.9f)] public float cameraDeadZone = 0.4f;

        [Header("기체 전환")]
        [Tooltip("무방비 중 세로 이동 배율"), Range(0f, 1f)] public float vulnerableMoveScale = 0.5f;
        [Tooltip("전멸 시 2초 뒤 편대 복구 (테스트용)")] public bool restoreSquadronOnWipe = true;
        [Tooltip("전환 무방비 시간 (초)"), Range(0f, 2f)] public float switchVulnerableTime = 0.5f;
        [Tooltip("탭/홀드 구분 시간 (초)"), Range(0.1f, 1f)] public float switchHoldThreshold = 0.25f;
        [Tooltip("링 메뉴 슬로모션")] public bool ringSlowMotion = true;
        [Tooltip("링 메뉴 슬로모션 배율"), Range(0.05f, 1f)] public float ringSlowScale = 0.3f;

        [Header("입력 · 선회")]
        [Tooltip("패드 입력 배치")] public PadLayout padLayout = PadLayout.Layout1;
        [Tooltip("임의 선회 허용 (RB / I)")] public bool manualTurnEnabled = false;
        [Tooltip("선회 시간 (초)"), Range(0.3f, 3f)] public float turnDuration = 1.2f;

        [Header("지형 충돌")]
        [Tooltip("지형 충돌 피해"), Range(0f, 100f)] public float terrainDamage = 10f;
        [Tooltip("지형 충돌 튕김 강도"), Range(0f, 30f)] public float terrainBounce = 8f;

        [Header("기체 체력")]
        [Tooltip("기체 무적")] public bool playerInvincible = false;
        [Tooltip("기체 최대 체력 (공격기 기준)"), Range(10f, 300f)] public float playerMaxHp = 100f;
        [Tooltip("적 탄 피해"), Range(0f, 50f)] public float enemyBulletDamage = 10f;
        [Tooltip("적 기체 충돌 피해"), Range(0f, 100f)] public float enemyContactDamage = 20f;
        [Tooltip("피격 후 무적 시간 (초)"), Range(0f, 2f)] public float hitInvulnTime = 0.5f;

        [Header("기관포 (공격기 기준, 기체 배율 적용)")]
        [Tooltip("연사 속도 (발/초)"), Range(1f, 30f)] public float gunFireRate = 12f;
        [Tooltip("기관포 피해"), Range(0.1f, 5f)] public float gunDamage = 1f;
        [Tooltip("기관포 탄속"), Range(10f, 80f)] public float gunBulletSpeed = 40f;

        [Header("하드포인트 무장 (공격기 기준, 장탄은 기체 정의)")]
        [Tooltip("미사일 재발사 간격 (초)"), Range(0.05f, 2f)] public float missileCooldown = 0.5f;
        [Tooltip("미사일 피해"), Range(1f, 30f)] public float missileDamage = 8f;
        [Tooltip("미사일 최고 속도"), Range(5f, 50f)] public float missileSpeed = 22f;
        [Tooltip("미사일 선회 속도 (도/초)"), Range(0f, 720f)] public float missileTurnRate = 180f;
        [Tooltip("폭탄 재투하 간격 (초)"), Range(0.05f, 2f)] public float bombCooldown = 0.4f;
        [Tooltip("폭탄 피해"), Range(1f, 60f)] public float bombDamage = 20f;
        [Tooltip("폭탄 폭발 반경"), Range(0.5f, 8f)] public float bombRadius = 3f;
        [Tooltip("폭탄 중력"), Range(1f, 60f)] public float bombGravity = 20f;

        [Header("적")]
        [Tooltip("공중 적 등장")] public bool airSpawnEnabled = true;
        [Tooltip("지상 목표 등장")] public bool groundSpawnEnabled = true;
        [Tooltip("공중 적 등장 간격 (스테이지 끄기 시)"), Range(0.2f, 10f)] public float airSpawnInterval = 1.5f;
        [Tooltip("지상 목표 등장 간격 (스테이지 끄기 시)"), Range(0.5f, 15f)] public float groundSpawnInterval = 4f;
        [Tooltip("공중 적 대지 속도"), Range(0f, 20f)] public float enemyAirSpeed = 3f;
        [Tooltip("공중 적 체력"), Range(1f, 20f)] public float airEnemyHp = 3f;
        [Tooltip("대공포 체력"), Range(1f, 40f)] public float antiAirHp = 6f;
        [Tooltip("고정 목표 체력"), Range(1f, 100f)] public float targetHp = 30f;
        [Tooltip("공중 적 발사 간격 (초)"), Range(0.5f, 10f)] public float airFireInterval = 3f;
        [Tooltip("대공포 발사 간격 (초)"), Range(0.3f, 10f)] public float antiAirFireInterval = 2f;
        [Tooltip("적 탄속"), Range(2f, 30f)] public float enemyBulletSpeed = 10f;
        [Tooltip("적 예측 사격 정확도 (0=현재 위치, 1=완전 예측)"), Range(0f, 1f)] public float enemyLeadAccuracy = 1f;

        [Header("보스")]
        [Tooltip("포탑 체력"), Range(1f, 200f)] public float bossTurretHp = 40f;
        [Tooltip("코어 체력"), Range(1f, 400f)] public float bossCoreHp = 100f;
        [Tooltip("보스 사격 간격 (초)"), Range(0.3f, 6f)] public float bossFireInterval = 2f;

        [Header("스테이지")]
        [Tooltip("스테이지 사용 (끄면 평지 무한 등장, 리셋 시 반영)")] public bool stageEnabled = true;
        [Tooltip("스테이지 등장 간격 배율"), Range(0.25f, 4f)] public float stageSpawnScale = 1f;

        [Header("HUD")]
        [Tooltip("레이더 HUD")] public RadarMode radarMode = RadarMode.Both;
    }
}
