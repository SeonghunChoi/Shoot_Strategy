using System;
using System.Collections.Generic;
using System.Linq;

namespace Prototype.Strategy.Rules
{
    /// <summary>
    /// C안 다턴 비행: 출격한 편대는 맵 위 부대가 되어 턴마다 속도만큼 날고, 연료가 떨어지기 전에 아군 거점에 내려야 한다.
    /// 명령은 내 턴 안에 바로 실행·정산된다 (턴 순서 원칙). 적 턴에는 맞닿은 적 부대가 비행 중 편대에 먼저 교전을 건다 (EnemyTurn).
    /// </summary>
    public static class FlightRules
    {
        public static bool IsFlying(Squadron s) => s.AirHex != null;

        public static Hex Position(World w, Squadron s) => s.AirHex ?? w.BaseById(s.BaseId).Hex;

        /// <summary>편대 속도 = 가장 느린 기체 (비행 중이면 날고 있는 기체 기준).</summary>
        public static int Speed(World w, Balance bal, Squadron s)
        {
            var ids = IsFlying(s) && s.FlyingAircraftIds.Count > 0 ? s.FlyingAircraftIds : s.AircraftIds;
            var speeds = ids.Select(w.AircraftById).Where(a => a != null).Select(a => bal.Def(a.Type).Speed).ToList();
            return speeds.Count == 0 ? 0 : speeds.Min();
        }

        /// <summary>기체 연료 = 작전 반경 × 2 (왕복) + 예비.</summary>
        public static int MaxFuel(World w, Balance bal, Squadron s) => SquadronRules.OperationRadius(w, bal, s) * 2 + bal.ReserveFuel;

        /// <summary>드롭탱크 연료 = 기체 연료 × (1 + 비율)을 반올림한 값 − 기체 연료.</summary>
        public static int TankCapacity(World w, Balance bal, Squadron s)
        {
            var max = MaxFuel(w, bal, s);
            return (int)Math.Round(max * (1f + bal.DropTankRatio), MidpointRounding.AwayFromZero) - max;
        }

        /// <summary>남은 연료 합 (기체 + 드롭탱크).</summary>
        public static int TotalFuel(Squadron s) => s.Fuel + s.TankFuel;

        /// <summary>연료 소모: 드롭탱크 분량부터.</summary>
        static void Burn(Squadron s, int amount)
        {
            var fromTank = Math.Min(s.TankFuel, amount);
            s.TankFuel -= fromTank;
            s.Fuel -= amount - fromTank;
        }

        /// <summary>드롭탱크 투하 (교전·공격 목표 도착). 남은 탱크 연료도 사라진다.</summary>
        public static void Jettison(World w, Squadron s)
        {
            if (!s.TankAttached) return;
            var left = s.TankFuel;
            s.TankAttached = false;
            s.TankFuel = 0;
            w.Log("드롭탱크", $"{s.Name} 투하 (남은 탱크 연료 {left})");
        }

        /// <summary>아군 영역 상공인지 (초계: 여기서 턴을 마치면 연료를 쓰지 않는다).</summary>
        public static bool OverOwnTerritory(World w, Squadron s) => s.AirHex is { } at && w.BaseOf(at)?.Owner == World.Player;

        /// <summary>비행 중 교전에 나서는 기체: 출격했고, 파일럿이 타 있고, 격추되지 않은 기체.</summary>
        public static List<Aircraft> Flyers(World w, Squadron s) =>
            s.FlyingAircraftIds.Select(w.AircraftById).Where(a => a != null && a.PilotId != 0 && a.Damage < 100 && s.AircraftIds.Contains(a.Id)).ToList();

        /// <summary>이번 명령으로 갈 수 있는 거리 (거점에 있으면 이륙 후 속도·연료 기준).</summary>
        public static int Reach(World w, Balance bal, Squadron s) =>
            IsFlying(s) ? Math.Min(s.MovesLeft, TotalFuel(s)) : Math.Min(Speed(w, bal, s), MaxFuel(w, bal, s) + (s.DropTank ? TankCapacity(w, bal, s) : 0));

        /// <summary>가장 가까운 아군 거점 (착륙 후보)과 거리.</summary>
        public static (Base b, int distance) NearestLanding(World w, Squadron s)
        {
            var at = Position(w, s);
            var b = w.Bases.Where(x => x.Owner == World.Player).OrderBy(x => Hex.Distance(x.Hex, at)).FirstOrDefault();
            return (b, b == null ? int.MaxValue : Hex.Distance(b.Hex, at));
        }

        public static bool IsAttackTarget(World w, Squadron s, Hex h) => !s.AttackedThisTurn && SortieRules.MissionAt(w, h) != null;

        public static string CanLaunch(World w, Balance bal, Squadron s)
        {
            if (IsFlying(s)) return "비행 중";
            if (s.LandedThisTurn) return "착륙한 턴 — 출격 불가";
            if (s.MovedThisTurn) return "이번 턴 재배치함";
            if (!SortieRules.SortieAircraft(w, bal, s).Any()) return "출격 가능한 기체 없음";
            if (w.Funds < SortieRules.Cost(w, bal, s)) return "자금 부족";
            return null;
        }

        public static string CanOrder(World w, Balance bal, Squadron s, Hex dest)
        {
            if (!IsFlying(s))
            {
                var launch = CanLaunch(w, bal, s);
                if (launch != null) return launch;
            }
            else if (s.MovesLeft <= 0) return "이번 턴 이동 끝";
            else if (TotalFuel(s) <= 0) return "연료 없음";
            if (!w.Map.Contains(dest)) return "맵 밖";
            var d = Hex.Distance(Position(w, s), dest);
            if (d == 0) return "현재 위치";
            if (d > Reach(w, bal, s)) return "이동 범위 밖";
            if (w.BaseAt(dest) is { Owner: World.Player } lb)
            {
                if (!IsFlying(s)) return "아군 거점 (재배치는 격납고 탭)";
                if (lb.Id != s.BaseId && w.SquadronsAt(lb).Count(x => !IsFlying(x)) >= SquadronRules.HangarCapacity(w, bal, lb)) return "격납고 부족";
                if (s.AircraftIds.Any(id => SquadronRules.CanOperate(w, bal, lb, w.AircraftById(id).Type) != null)) return "운용 불가 기체 (활주로·정비소)";
            }
            else if (SortieRules.MissionAt(w, dest) != null && s.AttackedThisTurn) return "이번 턴 이미 교전함";
            return null;
        }

        /// <summary>갈 수 있는 칸 (명령 가능한 칸만).</summary>
        public static IEnumerable<Hex> Destinations(World w, Balance bal, Squadron s) =>
            Hex.Range(Position(w, s), Reach(w, bal, s)).Where(h => CanOrder(w, bal, s, h) == null);

        /// <summary>
        /// 명령: 거점에 있으면 이륙(비용·연료) → 직선으로 날아감(칸마다 연료·이동 1) → 아군 거점이면 착륙, 적 부대·거점이면 교전.
        /// 교전이 있었으면 그 디브리핑을 돌려준다. 맵 재생 기록은 World.Traces에 새로 쌓인다.
        /// </summary>
        public static SortieReport Order(World w, Balance bal, Squadron s, Hex dest)
        {
            if (CanOrder(w, bal, s, dest) != null) return null;
            w.Traces.Clear();
            if (!IsFlying(s)) Launch(w, bal, s);

            var from = s.AirHex.Value;
            var trace = w.AddTrace(TraceKind.Squadron, World.Player, s.Id, s.Name, new[] { from });
            foreach (var step in Hex.Line(from, dest).Skip(1))
            {
                s.AirHex = step;
                Burn(s, 1);
                s.MovesLeft--;
                trace.Path.Add(step);
                trace.Seen.Add(true);
            }

            if (w.BaseAt(dest) is { Owner: World.Player } lb)
            {
                Land(w, s, lb);
                return null;
            }
            if (!IsAttackTarget(w, s, dest)) return null;

            s.AttackedThisTurn = true;
            Jettison(w, s); // 공격 목표에 도착하면 드롭탱크를 버린다
            var rep = SortieRules.Resolve(w, bal, s, dest);
            rep.Order = w.Debriefs.Count + 1;
            w.Debriefs.Add(rep);
            w.SortiedTargets.Add(dest);
            w.AddEvent(dest, rep.Exit == ExitReason.Retreat ? "철수" : $"교전 {rep.Rounds}R");
            var downs = rep.Aircraft.Count(o => o.ShotDown);
            w.Log("출격", $"{s.Name} → {rep.TargetName} · {SortieRules.MissionName(rep.Mission)} · {(rep.OverEnemyTerritory ? "적 영역" : "아군·중립")}" +
                          $"{(rep.OverlapBonus ? " · 겹침 보정" : "")} · 파괴율 {rep.DestructionRate:0.00} · {rep.Rounds}R · 보상 {rep.Reward}" +
                          (downs > 0 ? $" · 격추 {downs}" : "") + $" · 연료 {TotalFuel(s)}");
            CheckGrounded(w, s);
            return rep;
        }

        /// <summary>가장 가까운 아군 거점 쪽으로 이번 턴에 갈 수 있는 만큼 (닿으면 착륙).</summary>
        public static Hex? ReturnStep(World w, Balance bal, Squadron s)
        {
            if (!IsFlying(s)) return null;
            var (b, d) = NearestLanding(w, s);
            if (b == null) return null;
            var reach = Reach(w, bal, s);
            if (reach <= 0) return null;
            if (d <= reach && CanOrder(w, bal, s, b.Hex) == null) return b.Hex;
            var line = Hex.Line(s.AirHex.Value, b.Hex);
            // 도중 칸은 교전·착륙이 아닌 빈 칸으로
            for (var i = Math.Min(reach, line.Count - 1); i >= 1; i--)
                if (CanOrder(w, bal, s, line[i]) == null && SortieRules.MissionAt(w, line[i]) == null && w.BaseAt(line[i]) == null) return line[i];
            return null;
        }

        static void Launch(World w, Balance bal, Squadron s)
        {
            var cost = SortieRules.Cost(w, bal, s);
            w.Funds -= cost;
            s.FlyingAircraftIds.Clear();
            s.FlyingAircraftIds.AddRange(SortieRules.SortieAircraft(w, bal, s).Select(a => a.Id));
            s.Fuel = MaxFuel(w, bal, s);
            s.TankAttached = s.DropTank;
            s.TankFuel = s.DropTank ? TankCapacity(w, bal, s) : 0;
            s.RefueledTankerId = 0;
            s.AirHex = w.BaseById(s.BaseId).Hex;
            s.MovesLeft = Speed(w, bal, s);
            s.AttackedThisTurn = false;
            s.SortiedThisTurn = true;
            w.Stats.Sorties++;
            w.Log("이륙", $"{s.Name} · {s.FlyingAircraftIds.Count}기 · 연료 {s.Fuel}{(s.TankAttached ? $" + 탱크 {s.TankFuel}" : "")} · 속도 {s.MovesLeft} · 비용 {cost}");
        }

        static void Land(World w, Squadron s, Base b)
        {
            var moved = b.Id != s.BaseId;
            s.BaseId = b.Id;
            ClearFlight(s);
            s.LandedThisTurn = true;
            w.Log("착륙", $"{s.Name} → {b.Name}{(moved ? " (소속 변경)" : "")}");
        }

        static void ClearFlight(Squadron s)
        {
            s.AirHex = null;
            s.FlyingAircraftIds.Clear();
            s.Fuel = 0;
            s.TankFuel = 0;
            s.TankAttached = false;
            s.MovesLeft = 0;
        }

        /// <summary>날 수 있는 기체가 하나도 없으면 비행 종료 (잔해는 회수된 것으로 보고 소속 거점에).</summary>
        public static void CheckGrounded(World w, Squadron s)
        {
            if (!IsFlying(s) || Flyers(w, s).Count > 0) return;
            ClearFlight(s);
            s.LandedThisTurn = true;
            w.Log("비행종료", $"{s.Name} 날 수 있는 기체 없음");
        }

        /// <summary>하늘에서 턴을 마침: 비행 피로, 연료 소모(아군 영역 위 초계면 없음), 모자라면 불시착. 다음 턴 이동력 회복.</summary>
        public static void EndTurnInAir(World w, Balance bal, Squadron s)
        {
            foreach (var a in Flyers(w, s))
                if (w.PilotById(a.PilotId) is { } p)
                    p.Fatigue = Math.Min(100, p.Fatigue + bal.FlightFatigue);
            if (!OverOwnTerritory(w, s))
            {
                if (TotalFuel(s) < bal.IdleFuelPerTurn)
                {
                    Crash(w, bal, s);
                    return;
                }
                Burn(s, bal.IdleFuelPerTurn);
            }
            s.MovesLeft = Speed(w, bal, s);
            s.AttackedThisTurn = false;
        }

        /// <summary>연료 고갈 불시착: 날던 기체는 손실. 적 영역이면 파일럿 포로 확률, 아니면 거리에 따라 복귀 대기.</summary>
        static void Crash(World w, Balance bal, Squadron s)
        {
            var at = s.AirHex.Value;
            var enemy = w.BaseOf(at) is { } tb && tb.Owner != World.Player;
            var (_, d) = NearestLanding(w, s);
            var lost = 0;
            foreach (var a in Flyers(w, s))
            {
                var p = w.PilotById(a.PilotId);
                SquadronRules.UnassignPilot(w, a);
                SquadronRules.DetachAircraft(w, s, a);
                w.Aircraft.Remove(a);
                lost++;
                if (p == null) continue;
                if (enemy && w.Rng.Chance(bal.CaptureChance)) p.Captured = true;
                else p.ReturnTurns = Math.Clamp((Math.Min(d, 99) + 1) / 2, 1, bal.MaxReturnTurns);
            }
            w.EnemyEvents.Add($"{s.Name} 연료 고갈 — {at.ToOffset()}에 불시착, 기체 {lost}기 손실{(enemy ? " (적 영역)" : "")}");
            w.Log("불시착", $"{s.Name} @ {at.ToOffset()} · 손실 {lost}{(enemy ? " · 적 영역" : "")}");
            w.AddEvent(at, "불시착");
            ClearFlight(s);
        }
    }
}
