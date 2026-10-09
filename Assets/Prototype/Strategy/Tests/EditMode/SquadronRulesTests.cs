using System.Linq;
using NUnit.Framework;
using Prototype.Strategy.Rules;

namespace Prototype.Strategy.Tests
{
    public class SquadronRulesTests
    {
        Balance _bal;
        World _w;
        Base _coast;
        Base _south;

        [SetUp]
        public void SetUp()
        {
            _bal = new Balance();
            _w = World.Create(1, _bal);
            _coast = _w.Bases[0];
            _south = _w.Bases[1];
        }

        [Test]
        public void StartingSquadrons_MatchPlan()
        {
            Assert.AreEqual(2, _w.Squadrons.Count);
            Assert.AreEqual(3, _w.Aircraft.Count);
            Assert.AreEqual(4, _w.Pilots.Count);
            Assert.AreEqual(1, _w.IdlePilots.Count());
            Assert.AreEqual(2, SquadronRules.HangarCapacity(_w, _bal, _coast));
            Assert.AreEqual(200, SquadronRules.Salaries(_w, _bal));
        }

        [Test]
        public void CanOperate_ChecksRunwayAndMaintenance()
        {
            // 해안 기지: 활주로 2, 정비소 1등급 → 소형/중형 크기 OK, 티어 2 불가
            Assert.IsNull(SquadronRules.CanOperate(_w, _bal, _coast, AircraftType.F5));
            StringAssert.Contains("정비소", SquadronRules.CanOperate(_w, _bal, _coast, AircraftType.F4));
            // 남부: 활주로 1 → 중형 불가
            StringAssert.Contains("활주로", SquadronRules.CanOperate(_w, _bal, _south, AircraftType.F4));
        }

        [Test]
        public void CreateSquadron_LimitedByHangars()
        {
            Assert.AreEqual("격납고 부족", SquadronRules.CanCreateSquadron(_w, _bal, _coast));
            Assert.IsNull(SquadronRules.CanCreateSquadron(_w, _bal, _south));
            var s = SquadronRules.CreateSquadron(_w, _bal, _south);
            Assert.IsNotNull(s);
            Assert.AreEqual(_south.Id, s.BaseId);
        }

        [Test]
        public void Move_IsImmediateAndConsumesTurn()
        {
            var s = _w.Squadrons[1]; // 2편대: F-5 1기
            var pilot = _w.PilotById(_w.AircraftById(s.AircraftIds[0]).PilotId);
            Assert.IsNull(SquadronRules.CanMove(_w, _bal, s, _south));
            SquadronRules.Move(_w, _bal, s, _south);
            Assert.AreEqual(_south.Id, s.BaseId);
            Assert.IsTrue(s.MovedThisTurn);
            Assert.AreEqual(10, pilot.Fatigue);
            Assert.AreEqual("이번 턴 재배치함", SortieRules.CanSortie(_w, _bal, s));
            Assert.AreEqual("이번 턴 이미 재배치함", SquadronRules.CanMove(_w, _bal, s, _coast));
            _bal.VisionEnabled = false;
            Economy.EndTurn(_w, _bal);
            Assert.IsFalse(s.MovedThisTurn);
            Assert.AreEqual(_south.Id, s.BaseId);
        }

        [Test]
        public void BuyAndSell_AdjustFunds()
        {
            _w.Funds = 1000;
            var a = SquadronRules.Buy(_w, _bal, AircraftType.A4);
            Assert.AreEqual(550, _w.Funds);
            a.Damage = 50;
            Assert.AreEqual(112, SquadronRules.SellPrice(_bal, a)); // 450 × 0.5 × 0.5
            SquadronRules.Sell(_w, _bal, a);
            Assert.AreEqual(662, _w.Funds);
            Assert.IsFalse(_w.Aircraft.Contains(a));
        }

        [Test]
        public void Wear_RecoversWithMaintenanceAndCostsRepair()
        {
            var s = _w.Squadrons[0]; // 해안 기지 (정비소 1등급 → 15%/턴)
            SquadronRules.SimulateWear(_w, s, damage: 50, fatigue: 80);
            var a = _w.AircraftById(s.AircraftIds[0]);
            var p = _w.PilotById(a.PilotId);
            Assert.IsTrue(SquadronRules.IsExhausted(_bal, p));
            Assert.AreEqual("탈진", SquadronRules.CannotSortie(_w, _bal, a));

            var (percent, cost) = SquadronRules.RepairForecast(_w, _bal);
            Assert.AreEqual(30, percent); // 2기 × 15
            Assert.Greater(cost, 0);
            var fundsBefore = _w.Funds;
            var report = Economy.EndTurn(_w, _bal);
            Assert.AreEqual(35, a.Damage);
            Assert.AreEqual(60, p.Fatigue);
            Assert.IsTrue(report.Lines.Any(l => l.Label.StartsWith("기체 수리")));
            Assert.AreEqual(fundsBefore - cost, _w.Funds); // 순 0 + 수리비
        }

        [Test]
        public void Entertainment_RefundsSalaryAndSpeedsRecovery()
        {
            _w.Funds = 100000;
            BaseRules.Build(_w, _bal, Hex.FromOffset(2, 4), FacilityType.Entertainment);
            // 해안 기지 주둔 파일럿: 네임드 80 + 일반 40 ×2 = 160 × 15% = 24
            Assert.AreEqual(24, SquadronRules.EntertainmentRefund(_w, _bal));
            var p = _w.PilotById(_w.AircraftById(_w.Squadrons[0].AircraftIds[0]).PilotId);
            p.Fatigue = 50;
            Economy.EndTurn(_w, _bal);
            Assert.AreEqual(20, p.Fatigue); // 50 − 20 − 10
        }
    }
}

namespace Prototype.Strategy.Tests
{
    public class SquadronOrderTests
    {
        [Test]
        public void ReorderAircraft_SwapsWithinSquadron()
        {
            var bal = new Balance();
            var w = World.Create(1, bal);
            var s = w.Squadrons[0];
            var first = w.AircraftById(s.AircraftIds[0]);
            var second = w.AircraftById(s.AircraftIds[1]);
            SquadronRules.ReorderAircraft(s, second, -1);
            Assert.AreEqual(second.Id, s.AircraftIds[0]);
            Assert.AreEqual(first.Id, s.AircraftIds[1]);
            SquadronRules.ReorderAircraft(s, second, -1); // 맨 앞에서는 변화 없음
            Assert.AreEqual(second.Id, s.AircraftIds[0]);
        }
    }
}

namespace Prototype.Strategy.Tests
{
    public class HireTests
    {
        [Test]
        public void Candidates_HireChosenOneAndRefresh()
        {
            var bal = new Balance();
            var w = World.Create(3, bal);
            Assert.AreEqual(3, w.HireCandidates.Count);
            Assert.IsTrue(w.HireCandidates.All(c => c.Skill >= 1 && c.Skill <= 3 && c.Id == 0));

            var pick = w.HireCandidates.OrderByDescending(c => c.Skill).First();
            var cost = SquadronRules.HireCost(bal, pick);
            Assert.AreEqual(bal.HirePilotCost + (pick.Skill - 1) * bal.HireCostPerSkill, cost);
            var funds = w.Funds;
            var hired = SquadronRules.Hire(w, bal, pick);
            Assert.AreSame(pick, hired);
            Assert.AreNotEqual(0, hired.Id);
            Assert.Contains(hired, w.Pilots);
            Assert.AreEqual(2, w.HireCandidates.Count);
            Assert.AreEqual(funds - cost, w.Funds);
            Assert.AreEqual("후보가 아님", SquadronRules.CanHire(w, bal, hired));

            // 교체 주기 2턴: 2턴째를 마치면 새 후보 3명
            bal.VisionEnabled = false;
            Economy.EndTurn(w, bal);
            Assert.AreEqual(2, w.HireCandidates.Count);
            Economy.EndTurn(w, bal);
            Assert.AreEqual(3, w.HireCandidates.Count);
        }
    }
}
