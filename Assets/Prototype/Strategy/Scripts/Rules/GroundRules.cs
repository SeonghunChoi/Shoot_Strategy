using System;
using System.Collections.Generic;
using System.Linq;

namespace Prototype.Strategy.Rules
{
    /// <summary>아군 지상군 1개 (기획서 4장 지상군: 등급과 수로만 처리 → 전력 하나로 단순화).</summary>
    public sealed class GroundUnit
    {
        public int Id;
        public string Name;
        /// <summary>주둔 거점 (출동 중이면 출발 거점).</summary>
        public int BaseId;
        public bool Deployed;
        public Hex Hex;
        public Hex? Target;
        public int Strength;
    }

    /// <summary>
    /// 지상군 고용·출동·이동·점령 (기획서 4장, 5.5). 무상태 서비스.
    /// 턴 순서: 플레이어 턴 종료 → 아군 지상군 이동(MovePhase) → 적 턴. 이동 중 피습은 EnemyTurn이 처리한다.
    /// </summary>
    public static class GroundRules
    {
        public static int BarracksGrade(World w, Base b) => BaseRules.GradeOf(b, FacilityType.Barracks);

        public static IEnumerable<GroundUnit> StationedAt(World w, Base b) => w.GroundUnits.Where(g => !g.Deployed && g.BaseId == b.Id);

        public static int StationedStrength(World w, Base b) => StationedAt(w, b).Sum(g => g.Strength);

        // ---- 고용 ----

        public static string CanHire(World w, Balance bal, Base b)
        {
            if (b.Owner != World.Player) return "아군 거점이 아님";
            if (BarracksGrade(w, b) == 0) return "병영 필요";
            if (w.Funds < bal.GroundHireCost) return "자금 부족";
            return null;
        }

        public static GroundUnit Hire(World w, Balance bal, Base b)
        {
            if (CanHire(w, bal, b) != null) return null;
            w.Funds -= bal.GroundHireCost;
            var g = new GroundUnit
            {
                Id = w.NextId++,
                Name = $"지상군 {w.GroundUnits.Count + 1}",
                BaseId = b.Id,
                Hex = b.Hex,
                Strength = bal.GroundBaseStrength + BarracksGrade(w, b) * bal.GroundStrengthPerBarracksGrade,
            };
            w.GroundUnits.Add(g);
            return g;
        }

        public static int Upkeep(World w, Balance bal) => w.GroundUnits.Count * bal.GroundUpkeep;

        // ---- 출동 ----

        public static string CanDeploy(World w, Balance bal, GroundUnit g, Hex target)
        {
            if (!w.Map.Contains(target)) return "맵 밖";
            if (w.Map[target].Terrain == TerrainType.Sea) return "해상";
            if (target == g.Hex) return "현재 위치";
            if (HexPath.Find(w.Map, bal, g.Hex, target) == null) return "갈 수 없는 곳";
            if (!g.Deployed && w.Funds < bal.GroundDeployCost) return "자금 부족";
            return null;
        }

        /// <summary>출동(처음이면 비용) 또는 출동 중 목표 변경(무료).</summary>
        public static void Deploy(World w, Balance bal, GroundUnit g, Hex target)
        {
            if (CanDeploy(w, bal, g, target) != null) return;
            if (!g.Deployed)
            {
                w.Funds -= bal.GroundDeployCost;
                g.Deployed = true;
            }
            g.Target = target;
        }

        /// <summary>가장 가까운 아군 거점으로 복귀 명령.</summary>
        public static void Recall(World w, GroundUnit g)
        {
            var home = w.Bases.Where(b => b.Owner == World.Player).OrderBy(b => Hex.Distance(b.Hex, g.Hex)).FirstOrDefault();
            if (home != null) g.Target = home.Hex;
        }

        /// <summary>목표가 적 거점이면 그 거점.</summary>
        public static Base TargetBase(World w, GroundUnit g) =>
            g.Target is { } t && w.BaseAt(t) is { } b && b.Owner != World.Player ? b : null;

        // ---- 이동 (적 턴 전) ----

        public static void MovePhase(World w, Balance bal)
        {
            foreach (var g in w.GroundUnits.Where(x => x.Deployed && x.Target != null).ToList())
            {
                var target = g.Target.Value;
                var enemyBase = TargetBase(w, g);

                // 적 거점 인접이면 공격
                if (enemyBase != null && Hex.Distance(g.Hex, target) <= 1)
                {
                    Assault(w, bal, g, enemyBase);
                    continue;
                }

                var path = HexPath.Find(w.Map, bal, g.Hex, target);
                if (path == null) continue;
                var points = bal.GroundMovePoints;
                var trace = w.AddTrace(TraceKind.Ground, World.Player, g.Id, g.Name, new[] { g.Hex });
                foreach (var step in path)
                {
                    if (w.EnemyUnitAt(step) != null) break;                       // 적 부대가 막음
                    if (w.BaseAt(step) is { } sb && sb.Owner != World.Player) break; // 적 거점은 인접에서 공격
                    var cost = HexPath.MoveCost(bal, w.Map[step].Terrain);
                    if (cost > points) break;
                    points -= cost;
                    g.Hex = step;
                    trace.Path.Add(step);
                    trace.Seen.Add(true);
                    // 자원 타일에 들어가면 점령 (수비대 없음)
                    if (w.SiteAt(step) is { } site) SiteRules.Capture(w, site, World.Player, g.Name);
                    if (enemyBase != null && Hex.Distance(g.Hex, target) <= 1) break;
                }

                // 아군 거점 도착 → 주둔
                if (w.BaseAt(g.Hex) is { Owner: World.Player } home && g.Hex == target)
                {
                    g.Deployed = false;
                    g.Target = null;
                    g.BaseId = home.Id;
                    w.EnemyEvents.Add($"{g.Name} {home.Name}에 주둔");
                    continue;
                }
                if (enemyBase != null && Hex.Distance(g.Hex, target) <= 1) Assault(w, bal, g, enemyBase);
            }
        }

        /// <summary>
        /// 적 거점 공격: 거점 칸의 적 부대와 먼저 교전, 그다음 지상군 전력 > 대공 + 시설이면 점령, 아니면 포위(서로 깎임).
        /// </summary>
        static void Assault(World w, Balance bal, GroundUnit g, Base b)
        {
            w.AddEvent(b.Hex, "지상 공격");
            var garrison = w.EnemyUnitAt(b.Hex);
            if (garrison != null)
            {
                var dealt = Math.Min(bal.FactionCombatMaxDamage, g.Strength / 2);
                var taken = Math.Min(bal.FactionCombatMaxDamage, garrison.Strength / 2);
                var air = Math.Min(garrison.Air, dealt / 2);
                garrison.Air -= air;
                garrison.Ground = Math.Max(0, garrison.Ground - (dealt - air));
                g.Strength = Math.Max(0, g.Strength - taken);
                w.EnemyEvents.Add($"{g.Name} → {b.Name} 주둔 {garrison.Name} 교전: 적 −{dealt}, 아군 −{taken}");
                if (garrison.Strength == 0) EnemyTurn.RemoveUnit(w, garrison);
                if (g.Strength == 0) Destroy(w, g, "교전 중 괴멸");
                return;
            }

            var defense = b.AntiAir + b.Structures;
            if (g.Strength > defense)
            {
                CaptureEnemyBase(w, bal, g, b);
                return;
            }
            // 포위: 시설을 깎고 대공에 깎인다
            var siege = Math.Max(1, g.Strength / 3);
            var loss = Math.Max(1, b.AntiAir / 3);
            b.Structures = Math.Max(0, b.Structures - siege);
            g.Strength = Math.Max(0, g.Strength - loss);
            w.EnemyEvents.Add($"{g.Name} {b.Name} 포위: 시설 −{siege}, 아군 −{loss} (점령하려면 전력 > 대공 {b.AntiAir} + 시설 {b.Structures})");
            if (g.Strength == 0) Destroy(w, g, "포위 중 괴멸");
        }

        /// <summary>점령 (기획서 5.6의 대칭): 등급 −1, 상한 초과 시설 파괴, 영역은 거점과 함께, 지상군은 주둔.</summary>
        public static void CaptureEnemyBase(World w, Balance bal, GroundUnit g, Base b)
        {
            var oldOwner = b.Owner;
            b.Owner = World.Player;
            b.Grade = Math.Max(1, b.Grade - 1);
            BaseRules.TrimToCap(bal, b);
            BaseRules.RefreshTerritory(w, bal);
            b.AntiAir = 0;
            b.Structures = 0;
            g.Deployed = false;
            g.Target = null;
            g.BaseId = b.Id;
            g.Hex = b.Hex;
            if (w.FactionById(oldOwner) is { } f) f.LastLossTurn = w.Turn;
            w.Stats.BasesCaptured++;
            w.EnemyEvents.Add($"{g.Name} {b.Name} 점령!");
            w.Log("점령", $"{g.Name}(전력 {g.Strength}) → {b.Name}");
            HqRules.AddTrust(w, bal, bal.TrustCapture, $"{b.Name} 점령");
            if (b.Id == w.GoalBaseId) HqRules.Win(w, bal, b);
        }

        public static void Destroy(World w, GroundUnit g, string why)
        {
            w.GroundUnits.Remove(g);
            w.EnemyEvents.Add($"{g.Name} {why}");
        }
    }
}
