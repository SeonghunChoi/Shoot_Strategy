using System;
using System.Collections.Generic;
using System.Linq;

namespace Prototype.Strategy.Rules
{
    public enum MissionType
    {
        UnitAttack, // 부대 공격
        BaseBombing, // 거점 폭격
    }

    public enum ExitReason
    {
        Complete, // 목표 전멸 또는 라운드 소진
        Retreat,  // 전경에 세울 기체가 없어 철수
    }

    public sealed class SortiePlan
    {
        public int SquadronId;
        public Hex Target;
        public MissionType Mission;
    }

    public sealed class AircraftOutcome
    {
        public int AircraftId;
        public string AircraftName;
        public string PilotName;
        public int DamageBefore;
        public int DamageAfter;
        public int Rounds;       // 전경 라운드 수
        public bool ShotDown;
        public string Fate;      // 잔해 회수 / 영구 손실 / —
        public string PilotFate; // 탈출 n턴 / 포로 / —
        public int XpGain;
        public bool SkillUp;
    }

    /// <summary>디브리핑 (기술 설계 5.1 SortieResult + 설명용 라운드 로그).</summary>
    public sealed class SortieReport
    {
        public int Order;
        public string SquadronName;
        public MissionType Mission;
        public string TargetName;
        public Hex Target;
        public bool OverEnemyTerritory;
        public bool OverlapBonus;
        public int Cost;
        public int Reward;
        public float DestructionRate;
        public ExitReason Exit;
        public int Rounds;
        public readonly List<string> Log = new();
        public readonly List<AircraftOutcome> Aircraft = new();
        public string Effect;
    }

    /// <summary>
    /// 출격 계획과 자동 정산 (기획서 3장 출격 규칙, 기술 설계 5.4 추상 교전 라운드). 무상태 서비스.
    /// 직접 플레이는 범위 밖이라 모든 출격이 자동 정산이다.
    /// </summary>
    public static class SortieRules
    {
        sealed class Group
        {
            public string Name;
            public int Strength;
            public int Initial;
            public Func<AircraftDef, int> Stat; // 이 그룹을 치는 기체 수치
        }

        // ---- 계획 ----

        public static IEnumerable<Hex> OperationArea(Hex target) => Hex.Range(target, 1);

        public static bool AreasOverlap(Hex a, Hex b) => Hex.Distance(a, b) <= 2;

        /// <summary>목표 종류. 적 부대는 지금 보이는 것만 (시야 켜짐 시), 적 거점은 항상.</summary>
        public static MissionType? MissionAt(World w, Hex h)
        {
            if (w.EnemyUnitAt(h) != null && (w.VisionBalance == null || Vision.IsVisible(w, w.VisionBalance, h))) return MissionType.UnitAttack;
            if (w.BaseAt(h) is { } b && b.Owner != World.Player) return MissionType.BaseBombing;
            return null;
        }

        public static string TargetName(World w, Hex h) =>
            w.EnemyUnitAt(h)?.Name ?? w.BaseAt(h)?.Name ?? h.ToString();

        public static IEnumerable<Aircraft> SortieAircraft(World w, Balance bal, Squadron s) =>
            s.AircraftIds.Select(w.AircraftById).Where(a => SquadronRules.CannotSortie(w, bal, a) == null);

        public static string CanSortie(World w, Balance bal, Squadron s)
        {
            if (s.MovedThisTurn) return "이번 턴 재배치함";
            if (s.SortiedThisTurn) return "이번 턴 출격함";
            if (w.SortiePlan.Any(p => p.SquadronId == s.Id)) return "이미 계획됨";
            if (!SortieAircraft(w, bal, s).Any()) return "출격 가능한 기체 없음";
            return null;
        }

        public static string CanPlan(World w, Balance bal, Squadron s, Hex target)
        {
            var sq = CanSortie(w, bal, s);
            if (sq != null) return sq;
            var b = w.BaseById(s.BaseId);
            if (Hex.Distance(b.Hex, target) > SquadronRules.OperationRadius(w, bal, s)) return "작전 반경 밖";
            if (MissionAt(w, target) == null) return "목표 없음";
            if (w.SortiePlan.Any(p => p.Target == target)) return "같은 목표에 이미 편대 배정 (작전 지역당 1개)";
            if (w.Funds < PlannedCost(w, bal) + Cost(w, bal, s)) return "자금 부족";
            return null;
        }

        /// <summary>편대의 작전 반경 안에서 고를 수 있는 목표.</summary>
        public static IEnumerable<Hex> Targets(World w, Balance bal, Squadron s)
        {
            var b = w.BaseById(s.BaseId);
            return Hex.Range(b.Hex, SquadronRules.OperationRadius(w, bal, s))
                .Where(h => w.Map.Contains(h) && MissionAt(w, h) != null);
        }

        /// <summary>가장 가까운 목표까지의 거리와 이름 (반경 밖일 때 안내용).</summary>
        public static (int distance, string name) NearestTarget(World w, Squadron s)
        {
            var from = w.BaseById(s.BaseId).Hex;
            var best = int.MaxValue;
            string name = null;
            foreach (var h in w.Map.AllHexes())
            {
                if (MissionAt(w, h) == null) continue;
                var d = Hex.Distance(from, h);
                if (d < best) { best = d; name = TargetName(w, h); }
            }
            return (best, name);
        }

        public static int Cost(World w, Balance bal, Squadron s)
        {
            var b = w.BaseById(s.BaseId);
            var cost = SortieAircraft(w, bal, s).Sum(a => bal.SortieCostBySize[bal.Def(a.Type).Size]);
            if (BaseRules.Has(w, b, FacilityType.Depot)) cost = (int)(cost * (1f - bal.DepotSortieDiscount));
            if (s.Refuel) cost += (int)(cost * bal.RefuelCostRatio);
            return cost;
        }

        public static int PlannedCost(World w, Balance bal) =>
            w.SortiePlan.Sum(p => Cost(w, bal, w.SquadronById(p.SquadronId)));

        public static void Plan(World w, Balance bal, Squadron s, Hex target)
        {
            if (CanPlan(w, bal, s, target) != null) return;
            w.SortiePlan.Add(new SortiePlan { SquadronId = s.Id, Target = target, Mission = MissionAt(w, target).Value });
        }

        public static void Unplan(World w, SortiePlan p) => w.SortiePlan.Remove(p);

        public static void Reorder(World w, SortiePlan p, int delta)
        {
            var i = w.SortiePlan.IndexOf(p);
            var j = i + delta;
            if (i < 0 || j < 0 || j >= w.SortiePlan.Count) return;
            w.SortiePlan[i] = w.SortiePlan[j];
            w.SortiePlan[j] = p;
        }

        /// <summary>편대가 모두 나가는 아군 거점 (방어 공백 경고).</summary>
        public static IEnumerable<Base> DefenseGaps(World w)
        {
            foreach (var b in w.Bases.Where(x => x.Owner == World.Player))
            {
                var squadrons = w.SquadronsAt(b).ToList();
                if (squadrons.Count == 0) continue;
                if (squadrons.All(s => w.SortiePlan.Any(p => p.SquadronId == s.Id) || s.SortiedThisTurn)) yield return b;
            }
        }

        // ---- 실행 ----

        public static List<SortieReport> ExecuteAll(World w, Balance bal)
        {
            w.Debriefs.Clear();
            var order = 1;
            foreach (var plan in w.SortiePlan.ToList())
            {
                var s = w.SquadronById(plan.SquadronId);
                if (s == null) continue;
                // 앞 출격으로 목표가 사라졌으면 건너뜀
                if (MissionAt(w, plan.Target) == null)
                {
                    s.Refuel = false;
                    w.Debriefs.Add(new SortieReport
                    {
                        Order = order++, SquadronName = s.Name, Mission = plan.Mission, Target = plan.Target,
                        TargetName = plan.Target.ToString(), Effect = "목표가 이미 사라져 출격 취소 (비용 없음)",
                    });
                    continue;
                }
                var report = Resolve(w, bal, s, plan.Target);
                report.Order = order++;
                w.Debriefs.Add(report);
                w.SortiedTargets.Add(plan.Target);
            }
            w.SortiePlan.Clear();
            return w.Debriefs;
        }

        public static SortieReport Resolve(World w, Balance bal, Squadron s, Hex target)
        {
            var rng = w.Rng;
            var mission = MissionAt(w, target) ?? MissionType.UnitAttack;
            var unit = w.EnemyUnitAt(target);
            var enemyBase = w.BaseAt(target);
            var report = new SortieReport
            {
                SquadronName = s.Name,
                Mission = mission,
                Target = target,
                TargetName = TargetName(w, target),
                OverEnemyTerritory = w.BaseOf(target) is { } tb && tb.Owner != World.Player,
                OverlapBonus = w.SortiedTargets.Any(t => AreasOverlap(t, target)),
            };

            // 비용
            report.Cost = Cost(w, bal, s);
            w.Funds -= report.Cost;

            // 적 교전 그룹 (기술 설계 5.4): 부대 공격은 공중 → 지상, 거점 폭격은 대공 → 시설
            var groups = new List<Group>();
            if (mission == MissionType.UnitAttack && unit != null)
            {
                groups.Add(new Group { Name = "공중", Strength = unit.Air, Stat = d => d.Air });
                groups.Add(new Group { Name = "지상", Strength = unit.Ground, Stat = d => d.Ground });
            }
            else if (enemyBase != null)
            {
                var aa = report.OverlapBonus ? (int)(enemyBase.AntiAir * (1f - bal.OverlapAaReduction)) : enemyBase.AntiAir;
                groups.Add(new Group { Name = "대공", Strength = aa, Stat = d => d.Bombing });
                groups.Add(new Group { Name = "시설", Strength = enemyBase.Structures, Stat = d => d.Bombing });
                if (report.OverlapBonus) report.Log.Add($"앞 출격이 같은 작전 지역을 쳤으므로 대공 −{bal.OverlapAaReduction:P0}: {enemyBase.AntiAir} → {aa}");
            }
            foreach (var g in groups) g.Initial = g.Strength;
            var totalInitial = groups.Sum(g => g.Initial);

            // 출격 기체 (순서 = 편대 순서)
            var flying = SortieAircraft(w, bal, s).ToList();
            var outcomes = flying.ToDictionary(a => a.Id, a => new AircraftOutcome
            {
                AircraftId = a.Id,
                AircraftName = bal.Def(a.Type).Name,
                PilotName = w.PilotById(a.PilotId).Name,
                DamageBefore = a.Damage,
                Fate = "—",
                PilotFate = "—",
            });
            report.Log.Add($"출격 {flying.Count}기, 비용 {report.Cost}. 적: {string.Join(", ", groups.Select(g => $"{g.Name} {g.Initial}"))}");

            var round = 0;
            while (round < bal.MaxRounds)
            {
                var group = groups.FirstOrDefault(g => g.Strength > 0);
                if (group == null) break;
                var front = flying.FirstOrDefault(a => a.Damage < bal.SwapDamage);
                if (front == null)
                {
                    report.Exit = ExitReason.Retreat;
                    report.Log.Add($"R{round + 1}: 전경에 세울 기체가 없어 철수");
                    break;
                }
                round++;
                var def = bal.Def(front.Type);
                var pilot = w.PilotById(front.PilotId);
                var outcome = outcomes[front.Id];
                outcome.Rounds++;

                // 아군 공격: 수치 × 숙련 × 숙련 기종 × 손상 × 피로 × 불리 계수 × 난수(0.8~1.2)
                var skillMul = 0.7f + 0.1f * pilot.Skill;
                var aptMul = pilot.Aptitude == front.Type ? 1.2f : 1f;
                var condMul = (1f - front.Damage / 200f) * (1f - pilot.Fatigue / 300f);
                var roll = 0.8f + 0.4f * (float)rng.NextDouble();
                var attack = (int)Math.Round(group.Stat(def) * skillMul * aptMul * condMul * bal.AutoResolvePenalty * roll);
                attack = Math.Max(1, attack);
                var before = group.Strength;
                group.Strength = Math.Max(0, group.Strength - attack);

                // 적 반격: 남은 전력(공격 전) × 계수 × 난수(0.6~1.4) ÷ 숙련 보정
                var hitRoll = 0.6f + 0.8f * (float)rng.NextDouble();
                var hit = (int)Math.Round(before * bal.EnemyHitPerStrength * hitRoll / (0.8f + 0.1f * pilot.Skill));
                front.Damage = Math.Min(100, front.Damage + hit);

                report.Log.Add($"R{round}: {def.Name}({pilot.Name}) → {group.Name} −{attack} ({before}→{group.Strength}), 피격 +{hit}% (손상 {front.Damage}%)");

                if (front.Damage >= 100)
                {
                    outcome.ShotDown = true;
                    ShotDown(w, bal, s, front, pilot, report, outcome);
                    flying.Remove(front);
                }
                else if (front.Damage >= bal.SwapDamage)
                {
                    report.Log.Add($"   {def.Name} 손상 {front.Damage}% ≥ {bal.SwapDamage}% → 교대");
                }
            }
            if (report.Exit != ExitReason.Retreat)
            {
                report.Exit = ExitReason.Complete;
                if (groups.Any(g => g.Strength > 0)) report.Log.Add($"라운드 {bal.MaxRounds} 소진 — 귀환");
                else report.Log.Add("목표 전력 소멸 — 귀환");
            }
            report.Rounds = round;

            // 결과 반영
            var destroyed = groups.Sum(g => g.Initial - g.Strength);
            report.DestructionRate = totalInitial > 0 ? (float)destroyed / totalInitial : 0f;
            if (unit != null)
            {
                unit.Air = groups[0].Strength;
                unit.Ground = groups[1].Strength;
                report.Effect = unit.Strength == 0 ? $"{unit.Name} 격멸" : $"{unit.Name} 전력 공중 {unit.Air} · 지상 {unit.Ground}로 감소";
                if (unit.Strength == 0) EnemyTurn.RemoveUnit(w, unit);
                report.Reward = (int)(bal.BountyUnit * report.DestructionRate);
            }
            else if (enemyBase != null)
            {
                // 겹침 보정으로 깎인 몫은 실제 전력에서도 빠진 것으로 본다
                enemyBase.AntiAir = Math.Min(enemyBase.AntiAir, groups[0].Strength);
                enemyBase.Structures = groups[1].Strength;
                report.Effect = $"{enemyBase.Name} 대공 {enemyBase.AntiAir} · 시설 {enemyBase.Structures}" +
                                (enemyBase.Structures == 0 ? " — 시설 전파 (점령 가능, 6단계)" : "");
                report.Reward = (int)(bal.BountyBase * report.DestructionRate);
            }
            w.Funds += report.Reward;

            // 파일럿 피로·경험치, 편대 표시
            foreach (var o in outcomes.Values)
            {
                var a = w.AircraftById(o.AircraftId);
                o.DamageAfter = a?.Damage ?? 100;
                var p = w.Pilots.FirstOrDefault(x => x.Name == o.PilotName);
                if (p == null) continue;
                p.Fatigue = Math.Min(100, p.Fatigue + bal.SortieFatigue);
                o.XpGain = o.Rounds * bal.XpPerRound;
                p.Xp += o.XpGain;
                if (p.Skill < 5 && p.Xp >= p.Skill * bal.XpPerSkill)
                {
                    p.Xp -= p.Skill * bal.XpPerSkill;
                    p.Skill++;
                    o.SkillUp = true;
                }
                report.Aircraft.Add(o);
            }
            s.SortiedThisTurn = true;
            s.Refuel = false; // 급유는 출격 1회분
            w.Stats.Sorties++;
            return report;
        }

        /// <summary>격추 (기획서 6.4): 아군·중립 상공이면 잔해 회수, 적 영역이면 영구 손실 + 포로 확률.</summary>
        static void ShotDown(World w, Balance bal, Squadron s, Aircraft a, Pilot p, SortieReport report, AircraftOutcome o)
        {
            SquadronRules.UnassignPilot(w, a);
            var nearest = w.Bases.Where(b => b.Owner == World.Player).Min(b => Hex.Distance(b.Hex, report.Target));
            if (report.OverEnemyTerritory)
            {
                SquadronRules.DetachAircraft(w, s, a);
                w.Aircraft.Remove(a);
                o.Fate = "영구 손실 (적 영역)";
                if (w.Rng.Chance(bal.CaptureChance))
                {
                    p.Captured = true;
                    o.PilotFate = $"포로 (몸값 {bal.RansomCost})";
                }
                else
                {
                    p.ReturnTurns = Math.Clamp((nearest + 1) / 2, 1, bal.MaxReturnTurns);
                    o.PilotFate = $"탈출, {p.ReturnTurns}턴 뒤 복귀";
                }
            }
            else
            {
                a.Damage = 100; // 편대에 남겨 두고 수리
                o.Fate = "잔해 회수 (수리 필요)";
                p.ReturnTurns = Math.Clamp((nearest + 1) / 2, 1, bal.MaxReturnTurns);
                o.PilotFate = $"탈출, {p.ReturnTurns}턴 뒤 복귀";
            }
            report.Log.Add($"   {o.AircraftName} 격추 — {o.Fate}, {p.Name} {o.PilotFate}");
        }

        public static string MissionName(MissionType m) => m switch
        {
            MissionType.UnitAttack => "부대 공격",
            MissionType.BaseBombing => "거점 폭격",
            _ => m.ToString(),
        };
    }
}
