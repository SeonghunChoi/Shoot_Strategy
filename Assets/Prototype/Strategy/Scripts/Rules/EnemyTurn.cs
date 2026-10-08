using System;
using System.Collections.Generic;
using System.Linq;

namespace Prototype.Strategy.Rules
{
    public enum Stance
    {
        Offensive, // 공세
        Defensive, // 방어
        Regroup,   // 재정비
    }

    /// <summary>적 세력 (기술 설계 4.6: 태세 + 행동 점수화의 단순판).</summary>
    public sealed class Faction
    {
        public int Id;
        public string Name;
        public Stance Stance = Stance.Offensive;
        public int LastLossTurn = -100;
        public int ReinforceTimer;
    }

    /// <summary>거점 피습 결과 (적 턴 요약·S1 로그).</summary>
    public sealed class InterceptReport
    {
        public string BaseName;
        public string UnitName;
        public int EnemyBefore;
        public int EnemyAfterAir;      // 편대 요격 후
        public int EnemyAfterFacility; // 대공 시설 후
        public int FacilityDefense;
        public int SquadronsEngaged;
        public int Rounds;
        public bool Captured;
        public readonly List<string> Log = new();
    }

    /// <summary>
    /// 적 턴 (기획서 3장): 태세 갱신 → 부대 이동·공격 → 세력 간 교전 → 보충·회복.
    /// 플레이어 주둔 편대의 자동 요격과 거점 상실(기획서 5.6)도 여기서 처리한다.
    /// </summary>
    public static class EnemyTurn
    {
        public static Faction FactionOf(World w, int owner) => w.Factions.First(f => f.Id == owner);

        public static int FactionStrength(World w, Faction f) => w.EnemyUnits.Where(u => u.Owner == f.Id).Sum(u => u.Strength);

        public static IEnumerable<Base> BasesOf(World w, Faction f) => w.Bases.Where(b => b.Owner == f.Id);

        /// <summary>대공 시설 방어력 (대공포 등급 × 5, SAM 등급 × 10).</summary>
        public static int FacilityDefense(World w, Balance bal, Base b) =>
            w.FacilitiesOf(b).Sum(x => x.facility.Type switch
            {
                FacilityType.AntiAir => x.facility.Grade * bal.AntiAirDefensePerGrade,
                FacilityType.Sam => x.facility.Grade * bal.SamDefensePerGrade,
                _ => 0,
            });

        /// <summary>세력이 이 거점의 주둔 편대 수를 아는가 (세력 부대·거점의 탐지 범위 안).</summary>
        public static bool FactionSees(World w, Balance bal, Faction f, Hex h) =>
            w.EnemyUnits.Any(u => u.Owner == f.Id && Hex.Distance(u.Hex, h) <= bal.EnemyDetectRadius) ||
            BasesOf(w, f).Any(b => Hex.Distance(b.Hex, h) <= bal.EnemyDetectRadius);

        public static void Run(World w, Balance bal)
        {
            w.EnemyEvents.Clear();
            w.Intercepts.Clear();
            foreach (var f in w.Factions)
            {
                UpdateStance(w, bal, f);
                foreach (var u in w.EnemyUnits.Where(x => x.Owner == f.Id).ToList())
                    ActUnit(w, bal, f, u);
            }
            FactionCombat(w, bal);
            Reinforce(w, bal);
            if (w.Bases.All(b => b.Owner != World.Player)) w.GameOver = "모든 거점 상실";
        }

        // ---- 태세 ----

        static void UpdateStance(World w, Balance bal, Faction f)
        {
            var old = f.Stance;
            var strength = FactionStrength(w, f);
            if (strength < bal.RegroupBelowStrength) f.Stance = Stance.Regroup;
            else if (w.Turn - f.LastLossTurn < bal.DefensiveTurnsAfterLoss) f.Stance = Stance.Defensive;
            else f.Stance = Stance.Offensive;
            if (f.Stance != old) w.EnemyEvents.Add($"{f.Name} 태세 {StanceName(old)} → {StanceName(f.Stance)} (전력 {strength})");
        }

        public static string StanceName(Stance s) => s switch
        {
            Stance.Offensive => "공세",
            Stance.Defensive => "방어",
            Stance.Regroup => "재정비",
            _ => s.ToString(),
        };

        // ---- 부대 행동 ----

        static void ActUnit(World w, Balance bal, Faction f, EnemyUnit u)
        {
            var home = BasesOf(w, f).OrderBy(b => Hex.Distance(b.Hex, u.Hex)).FirstOrDefault();
            Hex? goal = null;
            switch (f.Stance)
            {
                case Stance.Offensive:
                    goal = BestTarget(w, bal, f, u)?.Hex;
                    break;
                case Stance.Defensive:
                    if (home != null && Hex.Distance(home.Hex, u.Hex) > 1) goal = home.Hex;
                    break;
                case Stance.Regroup:
                    if (home != null && u.Hex != home.Hex) goal = home.Hex;
                    break;
            }
            u.Goal = goal;
            if (goal is not { } g) return;

            // 이미 인접하면 공격 (공세) / 도착 (그 외)
            if (f.Stance == Stance.Offensive && w.BaseAt(g) is { Owner: World.Player } target0 && Hex.Distance(u.Hex, g) <= 1)
            {
                Attack(w, bal, f, u, target0);
                return;
            }

            var path = HexPath.Find(w.Map, bal, u.Hex, g);
            if (path == null) return;
            var points = bal.EnemyMovePoints;
            var moved = false;
            foreach (var step in path)
            {
                // 목표 거점 본부 칸에는 들어가지 않고 인접에서 멈춘다 (아군 거점) — 자기 기지는 들어간다
                if (w.BaseAt(step) is { } sb && sb.Owner != f.Id) break;
                var cost = HexPath.MoveCost(bal, w.Map[step].Terrain);
                if (cost > points) break;
                points -= cost;
                u.Hex = step;
                moved = true;
                if (f.Stance == Stance.Offensive && Hex.Distance(u.Hex, g) <= 1) break;
            }
            if (moved && Vision.IsVisible(w, bal, u.Hex)) w.EnemyEvents.Add($"{u.Name} 이동 → {u.Hex.ToOffset()}");

            if (f.Stance == Stance.Offensive && w.BaseAt(g) is { Owner: World.Player } target && Hex.Distance(u.Hex, g) <= 1)
                Attack(w, bal, f, u, target);
        }

        /// <summary>공세 목표: 가치(등급·시설) ÷ 거리 ÷ 알려진 방어. 편대 수는 탐지 범위 안일 때만 반영.</summary>
        public static Base BestTarget(World w, Balance bal, Faction f, EnemyUnit u)
        {
            Base best = null;
            var bestScore = float.MinValue;
            foreach (var b in w.Bases.Where(x => x.Owner == World.Player))
            {
                var value = b.Grade * 10f + BaseRules.FacilityCount(w, b) * 3f;
                var defense = FacilityDefense(w, bal, b);
                if (FactionSees(w, bal, f, b.Hex)) defense += (int)(w.SquadronsAt(b).Sum(s => s.AircraftIds.Count) * bal.KnownAircraftDefenseWeight);
                var dist = Hex.Distance(u.Hex, b.Hex);
                var score = value / (1f + dist * 0.3f) / (1f + defense * 0.05f);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = b;
                }
            }
            return best;
        }

        // ---- 피습·요격·거점 상실 ----

        static void Attack(World w, Balance bal, Faction f, EnemyUnit u, Base target)
        {
            var rep = new InterceptReport
            {
                BaseName = target.Name,
                UnitName = u.Name,
                EnemyBefore = u.Strength,
                FacilityDefense = FacilityDefense(w, bal, target),
            };

            // 1) 주둔 편대 자동 요격 (이번 턴 출격한 편대 제외, 재배치해 온 편대는 참가): 4단계 리졸버와 같은 라운드 교전
            var squadrons = w.SquadronsAt(target).Where(s => !s.SortiedThisTurn).ToList();
            var flying = squadrons.SelectMany(s => SortieRules.SortieAircraft(w, bal, s)).ToList();
            rep.SquadronsEngaged = squadrons.Count(s => SortieRules.SortieAircraft(w, bal, s).Any());
            if (flying.Count == 0) rep.Log.Add("요격 편대 없음 — 대공 시설만으로 방어");
            else rep.Log.Add($"요격: 편대 {rep.SquadronsEngaged}개, {flying.Count}기 vs 공중 {u.Air} 지상 {u.Ground}");

            var air = u.Air;
            var ground = u.Ground;
            var round = 0;
            while (round < bal.MaxRounds && air + ground > 0)
            {
                var front = flying.FirstOrDefault(a => a.Damage < bal.SwapDamage);
                if (front == null) break;
                round++;
                var def = bal.Def(front.Type);
                var pilot = w.PilotById(front.PilotId);
                var hitAir = air > 0;
                var stat = hitAir ? def.Air : def.Ground;
                var skillMul = 0.7f + 0.1f * pilot.Skill;
                var aptMul = pilot.Aptitude == front.Type ? 1.2f : 1f;
                var condMul = (1f - front.Damage / 200f) * (1f - pilot.Fatigue / 300f);
                var roll = 0.8f + 0.4f * (float)w.Rng.NextDouble();
                var attack = Math.Max(1, (int)Math.Round(stat * skillMul * aptMul * condMul * bal.AutoResolvePenalty * roll));
                var before = hitAir ? air : ground;
                if (hitAir) air = Math.Max(0, air - attack);
                else ground = Math.Max(0, ground - attack);
                var hitRoll = 0.6f + 0.8f * (float)w.Rng.NextDouble();
                var hit = (int)Math.Round(before * bal.EnemyHitPerStrength * hitRoll / (0.8f + 0.1f * pilot.Skill));
                front.Damage = Math.Min(100, front.Damage + hit);
                rep.Log.Add($"R{round}: {def.Name}({pilot.Name}) → {(hitAir ? "공중" : "지상")} −{attack} ({before}→{(hitAir ? air : ground)}), 피격 +{hit}% (손상 {front.Damage}%)");
                if (front.Damage >= 100)
                {
                    // 아군 영역 상공: 잔해 회수, 파일럿 복귀 대기 1턴
                    var p = pilot;
                    SquadronRules.UnassignPilot(w, front);
                    p.ReturnTurns = 1;
                    rep.Log.Add($"   {def.Name} 격추 — 잔해 회수, {p.Name} 1턴 뒤 복귀");
                    flying.Remove(front);
                }
            }
            foreach (var a in flying.Concat(squadrons.SelectMany(s => s.AircraftIds.Select(w.AircraftById))).Distinct())
            {
                var p = w.PilotById(a.PilotId);
                if (p != null && round > 0) p.Fatigue = Math.Min(100, p.Fatigue + bal.InterceptFatigue);
            }
            rep.Rounds = round;
            rep.EnemyAfterAir = air + ground;

            // 2) 대공 시설: 남은 전력에서 방어력만큼 더 깎는다 (공중부터)
            var remaining = Math.Max(0, air + ground - rep.FacilityDefense);
            rep.EnemyAfterFacility = remaining;
            if (rep.FacilityDefense > 0) rep.Log.Add($"대공 시설 방어력 {rep.FacilityDefense} → 적 전력 {air + ground} → {remaining}");
            var scale = air + ground > 0 ? (float)remaining / (air + ground) : 0f;
            u.Air = (int)Math.Round(air * scale);
            u.Ground = remaining - u.Air;

            // 3) 판정
            if (remaining >= bal.CaptureThreshold)
            {
                rep.Captured = true;
                rep.Log.Add($"남은 전력 {remaining} ≥ {bal.CaptureThreshold} — 거점 상실");
                Capture(w, bal, f, u, target, rep);
            }
            else
            {
                rep.Log.Add(remaining == 0 ? $"{u.Name} 격멸" : $"남은 전력 {remaining} < {bal.CaptureThreshold} — 격퇴, 후퇴");
                if (remaining == 0) RemoveUnit(w, u);
                else
                {
                    var home = BasesOf(w, f).OrderBy(b => Hex.Distance(b.Hex, u.Hex)).FirstOrDefault();
                    if (home != null) u.Hex = home.Hex;
                    u.Goal = null;
                }
            }
            w.Intercepts.Add(rep);
            w.EnemyEvents.Add($"{target.Name} 피습 ({u.Name}): {(rep.Captured ? "거점 상실" : remaining == 0 ? "격멸" : "격퇴")}");
        }

        /// <summary>거점 상실 (기획서 5.6): 등급 −1, 상한 초과 시설 파괴, 주둔 편대 강제 이탈(자리 없으면 기체 손실), 영역은 거점과 함께.</summary>
        public static void Capture(World w, Balance bal, Faction f, EnemyUnit u, Base target, InterceptReport rep)
        {
            target.Owner = f.Id;
            target.Grade = Math.Max(1, target.Grade - 1);
            var cap = BaseRules.FacilityCap(bal, target);
            var facilities = w.FacilitiesOf(target).ToList();
            var destroyed = 0;
            foreach (var (h, _) in facilities.Skip(cap))
            {
                w.Facilities.Remove(h);
                destroyed++;
            }
            if (destroyed > 0) rep.Log.Add($"   등급 {target.Grade + 1} → {target.Grade}, 상한 초과 시설 {destroyed}개 파괴");
            else rep.Log.Add($"   등급 {target.Grade + 1} → {target.Grade}");

            foreach (var s in w.SquadronsAt(target).ToList())
            {
                var refuge = w.Bases.Where(b => b.Owner == World.Player && b.Id != target.Id)
                    .Where(b => w.SquadronsAt(b).Count() < SquadronRules.HangarCapacity(w, bal, b))
                    .Where(b => s.AircraftIds.All(id => SquadronRules.CanOperate(w, bal, b, w.AircraftById(id).Type) == null))
                    .OrderBy(b => Hex.Distance(b.Hex, target.Hex)).FirstOrDefault();
                if (refuge != null)
                {
                    s.BaseId = refuge.Id;
                    rep.Log.Add($"   {s.Name} → {refuge.Name}로 강제 이탈");
                }
                else
                {
                    foreach (var id in s.AircraftIds.ToList())
                    {
                        var a = w.AircraftById(id);
                        SquadronRules.UnassignPilot(w, a);
                        w.Aircraft.Remove(a);
                    }
                    s.AircraftIds.Clear();
                    w.Squadrons.Remove(s);
                    rep.Log.Add($"   {s.Name} 받아 줄 거점 없음 — 기체 전부 손실");
                }
            }
            // 적 거점으로 전환: 방어 수치 부여, 부대는 주둔
            target.AntiAir = FacilityDefense(w, bal, target) + bal.CapturedBaseAntiAir;
            target.Structures = BaseRules.FacilityCount(w, target) * 10 + 10;
            u.Hex = target.Hex;
            u.Goal = null;
        }

        // ---- 세력 간 교전 ----

        static void FactionCombat(World w, Balance bal)
        {
            var units = w.EnemyUnits.ToList();
            for (var i = 0; i < units.Count; i++)
            for (var j = i + 1; j < units.Count; j++)
            {
                var a = units[i];
                var b = units[j];
                if (a.Owner == b.Owner || Hex.Distance(a.Hex, b.Hex) > 1) continue;
                if (!w.EnemyUnits.Contains(a) || !w.EnemyUnits.Contains(b)) continue;
                var da = Math.Min(bal.FactionCombatMaxDamage, b.Strength / 2);
                var db = Math.Min(bal.FactionCombatMaxDamage, a.Strength / 2);
                Damage(a, da);
                Damage(b, db);
                if (Vision.IsVisible(w, bal, a.Hex) || Vision.IsVisible(w, bal, b.Hex))
                    w.EnemyEvents.Add($"세력 간 교전: {a.Name} −{da}, {b.Name} −{db}");
                if (a.Strength == 0) RemoveUnit(w, a);
                if (b.Strength == 0) RemoveUnit(w, b);
            }
        }

        static void Damage(EnemyUnit u, int amount)
        {
            var air = Math.Min(u.Air, amount / 2);
            u.Air -= air;
            u.Ground = Math.Max(0, u.Ground - (amount - air));
        }

        public static void RemoveUnit(World w, EnemyUnit u)
        {
            w.EnemyUnits.Remove(u);
            FactionOf(w, u.Owner).LastLossTurn = w.Turn;
        }

        // ---- 보충·회복 ----

        static void Reinforce(World w, Balance bal)
        {
            foreach (var f in w.Factions)
            {
                var bases = BasesOf(w, f).ToList();
                if (bases.Count == 0) continue;
                f.ReinforceTimer++;
                if (f.ReinforceTimer >= bal.ReinforceTurns)
                {
                    f.ReinforceTimer = 0;
                    var b = bases[w.Rng.Next(bases.Count)];
                    var n = w.EnemyUnits.Count(x => x.Owner == f.Id) + 1;
                    w.EnemyUnits.Add(new EnemyUnit
                    {
                        Id = w.NextId++, Name = $"{f.Name} {n}부대", Hex = b.Hex, Owner = f.Id,
                        Air = bal.ReinforceAir, Ground = bal.ReinforceGround,
                    });
                    if (Vision.IsVisible(w, bal, b.Hex)) w.EnemyEvents.Add($"{f.Name} 보충: {b.Name}에 새 부대");
                }
                foreach (var b in bases)
                {
                    b.AntiAir += bal.EnemyBaseRegen;
                    b.Structures += bal.EnemyBaseRegen;
                    // 기지에 있는 부대는 회복
                    foreach (var u in w.EnemyUnits.Where(x => x.Owner == f.Id && x.Hex == b.Hex))
                    {
                        u.Air += bal.EnemyUnitRegen;
                        u.Ground += bal.EnemyUnitRegen;
                    }
                }
            }
        }
    }
}
