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
        public int OperationRadius = 3;
        public int DepotRadiusBonus = 1;
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

        public readonly Dictionary<AircraftType, AircraftDef> Aircraft = new();

        public Balance()
        {
            AddAircraft(AircraftType.F5, "F-5", AircraftRole.Fighter, 1, 1, 400, 80, air: 8, ground: 3, bombing: 1);
            AddAircraft(AircraftType.A4, "A-4", AircraftRole.Attacker, 1, 1, 450, 100, air: 4, ground: 8, bombing: 3);
            AddAircraft(AircraftType.F4, "F-4", AircraftRole.Multirole, 2, 2, 900, 120, air: 7, ground: 6, bombing: 4);
            AddAircraft(AircraftType.B57, "B-57", AircraftRole.Bomber, 3, 2, 1400, 160, air: 1, ground: 5, bombing: 10);

            Add(FacilityType.Hangar, "격납고", "격", 150, 1, false, effect: "주둔 편대 2 (3단계)");
            Add(FacilityType.Runway, "활주로", "활", 100, 1, false, effect: "직선 연속 길이 = 운용 기체 크기");
            Add(FacilityType.Barracks, "병영", "병", 150, 1, true, effect: "지상군 고용 (6단계)");
            Add(FacilityType.AntiAir, "대공포", "대", 120, 1, true, effect: "거점 방어 (5단계)");
            Add(FacilityType.Maintenance, "정비소", "정", 250, 2, true, effect: "기체 수리·티어 (3단계)");
            Add(FacilityType.Radar, "레이더", "레", 200, 2, true, effect: "탐지 반경 (5단계)");
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
            int air, int ground, int bombing)
        {
            Aircraft[type] = new AircraftDef
            {
                Type = type, Name = name, Role = role, Size = size, Tier = tier, Price = price, Hp = hp,
                Air = air, Ground = ground, Bombing = bombing,
            };
        }

        public FacilityDef Def(FacilityType type) => Facilities[type];

        public AircraftDef Def(AircraftType type) => Aircraft[type];

        public int Salary(Pilot p) => p.Named ? SalaryNamed : SalaryRegular;
    }
}
