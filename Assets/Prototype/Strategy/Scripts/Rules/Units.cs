using System.Collections.Generic;

namespace Prototype.Strategy.Rules
{
    public enum AircraftRole
    {
        Fighter,   // 제공기
        Attacker,  // 공격기
        Multirole, // 다용도기
        Bomber,    // 폭격기
    }

    public enum AircraftType
    {
        F5,
        A4,
        F4,
        B57,
    }

    /// <summary>기체 정의 (기획서 6.2). 전투 수치(Air·Ground·Bombing)는 4단계 자동 정산에서 쓴다.</summary>
    public sealed class AircraftDef
    {
        public AircraftType Type;
        public string Name;
        public AircraftRole Role;
        public int Size;  // 1 소형, 2 중형, 3 대형 (활주로 길이)
        public int Tier;  // 1~4 (정비소 등급)
        [Tune("기체", "가격", 0f, 3000f)]
        public int Price;
        public int Hp;
        [Tune("기체", "공중", 0f, 20f)]
        public int Air;
        [Tune("기체", "지상", 0f, 20f)]
        public int Ground;
        [Tune("기체", "폭격", 0f, 20f)]
        public int Bombing;
        /// <summary>기체 작전 반경 (헥스). 편대 반경은 편대 안 최솟값.</summary>
        [Tune("기체", "작전 반경", 1f, 8f)]
        public int Range;
    }

    /// <summary>보유 기체 1기. 편대에 들어 있지 않으면 예비 (위치 없음).</summary>
    public sealed class Aircraft
    {
        public int Id;
        public AircraftType Type;
        /// <summary>손상 0~100 (%).</summary>
        public int Damage;
        public int PilotId;    // 0이면 없음
        public int SquadronId; // 0이면 예비
    }

    /// <summary>파일럿 (기획서 6.3). 네임드는 이름과 더 높은 급여, 일반은 숙련도·피로만.</summary>
    public sealed class Pilot
    {
        public int Id;
        public string Name;
        public bool Named;
        public int Skill;              // 1~5
        public AircraftType Aptitude;  // 숙련 기종
        /// <summary>피로 0~100.</summary>
        public int Fatigue;
        public int AircraftId; // 0이면 대기
        public int Xp;
        /// <summary>격추 후 복귀까지 남은 턴 (0이면 가용).</summary>
        public int ReturnTurns;
        /// <summary>포로 (몸값으로 복귀).</summary>
        public bool Captured;
    }

    /// <summary>적 부대 (4단계: 고정 표적, 5단계: 이동·공격). 전력은 공중·지상 수치.</summary>
    public sealed class EnemyUnit
    {
        public int Id;
        public string Name;
        public Hex Hex;
        public int Air;
        public int Ground;
        public int Owner = 1;
        /// <summary>이번 턴 행동 목표 (표시용).</summary>
        public Hex? Goal;
        /// <summary>이번 적 턴에 지상군과 교전함 (중복 교전 방지).</summary>
        public bool EngagedThisTurn;
        public int Strength => Air + Ground;
    }

    /// <summary>편대 (기획서 6.1). 거점에 주둔하며 2~4기.</summary>
    public sealed class Squadron
    {
        public int Id;
        public string Name;
        public int BaseId;
        /// <summary>이번 턴에 재배치함 (즉시 도착, 그 턴 출격 불가. 요격은 새 거점에서 가능).</summary>
        public bool MovedThisTurn;
        public readonly List<int> AircraftIds = new();
        /// <summary>이번 턴 출격 여부 (피로 회복 판정, 4단계부터 설정).</summary>
        public bool SortiedThisTurn;
        /// <summary>이번 출격에 공중급유 (비용 추가, 작전 반경 배율). 출격 후 꺼진다.</summary>
        public bool Refuel;
    }
}
