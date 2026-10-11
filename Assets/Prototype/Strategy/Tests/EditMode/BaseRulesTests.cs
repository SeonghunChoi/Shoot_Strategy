using System.Linq;
using NUnit.Framework;
using Prototype.Strategy.Rules;

namespace Prototype.Strategy.Tests
{
    public class BaseRulesTests
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

        static Hex Off(int col, int row) => Hex.FromOffset(col, row);

        [Test]
        public void StartingSetup_MatchesPlan()
        {
            Assert.AreEqual(_bal.StartingFunds, _w.Funds);
            Assert.AreEqual(7, _w.TerritoryOf(_coast).Count());
            Assert.AreEqual(3, _w.TerritoryOf(_south).Count());
            Assert.AreEqual(2, BaseRules.RunwayLength(_w, _coast));
            Assert.AreEqual(1, BaseRules.RunwayLength(_w, _south));
            Assert.AreEqual(4, BaseRules.FacilityCount(_w, _coast)); // 활주로 2, 격납고, 정비소
        }

        [Test]
        public void RunwayLength_CountsOnlyStraightContiguousCells()
        {
            _w.Funds = 100000;
            // 해안 기지 활주로 (2,3)-(3,3) 동쪽 줄. 서쪽 (1,3)은 영역 밖이라 확장 후 건설
            BaseRules.Expand(_w, _bal, _coast, Off(1, 3));
            BaseRules.Build(_w, _bal, Off(1, 3), FacilityType.Runway);
            Assert.AreEqual(3, BaseRules.RunwayLength(_w, _coast));
            Assert.AreEqual(3, BaseRules.MaxAircraftSize(_bal, 3));

            // 직선이 아닌 곳에 붙여도 최장 직선은 그대로 (상한 5를 비우려고 정비소 철거)
            BaseRules.Demolish(_w, Off(3, 5));
            BaseRules.Build(_w, _bal, Off(2, 4), FacilityType.Runway);
            Assert.IsTrue(_w.Facilities.ContainsKey(Off(2, 4)));
            Assert.AreEqual(3, BaseRules.RunwayLength(_w, _coast));
        }

        [Test]
        public void ExpandCost_GrowsWithTerritory()
        {
            _w.Funds = 100000;
            var first = BaseRules.ExpandCost(_w, _bal, _coast);
            Assert.AreEqual(_bal.ExpandBaseCost + _bal.ExpandCostPerHex * 7, first);
            var target = BaseRules.ExpandCandidates(_w, _coast).First();
            BaseRules.Expand(_w, _bal, _coast, target);
            Assert.AreEqual(first + _bal.ExpandCostPerHex, BaseRules.ExpandCost(_w, _bal, _coast));
            Assert.AreEqual(100000 - first, _w.Funds);
        }

        [Test]
        public void Expand_RejectsNonAdjacentAndOwnedCells()
        {
            Assert.IsNotNull(BaseRules.CanExpand(_w, _bal, _coast, Off(10, 10)));
            Assert.IsNotNull(BaseRules.CanExpand(_w, _bal, _coast, Off(3, 3)));
            Assert.IsNull(BaseRules.CanExpand(_w, _bal, _coast, BaseRules.ExpandCandidates(_w, _coast).First()));
        }

        [Test]
        public void Build_RespectsCapGradeSiteAndHq()
        {
            _w.Funds = 100000;
            Assert.AreEqual("거점 본부 칸", BaseRules.CanBuild(_w, _bal, _south.Hex, FacilityType.Hangar));
            // 남부 전초기지(1등급)는 2등급 시설 불가
            BaseRules.Expand(_w, _bal, _south, Off(4, 8));
            Assert.AreEqual("거점 2등급 필요", BaseRules.CanBuild(_w, _bal, Off(4, 8), FacilityType.Maintenance));
            // 상한 3: 이미 2개 → 1개 더 짓고 나면 상한. 그다음부터는 할증 (+50%, +100% …)
            Assert.AreEqual(150, BaseRules.BuildCost(_w, _bal, _south, FacilityType.Barracks));
            BaseRules.Build(_w, _bal, Off(4, 8), FacilityType.Barracks);
            BaseRules.Expand(_w, _bal, _south, Off(4, 10));
            Assert.IsNull(BaseRules.CanBuild(_w, _bal, Off(4, 10), FacilityType.AntiAir));
            Assert.AreEqual(180, BaseRules.BuildCost(_w, _bal, _south, FacilityType.AntiAir)); // 120 × 1.5
            var funds = _w.Funds;
            BaseRules.Build(_w, _bal, Off(4, 10), FacilityType.AntiAir);
            Assert.AreEqual(funds - 180, _w.Funds);
            Assert.AreEqual(240, BaseRules.BuildCost(_w, _bal, _south, FacilityType.AntiAir)); // 두 번째 초과 × 2.0
            // 엄격 적용(원래 방식)이면 상한에서 막힌다
            _bal.HardFacilityCap = true;
            BaseRules.Expand(_w, _bal, _south, Off(5, 10));
            Assert.AreEqual("시설 상한", BaseRules.CanBuild(_w, _bal, Off(5, 10), FacilityType.AntiAir));
            _bal.HardFacilityCap = false;
            // 정유시설은 유전 칸만
            Assert.AreEqual("유전 칸 필요", BaseRules.CanBuild(_w, _bal, Off(2, 4), FacilityType.Refinery));
        }

        [Test]
        public void UpgradeBase_NeedsFacilitiesAndFunds()
        {
            _w.Funds = 100000;
            // 해안 기지 2→3: 정비소 있음, 레이더 없음
            StringAssert.Contains("레이더", BaseRules.CanUpgradeBase(_w, _bal, _coast));
            BaseRules.Build(_w, _bal, Off(2, 4), FacilityType.Radar);
            Assert.IsNull(BaseRules.CanUpgradeBase(_w, _bal, _coast));
            BaseRules.UpgradeBase(_w, _bal, _coast);
            Assert.AreEqual(3, _coast.Grade);
            Assert.AreEqual(8, BaseRules.FacilityCap(_bal, _coast));
        }

        [Test]
        public void Outpost_NeedsDistanceAndAirReach()
        {
            _w.Funds = 100000;
            Assert.AreEqual("거점 본부 칸", BaseRules.CanBuildOutpost(_w, _bal, _coast.Hex));
            StringAssert.Contains("떨어져야", BaseRules.CanBuildOutpost(_w, _bal, Off(2, 4))); // 인접 칸 불가
            // (7,4): 해안 기지에서 거리 4 — 급유 없는 반경 3 밖이지만 급유(×2 = 6) 포함 도달 가능
            var target = Off(7, 4);
            Assert.IsNull(BaseRules.CanBuildOutpost(_w, _bal, target));
            var cost = BaseRules.OutpostCost(_w, _bal, target);
            Assert.AreEqual(_bal.OutpostCost + _bal.OutpostCostPerDistance * 4, cost);
            var nb = BaseRules.BuildOutpost(_w, _bal, target);
            Assert.IsNotNull(nb);
            Assert.AreEqual(1, nb.Grade);
            Assert.AreEqual(nb.Id, _w.Territory[target]);
            Assert.AreEqual(100000 - cost, _w.Funds);
            // 아무 편대도 닿지 않는 먼 칸은 불가
            StringAssert.Contains("닿지", BaseRules.CanBuildOutpost(_w, _bal, Off(15, 11)));
        }

        [Test]
        public void FacilityUpgrade_IncreasesRefineryIncome()
        {
            _w.Funds = 100000;
            // 유전 (5,5)를 해안 기지 영역으로
            BaseRules.Expand(_w, _bal, _coast, Off(4, 5));
            BaseRules.Expand(_w, _bal, _coast, Off(5, 5));
            BaseRules.Build(_w, _bal, Off(5, 5), FacilityType.Refinery);
            Assert.AreEqual(80, Economy.Forecast(_w, _bal).Single(l => l.Label == "정유시설").Amount);
            BaseRules.UpgradeFacility(_w, _bal, Off(5, 5));
            Assert.AreEqual(160, Economy.Forecast(_w, _bal).Single(l => l.Label == "정유시설").Amount);
        }
    }

    public class EconomyTests
    {
        [Test]
        public void Finance_FlatPlusInterest_UpgradeRaisesNet()
        {
            var bal = new Balance();
            var w = World.Create(1, bal);
            var coast = w.Bases[0];
            var h = w.TerritoryOf(coast).First(x => x != coast.Hex && !w.Facilities.ContainsKey(x));
            var f = new Facility { Type = FacilityType.Finance, Grade = 2 };
            w.Facilities[h] = f;
            w.Funds = 2000;

            var lines = Economy.Forecast(w, bal);
            Assert.AreEqual(bal.FinanceFlatPerGrade * 2, lines.Single(l => l.Label == "금융 수입").Amount);
            Assert.AreEqual((int)(2000 * bal.FinanceRatePerGrade * 2), lines.Single(l => l.Label == "금융 이자").Amount);

            // 예전에는 자금 2000에서 2→3등급(비용 750)을 올리면 이자 120 → 112로 줄었다. 고정 수입으로 늘어나야 한다
            var before = Economy.Net(lines);
            BaseRules.UpgradeFacility(w, bal, h);
            Assert.AreEqual(3, f.Grade);
            Assert.Greater(Economy.Net(Economy.Forecast(w, bal)), before);
        }

        [Test]
        public void Forecast_IsBudgetMinusUpkeep()
        {
            var bal = new Balance();
            var w = World.Create(1, bal);
            // 영역 10칸 × 유지비, 급여 네임드 80 + 일반 40 × 3 = 200
            Assert.AreEqual(bal.HqBudget - 10 * bal.UpkeepPerHex - 200, Economy.Net(Economy.Forecast(w, bal)));
        }

        [Test]
        public void EndTurn_AppliesNetAndAdvances()
        {
            var bal = new Balance();
            var w = World.Create(1, bal);
            var net = Economy.Net(Economy.Forecast(w, bal));
            var report = Economy.EndTurn(w, bal);
            Assert.AreEqual(bal.StartingFunds + net, w.Funds);
            Assert.AreEqual(2, w.Turn);
            Assert.AreEqual(1, report.Turn);
        }

        [Test]
        public void Bankruptcy_AfterConsecutiveNegativeTurns()
        {
            var bal = new Balance { HqBudget = 0 };
            var w = World.Create(1, bal);
            w.Funds = 50; // 턴마다 −100

            Economy.EndTurn(w, bal); // −50 → 적자 1
            Assert.AreEqual(1, w.DeficitStreak);
            Economy.EndTurn(w, bal); // 적자 2
            Assert.IsNull(w.GameOver);
            Economy.EndTurn(w, bal); // 적자 3 → 파산
            Assert.IsNotNull(w.GameOver);
            Assert.IsNull(Economy.EndTurn(w, bal));
        }

        [Test]
        public void DeficitStreak_ResetsWhenFundsRecover()
        {
            var bal = new Balance();
            var w = World.Create(1, bal);
            w.Pilots.Clear();  // 급여 없음 → 순 +200
            w.Funds = -150;    // +200이면 0 이상으로 회복
            Economy.EndTurn(w, bal);
            Assert.AreEqual(0, w.DeficitStreak);
        }
    }
}
