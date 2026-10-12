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
            BaseRules.GradeOf(b, FacilityType.AntiAir) * bal.AntiAirDefensePerGrade +
            BaseRules.GradeOf(b, FacilityType.Sam) * bal.SamDefensePerGrade;

        /// <summary>세력이 이 거점의 주둔 편대 수를 아는가 (세력 부대·거점의 탐지 범위 안).</summary>
        public static bool FactionSees(World w, Balance bal, Faction f, Hex h) =>
            w.EnemyUnits.Any(u => u.Owner == f.Id && Hex.Distance(u.Hex, h) <= bal.EnemyDetectRadius) ||
            BasesOf(w, f).Any(b => Hex.Distance(b.Hex, h) <= bal.EnemyDetectRadius);

        public static void Run(World w, Balance bal)
        {
            w.Intercepts.Clear();
            foreach (var u in w.EnemyUnits) u.PrevHex = u.Hex;
            foreach (var f in w.Factions)
            {
                UpdateStance(w, bal, f);
                foreach (var u in w.EnemyUnits.Where(x => x.Owner == f.Id).ToList())
                    ActUnit(w, bal, f, u);
            }
            // 도착 대기 중인 급유기와 맞닿은 적(공중 전력)은 격추
            foreach (var u in w.EnemyUnits.ToList()) TankerRules.ShootDownAdjacent(w, u);
            FactionCombat(w, bal);
            GroundCombat(w, bal);
            Reinforce(w, bal);
            if (w.Bases.All(b => b.Owner != World.Player)) w.GameOver = "모든 거점 상실";
        }

        // ---- 태세 ----

        static void UpdateStance(World w, Balance bal, Faction f)
        {
            var old = f.Stance;
            f.Stance = ComputeStance(w, bal, f);
            if (f.Stance != old) w.EnemyEvents.Add($"{f.Name} 태세 {StanceName(old)} → {StanceName(f.Stance)} (전력 {FactionStrength(w, f)})");
        }

        /// <summary>적 턴 시작 시 정해질 태세 (상태를 바꾸지 않음).</summary>
        public static Stance ComputeStance(World w, Balance bal, Faction f)
        {
            if (FactionStrength(w, f) < bal.RegroupBelowStrength) return Stance.Regroup;
            if (w.Turn - f.LastLossTurn < bal.DefensiveTurnsAfterLoss) return Stance.Defensive;
            return Stance.Offensive;
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
            // 비행 중 편대와 맞닿아 있으면 먼저 교전을 걸고 이번 턴 행동 끝
            if (FlightContact(w, bal, u)) return;

            // 인접한 아군 지상군(출동 중)이 있으면 이동하지 않고 교전
            var engaged = w.GroundUnits.FirstOrDefault(g => g.Deployed && Hex.Distance(g.Hex, u.Hex) <= 1);
            if (engaged != null)
            {
                EngageGround(w, bal, u, engaged);
                return;
            }

            var goal = ChooseGoal(w, bal, f, u, f.Stance);
            u.Goal = goal;
            if (goal is not { } g) return;

            // 이미 인접하면 공격 (공세) / 도착 (그 외)
            if (f.Stance == Stance.Offensive && w.BaseAt(g) is { Owner: World.Player } target0 && Hex.Distance(u.Hex, g) <= 1)
            {
                Attack(w, bal, f, u, target0);
                return;
            }

            var steps = StepsThisTurn(w, bal, f, u.Hex, g, f.Stance, out _);
            if (steps.Count > 0)
            {
                // 한 칸씩 이동하며 기록 (맵 재생). 비행 중 편대와 맞닿으면 교전을 걸고 멈춘다
                var trace = w.AddTrace(TraceKind.Enemy, u.Owner, u.Id, u.Name, new[] { u.Hex }, Vision.IsVisible(w, bal, u.Hex));
                foreach (var step in steps)
                {
                    u.Hex = step;
                    trace.Path.Add(step);
                    trace.Seen.Add(Vision.IsVisible(w, bal, step));
                    // 자원 타일에 들어가면 점령 (수비대 없음)
                    if (w.SiteAt(step) is { } site) SiteRules.Capture(w, site, u.Owner, u.Name);
                    if (FlightContact(w, bal, u)) return;
                }
                if (Vision.IsVisible(w, bal, u.Hex)) w.EnemyEvents.Add($"{u.Name} 이동 → {u.Hex.ToOffset()}");
            }
            TankerRules.ShootDownAdjacent(w, u);

            if (f.Stance == Stance.Offensive && w.BaseAt(g) is { Owner: World.Player } target && Hex.Distance(u.Hex, g) <= 1)
                Attack(w, bal, f, u, target);
        }

        /// <summary>태세에 따른 이번 턴 목표 칸 (없으면 null).</summary>
        static Hex? ChooseGoal(World w, Balance bal, Faction f, EnemyUnit u, Stance stance)
        {
            // 탐지한 급유기가 이번 턴에 닿으면 그쪽으로 (재정비 중이 아니면)
            if (stance != Stance.Regroup && TankerRules.TargetFor(w, bal, f, u) is { } tanker) return tanker.Hex;
            var home = BasesOf(w, f).OrderBy(b => Hex.Distance(b.Hex, u.Hex)).FirstOrDefault();
            switch (stance)
            {
                case Stance.Offensive:
                {
                    // 아군 거점과 자원 타일 중 점수가 높은 쪽 (적도 자원을 노린다)
                    var (b, bs) = BestBase(w, bal, f, u);
                    var (site, ss) = BestSite(w, f, u);
                    if (site != null && (b == null || ss > bs)) return site.Hex;
                    return b?.Hex;
                }
                case Stance.Defensive:
                    return home != null && Hex.Distance(home.Hex, u.Hex) > 1 ? home.Hex : null;
                case Stance.Regroup:
                    return home != null && u.Hex != home.Hex ? home.Hex : null;
            }
            return null;
        }

        /// <summary>
        /// 비행 중 편대와 맞닿으면(1칸 안) 적 부대가 먼저 교전을 건다 (C안: 적과 같은 조건). 교전한 부대는 이번 턴 행동 끝.
        /// 같은 교전 계산을 쓰고, 편대는 날고 있는 기체로 싸운다.
        /// </summary>
        static bool FlightContact(World w, Balance bal, EnemyUnit u)
        {
            var s = w.Squadrons.FirstOrDefault(x => x.AirHex is { } ah && Hex.Distance(ah, u.Hex) <= 1 && FlightRules.Flyers(w, x).Count > 0);
            if (s == null) return false;
            var at = s.AirHex.Value;
            FlightRules.Jettison(w, s); // 교전하면 드롭탱크를 버린다
            var rep = SortieRules.Resolve(w, bal, s, u.Hex, u);
            rep.Order = w.Debriefs.Count + 1;
            w.Debriefs.Add(rep);
            w.AddEvent(at, "적 선공");
            w.EnemyEvents.Add($"{u.Name} → 비행 중인 {s.Name} 공격: {rep.Effect}");
            w.Log("비행피습", $"{u.Name} → {s.Name} @ {at.ToOffset()} · {rep.Rounds}R · 파괴율 {rep.DestructionRate:0.00}");
            FlightRules.CheckGrounded(w, s);
            u.Goal = null;
            return true;
        }

        /// <summary>이번 턴에 지나갈 칸들 (이동력 한도, 남의 거점 칸 앞에서 멈춤, 공세면 목표 인접에서 멈춤). rest = 남은 경로.</summary>
        static List<Hex> StepsThisTurn(World w, Balance bal, Faction f, Hex from, Hex goal, Stance stance, out List<Hex> rest)
        {
            var steps = new List<Hex>();
            rest = new List<Hex>();
            var path = HexPath.Find(w.Map, bal, from, goal);
            if (path == null) return steps;
            var enterGoal = w.SiteAt(goal) != null; // 자원 타일은 칸에 들어가야 점령
            var points = bal.EnemyMovePoints;
            var stopped = false;
            foreach (var step in path)
            {
                if (!stopped)
                {
                    // 목표 거점 본부 칸에는 들어가지 않고 인접에서 멈춘다 (아군 거점) — 자기 기지는 들어간다
                    var cost = HexPath.MoveCost(bal, w.Map[step].Terrain);
                    if (w.BaseAt(step) is { } sb && sb.Owner != f.Id || cost > points)
                    {
                        stopped = true;
                    }
                    else
                    {
                        points -= cost;
                        steps.Add(step);
                        if (stance == Stance.Offensive && !enterGoal && Hex.Distance(step, goal) <= 1) stopped = true;
                        continue;
                    }
                }
                if (w.BaseAt(step) is { } rb && rb.Owner != f.Id) break;
                rest.Add(step);
            }
            return steps;
        }

        /// <summary>적 부대의 다음 적 턴 행동 예상 (상태를 바꾸지 않음). 다른 부대의 움직임·교전은 반영하지 않는다.</summary>
        public sealed class Prediction
        {
            public Stance Stance;
            public Hex? Goal;
            public string GoalName;
            public List<Hex> ThisTurn = new();
            public List<Hex> Later = new();
            public Base AttackTarget;
            public GroundUnit EngageGround;
        }

        public static Prediction Predict(World w, Balance bal, EnemyUnit u)
        {
            var f = FactionOf(w, u.Owner);
            var p = new Prediction { Stance = ComputeStance(w, bal, f) };
            p.EngageGround = w.GroundUnits.FirstOrDefault(g => g.Deployed && Hex.Distance(g.Hex, u.Hex) <= 1);
            if (p.EngageGround != null) return p;
            p.Goal = ChooseGoal(w, bal, f, u, p.Stance);
            if (p.Goal is not { } goal) return p;
            p.GoalName = w.BaseAt(goal)?.Name ?? w.SiteAt(goal)?.Name ?? (w.Tankers.Any(t => t.Hex == goal) ? "급유기" : goal.ToOffset().ToString());
            if (!(p.Stance == Stance.Offensive && Hex.Distance(u.Hex, goal) <= 1))
                p.ThisTurn = StepsThisTurn(w, bal, f, u.Hex, goal, p.Stance, out p.Later);
            var end = p.ThisTurn.Count > 0 ? p.ThisTurn[^1] : u.Hex;
            if (p.Stance == Stance.Offensive && w.BaseAt(goal) is { Owner: World.Player } target && Hex.Distance(end, goal) <= 1)
                p.AttackTarget = target;
            return p;
        }

        /// <summary>공세 목표: 가치(등급·시설) ÷ 거리 ÷ 알려진 방어. 편대 수는 탐지 범위 안일 때만 반영.</summary>
        public static Base BestTarget(World w, Balance bal, Faction f, EnemyUnit u) => BestBase(w, bal, f, u).b;

        /// <summary>자원 타일 목표: 다른 세력 소유·중립 타일, 가치(등급) ÷ 거리. 수비대가 없어 방어는 보지 않는다. 아군 타일은 조금 더 노린다.</summary>
        public static (ResourceSite site, float score) BestSite(World w, Faction f, EnemyUnit u)
        {
            ResourceSite best = null;
            var bestScore = float.MinValue;
            foreach (var s in w.Sites.Where(x => x.Owner != f.Id))
            {
                var value = Math.Max(1, s.Grade) * 8f * (s.Owner == World.Player ? 1.2f : 1f);
                var score = value / (1f + Hex.Distance(u.Hex, s.Hex) * 0.3f);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = s;
                }
            }
            return (best, bestScore);
        }

        static (Base b, float score) BestBase(World w, Balance bal, Faction f, EnemyUnit u)
        {
            Base best = null;
            var bestScore = float.MinValue;
            foreach (var b in w.Bases.Where(x => x.Owner == World.Player))
            {
                var value = b.Grade * 10f + BaseRules.FacilityPoints(b) * 3f;
                var defense = FacilityDefense(w, bal, b);
                if (FactionSees(w, bal, f, b.Hex)) defense += (int)(w.SquadronsAt(b).Where(s => s.AirHex == null).Sum(s => s.AircraftIds.Count) * bal.KnownAircraftDefenseWeight);
                var dist = Hex.Distance(u.Hex, b.Hex);
                var score = value / (1f + dist * 0.3f) / (1f + defense * 0.05f);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = b;
                }
            }
            return (best, bestScore);
        }

        // ---- 피습·요격·거점 상실 ----

        static void Attack(World w, Balance bal, Faction f, EnemyUnit u, Base target)
        {
            w.AddEvent(target.Hex, "피습");
            var rep = new InterceptReport
            {
                BaseName = target.Name,
                UnitName = u.Name,
                EnemyBefore = u.Strength,
                FacilityDefense = FacilityDefense(w, bal, target),
            };

            // 1) 주둔 편대 자동 요격 (이번 턴 출격한 편대 제외, 재배치해 온 편대는 참가): 4단계 리졸버와 같은 라운드 교전
            var squadrons = w.SquadronsAt(target).Where(s => !s.SortiedThisTurn && s.AirHex == null).ToList();
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

            // 2) 대공 시설 + 주둔 지상군: 남은 전력에서 방어력만큼 더 깎는다 (공중부터)
            var groundDefense = GroundRules.StationedStrength(w, target);
            var remaining = Math.Max(0, air + ground - rep.FacilityDefense - groundDefense);
            rep.EnemyAfterFacility = remaining;
            if (rep.FacilityDefense > 0) rep.Log.Add($"대공 시설 방어력 {rep.FacilityDefense}");
            if (groundDefense > 0) rep.Log.Add($"주둔 지상군 방어력 {groundDefense}");
            if (rep.FacilityDefense + groundDefense > 0) rep.Log.Add($"→ 적 전력 {air + ground} → {remaining}");
            w.Stats.BasesAttacked++;
            var scale = air + ground > 0 ? (float)remaining / (air + ground) : 0f;
            u.Air = (int)Math.Round(air * scale);
            u.Ground = remaining - u.Air;

            // 3) 판정
            if (remaining >= bal.CaptureThreshold)
            {
                rep.Captured = true;
                rep.Log.Add($"남은 전력 {remaining} ≥ {bal.CaptureThreshold} — 거점 상실");
                Capture(w, bal, f, u, target, rep);
                HqRules.AddTrust(w, bal, -bal.TrustBaseLost, $"{target.Name} 상실");
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
            w.Log("피습", $"{target.Name} ← {u.Name} (전력 {rep.EnemyBefore}) · 요격 편대 {rep.SquadronsEngaged} · {rep.Rounds}R · 시설 {rep.FacilityDefense} · {(rep.Captured ? "상실" : "방어")}");
            w.EnemyEvents.Add($"{target.Name} 피습 ({u.Name}): {(rep.Captured ? "거점 상실" : remaining == 0 ? "격멸" : "격퇴")}");
        }

        /// <summary>거점 상실 (기획서 5.6): 등급 −1, 상한 초과 시설 파괴, 주둔 편대 강제 이탈(자리 없으면 기체 손실), 영역은 거점과 함께.</summary>
        public static void Capture(World w, Balance bal, Faction f, EnemyUnit u, Base target, InterceptReport rep)
        {
            w.Stats.BasesLost++;
            foreach (var g in GroundRules.StationedAt(w, target).ToList())
            {
                w.GroundUnits.Remove(g);
                rep.Log.Add($"   주둔 {g.Name} 괴멸");
            }
            target.Owner = f.Id;
            target.Grade = Math.Max(1, target.Grade - 1);
            var destroyed = BaseRules.TrimToCap(bal, target);
            BaseRules.RefreshTerritory(w, bal);
            if (destroyed > 0) rep.Log.Add($"   등급 {target.Grade + 1} → {target.Grade}, 상한 초과 시설 규모 {destroyed} 파괴");
            else rep.Log.Add($"   등급 {target.Grade + 1} → {target.Grade}");

            foreach (var s in w.SquadronsAt(target).ToList())
            {
                // 비행 중 편대는 하늘에 남는다: 다른 아군 거점으로 소속만 옮기고, 없으면 그대로 (착륙할 곳을 찾아야 함)
                if (s.AirHex != null)
                {
                    var home = w.Bases.Where(b => b.Owner == World.Player && b.Id != target.Id).OrderBy(b => Hex.Distance(b.Hex, s.AirHex.Value)).FirstOrDefault();
                    if (home != null) s.BaseId = home.Id;
                    rep.Log.Add($"   {s.Name} 비행 중 — 소속 거점을 잃음");
                    continue;
                }
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
            target.Structures = BaseRules.FacilityPoints(target) * 10 + 10;
            u.Hex = target.Hex;
            u.Goal = null;
        }

        // ---- 출동 중 아군 지상군 피습 ----

        static void GroundCombat(World w, Balance bal)
        {
            foreach (var g in w.GroundUnits.Where(x => x.Deployed).ToList())
            {
                foreach (var u in w.EnemyUnits.Where(x => Hex.Distance(x.Hex, g.Hex) <= 1 && !x.EngagedThisTurn).ToList())
                {
                    if (!w.GroundUnits.Contains(g)) break;
                    EngageGround(w, bal, u, g);
                }
            }
            foreach (var u in w.EnemyUnits) u.EngagedThisTurn = false;
        }

        /// <summary>적 부대 ↔ 출동 중 아군 지상군 수치 교전 (세력 간 교전과 같은 방식).</summary>
        static void EngageGround(World w, Balance bal, EnemyUnit u, GroundUnit g)
        {
            w.AddEvent(g.Hex, "지상 교전");
            var dealt = Math.Min(bal.FactionCombatMaxDamage, g.Strength / 2);
            var taken = Math.Min(bal.FactionCombatMaxDamage, u.Strength / 2);
            Damage(u, dealt);
            g.Strength = Math.Max(0, g.Strength - taken);
            u.EngagedThisTurn = true;
            w.EnemyEvents.Add($"{u.Name}이(가) 이동 중인 {g.Name} 공격: 아군 −{taken}, 적 −{dealt}");
            if (u.Strength == 0) RemoveUnit(w, u);
            if (g.Strength == 0) GroundRules.Destroy(w, g, "괴멸");
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
                // 가진 자원 타일(1등급 이상)마다 보충이 1턴 빨라진다 (최소 2턴)
                var need = Math.Max(2, bal.ReinforceTurns - SiteRules.OwnedBy(w, f.Id).Count(x => x.Grade > 0));
                if (f.ReinforceTimer >= need)
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
