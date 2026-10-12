using System;
using System.Collections.Generic;
using System.Linq;

namespace Prototype.Strategy.Rules
{
    /// <summary>호출한 공중 급유기. 호출한 턴의 적 턴에 지점에 도착하고, 다음 내 턴에만 급유할 수 있다.</summary>
    public sealed class Tanker
    {
        public int Id;
        public Hex Hex;
        /// <summary>띄운 거점 (활주로 등급 조건, 거점마다 1대).</summary>
        public int BaseId;
        public int CalledTurn;
    }

    /// <summary>
    /// 공중 급유기 호출 (도시판 2단계, 예전 "공중급유 = 반경 × 2" 대체).
    /// T턴 호출(비용) → T턴 적 턴에 도착, 그 지점을 노릴 수 있는 적이 맞닿으면 격추 → T+1턴 내 턴에 주변 칸 편대 급유 → T+1턴 끝(적 턴 전)에 사라짐.
    /// </summary>
    public static class TankerRules
    {
        /// <summary>급유기를 띄울 수 있는 아군 거점 (활주로 등급 조건).</summary>
        public static IEnumerable<Base> Hosts(World w, Balance bal) =>
            w.Bases.Where(b => b.Owner == World.Player && BaseRules.MaxAircraftSize(b) >= bal.TankerMinRunway);

        public static int Range(World w, Balance bal, Base host) =>
            bal.TankerRange + (BaseRules.Has(w, host, FacilityType.Depot) ? bal.TankerDepotBonus : 0);

        /// <summary>아직 급유기를 띄우지 않은 거점 중 이 칸까지 닿는 가장 가까운 거점.</summary>
        public static Base HostFor(World w, Balance bal, Hex h) =>
            Hosts(w, bal).Where(b => w.Tankers.All(t => t.BaseId != b.Id) && Hex.Distance(b.Hex, h) <= Range(w, bal, b))
                .OrderBy(b => Hex.Distance(b.Hex, h)).FirstOrDefault();

        public static int Capacity(World w, Balance bal) => Hosts(w, bal).Count();

        /// <summary>도착 대기 중 (이번 적 턴에 도착, 격추될 수 있음).</summary>
        public static bool IsPending(World w, Tanker t) => w.Turn == t.CalledTurn;

        /// <summary>급유 가능 (도착한 다음 내 턴).</summary>
        public static bool IsActive(World w, Tanker t) => w.Turn == t.CalledTurn + 1;

        public static string CanCall(World w, Balance bal)
        {
            var cap = Capacity(w, bal);
            if (cap == 0) return $"활주로 {bal.TankerMinRunway}등급 이상인 거점 필요";
            if (w.Tankers.Count >= cap) return $"동시 호출 {cap}대 모두 사용 중";
            if (w.Funds < bal.TankerCost) return "자금 부족";
            return null;
        }

        public static string CanCallAt(World w, Balance bal, Hex h)
        {
            var reason = CanCall(w, bal);
            if (reason != null) return reason;
            if (!w.Map.Contains(h)) return "맵 밖";
            if (w.Tankers.Any(t => t.Hex == h)) return "이미 급유기 있음";
            if (HostFor(w, bal, h) == null) return "급유기를 띄울 거점에서 너무 멂";
            return null;
        }

        public static IEnumerable<Hex> CallCandidates(World w, Balance bal) =>
            CanCall(w, bal) != null ? Enumerable.Empty<Hex>() : w.Map.AllHexes().Where(h => CanCallAt(w, bal, h) == null);

        public static Tanker Call(World w, Balance bal, Hex h)
        {
            if (CanCallAt(w, bal, h) != null) return null;
            var host = HostFor(w, bal, h);
            w.Funds -= bal.TankerCost;
            var t = new Tanker { Id = w.NextId++, Hex = h, BaseId = host.Id, CalledTurn = w.Turn };
            w.Tankers.Add(t);
            w.Log("급유기 호출", $"{h.ToOffset()} · {host.Name}에서 · 비용 {bal.TankerCost}");
            return t;
        }

        /// <summary>이 편대가 지금 급유할 수 있는 급유기 (도착했고, 주변 1칸, 이 급유기에서 아직 안 받음).</summary>
        public static Tanker RefuelableFor(World w, Balance bal, Squadron s) =>
            s.AirHex is { } at
                ? w.Tankers.FirstOrDefault(t => IsActive(w, t) && Hex.Distance(t.Hex, at) <= bal.TankerRefuelRange && s.RefueledTankerId != t.Id)
                : null;

        /// <summary>급유: 기체 연료를 가득, 드롭탱크를 달고 있으면 탱크도 가득. 이동력은 쓰지 않는다.</summary>
        public static bool Refuel(World w, Balance bal, Squadron s)
        {
            var t = RefuelableFor(w, bal, s);
            if (t == null) return false;
            s.Fuel = FlightRules.MaxFuel(w, bal, s);
            if (s.TankAttached) s.TankFuel = FlightRules.TankCapacity(w, bal, s);
            s.RefueledTankerId = t.Id;
            w.Log("급유", $"{s.Name} @ {s.AirHex.Value.ToOffset()} · 연료 {s.Fuel}{(s.TankAttached ? $" + 탱크 {s.TankFuel}" : "")}");
            return true;
        }

        /// <summary>적 부대가 노리는 급유기: 세력이 탐지했고(부대·거점에서 탐지 거리 안), 도착 대기 중이고, 이번 턴에 맞닿을 수 있는 거리.</summary>
        public static Tanker TargetFor(World w, Balance bal, Faction f, EnemyUnit u)
        {
            if (u.Air <= 0) return null;
            return w.Tankers.Where(t => IsPending(w, t) && EnemyTurn.FactionSees(w, bal, f, t.Hex) && Hex.Distance(u.Hex, t.Hex) <= bal.EnemyMovePoints + 1)
                .OrderBy(t => Hex.Distance(u.Hex, t.Hex)).FirstOrDefault();
        }

        /// <summary>공중 전력이 있는 적 부대가 도착 대기 중인 급유기와 맞닿으면 격추.</summary>
        public static void ShootDownAdjacent(World w, EnemyUnit u)
        {
            if (u.Air <= 0) return;
            foreach (var t in w.Tankers.Where(t => IsPending(w, t) && Hex.Distance(t.Hex, u.Hex) <= 1).ToList())
            {
                w.Tankers.Remove(t);
                w.AddEvent(t.Hex, "급유기 격추");
                w.EnemyEvents.Add($"급유기 격추 — {t.Hex.ToOffset()}에서 {u.Name}에게");
                w.Log("급유기 격추", $"{t.Hex.ToOffset()} ← {u.Name}");
            }
        }

        /// <summary>대공 위협 범위: 보이는 적 부대(공중 전력 있음)가 다음 적 턴에 닿아 쏠 수 있는 칸 (이동력 + 1).</summary>
        public static HashSet<Hex> ThreatHexes(World w, Balance bal, HashSet<Hex> visible)
        {
            var set = new HashSet<Hex>();
            foreach (var u in w.EnemyUnits.Where(x => x.Air > 0 && visible.Contains(x.Hex)))
                foreach (var h in Hex.Range(u.Hex, bal.EnemyMovePoints + 1))
                    if (w.Map.Contains(h)) set.Add(h);
            return set;
        }

        /// <summary>턴 종료 시작(적 턴 전): 급유를 마친 급유기는 떠난다.</summary>
        public static void Depart(World w) => w.Tankers.RemoveAll(t => w.Turn > t.CalledTurn);
    }
}
