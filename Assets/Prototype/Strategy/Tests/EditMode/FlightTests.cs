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
}
