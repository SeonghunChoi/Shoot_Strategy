using System.Collections.Generic;
using System.Linq;

namespace Prototype.Strategy.Rules
{
    public enum TraceKind
    {
        Squadron, // 아군 편대 비행
        Enemy,    // 적 부대 이동
        Ground,   // 아군 지상군 이동
        Event,    // 교전·피습 등 한 칸 표시
    }

    /// <summary>맵 재생용 이동 기록. Path[0]이 출발 칸. Seen은 칸마다 그 순간 플레이어 시야 안이었는지.</summary>
    public sealed class MoveTrace
    {
        public TraceKind Kind;
        public int Owner;
        public int UnitId;
        public string Name;
        public readonly List<Hex> Path = new();
        public readonly List<bool> Seen = new();
        public string Event;
        /// <summary>체공 편대의 귀환: 재생 전까지는 체공 위치에 감시 반경과 함께 표시.</summary>
        public bool Loiter;
    }

    public sealed class Base
    {
        public int Id;
        public string Name;
        public Hex Hex;       // 본부 칸 (시설 불가)
        public int Grade;
        public int Owner;     // 0 = 플레이어
        /// <summary>적 거점 방어 전력 (거점 폭격의 대공 그룹).</summary>
        public int AntiAir;
        /// <summary>적 거점 시설 전력 (거점 폭격의 시설 그룹).</summary>
        public int Structures;
    }

    public sealed class GameEvent
    {
        public int Turn;
        public string Kind;
        public string Detail;
    }

    public sealed class Facility
    {
        public FacilityType Type;
        public int Grade = 1;
    }

    public struct BudgetLine
    {
        public string Label;
        public int Amount; // 수입 +, 지출 −
    }

    /// <summary>한 턴의 정산 결과 (턴 로그).</summary>
    public sealed class TurnReport
    {
        public int Turn;
        public List<BudgetLine> Lines = new();
        public int Net;
        public int FundsAfter;
        public int DeficitStreak;
    }

    /// <summary>
    /// 전략 월드 상태. 로직 없는 데이터로 두고 규칙은 Strategy·Economy 같은 무상태 서비스가 다룬다 (기술 설계 3.3 원칙만 차용).
    /// </summary>
    public sealed class World
    {
        public const int Player = 0;

        public HexMap Map;
        public Rng Rng;
        /// <summary>시야 판정용 Balance (World.Create가 넣음). 규칙 함수 시그니처를 늘리지 않기 위한 참조.</summary>
        public Balance VisionBalance;
        public int Turn = 1;
        public int Funds;
        /// <summary>자금 0 미만으로 마친 연속 턴 수.</summary>
        public int DeficitStreak;
        public string GameOver; // null이면 진행 중

        public readonly List<Base> Bases = new();
        /// <summary>영역 칸 → 소속 거점 Id (본부 칸 포함).</summary>
        public readonly Dictionary<Hex, int> Territory = new();
        public readonly Dictionary<Hex, Facility> Facilities = new();
        public readonly List<TurnReport> Reports = new();

        public readonly List<Aircraft> Aircraft = new();
        public readonly List<Pilot> Pilots = new();
        /// <summary>상점의 파일럿 고용 후보 (Id 0, 고용하면 Pilots로 옮김).</summary>
        public readonly List<Pilot> HireCandidates = new();
        public readonly List<Squadron> Squadrons = new();
        public int NextId = 1;

        public readonly List<EnemyUnit> EnemyUnits = new();
        public readonly List<Faction> Factions = new();
        /// <summary>적 부대 마지막 목격 (부대 Id → 기록).</summary>
        public readonly Dictionary<int, Sighting> Sightings = new();
        /// <summary>가장 최근 적 턴의 사건 요약.</summary>
        public readonly List<string> EnemyEvents = new();
        public readonly List<InterceptReport> Intercepts = new();

        public Faction FactionById(int id) => Factions.FirstOrDefault(f => f.Id == id);

        public readonly List<GroundUnit> GroundUnits = new();
        public int Trust;
        public readonly List<string> TrustLog = new();
        public Directive Directive;
        public int DirectiveTimer;
        /// <summary>장기 목표 거점 (점령하면 승리).</summary>
        public int GoalBaseId;
        public bool Victory;
        public readonly GameStats Stats = new();
        /// <summary>플레이 로그용 사건 기록 (종류, 내용). 로거가 턴마다 새로 쌓인 것만 내보낸다.</summary>
        public readonly List<GameEvent> Events = new();

        public void Log(string kind, string detail) => Events.Add(new GameEvent { Turn = Turn, Kind = kind, Detail = detail });
        /// <summary>이번 턴 출격 계획 (순서대로 실행).</summary>
        public readonly List<SortiePlan> SortiePlan = new();
        /// <summary>가장 최근 출격 실행의 디브리핑.</summary>
        public readonly List<SortieReport> Debriefs = new();
        /// <summary>이번 출격 실행·턴 종료에서 일어난 이동과 사건 (뷰가 맵 위에서 재생). 출격 실행·턴 종료 때 새로 쌓는다.</summary>
        public readonly List<MoveTrace> Traces = new();

        public MoveTrace AddTrace(TraceKind kind, int owner, int id, string name, IEnumerable<Hex> path, bool seen = true)
        {
            var t = new MoveTrace { Kind = kind, Owner = owner, UnitId = id, Name = name };
            foreach (var h in path)
            {
                t.Path.Add(h);
                t.Seen.Add(seen);
            }
            Traces.Add(t);
            return t;
        }

        public void AddEvent(Hex h, string text, bool seen = true) => AddTrace(TraceKind.Event, 0, 0, text, new[] { h }, seen).Event = text;
        /// <summary>이번 턴에 실행한 출격의 목표 (겹침 보정용).</summary>
        public readonly List<Hex> SortiedTargets = new();

        public EnemyUnit EnemyUnitAt(Hex h) => EnemyUnits.FirstOrDefault(u => u.Hex == h);

        public Aircraft AircraftById(int id) => id == 0 ? null : Aircraft.FirstOrDefault(a => a.Id == id);
        public Pilot PilotById(int id) => id == 0 ? null : Pilots.FirstOrDefault(p => p.Id == id);
        public Squadron SquadronById(int id) => id == 0 ? null : Squadrons.FirstOrDefault(s => s.Id == id);
        public IEnumerable<Squadron> SquadronsAt(Base b) => Squadrons.Where(s => s.BaseId == b.Id);
        public IEnumerable<Aircraft> ReserveAircraft => Aircraft.Where(a => a.SquadronId == 0);
        public IEnumerable<Pilot> IdlePilots => Pilots.Where(p => p.AircraftId == 0);

        public Base BaseById(int id) => Bases.First(b => b.Id == id);

        public Base BaseOf(Hex h) => Territory.TryGetValue(h, out var id) ? BaseById(id) : null;

        public Base BaseAt(Hex h) => Bases.FirstOrDefault(b => b.Hex == h);

        public IEnumerable<Hex> TerritoryOf(Base b) => Territory.Where(kv => kv.Value == b.Id).Select(kv => kv.Key);

        public IEnumerable<Hex> OwnedHexes(int owner) =>
            Territory.Where(kv => BaseById(kv.Value).Owner == owner).Select(kv => kv.Key);

        public IEnumerable<(Hex hex, Facility facility)> FacilitiesOf(Base b) =>
            Facilities.Where(kv => Territory.TryGetValue(kv.Key, out var id) && id == b.Id).Select(kv => (kv.Key, kv.Value));

        public static World Create(ulong seed, Balance balance)
        {
            var world = new World
            {
                Map = HexMap.Parse(MapLayouts.Zone1),
                Rng = new Rng(seed),
                Funds = balance.StartingFunds,
                Trust = balance.StartingTrust,
                VisionBalance = balance,
            };
            StartingSetup.Apply(world);
            SquadronRules.RefreshCandidates(world, balance);
            return world;
        }
    }
}
