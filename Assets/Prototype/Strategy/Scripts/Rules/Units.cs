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
        public int Price;
        public int Hp;
        public int Air;
        public int Ground;
        public int Bombing;
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
    }

    /// <summary>편대 (기획서 6.1). 거점에 주둔하며 2~4기.</summary>
    public sealed class Squadron
    {
        public int Id;
        public string Name;
        public int BaseId;
        /// <summary>재배치 목적지 (0이면 없음). 턴 종료 시 도착.</summary>
        public int MoveToBaseId;
        public readonly List<int> AircraftIds = new();
        /// <summary>이번 턴 출격 여부 (피로 회복 판정, 4단계부터 설정).</summary>
        public bool SortiedThisTurn;
    }
}
