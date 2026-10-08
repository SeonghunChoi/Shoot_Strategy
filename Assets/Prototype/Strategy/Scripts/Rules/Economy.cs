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
            var lines = new List<BudgetLine> { new() { Label = "상부 예산", Amount = bal.HqBudget } };

            var refinery = 0;
            var mine = 0;
            var finance = 0;
            foreach (var (hex, f) in w.Facilities)
            {
                if (w.BaseOf(hex)?.Owner != World.Player) continue;
                var def = bal.Def(f.Type);
                switch (f.Type)
                {
                    case FacilityType.Refinery: refinery += def.IncomePerGrade * f.Grade; break;
                    case FacilityType.Mine: mine += def.IncomePerGrade * f.Grade; break;
                    case FacilityType.Finance:
                        if (w.Funds > 0) finance += (int)(w.Funds * bal.FinanceRatePerGrade * f.Grade);
                        break;
                }
            }
            if (refinery > 0) lines.Add(new BudgetLine { Label = "정유시설", Amount = refinery });
            if (mine > 0) lines.Add(new BudgetLine { Label = "광산", Amount = mine });
            if (finance > 0) lines.Add(new BudgetLine { Label = "금융 이자", Amount = finance });

            var refund = SquadronRules.EntertainmentRefund(w, bal);
            if (refund > 0) lines.Add(new BudgetLine { Label = "유흥가 급여 환수", Amount = refund });

            var hexes = w.OwnedHexes(World.Player).Count();
            lines.Add(new BudgetLine { Label = $"영역 유지비 ({hexes}칸)", Amount = -bal.UpkeepPerHex * hexes });
            lines.Add(new BudgetLine { Label = $"파일럿 급여 ({w.Pilots.Count}명)", Amount = -SquadronRules.Salaries(w, bal) });
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
            // 적 턴은 플레이어 출격·수지 뒤, 편대 회복(SquadronRules.EndTurn) 전에: 이번 턴 출격한 편대는 요격 불가
            EnemyTurn.Run(w, bal);
            SquadronRules.EndTurn(w, bal);
            Vision.UpdateSightings(w, bal);
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
