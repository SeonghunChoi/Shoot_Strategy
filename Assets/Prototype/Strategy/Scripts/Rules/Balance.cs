using System.Collections.Generic;

namespace Prototype.Strategy.Rules
{
    public enum FacilityType
    {
        Hangar,        // 격납고
        Runway,        // 활주로
        Barracks,      // 병영
        AntiAir,       // 대공포
        Maintenance,   // 정비소
        Radar,         // 레이더
        Depot,         // 보급창고
        Entertainment, // 유흥가
        Armory,        // 무기고·개조 공방
        Sam,           // SAM
    }

    /// <summary>시설 정의 (기획서 5.4). 수치는 조절 대상이라 필드로 둔다.</summary>
    public sealed class FacilityDef
    {
        public FacilityType Type;
        public string Name;
        public string Short;       // 맵 표시용 한 글자
        public int Cost;
        public int UnlockGrade;    // 필요한 거점 등급
        public bool Graded;        // 시설 등급이 있는가
        public int MaxGrade = 1;   // 최고 등급 (등급 없는 시설은 1)
        public string Effect;      // 패널 설명
    }

    /// <summary>
    /// 전략 규칙의 모든 조절 수치. 7단계 디버그 메뉴가 이 객체를 직접 고친다.
    /// </summary>
    public sealed class Balance
    {
        [Tune("경제", "시작 자금", 0f, 5000f, restart: true)]
        public int StartingFunds = 2000;
        [Tune("경제", "상부 예산 기준", 0f, 1000f)]
        public int HqBudget = 400;
        /// <summary>도시판: 영역 칸이 아니라 거점 등급에 비례하는 유지비.</summary>
        [Tune("경제", "거점 유지비 / 등급", 0f, 100f)]
        public int UpkeepPerBaseGrade = 25;
        /// <summary>거점 등급별 자동 영역 반경 (인덱스 = 등급).</summary>
        public int[] TerritoryRadiusByGrade = { 0, 1, 1, 2, 2 };

        // ---- 자원 타일 (도시판) ----
        [Tune("자원 타일", "정유 수입 / 등급", 0f, 300f)]
        public int RefineryIncome = 80;
        [Tune("자원 타일", "광산 수입 / 등급", 0f, 300f)]
        public int MineIncome = 50;
        [Tune("자원 타일", "도시 수입 / 등급", 0f, 300f)]
        public int CityIncome = 60;
        [Tune("자원 타일", "정유 기준 비용", 0f, 1000f)]
        public int RefineryCost = 300;
        [Tune("자원 타일", "광산 기준 비용", 0f, 1000f)]
        public int MineCost = 250;
        [Tune("자원 타일", "도시 기준 비용", 0f, 1000f)]
        public int CityCost = 250;
        [Tune("자원 타일", "최고 등급", 1f, 5f)]
        public int MaxSiteGrade = 3;
        [Tune("자원 타일", "대공 / 등급 (폭격)", 0f, 20f)]
        public int SiteAntiAirPerGrade = 3;
        [Tune("자원 타일", "시설 / 등급 (폭격)", 1f, 40f)]
        public int SiteStructurePerGrade = 10;
        [Tune("자원 타일", "현상금: 자원 타일", 0f, 1000f)]
        public int BountySite = 200;
        [Tune("자원 타일", "소유 타일 시야", 0f, 3f)]
        public int SiteVisionRange = 1;
        [Tune("작전 반경·급유·전초기지", "전초기지 기본 비용", 0f, 2000f)]
        public int OutpostCost = 600;
        [Tune("경제", "파산: 연속 적자 턴", 1f, 10f)]
        public int BankruptTurns = 3;
        [Tune("거점·시설", "시설 등급 상승 비용 계수", 0.1f, 2f)]
        public float FacilityUpgradeFactor = 0.5f; // 비용 = 건설비 × 다음 등급 × 계수
        public int MaxFacilityGrade = 4;
        /// <summary>시설 상한을 넘는 시설 하나마다 건설비 +이 비율 (누적). 상한은 "할증 없이 지을 수 있는 수".</summary>
        [Tune("거점·시설", "상한 초과 건설비 할증 / 개", 0f, 2f)]
        public float FacilityOverCapCostRatio = 0.5f;
        /// <summary>켜면 상한에서 건설을 막는다 (원래 방식, 비교용).</summary>
        [Tune("거점·시설", "시설 상한 엄격 적용", 0f, 1f)]
        public bool HardFacilityCap = false;

        /// <summary>거점 등급별 시설 규모(등급 합) 상한 (인덱스 = 등급).</summary>
        public int[] FacilityCap = { 0, 3, 5, 8, 12 };

        /// <summary>거점 등급 상승 비용 (인덱스 = 목표 등급).</summary>
        public int[] BaseUpgradeCost = { 0, 0, 300, 700, 1500 };

        /// <summary>거점 등급 상승에 필요한 시설 (인덱스 = 목표 등급).</summary>
        public FacilityType[][] BaseUpgradeRequires =
        {
            new FacilityType[0],
            new FacilityType[0],
            new[] { FacilityType.Hangar, FacilityType.Runway },
            new[] { FacilityType.Maintenance, FacilityType.Radar },
            new[] { FacilityType.Armory, FacilityType.Depot },
        };

        public readonly Dictionary<FacilityType, FacilityDef> Facilities = new();

        // ---- 편대·기체·파일럿 (3단계) ----
        [Tune("편대·파일럿", "편대 최대 기체 수", 1f, 6f)]
        public int SquadronMaxSize = 4;
        [Tune("편대·파일럿", "격납고당 편대 수", 1f, 4f)]
        public int SquadronsPerHangar = 2;
        /// <summary>기지 설비(보급창고)에 의한 작전 반경 증가.</summary>
        [Tune("작전 반경·급유·전초기지", "보급창고 반경 보너스", 0f, 3f)]
        public int DepotRadiusBonus = 1;
        // ---- 공중 급유기·드롭탱크 (도시판 2단계) ----
        [Tune("작전 반경·급유·전초기지", "급유기 호출 비용", 0f, 600f)]
        public int TankerCost = 150;
        /// <summary>급유기를 띄울 거점에서 호출 지점까지 최대 거리.</summary>
        [Tune("작전 반경·급유·전초기지", "급유기 호출 범위", 1f, 12f)]
        public int TankerRange = 6;
        [Tune("작전 반경·급유·전초기지", "급유기 범위 + (보급창고)", 0f, 6f)]
        public int TankerDepotBonus = 2;
        /// <summary>이 등급 이상 활주로가 있는 거점마다 급유기 1대.</summary>
        [Tune("작전 반경·급유·전초기지", "급유기 필요 활주로 등급", 1f, 3f)]
        public int TankerMinRunway = 2;
        [Tune("작전 반경·급유·전초기지", "급유 거리 (급유기 주변 칸)", 0f, 3f)]
        public int TankerRefuelRange = 1;
        /// <summary>드롭탱크: 연료 × (1 + 이 값), 반올림. 탱크 분량을 먼저 쓴다.</summary>
        [Tune("비행", "드롭탱크 추가 연료 비율", 0f, 2f)]
        public float DropTankRatio = 0.5f;
        /// <summary>레이더 미확인 접촉: 시야 밖이라도 시야 + 이 칸 안의 적은 "?"로 보인다.</summary>
        [Tune("적 세력·시야", "미확인 접촉 추가 범위", 0f, 6f)]
        public int ContactExtraRange = 2;
        /// <summary>전초기지는 가장 가까운 아군 거점에서 이 거리 이상.</summary>
        [Tune("작전 반경·급유·전초기지", "전초기지 최소 거리", 1f, 6f)]
        public int OutpostMinDistance = 3;
        [Tune("작전 반경·급유·전초기지", "전초기지 거리당 비용", 0f, 200f)]
        public int OutpostCostPerDistance = 50;
        [Tune("편대·파일럿", "일반 파일럿 급여", 0f, 150f)]
        public int SalaryRegular = 40;
        [Tune("편대·파일럿", "네임드 파일럿 급여", 0f, 300f)]
        public int SalaryNamed = 80;
        [Tune("편대·파일럿", "파일럿 고용비 (숙련 1)", 0f, 600f)]
        public int HirePilotCost = 200;
        [Tune("편대·파일럿", "고용비 추가 / 숙련", 0f, 400f)]
        public int HireCostPerSkill = 100;
        [Tune("편대·파일럿", "고용 후보 수", 1f, 6f)]
        public int HireCandidateCount = 3;
        [Tune("편대·파일럿", "고용 후보 교체 주기 (턴)", 1f, 10f)]
        public int HireRefreshTurns = 2;
        [Tune("편대·파일럿", "기체 판매 비율", 0f, 1f)]
        public float SellRatio = 0.5f;
        /// <summary>유흥가: 주둔 파일럿 급여 환수율 = 등급 × 이 값.</summary>
        [Tune("편대·파일럿", "유흥가 급여 환수 / 등급", 0f, 0.5f)]
        public float EntertainmentRefundPerGrade = 0.15f;
        /// <summary>턴당 손상 회복(%): 정비소 등급 × 이 값. 정비소 없으면 RepairWithoutMaintenance.</summary>
        [Tune("편대·파일럿", "정비소 수리 % / 등급", 0f, 50f)]
        public int RepairPerMaintenanceGrade = 15;
        [Tune("편대·파일럿", "정비소 없을 때 수리 %", 0f, 30f)]
        public int RepairWithoutMaintenance = 5;
        /// <summary>수리비 = 기체 가격 × 회복 비율 × 이 값.</summary>
        [Tune("편대·파일럿", "수리비 계수", 0f, 1f)]
        public float RepairCostFactor = 0.3f;
        [Tune("편대·파일럿", "피로 회복 / 턴", 0f, 50f)]
        public int FatigueRecovery = 20;
        [Tune("편대·파일럿", "유흥가 피로 회복 / 등급", 0f, 30f)]
        public int FatigueRecoveryPerEntertainmentGrade = 10;
        /// <summary>이 값 이상이면 탈진 (출격 불가).</summary>
        [Tune("편대·파일럿", "탈진 기준 피로", 30f, 100f)]
        public int FatigueExhausted = 70;

        // ---- 출격·자동 정산 (4단계) ----
        /// <summary>기체 크기별 출격 비용 (연료·탄약). 인덱스 = 크기.</summary>
        public int[] SortieCostBySize = { 0, 30, 50, 80 };
        [Tune("출격·자동 정산", "보급창고 출격 비용 할인", 0f, 0.8f)]
        public float DepotSortieDiscount = 0.2f;
        /// <summary>이 손상(%) 이상이면 다음 기체로 교대.</summary>
        [Tune("출격·자동 정산", "교대 기준 손상 %", 20f, 100f)]
        public int SwapDamage = 60;
        [Tune("출격·자동 정산", "최대 라운드", 3f, 30f)]
        public int MaxRounds = 12;
        /// <summary>직접 플레이 대비 불리 계수 (아군 공격력에 곱함).</summary>
        [Tune("출격·자동 정산", "자동 정산 불리 계수", 0.3f, 1.5f)]
        public float AutoResolvePenalty = 0.85f;
        /// <summary>적 그룹이 전경 기체에 주는 라운드당 손상(%) = 남은 전력 × 이 값.</summary>
        [Tune("출격·자동 정산", "적 반격 계수", 0.1f, 3f)]
        public float EnemyHitPerStrength = 1.0f;
        [Tune("출격·자동 정산", "겹침 보정: 대공 감소", 0f, 0.9f)]
        public float OverlapAaReduction = 0.3f;
        [Tune("출격·자동 정산", "현상금: 부대", 0f, 1000f)]
        public int BountyUnit = 300;
        [Tune("출격·자동 정산", "현상금: 거점", 0f, 1500f)]
        public int BountyBase = 500;
        [Tune("출격·자동 정산", "적 영역 격추 시 포로 확률", 0f, 1f)]
        public float CaptureChance = 0.5f;
        [Tune("출격·자동 정산", "몸값", 0f, 1000f)]
        public int RansomCost = 300;
        /// <summary>포로 파일럿 급여 비율 (0~1). 0이면 포로는 급여 없음.</summary>
        [Tune("출격·자동 정산", "포로 급여 비율", 0f, 1f)]
        public float CapturedSalaryRatio = 0.5f;
        [Tune("출격·자동 정산", "출격 피로", 0f, 60f)]
        public int SortieFatigue = 30;
        [Tune("출격·자동 정산", "경험치 / 라운드", 0f, 50f)]
        public int XpPerRound = 10;
        /// <summary>출격에 참가한 모든 파일럿이 받는 기본 경험치.</summary>
        [Tune("출격·자동 정산", "경험치: 출격 기본", 0f, 50f)]
        public int SortieBaseXp = 10;
        /// <summary>편대의 다른 기체가 얻은 교전 경험치 합 × 이 비율을 나눠 받는다.</summary>
        [Tune("출격·자동 정산", "경험치: 편대 공유 비율", 0f, 1f)]
        public float SortieXpShare = 0.3f;
        /// <summary>숙련도 상승에 필요한 경험치 = 현재 숙련도 × 이 값.</summary>
        /// <summary>C안 다턴 비행. 연료 = 작전 반경 × 2 + 예비, 한 칸 1, 하늘에서 턴을 마칠 때 1.</summary>
        [Tune("비행", "출격 예비 연료", 0f, 6f)]
        public int ReserveFuel = 1;
        [Tune("비행", "하늘에서 턴을 마칠 때 연료", 0f, 4f)]
        public int IdleFuelPerTurn = 1;
        [Tune("비행", "비행 피로 / 턴", 0f, 50f)]
        public int FlightFatigue = 15;
        [Tune("비행", "비행 중 교전 피로", 0f, 50f)]
        public int FlightEngageFatigue = 15;
        [Tune("비행", "비행 편대 시야", 0f, 4f)]
        public int FlightVisionRange = 2;
        [Tune("출격·자동 정산", "숙련도 상승 경험치 / 숙련", 20f, 300f)]
        public int XpPerSkill = 100;
        public int MaxReturnTurns = 3;

        // ---- 적 세력·시야 (5단계) ----
        [Tune("적 세력·시야", "시야 사용", 0f, 1f)]
        public bool VisionEnabled = true;
        /// <summary>아군 영역 밖으로 기본 시야 칸 수.</summary>
        [Tune("적 세력·시야", "기본 시야 (영역 +칸)", 0f, 5f)]
        public int TerritoryVisionRange = 1;
        /// <summary>레이더 등급 1당 시야 +칸 (영역 기준).</summary>
        [Tune("적 세력·시야", "레이더 시야 / 등급", 0f, 3f)]
        public int RadarRangePerGrade = 1;
        [Tune("적 세력·시야", "마지막 목격 유지 턴", 0f, 15f)]
        public int SightingMemoryTurns = 5;
        [Tune("적 세력·시야", "적 이동력", 1f, 5f)]
        public int EnemyMovePoints = 2;
        public int MountainMoveCost = 2;
        /// <summary>적 세력이 아군 거점의 주둔 편대 수를 아는 범위 (부대·거점 기준).</summary>
        [Tune("적 세력·시야", "적이 편대를 아는 거리", 0f, 8f)]
        public int EnemyDetectRadius = 3;
        [Tune("적 세력·시야", "적 판단: 보이는 기체 방어 가중", 0f, 30f)]
        public float KnownAircraftDefenseWeight = 8f;
        [Tune("적 세력·시야", "재정비 기준 세력 전력", 0f, 60f)]
        public int RegroupBelowStrength = 15;
        [Tune("적 세력·시야", "손실 후 방어 턴", 0f, 6f)]
        public int DefensiveTurnsAfterLoss = 2;
        [Tune("적 세력·시야", "대공포 방어 / 등급", 0f, 20f)]
        public int AntiAirDefensePerGrade = 5;
        [Tune("적 세력·시야", "SAM 방어 / 등급", 0f, 40f)]
        public int SamDefensePerGrade = 10;
        /// <summary>요격·대공 후 남은 적 전력이 이 값 이상이면 거점 상실.</summary>
        [Tune("적 세력·시야", "거점 상실 기준 남은 전력", 1f, 40f)]
        public int CaptureThreshold = 10;
        [Tune("적 세력·시야", "요격 피로", 0f, 50f)]
        public int InterceptFatigue = 20;
        /// <summary>재배치(이동 비행) 피로.</summary>
        [Tune("적 세력·시야", "재배치 피로", 0f, 50f)]
        public int RelocateFatigue = 10;
        public int CapturedBaseAntiAir = 5;
        [Tune("적 세력·시야", "세력 간·지상 교전 최대 피해", 1f, 30f)]
        public int FactionCombatMaxDamage = 10;
        [Tune("적 세력·시야", "적 보충 주기 (턴)", 1f, 20f)]
        public int ReinforceTurns = 6;
        [Tune("적 세력·시야", "보충 부대 공중", 0f, 40f)]
        public int ReinforceAir = 10;
        [Tune("적 세력·시야", "보충 부대 지상", 0f, 40f)]
        public int ReinforceGround = 12;
        [Tune("적 세력·시야", "적 거점 회복 / 턴", 0f, 10f)]
        public int EnemyBaseRegen = 1;
        [Tune("적 세력·시야", "기지 주둔 부대 회복 / 턴", 0f, 10f)]
        public int EnemyUnitRegen = 2;

        // ---- 지상군·상부 (6단계) ----
        [Tune("지상군", "지상군 고용비", 0f, 800f)]
        public int GroundHireCost = 200;
        [Tune("지상군", "지상군 기본 전력", 1f, 60f)]
        public int GroundBaseStrength = 15;
        [Tune("지상군", "병영 등급당 전력", 0f, 20f)]
        public int GroundStrengthPerBarracksGrade = 5;
        [Tune("지상군", "지상군 유지비", 0f, 100f)]
        public int GroundUpkeep = 30;
        [Tune("지상군", "출동 비용", 0f, 300f)]
        public int GroundDeployCost = 50;
        [Tune("지상군", "지상군 이동력", 1f, 5f)]
        public int GroundMovePoints = 2;
        public int GroundVisionRange = 1;
        /// <summary>지상군(주둔·출동)에서 이 거리 안이면 전초기지 건설 가능 (공중 도달과 별개).</summary>
        [Tune("지상군", "지상군 전초기지 도달", 0f, 5f)]
        public int GroundOutpostReach = 2;
        [Tune("상부", "시작 신뢰도", 1f, 100f, restart: true)]
        public int StartingTrust = 50;
        /// <summary>상부 예산 = HqBudget × (이 값 + 신뢰도/100).</summary>
        [Tune("상부", "예산 기본 비율", 0f, 1.5f)]
        public float BudgetBaseRatio = 0.5f;
        [Tune("상부", "장기 목표 신뢰도", 0f, 60f)]
        public int TrustGoal = 30;
        [Tune("상부", "점령 신뢰도", 0f, 30f)]
        public int TrustCapture = 10;
        [Tune("상부", "전초기지 신뢰도", 0f, 20f)]
        public int TrustOutpost = 5;
        [Tune("상부", "지시 완수", 0f, 30f)]
        public int TrustDirectiveSuccess = 10;
        [Tune("상부", "지시 실패 (감소)", 0f, 40f)]
        public int TrustDirectiveFail = 15;
        [Tune("상부", "지시 거부 (감소)", 0f, 40f)]
        public int TrustRefuse = 10;
        [Tune("상부", "거점 상실 (감소)", 0f, 40f)]
        public int TrustBaseLost = 10;
        [Tune("상부", "지시 주기 (턴)", 1f, 15f)]
        public int DirectiveInterval = 5;
        [Tune("상부", "지시 기한 (턴)", 1f, 10f)]
        public int DirectiveDeadline = 4;

        public readonly Dictionary<AircraftType, AircraftDef> Aircraft = new();

        public Balance()
        {
            AddAircraft(AircraftType.F5, "F-5", AircraftRole.Fighter, 1, 1, 400, 80, air: 8, ground: 3, bombing: 1, range: 3, speed: 3);
            AddAircraft(AircraftType.A4, "A-4", AircraftRole.Attacker, 1, 1, 450, 100, air: 4, ground: 8, bombing: 3, range: 3, speed: 3);
            AddAircraft(AircraftType.F4, "F-4", AircraftRole.Multirole, 2, 2, 900, 120, air: 7, ground: 6, bombing: 4, range: 4, speed: 4);
            AddAircraft(AircraftType.B57, "B-57", AircraftRole.Bomber, 3, 2, 1400, 160, air: 1, ground: 5, bombing: 10, range: 5, speed: 3);

            Add(FacilityType.Hangar, "격납고", "격", 150, 1, 4, "주둔 편대 2 × 등급");
            Add(FacilityType.Runway, "활주로", "활", 100, 1, 3, "등급 = 운용 기체 크기 (1 소형 · 2 중형 · 3 대형)");
            Add(FacilityType.Barracks, "병영", "병", 150, 1, 4, "지상군 고용, 전력 +5 × 등급");
            Add(FacilityType.AntiAir, "대공포", "대", 120, 1, 4, "거점 방어 +5 × 등급");
            Add(FacilityType.Maintenance, "정비소", "정", 250, 2, 4, "기체 수리 15% × 등급, 기체 티어");
            Add(FacilityType.Radar, "레이더", "레", 200, 2, 4, "이 거점 시야 +등급칸 (기본: 영역 +1)");
            Add(FacilityType.Depot, "보급창고", "보", 150, 2, 1, "출격 비용 −20%·작전 반경 +1");
            Add(FacilityType.Entertainment, "유흥가", "유", 200, 2, 4, "급여 환수·피로 회복");
            Add(FacilityType.Armory, "무기고·개조 공방", "무", 300, 3, 1, "부품 개조·무장 관리 (효과 없음)");
            Add(FacilityType.Sam, "SAM", "S", 350, 3, 4, "거점 방어 +10 × 등급");
        }

        void Add(FacilityType type, string name, string shortName, int cost, int unlock, int maxGrade, string effect)
        {
            Facilities[type] = new FacilityDef
            {
                Type = type, Name = name, Short = shortName, Cost = cost, UnlockGrade = unlock,
                Graded = maxGrade > 1, MaxGrade = maxGrade, Effect = effect,
            };
        }

        void AddAircraft(AircraftType type, string name, AircraftRole role, int size, int tier, int price, int hp,
            int air, int ground, int bombing, int range, int speed)
        {
            Aircraft[type] = new AircraftDef
            {
                Type = type, Name = name, Role = role, Size = size, Tier = tier, Price = price, Hp = hp,
                Air = air, Ground = ground, Bombing = bombing, Range = range, Speed = speed,
            };
        }

        public FacilityDef Def(FacilityType type) => Facilities[type];

        public AircraftDef Def(AircraftType type) => Aircraft[type];

        public int Salary(Pilot p) => p.Named ? SalaryNamed : SalaryRegular;
    }
}
