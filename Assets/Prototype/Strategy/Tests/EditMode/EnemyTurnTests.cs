using System.Linq;
using NUnit.Framework;
using Prototype.Strategy.Rules;

namespace Prototype.Strategy.Tests
{
    public class EnemyTurnTests
    {
        Balance _bal;
        World _w;

        [SetUp]
        public void SetUp()
        {
            _bal = new Balance();
            _w = World.Create(1, _bal);
        }

        static Hex Off(int c, int r) => Hex.FromOffset(c, r);

        [Test]
        public void Path_AvoidsSeaAndCountsMountainCost()
        {
            // (2,3)→(1,3)은 해안, 서쪽 (0,3)은 해상이라 못 감
            Assert.IsNull(HexPath.Find(_w.Map, _bal, Off(2, 3), Off(0, 3)));
            var path = HexPath.Find(_w.Map, _bal, Off(6, 3), Off(3, 4));
            Assert.IsNotNull(path);
            Assert.AreEqual(Off(3, 4), path[^1]);
            Assert.IsTrue(path.All(h => _w.Map[h].Terrain != TerrainType.Sea));
            Assert.AreEqual(2, HexPath.MoveCost(_bal, TerrainType.Mountain));
        }

        [Test]
        public void Vision_TerritoryRadarAndMemory()
        {
            Assert.IsTrue(Vision.IsVisible(_w, _bal, Off(3, 4)));          // 아군 영역
            Assert.IsTrue(Vision.IsVisible(_w, _bal, Off(3, 2)));          // 영역 +1
            Assert.IsFalse(Vision.IsVisible(_w, _bal, Off(3, 1)));         // 영역 +2
            Assert.IsFalse(Vision.IsVisible(_w, _bal, Off(10, 6)));        // 경비대 위치
            _w.Funds = 100000;
            BaseRules.Build(_w, _bal, _w.Bases[0], FacilityType.Radar);     // 해안 기지 시야 +1
            Assert.AreEqual(2, Vision.VisionRange(_w, _bal, _w.Bases[0]));
            Assert.IsTrue(Vision.IsVisible(_w, _bal, Off(3, 1)));
            _bal.VisionEnabled = false;
            Assert.IsTrue(Vision.IsVisible(_w, _bal, Off(10, 6)));
            _bal.VisionEnabled = true;

            // 목격 기억: 보이는 부대를 기록하고, 사라지면 유령으로 남다가 5턴 뒤 삭제
            var a = _w.EnemyUnits[0];
            a.Hex = Off(3, 2); // 레이더 반경 안
            Vision.UpdateSightings(_w, _bal);
            Assert.IsTrue(_w.Sightings.ContainsKey(a.Id));
            a.Hex = Off(9, 3);
            Vision.UpdateSightings(_w, _bal);
            Assert.IsTrue(Vision.StaleSightings(_w, _bal).Any(s => s.UnitId == a.Id));
            _w.Turn += 6;
            Vision.UpdateSightings(_w, _bal);
            Assert.IsFalse(_w.Sightings.ContainsKey(a.Id));
        }

        [Test]
        public void Sortie_TargetsOnlyVisibleUnits()
        {
            foreach (var d in _bal.Aircraft.Values) d.Range = 12;
            var s1 = _w.Squadrons[0];
            Assert.IsFalse(SortieRules.Targets(_w, _bal, s1).Contains(Off(10, 6))); // 경비대: 안 보임
            Assert.IsTrue(SortieRules.Targets(_w, _bal, s1).Contains(Off(11, 6)));  // 적 거점: 항상
            _bal.VisionEnabled = false;
            Assert.IsTrue(SortieRules.Targets(_w, _bal, s1).Contains(Off(10, 6)));
        }

        [Test]
        public void Stance_FollowsStrengthAndLosses()
        {
            var east = _w.FactionById(1);
            EnemyTurn.Run(_w, _bal);
            Assert.AreEqual(Stance.Offensive, east.Stance);
            east.LastLossTurn = _w.Turn;
            EnemyTurn.Run(_w, _bal);
            Assert.AreEqual(Stance.Defensive, east.Stance);
            foreach (var u in _w.EnemyUnits.Where(u => u.Owner == 1)) { u.Air = 1; u.Ground = 1; }
            EnemyTurn.Run(_w, _bal);
            Assert.AreEqual(Stance.Regroup, east.Stance);
        }

        [Test]
        public void BestTarget_IgnoresUnseenSquadrons()
        {
            var east = _w.FactionById(1);
            var unit = _w.EnemyUnits[0]; // (6,3), 해안 기지까지 4 > 탐지 3
            var coast = _w.Bases[0];
            Assert.IsFalse(EnemyTurn.FactionSees(_w, _bal, east, coast.Hex));
            unit.Hex = Off(5, 4);
            Assert.IsTrue(EnemyTurn.FactionSees(_w, _bal, east, coast.Hex));
        }

        [Test]
        public void Offensive_UnitMovesTowardBaseAndAttacksWhenAdjacent()
        {
            _bal.VisionEnabled = false; // 이동 사건은 보이는 부대만 기록되므로
            var unit = _w.EnemyUnits[0];
            var start = unit.Hex;
            EnemyTurn.Run(_w, _bal);
            Assert.AreNotEqual(start, unit.Hex);
            Assert.IsTrue(_w.EnemyEvents.Count > 0);
        }

        [Test]
        public void Intercept_Repelled_WhenDefenseStrong()
        {
            var coast = _w.Bases[0];
            var unit = _w.EnemyUnits[0];
            unit.Hex = Off(4, 3); // 인접
            unit.Air = 4;
            unit.Ground = 4;
            _bal.ReinforceTurns = 100;
            EnemyTurn.Run(_w, _bal);
            Assert.AreEqual(World.Player, coast.Owner);
            var rep = _w.Intercepts.Single();
            Assert.IsFalse(rep.Captured);
            Assert.Greater(rep.SquadronsEngaged, 0);
        }

        [Test]
        public void Intercept_Captured_WhenNoDefense()
        {
            var coast = _w.Bases[0];
            foreach (var s in _w.SquadronsAt(coast).ToList()) s.SortiedThisTurn = true; // 편대 모두 출격 중 (방어 공백)
            var unit = _w.EnemyUnits[0];
            _w.EnemyUnits.RemoveAll(u => u != unit); // 다른 부대가 같은 턴에 남부 전초기지까지 빼앗지 않도록
            unit.Hex = Off(4, 3);
            unit.Air = 30;
            unit.Ground = 30;
            var gradeBefore = coast.Grade;
            var squadronsBefore = _w.Squadrons.Count;
            EnemyTurn.Run(_w, _bal);
            var rep = _w.Intercepts.Single();
            Assert.IsTrue(rep.Captured);
            Assert.AreEqual(1, coast.Owner);
            Assert.AreEqual(gradeBefore - 1, coast.Grade);
            Assert.AreEqual(coast.Hex, unit.Hex);
            // 주둔 편대 2개: 남부 전초기지 격납고(수용 2)로 강제 이탈
            Assert.AreEqual(squadronsBefore, _w.Squadrons.Count);
            Assert.IsTrue(_w.Squadrons.All(s => s.BaseId == _w.Bases[1].Id));
            Assert.IsNull(_w.GameOver);
        }

        [Test]
        public void MovedSquadron_InterceptsAtDestinationSameTurn()
        {
            var south = _w.Bases[1];
            var s2 = _w.Squadrons[1];
            SquadronRules.Move(_w, _bal, s2, south);
            var b = _w.EnemyUnits[1];
            _w.EnemyUnits.RemoveAll(u => u != b);
            b.Hex = Hex.FromOffset(4, 10); // 남부 전초기지 인접
            b.Air = 8;   // 세력 전력 16 ≥ 15 → 공세 유지 (더 약하면 재정비로 물러난다)
            b.Ground = 8;
            _bal.KnownAircraftDefenseWeight = 0f; // 보이는 편대 때문에 더 먼 해안 기지를 고르지 않도록
            Economy.EndTurn(_w, _bal);
            var rep = _w.Intercepts.Single();
            Assert.AreEqual(1, rep.SquadronsEngaged); // 재배치해 온 2편대가 요격
            Assert.AreEqual(World.Player, south.Owner);
        }

        [Test]
        public void Predict_MatchesActualMoveAndAttack()
        {
            var u = _w.EnemyUnits[0]; // A부대 → 해안 기지
            _w.EnemyUnits.RemoveAll(x => x != u);
            _bal.ReinforceTurns = 100;
            var p = EnemyTurn.Predict(_w, _bal, u);
            Assert.AreEqual(Stance.Offensive, p.Stance);
            Assert.IsNotEmpty(p.ThisTurn);
            var start = u.Hex;
            EnemyTurn.Run(_w, _bal);
            if (_w.EnemyUnits.Contains(u) && _w.Intercepts.Count == 0) Assert.AreEqual(p.ThisTurn[^1], u.Hex);
            Assert.AreEqual(p.AttackTarget != null, _w.Intercepts.Count > 0);
            Assert.AreNotEqual(start, p.ThisTurn[^1]);
        }

        [Test]
        public void AllBasesLost_IsGameOver()
        {
            foreach (var b in _w.Bases.Where(b => b.Owner == World.Player).ToList()) b.Owner = 1;
            EnemyTurn.Run(_w, _bal);
            Assert.AreEqual("모든 거점 상실", _w.GameOver);
        }

        [Test]
        public void Reinforce_AddsUnitOnTimer()
        {
            var before = _w.EnemyUnits.Count(u => u.Owner == 2);
            _bal.ReinforceTurns = 2;
            EnemyTurn.Run(_w, _bal);
            EnemyTurn.Run(_w, _bal);
            Assert.AreEqual(before + 1, _w.EnemyUnits.Count(u => u.Owner == 2));
        }

        [Test]
        public void EnemyTurn_IsDeterministic()
        {
            var a = World.Create(5, new Balance());
            var b = World.Create(5, new Balance());
            for (var i = 0; i < 5; i++) { Economy.EndTurn(a, new Balance()); Economy.EndTurn(b, new Balance()); }
            CollectionAssert.AreEqual(a.EnemyUnits.Select(u => (u.Name, u.Hex, u.Strength)).ToList(),
                b.EnemyUnits.Select(u => (u.Name, u.Hex, u.Strength)).ToList());
        }
    }
}
