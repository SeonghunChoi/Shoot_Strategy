using System.Linq;
using NUnit.Framework;
using Prototype.Strategy.Rules;

namespace Prototype.Strategy.Tests
{
    /// <summary>C안 다턴 비행: 이륙·이동·연료·착륙·불시착·적 선공과 맵 재생 기록.</summary>
    public class FlightTests
    {
        Balance _bal;
        World _w;
        Squadron _s1;
        Hex _coast;

        [SetUp]
        public void SetUp()
        {
            _bal = new Balance { VisionEnabled = false, DirectiveInterval = 1000 };
            _w = World.Create(1, _bal);
            _w.Funds = 100000;
            _s1 = _w.Squadrons[0];
            _coast = _w.BaseById(_s1.BaseId).Hex;
            // 적 부대는 모두 자기 기지로 (교전이 끼어들지 않게)
            foreach (var u in _w.EnemyUnits) u.Hex = _w.Bases.First(b => b.Owner == u.Owner).Hex;
        }

        Hex East(int n)
        {
            var h = _coast;
            for (var i = 0; i < n; i++) h = h.Neighbor(0);
            return h;
        }

        [Test]
        public void Launch_FliesWithinSpeedAndUsesFuel()
        {
            Assert.AreEqual(3, FlightRules.Speed(_w, _bal, _s1));
            Assert.AreEqual(7, FlightRules.MaxFuel(_w, _bal, _s1)); // 반경 3 × 2 + 예비 1
            StringAssert.Contains("범위 밖", FlightRules.CanOrder(_w, _bal, _s1, East(4)));

            var funds = _w.Funds;
            Assert.IsNull(FlightRules.Order(_w, _bal, _s1, East(2)));
            Assert.AreEqual(East(2), _s1.AirHex);
            Assert.AreEqual(5, _s1.Fuel);
            Assert.AreEqual(1, _s1.MovesLeft);
            Assert.AreEqual(funds - SortieRules.Cost(_w, _bal, _s1), _w.Funds);
            StringAssert.Contains("범위 밖", FlightRules.CanOrder(_w, _bal, _s1, East(4)));
            Assert.IsTrue(FlightRules.IsFlying(_s1));
            Assert.AreEqual("비행 중", SortieRules.CanSortie(_w, _bal, _s1));

            var flight = _w.Traces.Single(t => t.Kind == TraceKind.Squadron);
            Assert.AreEqual(_coast, flight.Path[0]);
            Assert.AreEqual(East(2), flight.Path[^1]);
        }

        [Test]
        public void Attack_InFlight_OncePerTurn_CostPaidAtLaunch()
        {
            var a = _w.EnemyUnits[0];
            a.Hex = East(3);
            var funds = _w.Funds;
            var rep = FlightRules.Order(_w, _bal, _s1, a.Hex);
            Assert.IsNotNull(rep);
            Assert.AreEqual(0, rep.Cost);
            Assert.AreEqual(funds - SortieRules.Cost(_w, _bal, _s1) + rep.Reward, _w.Funds);
            Assert.IsTrue(_s1.AttackedThisTurn);
            Assert.Greater(rep.Rounds, 0);
        }

        [Test]
        public void AttackThenLand_NextLaunchCanAttackAgain()
        {
            var a = _w.EnemyUnits[0];
            a.Hex = East(1);
            Assert.IsNotNull(FlightRules.Order(_w, _bal, _s1, a.Hex));
            if (FlightRules.IsFlying(_s1)) FlightRules.Order(_w, _bal, _s1, _coast); // 공격 후 같은 턴에 착륙
            Assert.IsFalse(FlightRules.IsFlying(_s1));
            Assert.IsFalse(_s1.AttackedThisTurn);

            Economy.EndTurn(_w, _bal);
            foreach (var u in _w.EnemyUnits) u.Hex = _w.Bases.First(b => b.Owner == u.Owner).Hex;
            var next = _w.EnemyUnits[0]; // 처음 친 부대는 격파됐을 수 있음
            next.Hex = East(2);
            Assert.IsTrue(FlightRules.IsAttackTarget(_w, _s1, next.Hex));
            CollectionAssert.Contains(FlightRules.Destinations(_w, _bal, _s1).ToList(), next.Hex);
        }

        [Test]
        public void EndTurnInAir_FuelFatigueAndMovesReset_ThenLand()
        {
            FlightRules.Order(_w, _bal, _s1, East(2));
            var pilot = _w.PilotById(_w.AircraftById(_s1.FlyingAircraftIds[0]).PilotId);
            var fatigue = pilot.Fatigue;

            Economy.EndTurn(_w, _bal);
            Assert.AreEqual(East(2), _s1.AirHex);
            Assert.AreEqual(4, _s1.Fuel); // 5 − 하늘에서 턴을 마침 1
            Assert.AreEqual(3, _s1.MovesLeft);
            Assert.AreEqual(fatigue + _bal.FlightFatigue, pilot.Fatigue);

            // 돌아와 착륙 → 이번 턴 출격 불가, 다음 턴 가능
            Assert.AreEqual(_coast, FlightRules.ReturnStep(_w, _bal, _s1));
            FlightRules.Order(_w, _bal, _s1, _coast);
            Assert.IsFalse(FlightRules.IsFlying(_s1));
            Assert.IsTrue(_s1.LandedThisTurn);
            StringAssert.Contains("착륙한 턴", FlightRules.CanLaunch(_w, _bal, _s1));
            Economy.EndTurn(_w, _bal);
            Assert.IsNull(FlightRules.CanLaunch(_w, _bal, _s1));
        }

        [Test]
        public void FuelRunsOut_CrashLosesFlyingAircraft()
        {
            FlightRules.Order(_w, _bal, _s1, East(2));
            var flying = _s1.FlyingAircraftIds.ToList();
            var count = _w.Aircraft.Count;
            _s1.Fuel = 0;
            Economy.EndTurn(_w, _bal);
            Assert.IsFalse(FlightRules.IsFlying(_s1));
            Assert.AreEqual(count - flying.Count, _w.Aircraft.Count);
            Assert.IsTrue(flying.All(id => _w.AircraftById(id) == null));
            Assert.IsTrue(_w.Traces.Any(t => t.Kind == TraceKind.Event && t.Event == "불시착"));
        }

        [Test]
        public void EnemyNextToFlyingSquadron_AttacksFirst()
        {
            FlightRules.Order(_w, _bal, _s1, East(2));
            var b = _w.EnemyUnits[1];
            b.Hex = East(3);
            Economy.EndTurn(_w, _bal);
            var rep = _w.Debriefs.FirstOrDefault(r => r.EnemyInitiated);
            Assert.IsNotNull(rep, "적 선공 디브리핑");
            Assert.AreEqual(0, rep.Cost);
            Assert.IsTrue(_w.Traces.Any(t => t.Kind == TraceKind.Event && t.Event == "적 선공"));
        }

        [Test]
        public void FlyingSquadron_DoesNotDefendBase()
        {
            FlightRules.Order(_w, _bal, _s1, East(2));
            Assert.AreEqual("비행 중 (아군 거점에 착륙하면 소속이 바뀜)", SquadronRules.CanMove(_w, _bal, _s1, _w.Bases[1]));
            Assert.AreEqual("비행 중", SquadronRules.CanDisband(_w, _s1));
        }

        [Test]
        public void EnemyMoves_AreRecordedStepByStep()
        {
            foreach (var u in _w.EnemyUnits) u.Hex = _w.Bases.First(x => x.Owner == u.Owner).Hex.Neighbor(3);
            Economy.EndTurn(_w, _bal);
            var moves = _w.Traces.Where(t => t.Kind == TraceKind.Enemy).ToList();
            Assert.IsNotEmpty(moves);
            foreach (var m in moves)
            {
                Assert.AreEqual(m.Path.Count, m.Seen.Count);
                for (var i = 1; i < m.Path.Count; i++) Assert.AreEqual(1, Hex.Distance(m.Path[i - 1], m.Path[i]));
            }
        }
    }

    /// <summary>도시판 2단계: 드롭탱크, 아군 영역 위 초계, 급유기, 레이더 미확인 접촉.</summary>
    public class FlightSupportTests
    {
        Balance _bal;
        World _w;
        Squadron _s1;
        Hex _coast;

        [SetUp]
        public void SetUp()
        {
            _bal = new Balance { VisionEnabled = false, DirectiveInterval = 1000 };
            _w = World.Create(1, _bal);
            _w.Funds = 100000;
            _s1 = _w.Squadrons[0];
            _coast = _w.BaseById(_s1.BaseId).Hex;
            foreach (var u in _w.EnemyUnits) u.Hex = _w.Bases.First(b => b.Owner == u.Owner).Hex;
        }

        Hex East(int n)
        {
            var h = _coast;
            for (var i = 0; i < n; i++) h = h.Neighbor(0);
            return h;
        }

        [Test]
        public void DropTank_UsedFirst_DroppedAtAttackTarget()
        {
            _s1.DropTank = true;
            Assert.AreEqual(4, FlightRules.TankCapacity(_w, _bal, _s1)); // 7 × 1.5 = 10.5 → 11
            FlightRules.Order(_w, _bal, _s1, East(2));
            Assert.AreEqual(7, _s1.Fuel);      // 탱크부터 씀
            Assert.AreEqual(2, _s1.TankFuel);
            var a = _w.EnemyUnits[0];
            a.Hex = East(3);
            FlightRules.Order(_w, _bal, _s1, a.Hex);
            Assert.IsFalse(_s1.TankAttached);
            Assert.AreEqual(0, _s1.TankFuel);
            Assert.AreEqual(7, _s1.Fuel);
        }

        [Test]
        public void CombatAirPatrol_OverOwnTerritory_KeepsFuel()
        {
            FlightRules.Order(_w, _bal, _s1, East(1)); // 해안 기지 영역 안
            Assert.IsTrue(FlightRules.OverOwnTerritory(_w, _s1));
            var fuel = _s1.Fuel;
            Economy.EndTurn(_w, _bal);
            Assert.AreEqual(fuel, _s1.Fuel);
        }

        [Test]
        public void Tanker_ArrivesNextTurn_Refuels_ThenLeaves()
        {
            Assert.AreEqual(1, TankerRules.Capacity(_w, _bal)); // 해안 기지 활주로 2등급
            var funds = _w.Funds;
            var t = TankerRules.Call(_w, _bal, East(4));
            Assert.IsNotNull(t);
            Assert.AreEqual(funds - _bal.TankerCost, _w.Funds);
            StringAssert.Contains("모두 사용 중", TankerRules.CanCall(_w, _bal));

            FlightRules.Order(_w, _bal, _s1, East(3));
            Assert.IsNull(TankerRules.RefuelableFor(_w, _bal, _s1)); // 아직 도착 전
            Economy.EndTurn(_w, _bal);
            Assert.AreEqual(1, _w.Tankers.Count);
            Assert.IsTrue(TankerRules.IsActive(_w, t));
            Assert.Less(_s1.Fuel, FlightRules.MaxFuel(_w, _bal, _s1));
            Assert.IsTrue(TankerRules.Refuel(_w, _bal, _s1));
            Assert.AreEqual(FlightRules.MaxFuel(_w, _bal, _s1), _s1.Fuel);
            Assert.IsFalse(TankerRules.Refuel(_w, _bal, _s1)); // 급유기마다 1회

            Economy.EndTurn(_w, _bal);
            Assert.IsEmpty(_w.Tankers);
        }

        [Test]
        public void Tanker_ShotDown_ByAdjacentEnemy()
        {
            var t = TankerRules.Call(_w, _bal, East(4));
            var a = _w.EnemyUnits[0];
            a.Hex = East(5);
            Economy.EndTurn(_w, _bal);
            Assert.IsEmpty(_w.Tankers);
            Assert.IsTrue(_w.Traces.Any(x => x.Kind == TraceKind.Event && x.Event == "급유기 격추"));
        }

        [Test]
        public void RadarContact_ShowsUnknownBlip_ExactWithRadar()
        {
            _bal.VisionEnabled = true;
            var a = _w.EnemyUnits[0];
            a.Hex = East(4);
            var visible = Vision.VisibleHexes(_w, _bal);
            Assert.IsFalse(visible.Contains(a.Hex));
            var near = Vision.Contacts(_w, _bal, visible).Where(c => Hex.Distance(c.Hex, a.Hex) <= 1).ToList();
            Assert.AreEqual(1, near.Count, "레이더 없으면 1칸 오차 안의 '?'");

            BaseRules.Build(_w, _bal, _w.Bases[0], FacilityType.Radar);
            visible = Vision.VisibleHexes(_w, _bal);
            Assert.IsFalse(visible.Contains(a.Hex));
            Assert.IsTrue(Vision.Contacts(_w, _bal, visible).Any(c => c.Hex == a.Hex), "레이더가 있으면 정확한 위치");
        }
    }
}
