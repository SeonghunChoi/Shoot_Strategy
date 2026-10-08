using System;
using System.Collections.Generic;
using System.Linq;

namespace Prototype.Strategy.Rules
{
    /// <summary>
    /// 편대·기체·파일럿 명령 (기획서 6장). 무상태 서비스.
    /// 예비 기체와 대기 파일럿은 위치가 없고, 편대에 넣는 순간 그 편대의 거점에 있는 것으로 본다.
    /// </summary>
    public static class SquadronRules
    {
        // ---- 생성 ----

        public static Aircraft AddAircraft(World w, AircraftType type)
        {
            var a = new Aircraft { Id = w.NextId++, Type = type };
            w.Aircraft.Add(a);
            return a;
        }

        public static Pilot AddPilot(World w, string name, bool named, int skill, AircraftType aptitude)
        {
            var p = new Pilot { Id = w.NextId++, Name = name, Named = named, Skill = skill, Aptitude = aptitude };
            w.Pilots.Add(p);
            return p;
        }

        public static Squadron AddSquadron(World w, string name, Base b)
        {
            var s = new Squadron { Id = w.NextId++, Name = name, BaseId = b.Id };
            w.Squadrons.Add(s);
            return s;
        }

        // ---- 거점 운용 조건 ----

        public static int HangarCapacity(World w, Balance bal, Base b) =>
            w.FacilitiesOf(b).Count(f => f.facility.Type == FacilityType.Hangar) * bal.SquadronsPerHangar;

        public static int MaintenanceGrade(World w, Base b) =>
            w.FacilitiesOf(b).Where(f => f.facility.Type == FacilityType.Maintenance).Select(f => f.facility.Grade).DefaultIfEmpty(0).Max();

        public static int EntertainmentGrade(World w, Base b) =>
            w.FacilitiesOf(b).Where(f => f.facility.Type == FacilityType.Entertainment).Select(f => f.facility.Grade).DefaultIfEmpty(0).Max();

        /// <summary>거점이 이 기종을 운용할 수 있는지 (활주로 길이 ≥ 크기, 정비소 등급 ≥ 티어; 정비소 없으면 티어 1만).</summary>
        public static string CanOperate(World w, Balance bal, Base b, AircraftType type)
        {
            var def = bal.Def(type);
            var runway = BaseRules.RunwayLength(w, b);
            if (BaseRules.MaxAircraftSize(bal, runway) < def.Size) return $"활주로 {bal.RunwayForSize[def.Size]}칸 필요 (현재 {runway})";
            var maint = Math.Max(1, MaintenanceGrade(w, b));
            if (maint < def.Tier) return $"정비소 {def.Tier}등급 필요";
            return null;
        }

        /// <summary>기지 설비에 의한 작전 반경 증가 (보급창고).</summary>
        public static int FacilityRadiusBonus(World w, Balance bal, Base b) =>
            BaseRules.Has(w, b, FacilityType.Depot) ? bal.DepotRadiusBonus : 0;

        /// <summary>편대 작전 반경 = (편대 안 기체 반경의 최솟값 + 설비 보너스), 공중급유면 × 배율. 기체가 없으면 0.</summary>
        public static int OperationRadius(World w, Balance bal, Squadron s, bool? refuel = null)
        {
            if (s.AircraftIds.Count == 0) return 0;
            var b = w.BaseById(s.BaseId);
            var range = s.AircraftIds.Min(id => bal.Def(w.AircraftById(id).Type).Range) + FacilityRadiusBonus(w, bal, b);
            return (refuel ?? s.Refuel) ? (int)(range * bal.RefuelRadiusMultiplier) : range;
        }

        /// <summary>어떤 편대든 (공중급유 포함) 닿을 수 있는 칸인지 — 전초기지 건설 조건.</summary>
        public static bool ReachableByAir(World w, Balance bal, Hex h) =>
            w.Squadrons.Any(s => Hex.Distance(w.BaseById(s.BaseId).Hex, h) <= OperationRadius(w, bal, s, refuel: true));

        // ---- 편대 편성 ----

        public static string CanCreateSquadron(World w, Balance bal, Base b)
        {
            if (b.Owner != World.Player) return "아군 거점이 아님";
            if (w.SquadronsAt(b).Count() >= HangarCapacity(w, bal, b)) return "격납고 부족";
            return null;
        }

        public static Squadron CreateSquadron(World w, Balance bal, Base b)
        {
            if (CanCreateSquadron(w, bal, b) != null) return null;
            var n = w.Squadrons.Count + 1;
            return AddSquadron(w, $"{n}편대", b);
        }

        /// <summary>빈 편대 해산.</summary>
        public static string CanDisband(World w, Squadron s) => s.AircraftIds.Count > 0 ? "기체가 남아 있음" : null;

        public static void Disband(World w, Squadron s)
        {
            if (CanDisband(w, s) != null) return;
            w.Squadrons.Remove(s);
        }

        public static string CanAttachAircraft(World w, Balance bal, Squadron s, Aircraft a)
        {
            if (a.SquadronId != 0) return "다른 편대 소속";
            if (s.AircraftIds.Count >= bal.SquadronMaxSize) return "편대 정원";
            if (s.MoveToBaseId != 0) return "이동 중";
            return CanOperate(w, bal, w.BaseById(s.BaseId), a.Type);
        }

        public static void AttachAircraft(World w, Squadron s, Aircraft a)
        {
            s.AircraftIds.Add(a.Id);
            a.SquadronId = s.Id;
        }

        public static void DetachAircraft(World w, Squadron s, Aircraft a)
        {
            s.AircraftIds.Remove(a.Id);
            a.SquadronId = 0;
        }

        /// <summary>편대 안 기체 순서 변경 (기획서 11.3 편성 확인). 앞 기체가 먼저 전경에 선다.</summary>
        public static void ReorderAircraft(Squadron s, Aircraft a, int delta)
        {
            var i = s.AircraftIds.IndexOf(a.Id);
            var j = i + delta;
            if (i < 0 || j < 0 || j >= s.AircraftIds.Count) return;
            s.AircraftIds[i] = s.AircraftIds[j];
            s.AircraftIds[j] = a.Id;
        }

        public static void AssignPilot(World w, Aircraft a, Pilot p)
        {
            UnassignPilot(w, a);
            if (p.AircraftId != 0) UnassignPilot(w, w.AircraftById(p.AircraftId));
            a.PilotId = p.Id;
            p.AircraftId = a.Id;
        }

        public static void UnassignPilot(World w, Aircraft a)
        {
            var p = w.PilotById(a.PilotId);
            if (p != null) p.AircraftId = 0;
            a.PilotId = 0;
        }

        // ---- 재배치 ----

        public static string CanMove(World w, Balance bal, Squadron s, Base to)
        {
            if (to.Owner != World.Player) return "아군 거점이 아님";
            if (to.Id == s.BaseId) return "이미 주둔 중";
            if (w.SquadronsAt(to).Count() + w.Squadrons.Count(x => x.MoveToBaseId == to.Id) >= HangarCapacity(w, bal, to)) return "격납고 부족";
            foreach (var id in s.AircraftIds)
            {
                var reason = CanOperate(w, bal, to, w.AircraftById(id).Type);
                if (reason != null) return reason;
            }
            return null;
        }

        /// <summary>다음 턴 종료 시 도착.</summary>
        public static void Move(World w, Balance bal, Squadron s, Base to)
        {
            if (CanMove(w, bal, s, to) != null) return;
            s.MoveToBaseId = to.Id;
        }

        public static void CancelMove(Squadron s) => s.MoveToBaseId = 0;

        // ---- 상점 ----

        public static string CanBuy(World w, Balance bal, AircraftType type) =>
            w.Funds < bal.Def(type).Price ? "자금 부족" : null;

        public static Aircraft Buy(World w, Balance bal, AircraftType type)
        {
            if (CanBuy(w, bal, type) != null) return null;
            w.Funds -= bal.Def(type).Price;
            return AddAircraft(w, type);
        }

        public static int SellPrice(Balance bal, Aircraft a) =>
            (int)(bal.Def(a.Type).Price * bal.SellRatio * (100 - a.Damage) / 100f);

        public static string CanSell(Aircraft a) => a.SquadronId != 0 ? "편대에서 빼야 함" : null;

        public static void Sell(World w, Balance bal, Aircraft a)
        {
            if (CanSell(a) != null) return;
            UnassignPilot(w, a);
            w.Funds += SellPrice(bal, a);
            w.Aircraft.Remove(a);
        }

        public static string CanHire(World w, Balance bal) => w.Funds < bal.HirePilotCost ? "자금 부족" : null;

        public static Pilot Hire(World w, Balance bal)
        {
            if (CanHire(w, bal) != null) return null;
            w.Funds -= bal.HirePilotCost;
            var n = w.Pilots.Count(p => !p.Named) + 1;
            var types = (AircraftType[])Enum.GetValues(typeof(AircraftType));
            return AddPilot(w, $"파일럿 {n}", named: false, skill: 1 + w.Rng.Next(2), types[w.Rng.Next(types.Length)]);
        }

        // ---- 상태 ----

        public static bool IsExhausted(Balance bal, Pilot p) => p.Fatigue >= bal.FatigueExhausted;

        /// <summary>출격 불가 사유 (4단계에서 사용). 파일럿 없음, 탈진, 이동 중.</summary>
        public static string CannotSortie(World w, Balance bal, Aircraft a)
        {
            var p = w.PilotById(a.PilotId);
            if (p == null) return "파일럿 없음";
            if (p.Captured) return "포로";
            if (p.ReturnTurns > 0) return $"복귀 대기 {p.ReturnTurns}턴";
            if (IsExhausted(bal, p)) return "탈진";
            if (a.Damage >= bal.SwapDamage) return $"손상 {a.Damage}%";
            return null;
        }

        /// <summary>디버그: 편대 전체에 손상·피로를 준다 (회복 관찰용).</summary>
        public static void SimulateWear(World w, Squadron s, int damage, int fatigue)
        {
            foreach (var id in s.AircraftIds)
            {
                var a = w.AircraftById(id);
                a.Damage = Math.Min(100, a.Damage + damage);
                var p = w.PilotById(a.PilotId);
                if (p != null) p.Fatigue = Math.Min(100, p.Fatigue + fatigue);
            }
        }

        // ---- 턴 처리 ----

        /// <summary>주둔 파일럿 급여 (거점별). 예비·대기는 급여만 낸다.</summary>
        public static int Salaries(World w, Balance bal) =>
            w.Pilots.Sum(p => p.Captured ? (int)(bal.Salary(p) * bal.CapturedSalaryRatio) : bal.Salary(p));

        public static string CanRansom(World w, Balance bal, Pilot p)
        {
            if (!p.Captured) return "포로가 아님";
            if (w.Funds < bal.RansomCost) return "자금 부족";
            return null;
        }

        public static void Ransom(World w, Balance bal, Pilot p)
        {
            if (CanRansom(w, bal, p) != null) return;
            w.Funds -= bal.RansomCost;
            p.Captured = false;
            p.ReturnTurns = 1;
        }

        /// <summary>유흥가 환수 (주둔 파일럿 급여 × 등급 × 비율).</summary>
        public static int EntertainmentRefund(World w, Balance bal)
        {
            var total = 0;
            foreach (var s in w.Squadrons)
            {
                var grade = EntertainmentGrade(w, w.BaseById(s.BaseId));
                if (grade == 0) continue;
                foreach (var id in s.AircraftIds)
                {
                    var p = w.PilotById(w.AircraftById(id).PilotId);
                    if (p != null) total += (int)(bal.Salary(p) * grade * bal.EntertainmentRefundPerGrade);
                }
            }
            return total;
        }

        /// <summary>이번 턴 종료 시 수리될 손상과 그 비용 (예상 수지용).</summary>
        public static (int repairedPercent, int cost) RepairForecast(World w, Balance bal)
        {
            var percent = 0;
            var cost = 0;
            foreach (var s in w.Squadrons)
            {
                var b = w.BaseById(s.BaseId);
                var maint = MaintenanceGrade(w, b);
                var rate = maint > 0 ? maint * bal.RepairPerMaintenanceGrade : bal.RepairWithoutMaintenance;
                foreach (var id in s.AircraftIds)
                {
                    var a = w.AircraftById(id);
                    var heal = Math.Min(a.Damage, rate);
                    if (heal <= 0) continue;
                    percent += heal;
                    cost += (int)(bal.Def(a.Type).Price * heal / 100f * bal.RepairCostFactor);
                }
            }
            return (percent, cost);
        }

        /// <summary>턴 종료: 수리 적용, 피로 회복, 재배치 도착, 출격 표시 초기화. 수리비는 Economy가 수지에 넣는다.</summary>
        public static void EndTurn(World w, Balance bal)
        {
            foreach (var s in w.Squadrons)
            {
                var b = w.BaseById(s.BaseId);
                var maint = MaintenanceGrade(w, b);
                var rate = maint > 0 ? maint * bal.RepairPerMaintenanceGrade : bal.RepairWithoutMaintenance;
                var ent = EntertainmentGrade(w, b);
                foreach (var id in s.AircraftIds)
                {
                    var a = w.AircraftById(id);
                    a.Damage = Math.Max(0, a.Damage - rate);
                    var p = w.PilotById(a.PilotId);
                    if (p != null && !s.SortiedThisTurn)
                        p.Fatigue = Math.Max(0, p.Fatigue - bal.FatigueRecovery - ent * bal.FatigueRecoveryPerEntertainmentGrade);
                }
                s.SortiedThisTurn = false;
                if (s.MoveToBaseId != 0)
                {
                    s.BaseId = s.MoveToBaseId;
                    s.MoveToBaseId = 0;
                }
            }
            // 대기 파일럿도 쉬는 중
            foreach (var p in w.IdlePilots) p.Fatigue = Math.Max(0, p.Fatigue - bal.FatigueRecovery);
            // 격추 후 복귀 대기는 편대 소속 여부와 무관하게 줄어든다
            foreach (var p in w.Pilots)
                if (p.ReturnTurns > 0) p.ReturnTurns--;
            w.SortiePlan.Clear();
            w.SortiedTargets.Clear();
        }
    }
}
