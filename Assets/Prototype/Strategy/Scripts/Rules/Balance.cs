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
        Refinery,      // 정유시설
        Mine,          // 광산
        Armory,        // 무기고·개조 공방
        Sam,           // SAM
        Finance,       // 금융
    }

    public enum SiteRequirement
    {
        None,
        Oil,
        Mountain,
    }

    /// <summary>시설 정의 (기획서 5.4). 수치는 조절 대상이라 필드로 둔다.</summary>
    public sealed class FacilityDef
    {
        public FacilityType Type;
        public string Name;
        public string Short;       // 맵 표시용 한 글자
        public int Cost;
        public int UnlockGrade;    // 필요한 거점 등급
        public bool Graded;        // 시설 등급(1~4)이 있는가
        public SiteRequirement Site;
        public int IncomePerGrade; // 정유·광산
        public string Effect;      // 패널 설명
    }

    /// <summary>
    /// 전략 규칙의 모든 조절 수치. 7단계 디버그 메뉴가 이 객체를 직접 고친다.
    /// </summary>
    public sealed class Balance
    {
        public int StartingFunds = 1000;
        public int HqBudget = 300;
        public int UpkeepPerHex = 10;
        public int ExpandBaseCost = 50;
        public int ExpandCostPerHex = 25;
        public int OutpostCost = 600;
        public int BankruptTurns = 3;
        public float FinanceRatePerGrade = 0.02f;
        public float FacilityUpgradeFactor = 0.5f; // 비용 = 건설비 × 다음 등급 × 계수
        public int MaxFacilityGrade = 4;

        /// <summary>거점 등급별 시설 총개수 상한 (인덱스 = 등급).</summary>
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

        /// <summary>활주로 직선 길이별 운용 가능 기체 크기: 1 소형, 2 중형, 3 대형.</summary>
        public int[] RunwayForSize = { 0, 1, 2, 3 };

        public readonly Dictionary<FacilityType, FacilityDef> Facilities = new();

        // ---- 편대·기체·파일럿 (3단계) ----
        public int SquadronMaxSize = 4;
        public int SquadronsPerHangar = 2;
        /// <summary>기지 설비(보급창고)에 의한 작전 반경 증가.</summary>
        public int DepotRadiusBonus = 1;
        /// <summary>공중급유: (기체 반경 + 설비 보너스) × 이 값.</summary>
        public float RefuelRadiusMultiplier = 2f;
        /// <summary>공중급유 비용 = 편대 출격 비용 × 이 값.</summary>
        public float RefuelCostRatio = 1f;
        /// <summary>전초기지는 가장 가까운 아군 거점에서 이 거리 이상.</summary>
        public int OutpostMinDistance = 3;
        public int OutpostCostPerDistance = 50;
        public int SalaryRegular = 40;
        public int SalaryNamed = 80;
        public int HirePilotCost = 200;
        public float SellRatio = 0.5f;
        /// <summary>유흥가: 주둔 파일럿 급여 환수율 = 등급 × 이 값.</summary>
        public float EntertainmentRefundPerGrade = 0.15f;
        /// <summary>턴당 손상 회복(%): 정비소 등급 × 이 값. 정비소 없으면 RepairWithoutMaintenance.</summary>
        public int RepairPerMaintenanceGrade = 15;
        public int RepairWithoutMaintenance = 5;
        /// <summary>수리비 = 기체 가격 × 회복 비율 × 이 값.</summary>
        public float RepairCostFactor = 0.3f;
        public int FatigueRecovery = 20;
        public int FatigueRecoveryPerEntertainmentGrade = 10;
        /// <summary>이 값 이상이면 탈진 (출격 불가).</summary>
        public int FatigueExhausted = 70;

        // ---- 출격·자동 정산 (4단계) ----
        /// <summary>기체 크기별 출격 비용 (연료·탄약). 인덱스 = 크기.</summary>
        public int[] SortieCostBySize = { 0, 30, 50, 80 };
        public float DepotSortieDiscount = 0.2f;
        /// <summary>이 손상(%) 이상이면 다음 기체로 교대.</summary>
        public int SwapDamage = 60;
        public int MaxRounds = 12;
        /// <summary>직접 플레이 대비 불리 계수 (아군 공격력에 곱함).</summary>
        public float AutoResolvePenalty = 0.85f;
        /// <summary>적 그룹이 전경 기체에 주는 라운드당 손상(%) = 남은 전력 × 이 값.</summary>
        public float EnemyHitPerStrength = 1.0f;
        public float OverlapAaReduction = 0.3f;
        public int BountyUnit = 300;
        public int BountyBase = 500;
        public float CaptureChance = 0.5f;
        public int RansomCost = 300;
        /// <summary>포로 파일럿 급여 비율 (0~1). 0이면 포로는 급여 없음.</summary>
        public float CapturedSalaryRatio = 0.5f;
        public int SortieFatigue = 30;
        public int XpPerRound = 10;
        /// <summary>숙련도 상승에 필요한 경험치 = 현재 숙련도 × 이 값.</summary>
        public int XpPerSkill = 100;
        public int MaxReturnTurns = 3;

        // ---- 적 세력·시야 (5단계) ----
        public bool VisionEnabled = true;
        /// <summary>아군 영역 밖으로 기본 시야 칸 수.</summary>
        public int TerritoryVisionRange = 1;
        /// <summary>레이더 등급 1당 시야 +칸 (영역 기준).</summary>
        public int RadarRangePerGrade = 1;
        public int SightingMemoryTurns = 5;
        public int EnemyMovePoints = 2;
        public int MountainMoveCost = 2;
        /// <summary>적 세력이 아군 거점의 주둔 편대 수를 아는 범위 (부대·거점 기준).</summary>
        public int EnemyDetectRadius = 3;
        public float KnownAircraftDefenseWeight = 8f;
        public int RegroupBelowStrength = 15;
        public int DefensiveTurnsAfterLoss = 2;
        public int AntiAirDefensePerGrade = 5;
        public int SamDefensePerGrade = 10;
        /// <summary>요격·대공 후 남은 적 전력이 이 값 이상이면 거점 상실.</summary>
        public int CaptureThreshold = 10;
        public int InterceptFatigue = 20;
        /// <summary>재배치(이동 비행) 피로.</summary>
        public int RelocateFatigue = 10;
        public int CapturedBaseAntiAir = 5;
        public int FactionCombatMaxDamage = 10;
        public int ReinforceTurns = 6;
        public int ReinforceAir = 10;
        public int ReinforceGround = 12;
        public int EnemyBaseRegen = 1;
        public int EnemyUnitRegen = 2;

        // ---- 지상군·상부 (6단계) ----
        public int GroundHireCost = 200;
        public int GroundBaseStrength = 15;
        public int GroundStrengthPerBarracksGrade = 5;
        public int GroundUpkeep = 30;
        public int GroundDeployCost = 50;
        public int GroundMovePoints = 2;
        public int GroundVisionRange = 1;
        /// <summary>지상군(주둔·출동)에서 이 거리 안이면 전초기지 건설 가능 (공중 도달과 별개).</summary>
        public int GroundOutpostReach = 2;
        public int StartingTrust = 50;
        /// <summary>상부 예산 = HqBudget × (이 값 + 신뢰도/100).</summary>
        public float BudgetBaseRatio = 0.5f;
        public int TrustGoal = 30;
        public int TrustCapture = 10;
        public int TrustOutpost = 5;
        public int TrustDirectiveSuccess = 10;
        public int TrustDirectiveFail = 15;
        public int TrustRefuse = 10;
        public int TrustBaseLost = 10;
        public int DirectiveInterval = 5;
        public int DirectiveDeadline = 4;
        public int DirectiveExpandHexes = 4;

        public readonly Dictionary<AircraftType, AircraftDef> Aircraft = new();

        public Balance()
        {
            AddAircraft(AircraftType.F5, "F-5", AircraftRole.Fighter, 1, 1, 400, 80, air: 8, ground: 3, bombing: 1, range: 3);
            AddAircraft(AircraftType.A4, "A-4", AircraftRole.Attacker, 1, 1, 450, 100, air: 4, ground: 8, bombing: 3, range: 3);
            AddAircraft(AircraftType.F4, "F-4", AircraftRole.Multirole, 2, 2, 900, 120, air: 7, ground: 6, bombing: 4, range: 4);
            AddAircraft(AircraftType.B57, "B-57", AircraftRole.Bomber, 3, 2, 1400, 160, air: 1, ground: 5, bombing: 10, range: 5);

            Add(FacilityType.Hangar, "격납고", "격", 150, 1, false, effect: "주둔 편대 2 (3단계)");
            Add(FacilityType.Runway, "활주로", "활", 100, 1, false, effect: "직선 연속 길이 = 운용 기체 크기");
            Add(FacilityType.Barracks, "병영", "병", 150, 1, true, effect: "지상군 고용 (6단계)");
            Add(FacilityType.AntiAir, "대공포", "대", 120, 1, true, effect: "거점 방어 (5단계)");
            Add(FacilityType.Maintenance, "정비소", "정", 250, 2, true, effect: "기체 수리·티어 (3단계)");
            Add(FacilityType.Radar, "레이더", "레", 200, 2, true, effect: "이 거점 시야 +등급칸 (기본: 영역 +1)");
            Add(FacilityType.Depot, "보급창고", "보", 150, 2, false, effect: "출격 비용 할인·작전 반경 (4단계)");
            Add(FacilityType.Entertainment, "유흥가", "유", 200, 2, true, effect: "급여 환수·피로 회복 (3단계)");
            Add(FacilityType.Refinery, "정유시설", "석", 300, 2, true, SiteRequirement.Oil, 80, "수입 +80 × 등급");
            Add(FacilityType.Mine, "광산", "광", 250, 2, true, SiteRequirement.Mountain, 50, "수입 +50 × 등급");
            Add(FacilityType.Armory, "무기고·개조 공방", "무", 300, 3, false, effect: "부품 개조·무장 관리");
            Add(FacilityType.Sam, "SAM", "S", 350, 3, true, effect: "거점 방어 (5단계)");
            Add(FacilityType.Finance, "금융", "금", 500, 4, true, effect: "보유 자금 × 2% × 등급");
        }

        void Add(FacilityType type, string name, string shortName, int cost, int unlock, bool graded,
            SiteRequirement site = SiteRequirement.None, int income = 0, string effect = "")
        {
            Facilities[type] = new FacilityDef
            {
                Type = type, Name = name, Short = shortName, Cost = cost, UnlockGrade = unlock,
                Graded = graded, Site = site, IncomePerGrade = income, Effect = effect,
            };
        }

        void AddAircraft(AircraftType type, string name, AircraftRole role, int size, int tier, int price, int hp,
            int air, int ground, int bombing, int range)
        {
            Aircraft[type] = new AircraftDef
            {
                Type = type, Name = name, Role = role, Size = size, Tier = tier, Price = price, Hp = hp,
                Air = air, Ground = ground, Bombing = bombing, Range = range,
            };
        }

        public FacilityDef Def(FacilityType type) => Facilities[type];

        public AircraftDef Def(AircraftType type) => Aircraft[type];

        public int Salary(Pilot p) => p.Named ? SalaryNamed : SalaryRegular;
    }
}
