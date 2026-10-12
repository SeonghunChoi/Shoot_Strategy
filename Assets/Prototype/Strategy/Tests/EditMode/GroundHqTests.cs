using System.Linq;
using NUnit.Framework;
using Prototype.Strategy.Rules;

namespace Prototype.Strategy.Tests
{
    public class GroundHqTests
    {
        Balance _bal;
        World _w;
        Base _coast;
        Base _goal;

        [SetUp]
        public void SetUp()
        {
            _bal = new Balance { DirectiveInterval = 1000 }; // 지시 발생은 따로 시험
            _w = World.Create(1, _bal);
            _coast = _w.Bases[0];
            _goal = _w.Bases.First(b => b.Id == _w.GoalBaseId);
            _w.Funds = 100000;
        }

        static Hex Off(int c, int r) => Hex.FromOffset(c, r);

        GroundUnit HireAtCoast()
        {
            BaseRules.Build(_w, _bal, _w.Bases[0], FacilityType.Barracks);
            return GroundRules.Hire(_w, _bal, _coast);
        }

        [Test]
        public void Hire_NeedsBarracksAndAddsUpkeep()
        {
            Assert.AreEqual("병영 필요", GroundRules.CanHire(_w, _bal, _coast));
            var g = HireAtCoast();
            Assert.IsNotNull(g);
            Assert.AreEqual(20, g.Strength); // 15 + 병영 1등급 × 5
            Assert.IsFalse(g.Deployed);
            Assert.AreEqual(20, GroundRules.StationedStrength(_w, _coast));
            Assert.IsTrue(Economy.Forecast(_w, _bal).Any(l => l.Label.StartsWith("지상군 유지비") && l.Amount == -30));
        }

        [Test]
        public void Deploy_MovesTwoPerTurnAndStationsAtOwnBase()
        {
            var g = HireAtCoast();
            var south = _w.Bases[1];
            _w.EnemyUnits.Clear();
            GroundRules.Deploy(_w, _bal, g, south.Hex);
            Assert.IsTrue(g.Deployed);
            var funds = _w.Funds;
            Assert.AreEqual(100000 - 200 - 150 - 50, funds); // 병영 150, 고용 200, 출동 50
            var start = g.Hex;
            GroundRules.MovePhase(_w, _bal);
            Assert.LessOrEqual(Hex.Distance(start, g.Hex), 2);
            for (var i = 0; i < 5 && g.Deployed; i++) GroundRules.MovePhase(_w, _bal);
            Assert.IsFalse(g.Deployed);
            Assert.AreEqual(south.Id, g.BaseId);
        }

        [Test]
        public void Assault_CapturesWhenStrongerThanDefense_AndWins()
        {
            var g = HireAtCoast();
            _w.EnemyUnits.Clear();
            g.Strength = 100;
            g.Deployed = true;
            g.Hex = _goal.Hex.Neighbor(3); // 서쪽 인접
            g.Target = _goal.Hex;
            var trust = _w.Trust;
            GroundRules.MovePhase(_w, _bal);
            Assert.AreEqual(World.Player, _goal.Owner);
            Assert.AreEqual(1, _goal.Grade);
            Assert.IsTrue(_w.Victory);
            StringAssert.StartsWith("승리", _w.GameOver);
            Assert.AreEqual(trust + _bal.TrustCapture + _bal.TrustGoal > 100 ? 100 : trust + _bal.TrustCapture + _bal.TrustGoal, _w.Trust);
        }

        [Test]
        public void Assault_SiegesWhenWeakerAndFightsGarrisonFirst()
        {
            var g = HireAtCoast();
            _w.EnemyUnits.Clear();
            g.Deployed = true;
            g.Hex = _goal.Hex.Neighbor(3);
            g.Target = _goal.Hex;
            var structures = _goal.Structures;
            GroundRules.MovePhase(_w, _bal); // 전력 20 < 대공 20 + 시설 30 → 포위
            Assert.AreEqual(1, _goal.Owner);
            Assert.Less(_goal.Structures, structures);

            _w.EnemyUnits.Add(new EnemyUnit { Id = 999, Name = "수비대", Hex = _goal.Hex, Air = 5, Ground = 5, Owner = 1 });
            var before = _w.EnemyUnits[0].Strength;
            GroundRules.MovePhase(_w, _bal);
            Assert.IsTrue(_w.EnemyUnits.All(u => u.Id != 999) || _w.EnemyUnits.First(u => u.Id == 999).Strength < before);
        }

        [Test]
        public void DeployedGround_AttackedByAdjacentEnemy()
        {
            var g = HireAtCoast();
            g.Deployed = true;
            g.Hex = Off(6, 4);
            var u = _w.EnemyUnits[0]; // A부대 (6,3) — 인접
            _w.EnemyUnits.RemoveAll(x => x != u);
            var before = g.Strength;
            EnemyTurn.Run(_w, _bal);
            Assert.IsTrue(!_w.GroundUnits.Contains(g) || g.Strength < before);
        }

        [Test]
        public void StationedGround_AddsBaseDefense()
        {
            var g = HireAtCoast();
            g.Strength = 100;
            foreach (var s in _w.SquadronsAt(_coast)) s.SortiedThisTurn = true; // 편대 없음
            var u = _w.EnemyUnits[0];
            _w.EnemyUnits.RemoveAll(x => x != u);
            u.Hex = Off(4, 3);
            u.Air = 30;
            u.Ground = 30;
            EnemyTurn.Run(_w, _bal);
            Assert.AreEqual(World.Player, _coast.Owner);
            Assert.IsFalse(_w.Intercepts.Single().Captured);
        }

        [Test]
        public void Outpost_AllowedWhereGroundReaches()
        {
            foreach (var d in _bal.Aircraft.Values) d.Range = 1; // 공중으로는 못 닿게
            var target = Off(9, 9);
            StringAssert.Contains("닿지", BaseRules.CanBuildOutpost(_w, _bal, target));
            var g = HireAtCoast();
            g.Deployed = true;
            g.Hex = Off(8, 9);
            Assert.IsNull(BaseRules.CanBuildOutpost(_w, _bal, target));
            var trust = _w.Trust;
            BaseRules.BuildOutpost(_w, _bal, target);
            Assert.AreEqual(trust + _bal.TrustOutpost, _w.Trust);
        }

        [Test]
        public void Budget_ScalesWithTrust()
        {
            // 예산 = 기준 × (0.5 + 신뢰도/100): 신뢰도 50이면 기준 그대로
            Assert.AreEqual(_bal.HqBudget, HqRules.Budget(_w, _bal));
            _w.Trust = 100;
            Assert.AreEqual(_bal.HqBudget * 3 / 2, HqRules.Budget(_w, _bal));
            _w.Trust = 0;
            Assert.AreEqual(_bal.HqBudget / 2, HqRules.Budget(_w, _bal));
        }

        [Test]
        public void Trust_ZeroIsDismissal()
        {
            HqRules.AddTrust(_w, _bal, -100, "시험");
            Assert.AreEqual(0, _w.Trust);
            StringAssert.StartsWith("해임", _w.GameOver);
        }

        [Test]
        public void Directive_IssuedOnTimer_SucceedsFailsOrRefused()
        {
            _bal.DirectiveInterval = 1;
            HqRules.EndTurn(_w, _bal);
            Assert.IsNotNull(_w.Directive);

            // 성공: 격멸 지시라면 대상 제거, 방어라면 기한, 확대라면 칸 추가
            var d = _w.Directive;
            var trust = _w.Trust;
            switch (d.Type)
            {
                case DirectiveType.DestroyUnit: _w.EnemyUnits.RemoveAll(u => u.Id == d.TargetUnitId); break;
                case DirectiveType.HoldBase: _w.Turn = d.Deadline; break;
                case DirectiveType.UpgradeBase: _w.Bases[1].Grade++; break;
                case DirectiveType.SecureSite: SiteRules.Capture(_w, _w.Sites.First(s => s.Owner != World.Player), World.Player, "시험"); break;
            }
            HqRules.EndTurn(_w, _bal);
            Assert.AreEqual(trust + _bal.TrustDirectiveSuccess, _w.Trust);
            Assert.AreEqual(1, _w.Stats.DirectivesDone);

            // 새 지시 → 거부
            Assert.IsNotNull(_w.Directive);
            trust = _w.Trust;
            HqRules.Refuse(_w, _bal);
            Assert.IsNull(_w.Directive);
            Assert.AreEqual(trust - _bal.TrustRefuse, _w.Trust);

            // 새 지시 → 기한 초과 실패
            HqRules.EndTurn(_w, _bal);
            var d2 = _w.Directive;
            Assert.IsNotNull(d2);
            if (d2.Type == DirectiveType.HoldBase) _w.BaseById(d2.TargetBaseId).Owner = 1; // 즉시 실패
            else _w.Turn = d2.Deadline;
            trust = _w.Trust;
            HqRules.EndTurn(_w, _bal);
            Assert.AreEqual(trust - _bal.TrustDirectiveFail, _w.Trust);
            Assert.AreEqual(1, _w.Stats.DirectivesFailed);
        }

        [Test]
        public void BaseLost_ReducesTrust()
        {
            foreach (var s in _w.SquadronsAt(_coast)) s.SortiedThisTurn = true;
            var u = _w.EnemyUnits[0];
            _w.EnemyUnits.RemoveAll(x => x != u);
            u.Hex = Off(4, 3);
            u.Air = 30;
            u.Ground = 30;
            var trust = _w.Trust;
            EnemyTurn.Run(_w, _bal);
            Assert.AreEqual(trust - _bal.TrustBaseLost, _w.Trust);
            Assert.AreEqual(1, _w.Stats.BasesLost);
        }
    }
}
