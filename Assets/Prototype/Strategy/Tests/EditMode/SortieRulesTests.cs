using System.Linq;
using NUnit.Framework;
using Prototype.Strategy.Rules;

namespace Prototype.Strategy.Tests
{
    public class SortieRulesTests
    {
        Balance _bal;
        World _w;
        Squadron _s1;
        Hex _unitA;

        [SetUp]
        public void SetUp()
        {
            _bal = new Balance { VisionEnabled = false }; // 출격 규칙만 시험 (시야는 EnemyTurnTests)
            _w = World.Create(1, _bal);
            _s1 = _w.Squadrons[0];
            _unitA = _w.EnemyUnits[0].Hex;
        }

        [Test]
        public void Targets_AreEnemiesWithinRadius()
        {
            // 해안 기지 (3,4) 반경 3: 적 A부대 (6,3)는 거리 4, B부대 (7,8)는 거리 6 → 반경 확장 전에는 없음
            Assert.IsEmpty(SortieRules.Targets(_w, _bal, _s1));
            Assert.AreEqual((4, "동부군 A부대"), SortieRules.NearestTarget(_w, _s1));
            Assert.AreEqual("작전 반경 밖", SortieRules.CanPlan(_w, _bal, _s1, _unitA));
            // 공중급유: 반경 3 × 2 = 6 → 닿음, 비용 두 배
            _s1.Refuel = true;
            Assert.AreEqual(6, SquadronRules.OperationRadius(_w, _bal, _s1));
            CollectionAssert.Contains(SortieRules.Targets(_w, _bal, _s1).ToList(), _unitA);
            Assert.AreEqual(120, SortieRules.Cost(_w, _bal, _s1));
            _s1.Refuel = false;
            // 보급창고 +1 → 4
            _w.Funds = 100000;
            BaseRules.Build(_w, _bal, _w.Bases[0], FacilityType.Depot);
            Assert.AreEqual(4, SquadronRules.OperationRadius(_w, _bal, _s1));
            CollectionAssert.Contains(SortieRules.Targets(_w, _bal, _s1).ToList(), _unitA);
        }

        [Test]
        public void Cost_DependsOnSizeAndDepot()
        {
            // 1편대: F-5(소형 30) + A-4(소형 30) = 60
            Assert.AreEqual(60, SortieRules.Cost(_w, _bal, _s1));
            _w.Funds = 100000;
            BaseRules.Build(_w, _bal, _w.Bases[0], FacilityType.Depot);
            Assert.AreEqual(48, SortieRules.Cost(_w, _bal, _s1));
        }

        [Test]
        public void Plan_OneSquadronPerTarget()
        {
            foreach (var d in _bal.Aircraft.Values) d.Range = 6;
            SortieRules.Plan(_w, _bal, _s1, _unitA);
            Assert.AreEqual(1, _w.SortiePlan.Count);
            var s2 = _w.Squadrons[1];
            StringAssert.Contains("이미", SortieRules.CanPlan(_w, _bal, s2, _unitA));
            Assert.AreEqual("이미 계획됨", SortieRules.CanSortie(_w, _bal, _s1));
        }

        [Test]
        public void Resolve_IsDeterministicPerSeed()
        {
            var a = Run(7);
            var b = Run(7);
            var c = Run(8);
            Assert.AreEqual(a.DestructionRate, b.DestructionRate);
            CollectionAssert.AreEqual(a.Log, b.Log);
            Assert.AreNotEqual(string.Join("|", a.Log), string.Join("|", c.Log));
        }

        SortieReport Run(ulong seed)
        {
            var bal = new Balance { VisionEnabled = false };
            foreach (var d in bal.Aircraft.Values) d.Range = 6;
            var w = World.Create(seed, bal);
            return SortieRules.Resolve(w, bal, w.Squadrons[0], w.EnemyUnits[0].Hex);
        }

        [Test]
        public void Resolve_ReducesEnemyAndPaysReward()
        {
            foreach (var d in _bal.Aircraft.Values) d.Range = 6;
            var unit = _w.EnemyUnits[0];
            var before = unit.Strength;
            var funds = _w.Funds;
            var r = SortieRules.Resolve(_w, _bal, _s1, _unitA);
            Assert.Greater(r.DestructionRate, 0f);
            Assert.IsTrue(_w.EnemyUnits.All(u => u.Id != unit.Id) || unit.Strength < before);
            Assert.AreEqual(funds - r.Cost + r.Reward, _w.Funds);
            Assert.IsTrue(_s1.SortiedThisTurn);
            Assert.IsTrue(r.Aircraft.All(o => o.Rounds >= 0));
            var pilot = _w.PilotById(_w.AircraftById(_s1.AircraftIds[0]).PilotId);
            Assert.AreEqual(30, pilot.Fatigue);
        }

        [Test]
        public void Xp_BasePlusCombatPlusShare()
        {
            foreach (var d in _bal.Aircraft.Values) d.Range = 6;
            var r = SortieRules.Resolve(_w, _bal, _s1, _unitA);
            var total = r.Aircraft.Sum(o => o.XpCombat);
            foreach (var o in r.Aircraft)
            {
                Assert.AreEqual(_bal.SortieBaseXp, o.XpBase);
                Assert.AreEqual(o.Rounds * _bal.XpPerRound, o.XpCombat);
                Assert.AreEqual((int)System.Math.Round((total - o.XpCombat) * _bal.SortieXpShare), o.XpShare);
                Assert.AreEqual(o.XpBase + o.XpCombat + o.XpShare, o.XpGain);
            }
            // 교전하지 않은 기체도 기본 경험치는 받는다
            Assert.IsTrue(r.Aircraft.All(o => o.XpGain >= _bal.SortieBaseXp));
        }

        [Test]
        public void Refuel_IsConsumedBySortie()
        {
            _s1.Refuel = true;
            SortieRules.Resolve(_w, _bal, _s1, _unitA);
            Assert.IsFalse(_s1.Refuel);
        }

        [Test]
        public void ShotDown_OverEnemyTerritory_LosesAircraft()
        {
            // 적 거점 폭격을 강한 대공으로: 거의 확실히 격추
            foreach (var d in _bal.Aircraft.Values) d.Range = 12;
            _bal.EnemyHitPerStrength = 10f;
            var enemyBase = _w.Bases.First(b => b.Owner != World.Player);
            var aircraftBefore = _w.Aircraft.Count;
            var r = SortieRules.Resolve(_w, _bal, _s1, enemyBase.Hex);
            Assert.IsTrue(r.OverEnemyTerritory);
            Assert.IsTrue(r.Aircraft.Any(o => o.ShotDown));
            Assert.Less(_w.Aircraft.Count, aircraftBefore);
            Assert.IsTrue(_w.Pilots.Any(p => p.Captured || p.ReturnTurns > 0));
        }

        [Test]
        public void ShotDown_OverOwnOrNeutral_KeepsWreck()
        {
            foreach (var d in _bal.Aircraft.Values) d.Range = 12;
            _bal.EnemyHitPerStrength = 10f;
            var aircraftBefore = _w.Aircraft.Count;
            var r = SortieRules.Resolve(_w, _bal, _s1, _unitA);
            Assert.IsFalse(r.OverEnemyTerritory);
            Assert.IsTrue(r.Aircraft.Any(o => o.ShotDown));
            Assert.AreEqual(aircraftBefore, _w.Aircraft.Count);
            Assert.IsTrue(_w.Aircraft.Any(a => a.Damage == 100));
        }

        [Test]
        public void Overlap_ReducesAntiAirForLaterSortie()
        {
            foreach (var d in _bal.Aircraft.Values) d.Range = 12;
            var enemyBase = _w.Bases.First(b => b.Owner != World.Player);
            _w.SortiedTargets.Add(enemyBase.Hex.Neighbor(0)); // 앞 출격이 인접 칸을 침
            var r = SortieRules.Resolve(_w, _bal, _s1, enemyBase.Hex);
            Assert.IsTrue(r.OverlapBonus);
            Assert.IsTrue(r.Log[0].Contains("대공 −"));
        }

        [Test]
        public void CapturedPilot_SalaryRatioAndRansom()
        {
            var p = _w.Pilots[0]; // 네임드 80
            var full = SquadronRules.Salaries(_w, _bal);
            p.Captured = true;
            _bal.CapturedSalaryRatio = 0.5f;
            Assert.AreEqual(full - 40, SquadronRules.Salaries(_w, _bal));
            _bal.CapturedSalaryRatio = 0f;
            Assert.AreEqual(full - 80, SquadronRules.Salaries(_w, _bal));
            _w.Funds = 1000;
            SquadronRules.Ransom(_w, _bal, p);
            Assert.IsFalse(p.Captured);
            Assert.AreEqual(700, _w.Funds);
            Assert.AreEqual(1, p.ReturnTurns);
            Economy.EndTurn(_w, _bal);
            Assert.AreEqual(0, p.ReturnTurns);
        }

        [Test]
        public void ExecuteAll_RunsInOrderAndClearsPlan()
        {
            foreach (var d in _bal.Aircraft.Values) d.Range = 12;
            SortieRules.Plan(_w, _bal, _w.Squadrons[0], _unitA);
            SortieRules.Plan(_w, _bal, _w.Squadrons[1], _w.EnemyUnits[1].Hex);
            var reports = SortieRules.ExecuteAll(_w, _bal);
            Assert.AreEqual(2, reports.Count);
            Assert.AreEqual(1, reports[0].Order);
            Assert.AreEqual("1편대", reports[0].SquadronName);
            Assert.IsEmpty(_w.SortiePlan);
            Assert.AreEqual(2, _w.SortiedTargets.Count);
            Economy.EndTurn(_w, _bal);
            Assert.IsEmpty(_w.SortiedTargets);
            Assert.IsFalse(_w.Squadrons[0].SortiedThisTurn);
        }
    }
}
