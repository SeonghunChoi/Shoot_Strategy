using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Prototype.Strategy.Rules
{
    /// <summary>헤드리스 시뮬레이션의 플레이어 대역. 한 턴의 운영·출격을 하고 나면 러너가 턴을 넘긴다.</summary>
    public interface IBot
    {
        string Name { get; }
        void PlayTurn(World w, Balance bal);
    }

    /// <summary>아무것도 하지 않음: 적이 얼마나 빨리 무너뜨리는지, 예산만으로 몇 턴 버티는지의 기준선.</summary>
    public sealed class PassiveBot : IBot
    {
        public string Name => "수동형";
        public void PlayTurn(World w, Balance bal) { }
    }

    /// <summary>
    /// 정해진 규칙만 따르는 서툰 플레이어: 수입 시설 → 병영·지상군으로 목표 거점 공략 → 보이는 가까운 적 출격(거점마다 편대 하나는 남김).
    /// 지시는 받기만 한다. 재미가 아니라 "서툰 플레이도 버티는가"를 보는 용도.
    /// </summary>
    public sealed class SimpleBot : IBot
    {
        public string Name => "단순형";

        public void PlayTurn(World w, Balance bal)
        {
            BuildIncome(w, bal);
            BuildBarracksAndGround(w, bal);
            Sorties(w, bal);
        }

        static void BuildIncome(World w, Balance bal)
        {
            foreach (var b in w.Bases.Where(x => x.Owner == World.Player).ToList())
            {
                // 영역 안의 유전·산악 칸에 수입 시설
                foreach (var h in w.TerritoryOf(b).ToList())
                {
                    var cell = w.Map[h];
                    if (cell.Oil && w.Funds >= bal.Def(FacilityType.Refinery).Cost + 200) BaseRules.Build(w, bal, h, FacilityType.Refinery);
                    else if (cell.Terrain == TerrainType.Mountain && w.Funds >= bal.Def(FacilityType.Mine).Cost + 200) BaseRules.Build(w, bal, h, FacilityType.Mine);
                }
                // 영역에 유전이 없으면 붙은 유전 칸으로 확장
                if (!w.TerritoryOf(b).Any(h => w.Map[h].Oil))
                {
                    var oil = BaseRules.ExpandCandidates(w, b).FirstOrDefault(h => w.Map[h].Oil);
                    if (w.Map.Contains(oil) && w.Map[oil].Oil && w.Funds >= BaseRules.ExpandCost(w, bal, b) + 400) BaseRules.Expand(w, bal, b, oil);
                }
            }
        }

        static void BuildBarracksAndGround(World w, Balance bal)
        {
            var bases = w.Bases.Where(x => x.Owner == World.Player).ToList();
            if (bases.Count == 0) return;
            var barracks = bases.FirstOrDefault(b => GroundRules.BarracksGrade(w, b) > 0);
            if (barracks == null && w.Funds >= bal.Def(FacilityType.Barracks).Cost + 300)
            {
                foreach (var b in bases)
                {
                    var spot = w.TerritoryOf(b).FirstOrDefault(h => BaseRules.CanBuild(w, bal, h, FacilityType.Barracks) == null);
                    if (BaseRules.CanBuild(w, bal, spot, FacilityType.Barracks) != null) continue;
                    BaseRules.Build(w, bal, spot, FacilityType.Barracks);
                    barracks = b;
                    break;
                }
            }
            if (barracks != null && w.GroundUnits.Count == 0 && w.Funds >= bal.GroundHireCost + bal.GroundDeployCost + 300)
                GroundRules.Hire(w, bal, barracks);

            var goal = w.Bases.FirstOrDefault(b => b.Id == w.GoalBaseId && b.Owner != World.Player);
            foreach (var g in w.GroundUnits.Where(x => !x.Deployed).ToList())
                if (goal != null && g.Strength > (goal.AntiAir + goal.Structures) / 2 && GroundRules.CanDeploy(w, bal, g, goal.Hex) == null)
                    GroundRules.Deploy(w, bal, g, goal.Hex);
        }

        static void Sorties(World w, Balance bal)
        {
            // C안: 비행 중 편대는 가장 가까운 아군 거점으로 돌아온다
            foreach (var s in w.Squadrons.Where(FlightRules.IsFlying).ToList())
                if (FlightRules.ReturnStep(w, bal, s) is { } step) FlightRules.Order(w, bal, s, step);

            foreach (var b in w.Bases.Where(x => x.Owner == World.Player).ToList())
            {
                // 거점마다 편대 하나는 남긴다 (방어)
                foreach (var s in w.SquadronsAt(b).Where(x => !FlightRules.IsFlying(x)).Skip(1).ToList())
                {
                    if (FlightRules.CanLaunch(w, bal, s) != null) continue;
                    // 이번 턴에 닿고, 다음 턴 돌아올 연료가 남는 목표만
                    var targets = FlightRules.Destinations(w, bal, s)
                        .Where(t => FlightRules.IsAttackTarget(w, s, t) && Hex.Distance(b.Hex, t) * 2 + bal.IdleFuelPerTurn <= FlightRules.MaxFuel(w, bal, s))
                        .ToList();
                    if (targets.Count == 0) continue;
                    var goalHex = w.Bases.FirstOrDefault(x => x.Id == w.GoalBaseId)?.Hex;
                    var target = targets.Contains(goalHex ?? default) && w.GroundUnits.Any(g => g.Deployed)
                        ? goalHex.Value
                        : targets.OrderBy(t => Hex.Distance(b.Hex, t)).First();
                    var mission = SortieRules.MissionAt(w, target);
                    s.AircraftIds.Sort((x, y) => Stat(bal, w.AircraftById(y), mission).CompareTo(Stat(bal, w.AircraftById(x), mission)));
                    FlightRules.Order(w, bal, s, target);
                }
            }
        }

        static int Stat(Balance bal, Aircraft a, MissionType? m)
        {
            var d = bal.Def(a.Type);
            return m == MissionType.BaseBombing ? d.Bombing : d.Air + d.Ground;
        }
    }

    public sealed class SimResult
    {
        public string Bot;
        public ulong Seed;
        public int BankruptTurns;
        public string Result; // 승리 / 해임 / 상실 / 파산 / 시간 초과
        public int Turns;
        public int FirstDeficitTurn; // 자금이 처음 0 미만으로 끝난 턴 (없으면 0)
        public int MinFunds;
        public int FinalFunds;
        public int FinalTrust;
        public GameStats Stats;
    }

    /// <summary>헤드리스 N턴 시뮬레이션 (검증계획 4장 7단계, 기술 설계 2.6 결정론 활용).</summary>
    public static class SimRunner
    {
        public static SimResult Run(IBot bot, ulong seed, int maxTurns, Action<Balance> tune = null)
        {
            var bal = new Balance();
            tune?.Invoke(bal);
            var w = World.Create(seed, bal);
            var firstDeficit = 0;
            var minFunds = w.Funds;
            while (w.GameOver == null && w.Turn <= maxTurns)
            {
                bot.PlayTurn(w, bal);
                Economy.EndTurn(w, bal);
                minFunds = Math.Min(minFunds, w.Funds);
                if (firstDeficit == 0 && w.Funds < 0) firstDeficit = w.Turn;
            }
            return new SimResult
            {
                Bot = bot.Name, Seed = seed, BankruptTurns = bal.BankruptTurns, Result = Classify(w), Turns = w.Turn,
                FirstDeficitTurn = firstDeficit, MinFunds = minFunds, FinalFunds = w.Funds, FinalTrust = w.Trust, Stats = w.Stats,
            };
        }

        static string Classify(World w)
        {
            if (w.Victory) return "승리";
            if (w.GameOver == null) return "시간 초과";
            if (w.GameOver.StartsWith("해임")) return "해임";
            if (w.GameOver.StartsWith("파산")) return "파산";
            if (w.GameOver.StartsWith("모든 거점")) return "상실";
            return w.GameOver;
        }

        /// <summary>봇 2종 × 파산 기준 여러 개 × 시드 N개. 결과 CSV와 요약 Markdown을 쓴다.</summary>
        public static string RunBatch(string folder, int seeds, int maxTurns, int[] bankruptTurns, Action<string> progress = null)
        {
            var bots = new IBot[] { new PassiveBot(), new SimpleBot() };
            var results = new List<SimResult>();
            foreach (var bot in bots)
            foreach (var bt in bankruptTurns)
            {
                progress?.Invoke($"{bot.Name} · 파산 {bt}턴");
                for (var i = 0; i < seeds; i++)
                    results.Add(Run(bot, 1000UL + (ulong)i, maxTurns, b => b.BankruptTurns = bt));
            }

            Directory.CreateDirectory(folder);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var csv = new StringBuilder();
            csv.AppendLine("bot,seed,bankrupt_turns,result,turns,first_deficit_turn,min_funds,final_funds,final_trust,sorties,attacked,bases_lost,captured,directives_done,directives_failed");
            foreach (var r in results)
                csv.AppendLine(PlaytestLog.Row(r.Bot, r.Seed, r.BankruptTurns, r.Result, r.Turns, r.FirstDeficitTurn, r.MinFunds, r.FinalFunds, r.FinalTrust,
                    r.Stats.Sorties, r.Stats.BasesAttacked, r.Stats.BasesLost, r.Stats.BasesCaptured, r.Stats.DirectivesDone, r.Stats.DirectivesFailed));
            File.WriteAllText(Path.Combine(folder, $"sim_{stamp}.csv"), csv.ToString(), new UTF8Encoding(true));

            var md = Summary(results, seeds, maxTurns);
            File.WriteAllText(Path.Combine(folder, $"sim_{stamp}.md"), md, new UTF8Encoding(true));
            return md;
        }

        public static string Summary(List<SimResult> results, int seeds, int maxTurns)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# 전략 프로토타입 헤드리스 시뮬레이션");
            sb.AppendLine();
            sb.AppendLine($"- 시드 {seeds}개 × 최대 {maxTurns}턴, 기본 튜닝 값 (파산 기준만 바꿈)");
            sb.AppendLine("- 봇은 사람보다 훨씬 서툽니다. \"이 수치에서 서툰 플레이도 버티는가 / 수동형은 몇 턴에 무너지는가\"를 보는 자료입니다.");
            sb.AppendLine();
            sb.AppendLine("| 봇 | 파산 기준 | 승리 | 파산 | 해임 | 상실 | 시간 초과 | 평균 턴 | 중앙 턴 | 첫 적자 턴(중앙) | 최저 자금(중앙) | 출격 평균 | 피습 평균 | 상실 평균 |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var g in results.GroupBy(r => (r.Bot, r.BankruptTurns)))
            {
                var list = g.ToList();
                int Count(string k) => list.Count(r => r.Result == k);
                string Pct(string k) => $"{Count(k) * 100 / list.Count}%";
                var deficits = list.Where(r => r.FirstDeficitTurn > 0).Select(r => r.FirstDeficitTurn).ToList();
                sb.AppendLine($"| {g.Key.Bot} | {g.Key.BankruptTurns}턴 | {Pct("승리")} | {Pct("파산")} | {Pct("해임")} | {Pct("상실")} | {Pct("시간 초과")} | " +
                              $"{list.Average(r => r.Turns):0.0} | {Median(list.Select(r => r.Turns))} | " +
                              $"{(deficits.Count > 0 ? Median(deficits).ToString() : "—")} ({deficits.Count}/{list.Count}) | {Median(list.Select(r => r.MinFunds))} | " +
                              $"{list.Average(r => r.Stats.Sorties):0.0} | {list.Average(r => r.Stats.BasesAttacked):0.0} | {list.Average(r => r.Stats.BasesLost):0.0} |");
            }
            return sb.ToString();
        }

        static int Median(IEnumerable<int> values)
        {
            var v = values.OrderBy(x => x).ToList();
            return v.Count == 0 ? 0 : v[v.Count / 2];
        }
    }
}
