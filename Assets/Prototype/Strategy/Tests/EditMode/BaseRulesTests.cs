using System.Linq;
using NUnit.Framework;
using Prototype.Strategy.Rules;

namespace Prototype.Strategy.Tests
{
    /// <summary>도시판 거점: 거점 = 칸 하나, 시설은 목록, 영역은 등급에 따라 자동.</summary>
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
            // 1~2등급 거점은 반경 1 자동 영역
            Assert.AreEqual(7, _w.TerritoryOf(_coast).Count());
            Assert.AreEqual(7, _w.TerritoryOf(_south).Count());
            Assert.AreEqual(2, BaseRules.MaxAircraftSize(_coast));
            Assert.AreEqual(1, BaseRules.MaxAircraftSize(_south));
            Assert.AreEqual(4, BaseRules.FacilityPoints(_coast)); // 활주로 2 + 격납고 1 + 정비소 1
            Assert.AreEqual(2, SquadronRules.HangarCapacity(_w, _bal, _coast));
            Assert.AreEqual(6, _w.Sites.Count);
            Assert.AreEqual(4, _w.Sites.Count(s => s.Owner == ResourceSite.Neutral));
        }

        [Test]
        public void Build_UniquePerBase_GradeUnlock_SoftCap()
        {
            _w.Funds = 100000;
            // 남부 전초기지(1등급): 2등급 시설 불가, 같은 시설은 하나만
            Assert.AreEqual("거점 2등급 필요", BaseRules.CanBuild(_w, _bal, _south, FacilityType.Maintenance));
            StringAssert.Contains("이미 있음", BaseRules.CanBuild(_w, _bal, _south, FacilityType.Hangar));
            // 상한 3, 규모 2 → 하나 더는 할증 없음, 그다음부터 +50%, +100% …
            Assert.AreEqual(150, BaseRules.BuildCost(_w, _bal, _south, FacilityType.Barracks));
            BaseRules.Build(_w, _bal, _south, FacilityType.Barracks);
            Assert.AreEqual(3, BaseRules.FacilityPoints(_south));
            Assert.AreEqual(180, BaseRules.BuildCost(_w, _bal, _south, FacilityType.AntiAir)); // 120 × 1.5
            var funds = _w.Funds;
            BaseRules.Build(_w, _bal, _south, FacilityType.AntiAir);
            Assert.AreEqual(funds - 180, _w.Funds);
            Assert.AreEqual(4, BaseRules.FacilityPoints(_south));
            // 등급 상승도 규모를 늘리므로 할증: 병영 150 × 2 × 0.5 × (1 + 0.5 × 2)
            Assert.AreEqual(300, BaseRules.FacilityUpgradeCost(_w, _bal, _south, BaseRules.Get(_south, FacilityType.Barracks)));
            // 엄격 적용이면 상한에서 막힌다
            _bal.HardFacilityCap = true;
            Assert.AreEqual("시설 상한", BaseRules.CanUpgradeFacility(_w, _bal, _south, FacilityType.Barracks));
        }

        [Test]
        public void Runway_GradeIsAircraftSize()
        {
            _w.Funds = 100000;
            StringAssert.Contains("활주로 3등급 필요", SquadronRules.CanOperate(_w, _bal, _coast, AircraftType.B57));
            BaseRules.UpgradeFacility(_w, _bal, _coast, FacilityType.Runway);
            Assert.AreEqual(3, BaseRules.MaxAircraftSize(_coast));
            Assert.AreEqual("최고 등급", BaseRules.CanUpgradeFacility(_w, _bal, _coast, FacilityType.Runway));
            // 격납고 등급 = 편대 2 × 등급
            BaseRules.UpgradeFacility(_w, _bal, _coast, FacilityType.Hangar);
            Assert.AreEqual(4, SquadronRules.HangarCapacity(_w, _bal, _coast));
            BaseRules.Demolish(_w, _coast, FacilityType.Hangar);
            Assert.AreEqual(0, SquadronRules.HangarCapacity(_w, _bal, _coast));
        }

        [Test]
        public void UpgradeBase_WidensTerritoryAtGrade3()
        {
            _w.Funds = 100000;
            StringAssert.Contains("레이더", BaseRules.CanUpgradeBase(_w, _bal, _coast));
            BaseRules.Build(_w, _bal, _coast, FacilityType.Radar);
            var before = _w.TerritoryOf(_coast).Count();
            BaseRules.UpgradeBase(_w, _bal, _coast);
            Assert.AreEqual(3, _coast.Grade);
            Assert.AreEqual(8, BaseRules.FacilityCap(_bal, _coast));
            Assert.Greater(_w.TerritoryOf(_coast).Count(), before);
            Assert.IsTrue(_w.TerritoryOf(_coast).All(h => Hex.Distance(h, _coast.Hex) <= 2));
        }

        [Test]
        public void TrimToCap_CutsLatestFacilitiesFirst()
        {
            _w.Funds = 100000;
            BaseRules.Build(_w, _bal, _south, FacilityType.Barracks);
            BaseRules.Build(_w, _bal, _south, FacilityType.AntiAir);
            BaseRules.UpgradeFacility(_w, _bal, _south, FacilityType.AntiAir);
            Assert.AreEqual(5, BaseRules.FacilityPoints(_south)); // 상한 3 초과
            var cut = BaseRules.TrimToCap(_bal, _south);
            Assert.AreEqual(2, cut);
            Assert.AreEqual(3, BaseRules.FacilityPoints(_south));
            Assert.IsFalse(BaseRules.Has(_w, _south, FacilityType.AntiAir)); // 마지막에 지은 대공포부터
            Assert.IsTrue(BaseRules.Has(_w, _south, FacilityType.Hangar));
        }

        [Test]
        public void Outpost_NeedsDistanceAndAirReach()
        {
            _w.Funds = 100000;
            Assert.AreEqual("거점 본부 칸", BaseRules.CanBuildOutpost(_w, _bal, _coast.Hex));
            StringAssert.Contains("떨어져야", BaseRules.CanBuildOutpost(_w, _bal, Off(2, 4)));
            Assert.AreEqual("자원 타일", BaseRules.CanBuildOutpost(_w, _bal, _w.Sites.First(s => s.Name == "중앙 도시").Hex));
            var target = Off(7, 4);
            Assert.IsNull(BaseRules.CanBuildOutpost(_w, _bal, target));
            var cost = BaseRules.OutpostCost(_w, _bal, target);
            Assert.AreEqual(_bal.OutpostCost + _bal.OutpostCostPerDistance * 4, cost);
            var nb = BaseRules.BuildOutpost(_w, _bal, target);
            Assert.IsNotNull(nb);
            Assert.AreEqual(nb.Id, _w.Territory[target]);
            Assert.AreEqual(7, _w.TerritoryOf(nb).Count(h => _w.Map.Contains(h)));
            Assert.AreEqual(100000 - cost, _w.Funds);
            StringAssert.Contains("닿지", BaseRules.CanBuildOutpost(_w, _bal, Off(15, 11)));
        }
    }

    /// <summary>자원 타일: 수입·등급·점령(아군·적)·폭격.</summary>
    public class SiteTests
    {
        Balance _bal;
        World _w;

        [SetUp]
        public void SetUp()
        {
            _bal = new Balance { VisionEnabled = false, DirectiveInterval = 1000 };
            _w = World.Create(1, _bal);
            _w.Funds = 100000;
        }

        ResourceSite Site(string name) => _w.Sites.Single(s => s.Name == name);

        [Test]
        public void OwnedSite_GivesIncome_UpgradeRaisesIt()
        {
            var oil = Site("서부 유전");
            Assert.IsFalse(Economy.Forecast(_w, _bal).Any(l => l.Label.StartsWith("정유")));
            Assert.AreEqual("아군 소유가 아님", SiteRules.CanUpgrade(_w, _bal, oil));
            SiteRules.Capture(_w, oil, World.Player, "시험");
            Assert.AreEqual(80, Economy.Forecast(_w, _bal).Single(l => l.Label.StartsWith("정유")).Amount);
            Assert.AreEqual(300, SiteRules.UpgradeCost(_bal, oil)); // 300 × 2 × 0.5
            SiteRules.Upgrade(_w, _bal, oil);
            Assert.AreEqual(160, Economy.Forecast(_w, _bal).Single(l => l.Label.StartsWith("정유")).Amount);
        }

        [Test]
        public void GroundUnit_EnteringSite_CapturesIt()
        {
            var city = Site("남부 마을");
            var g = new GroundUnit { Id = _w.NextId++, Name = "지상군 1", BaseId = _w.Bases[1].Id, Hex = city.Hex.Neighbor(3), Strength = 20, Deployed = true, Target = city.Hex };
            _w.GroundUnits.Add(g);
            GroundRules.MovePhase(_w, _bal);
            Assert.AreEqual(city.Hex, g.Hex);
            Assert.AreEqual(World.Player, city.Owner);
        }

        [Test]
        public void EnemyUnit_GoesForValuableSite_AndCapturesIt()
        {
            var mine = Site("중부 광산");
            mine.Grade = 3;
            foreach (var u in _w.EnemyUnits.Skip(1).ToList()) _w.EnemyUnits.Remove(u);
            var a = _w.EnemyUnits[0];
            a.Hex = mine.Hex.Neighbor(0);
            Economy.EndTurn(_w, _bal);
            Assert.AreEqual(a.Owner, mine.Owner);
            Assert.AreEqual(mine.Hex, a.Hex);
        }

        [Test]
        public void Bombing_BreaksStructures_DropsGrade()
        {
            _bal.SiteStructurePerGrade = 2;
            _bal.SiteAntiAirPerGrade = 1;
            var oil = Site("동부 유전");
            Assert.AreEqual(MissionType.SiteBombing, SortieRules.MissionAt(_w, oil.Hex));
            var rep = SortieRules.Resolve(_w, _bal, _w.Squadrons[0], oil.Hex);
            Assert.AreEqual(1, oil.Grade);
            Assert.IsTrue(rep.OverEnemyTerritory);
            StringAssert.Contains("등급 2 → 1", rep.Effect);
            // 0등급이 되면 중립 폐허, 더는 폭격 목표가 아님
            SiteRules.Damage(_w, oil);
            Assert.AreEqual(ResourceSite.Neutral, oil.Owner);
            Assert.IsNull(SortieRules.MissionAt(_w, oil.Hex));
        }

        [Test]
        public void EnemySites_SpeedUpReinforcements()
        {
            // 동부군은 동부 유전(2등급)을 가지고 시작 → 6 − 1 = 5턴마다 보충
            var east = _w.Factions.First(f => f.Id == 1);
            _w.EnemyUnits.Clear(); // 부대가 다른 타일을 점령해 주기가 더 줄지 않게
            for (var i = 0; i < 4; i++) Economy.EndTurn(_w, _bal);
            Assert.AreEqual(4, east.ReinforceTimer);
            Economy.EndTurn(_w, _bal);
            Assert.AreEqual(0, east.ReinforceTimer);
        }
    }

    public class EconomyTests
    {
        [Test]
        public void Forecast_IsBudgetMinusUpkeep()
        {
            var bal = new Balance();
            var w = World.Create(1, bal);
            // 거점 유지비 = 등급 합 3 × 25, 급여 네임드 80 + 일반 40 × 3 = 200
            Assert.AreEqual(bal.HqBudget - 3 * bal.UpkeepPerBaseGrade - 200, Economy.Net(Economy.Forecast(w, bal)));
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
            w.Funds = 50;

            Economy.EndTurn(w, bal);
            Assert.AreEqual(1, w.DeficitStreak);
            Economy.EndTurn(w, bal);
            Assert.IsNull(w.GameOver);
            Economy.EndTurn(w, bal);
            Assert.IsNotNull(w.GameOver);
            Assert.IsNull(Economy.EndTurn(w, bal));
        }

        [Test]
        public void DeficitStreak_ResetsWhenFundsRecover()
        {
            var bal = new Balance();
            var w = World.Create(1, bal);
            w.Pilots.Clear();
            w.Funds = -150;
            Economy.EndTurn(w, bal);
            Assert.AreEqual(0, w.DeficitStreak);
        }
    }
}
