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

    public enum PadLayout
    {
        [InspectorName("배치 1 (LB)")] Layout1,
        [InspectorName("배치 2 (North)")] Layout2,
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
        [Tooltip("기본 스크롤 속도"), Range(0f, 30f)] public float baseScrollSpeed = 8f;
        [Tooltip("A: 오프셋 범위 (±)"), Range(0f, 40f)] public float offsetRange = 16f;
        [Tooltip("A: 오프셋 이동 속도"), Range(0f, 40f)] public float offsetSpeed = 12f;
        [Tooltip("B: 최저 속도"), Range(0f, 30f)] public float minSpeed = 3f;
        [Tooltip("B: 순항 속도"), Range(0f, 30f)] public float cruiseSpeed = 8f;
        [Tooltip("B: 최고 속도"), Range(0f, 40f)] public float maxSpeed = 16f;
        [Tooltip("B: 가속도"), Range(0f, 60f)] public float acceleration = 10f;
        [Tooltip("B+: 순항 복귀 속도"), Range(0f, 30f)] public float cruiseReturnRate = 4f;

        [Header("세로 이동 · 카메라")]
        [Tooltip("세로 이동 속도"), Range(0f, 30f)] public float verticalSpeed = 10f;
        [Tooltip("카메라 세로 추적")] public CameraFollowMode cameraFollow = CameraFollowMode.Smooth;
        [Tooltip("부드럽게: 추적 시간"), Range(0.01f, 1f)] public float cameraSmoothTime = 0.2f;
        [Tooltip("데드존: 높이 (화면 비율)"), Range(0f, 0.9f)] public float cameraDeadZone = 0.4f;

        [Header("기체 전환")]
        [Tooltip("전환 무방비 시간 (초)"), Range(0f, 2f)] public float switchVulnerableTime = 0.5f;
        [Tooltip("탭/홀드 구분 시간 (초)"), Range(0.1f, 1f)] public float switchHoldThreshold = 0.25f;
        [Tooltip("링 메뉴 슬로모션")] public bool ringSlowMotion = true;
        [Tooltip("링 메뉴 슬로모션 배율"), Range(0.05f, 1f)] public float ringSlowScale = 0.3f;

        [Header("입력 · 선회")]
        [Tooltip("패드 입력 배치")] public PadLayout padLayout = PadLayout.Layout1;
        [Tooltip("임의 선회 허용")] public bool manualTurnEnabled = false;

        [Header("지형 충돌")]
        [Tooltip("지형 충돌 피해"), Range(0f, 100f)] public float terrainDamage = 10f;
        [Tooltip("지형 충돌 튕김 강도"), Range(0f, 30f)] public float terrainBounce = 8f;

        [Header("HUD")]
        [Tooltip("레이더 HUD")] public bool radarEnabled = true;
    }
}
