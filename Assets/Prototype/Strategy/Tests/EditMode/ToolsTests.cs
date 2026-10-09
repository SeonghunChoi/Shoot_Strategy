using System.IO;
using System.Linq;
using NUnit.Framework;
using Prototype.Strategy.Rules;

namespace Prototype.Strategy.Tests
{
    public class ToolsTests
    {
        [Test]
        public void Tuning_ItemsResetAndExport()
        {
            var bal = new Balance();
            var items = Tuning.Items(bal);
            Assert.Greater(items.Count, 80);
            Assert.IsTrue(items.Any(i => i.Group == "기체 F-5" && i.Label == "작전 반경"));
            Assert.AreEqual(0, Tuning.ChangedCount(bal));

            bal.BankruptTurns = 5;
            bal.Def(AircraftType.F5).Range = 6;
            Assert.AreEqual(2, Tuning.ChangedCount(bal));
            var md = Tuning.ToMarkdown(bal, "테스터", null);
            StringAssert.Contains("**5**", md);
            StringAssert.Contains("## 기체 F-5", md);

            Tuning.ResetToDefaults(bal);
            Assert.AreEqual(0, Tuning.ChangedCount(bal));
            Assert.AreEqual(3, bal.Def(AircraftType.F5).Range);

            // 내보낸 Markdown을 다시 가져오면 같은 값
            var other = new Balance();
            var (applied, unknown) = Tuning.FromMarkdown(other, md);
            Assert.IsEmpty(unknown);
            Assert.AreEqual(Tuning.Items(other).Count, applied);
            Assert.AreEqual(5, other.BankruptTurns);
            Assert.AreEqual(6, other.Def(AircraftType.F5).Range);
            Assert.AreEqual(2, Tuning.ChangedCount(other));

            // 모르는 항목은 건너뛰고 알려 준다
            var (_, unknown2) = Tuning.FromMarkdown(new Balance(), "## 경제\n| 없는 항목 | 1 | 1 |\n| 시작 자금 | 2000 | 1000 |");
            Assert.AreEqual(1, unknown2.Count);
        }

        [Test]
        public void PlaytestLog_WritesRunsTurnsEvents()
        {
            var dir = Path.Combine(Path.GetTempPath(), "strategy_log_test_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var bal = new Balance();
                var w = World.Create(1, bal);
                var log = new PlaytestLog(dir) { Tester = "테스트" };
                log.BeginRun(w, 1);
                w.Log("시험", "사건, 쉼표 포함");
                var rep = Economy.EndTurn(w, bal);
                log.LogTurn(w, rep, 12.5f, 1);
                log.EndRun(w, bal, "중단");

                var runs = File.ReadAllLines(Path.Combine(dir, "runs.csv"));
                var turns = File.ReadAllLines(Path.Combine(dir, "turns.csv"));
                var events = File.ReadAllLines(Path.Combine(dir, "events.csv"));
                Assert.AreEqual(2, runs.Length);
                StringAssert.Contains("중단", runs[1]);
                StringAssert.Contains(",12.5,", turns[1]);
                Assert.IsTrue(events.Any(e => e.Contains("\"사건, 쉼표 포함\"")));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void Sim_IsDeterministicAndBotsFinish()
        {
            var a = SimRunner.Run(new SimpleBot(), 7, 40);
            var b = SimRunner.Run(new SimpleBot(), 7, 40);
            Assert.AreEqual(a.Result, b.Result);
            Assert.AreEqual(a.Turns, b.Turns);
            Assert.AreEqual(a.FinalFunds, b.FinalFunds);

            var passive = SimRunner.Run(new PassiveBot(), 7, 40);
            Assert.AreEqual(0, passive.Stats.Sorties);
            Assert.LessOrEqual(passive.Turns, 41);
        }

        [Test]
        public void Sim_BatchWritesSummary()
        {
            var dir = Path.Combine(Path.GetTempPath(), "strategy_sim_test_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var md = SimRunner.RunBatch(dir, seeds: 3, maxTurns: 20, bankruptTurns: new[] { 2, 3 });
                StringAssert.Contains("| 수동형 | 2턴 |", md);
                StringAssert.Contains("| 단순형 | 3턴 |", md);
                Assert.AreEqual(1, Directory.GetFiles(dir, "sim_*.csv").Length);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
