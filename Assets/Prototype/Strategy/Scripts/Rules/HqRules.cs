using System;
using System.Linq;

namespace Prototype.Strategy.Rules
{
    public enum DirectiveType
    {
        DestroyUnit, // 적 부대 X 격멸
        HoldBase,    // 거점 Y 방어 (기한까지 상실 안 함)
        UpgradeBase, // 거점 등급 올리기 (도시판, 영역 확대 대신)
        SecureSite,  // 자원 타일 확보
    }

    /// <summary>상부 긴급 지시 (기획서 8.6).</summary>
    public sealed class Directive
    {
        public DirectiveType Type;
        public int TargetUnitId;
        public string TargetName;
        public int TargetBaseId;
        /// <summary>지시 시작 때의 아군 거점 등급 합 / 소유 자원 타일 수.</summary>
        public int StartCount;
        public int Deadline; // 이 턴을 마칠 때까지
        public string Text;
    }

    public sealed class GameStats
    {
        public int Sorties;
        public int BasesAttacked;
        public int BasesLost;
        public int BasesCaptured;
        public int DirectivesDone;
        public int DirectivesFailed;
        public int DirectivesRefused;
    }

    /// <summary>
    /// 상부 신뢰도·지시 임무·승패 (기획서 8.6, 10장 축소판). 무상태 서비스.
    /// 신뢰도는 명령 이행과 영역 확대로만 오른다. 0이면 해임.
    /// </summary>
    public static class HqRules
    {
        public static int Budget(World w, Balance bal) =>
            (int)Math.Round(bal.HqBudget * (bal.BudgetBaseRatio + w.Trust / 100f));

        public static void AddTrust(World w, Balance bal, int amount, string reason)
        {
            if (amount == 0) return;
            var before = w.Trust;
            w.Trust = Math.Clamp(w.Trust + amount, 0, 100);
            w.TrustLog.Add($"턴 {w.Turn}: {reason} {amount:+#;-#} ({before} → {w.Trust})");
            w.EnemyEvents.Add($"상부 신뢰도 {amount:+#;-#}: {reason}");
            if (w.Trust <= 0 && w.GameOver == null) w.GameOver = "해임 — 상부 신뢰도 0";
        }

        public static void Win(World w, Balance bal, Base goal)
        {
            AddTrust(w, bal, bal.TrustGoal, $"장기 목표 달성 ({goal.Name})");
            w.Victory = true;
            w.GameOver = $"승리 — {goal.Name} 점령";
        }

        public static string GoalText(World w)
        {
            var b = w.Bases.FirstOrDefault(x => x.Id == w.GoalBaseId);
            return b == null ? "—" : $"{b.Name} 점령{(b.Owner == World.Player ? " (달성)" : "")}";
        }

        // ---- 긴급 지시 ----

        public static void Refuse(World w, Balance bal)
        {
            if (w.Directive == null) return;
            w.Stats.DirectivesRefused++;
            AddTrust(w, bal, -bal.TrustRefuse, $"지시 거부: {w.Directive.Text}");
            w.Log("지시 거부", w.Directive.Text);
            w.Directive = null;
        }

        /// <summary>턴 종료 (적 턴 뒤): 진행 중 지시 판정 → 새 지시 발생.</summary>
        public static void EndTurn(World w, Balance bal)
        {
            if (w.GameOver != null) return;
            var d = w.Directive;
            if (d != null)
            {
                var result = Evaluate(w, d);
                if (result == true)
                {
                    w.Stats.DirectivesDone++;
                    AddTrust(w, bal, bal.TrustDirectiveSuccess, $"지시 완수: {d.Text}");
                    w.Log("지시 완수", d.Text);
                    w.Directive = null;
                }
                else if (result == false || w.Turn >= d.Deadline)
                {
                    w.Stats.DirectivesFailed++;
                    AddTrust(w, bal, -bal.TrustDirectiveFail, $"지시 실패: {d.Text}");
                    w.Log("지시 실패", d.Text);
                    w.Directive = null;
                }
            }

            if (w.Directive == null && w.GameOver == null)
            {
                w.DirectiveTimer++;
                if (w.DirectiveTimer >= bal.DirectiveInterval)
                {
                    w.DirectiveTimer = 0;
                    Issue(w, bal);
                }
            }
        }

        /// <summary>true 성공, false 즉시 실패, null 진행 중 (기한 도달 시 HoldBase는 성공).</summary>
        static bool? Evaluate(World w, Directive d)
        {
            switch (d.Type)
            {
                case DirectiveType.DestroyUnit:
                    return w.EnemyUnits.Any(u => u.Id == d.TargetUnitId) ? (bool?)null : true;
                case DirectiveType.HoldBase:
                    if (w.BaseById(d.TargetBaseId).Owner != World.Player) return false;
                    return w.Turn >= d.Deadline ? true : null;
                case DirectiveType.UpgradeBase:
                    return BaseGradeSum(w) > d.StartCount ? true : null;
                case DirectiveType.SecureSite:
                    return SiteRules.OwnedBy(w, World.Player).Count() > d.StartCount ? true : null;
            }
            return null;
        }

        static int BaseGradeSum(World w) => w.Bases.Where(b => b.Owner == World.Player).Sum(b => b.Grade);

        public static Directive Issue(World w, Balance bal)
        {
            var deadline = w.Turn + bal.DirectiveDeadline;
            var kind = w.Rng.Next(4);
            Directive d = null;
            if (kind == 0)
            {
                // 아군 거점에 가장 가까운 적 부대
                var unit = w.EnemyUnits.OrderBy(u => w.Bases.Where(b => b.Owner == World.Player)
                    .Select(b => Hex.Distance(b.Hex, u.Hex)).DefaultIfEmpty(99).Min()).FirstOrDefault();
                if (unit != null)
                    d = new Directive { Type = DirectiveType.DestroyUnit, TargetUnitId = unit.Id, TargetName = unit.Name, Text = $"{unit.Name} 격멸" };
            }
            else if (kind == 1)
            {
                // 적에게 가장 가까운 아군 거점
                var b = w.Bases.Where(x => x.Owner == World.Player).OrderBy(x => w.EnemyUnits
                    .Select(u => Hex.Distance(u.Hex, x.Hex)).DefaultIfEmpty(99).Min()).FirstOrDefault();
                if (b != null)
                    d = new Directive { Type = DirectiveType.HoldBase, TargetBaseId = b.Id, TargetName = b.Name, Text = $"{b.Name} 방어" };
            }
            else if (kind == 2 && w.Sites.Any(s => s.Owner != World.Player))
            {
                d = new Directive { Type = DirectiveType.SecureSite, StartCount = SiteRules.OwnedBy(w, World.Player).Count(), Text = "자원 타일 1곳 추가 확보" };
            }
            d ??= new Directive { Type = DirectiveType.UpgradeBase, StartCount = BaseGradeSum(w), Text = "아군 거점 등급 올리기" };
            d.Deadline = deadline;
            d.Text += $" (턴 {deadline}까지)";
            w.Directive = d;
            w.EnemyEvents.Add($"상부 긴급 지시: {d.Text}");
            w.Log("지시", d.Text);
            return d;
        }
    }
}
