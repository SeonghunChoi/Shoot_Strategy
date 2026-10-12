using System.Collections.Generic;
using System.Linq;

namespace Prototype.Strategy.Rules
{
    /// <summary>턴 수지 계산과 턴 종료 처리 (기획서 8장). 무상태 서비스.</summary>
    public static class Economy
    {
        /// <summary>이번 턴을 지금 마치면 적용될 수입·지출 항목.</summary>
        public static List<BudgetLine> Forecast(World w, Balance bal)
        {
            var lines = new List<BudgetLine> { new() { Label = $"상부 예산 (신뢰도 {w.Trust})", Amount = HqRules.Budget(w, bal) } };

            // 자원 타일 수입 (종류별)
            foreach (var g in SiteRules.OwnedBy(w, World.Player).Where(x => x.Grade > 0).GroupBy(x => x.Type).OrderBy(g => g.Key))
                lines.Add(new BudgetLine { Label = $"{SiteRules.TypeName(g.Key)} ({g.Count()}곳)", Amount = g.Sum(x => SiteRules.Income(bal, x)) });

            var refund = SquadronRules.EntertainmentRefund(w, bal);
            if (refund > 0) lines.Add(new BudgetLine { Label = "유흥가 급여 환수", Amount = refund });

            var grades = w.Bases.Where(b => b.Owner == World.Player).Sum(b => b.Grade);
            lines.Add(new BudgetLine { Label = $"거점 유지비 (등급 합 {grades})", Amount = -bal.UpkeepPerBaseGrade * grades });
            lines.Add(new BudgetLine { Label = $"파일럿 급여 ({w.Pilots.Count}명)", Amount = -SquadronRules.Salaries(w, bal) });
            if (w.GroundUnits.Count > 0) lines.Add(new BudgetLine { Label = $"지상군 유지비 ({w.GroundUnits.Count}개)", Amount = -GroundRules.Upkeep(w, bal) });
            var (repaired, repairCost) = SquadronRules.RepairForecast(w, bal);
            if (repairCost > 0) lines.Add(new BudgetLine { Label = $"기체 수리 ({repaired}%)", Amount = -repairCost });
            return lines;
        }

        public static int Net(IEnumerable<BudgetLine> lines) => lines.Sum(l => l.Amount);

        /// <summary>
        /// 턴 종료: 수지 적용 → 연속 적자(자금 0 미만으로 마친 턴) 집계 → 파산 판정 → 다음 턴.
        /// </summary>
        public static TurnReport EndTurn(World w, Balance bal)
        {
            if (w.GameOver != null) return null;

            var lines = Forecast(w, bal);
            var net = Net(lines);
            w.Funds += net;
            w.EnemyEvents.Clear();
            w.Traces.Clear();
            // 급유를 마친 급유기는 적 턴 전에 떠난다
            TankerRules.Depart(w);
            // 비행 중 편대가 있으면 적 선공 디브리핑을 새로 쌓는다
            if (w.Squadrons.Any(s => s.AirHex != null)) w.Debriefs.Clear();
            // 턴 순서: 아군 지상군 이동 → 적 턴 → 편대 회복. 이번 턴 출격한 편대는 요격 불가
            GroundRules.MovePhase(w, bal);
            if (w.GameOver == null) EnemyTurn.Run(w, bal);
            // 목격 기록은 적 턴이 끝난 순간의 시야로 (출격 지역·비행 편대 포함) — 불시착 처리 전에
            Vision.UpdateSightings(w, bal);
            SquadronRules.EndTurn(w, bal);
            HqRules.EndTurn(w, bal);
            w.DeficitStreak = w.Funds < 0 ? w.DeficitStreak + 1 : 0;

            var report = new TurnReport
            {
                Turn = w.Turn,
                Lines = lines,
                Net = net,
                FundsAfter = w.Funds,
                DeficitStreak = w.DeficitStreak,
            };
            w.Reports.Add(report);

            if (w.DeficitStreak >= bal.BankruptTurns) w.GameOver = $"파산 — 자금 0 미만으로 {w.DeficitStreak}턴 연속";
            else if (w.GameOver == null) w.Turn++;
            return report;
        }
    }
}
