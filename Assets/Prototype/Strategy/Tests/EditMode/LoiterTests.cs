using System.Linq;
using NUnit.Framework;
using Prototype.Strategy.Rules;

namespace Prototype.Strategy.Tests
{
    /// <summary>체공 (출격 후 적 턴 동안 목표 상공에 남기)과 맵 재생 기록.</summary>
    public class LoiterTests
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
        }

        EnemyUnit PlaceA(int distance)
        {
            var a = _w.EnemyUnits[0];
            var h = _coast;
            for (var i = 0; i < distance; i++) h = h.Neighbor(0);
            a.Hex = h;
            Assert.AreEqual(distance, Hex.Distance(_coast, h));
            return a;
        }

        [Test]
        public void Fuel_DecidesWhetherLoiterIsPossible()
        {
            // 반경 3 → 연료 6. 거리 2: 왕복 4 + 체공 2 = 6 → 가능, 거리 3: 8 → 불가
            Assert.AreEqual(6, SortieRules.Fuel(_w, _bal, _s1));
            Assert.IsNull(SortieRules.CanLoiter(_w, _bal, _s1, PlaceA(2).Hex));
            StringAssert.Contains("연료 부족", SortieRules.CanLoiter(_w, _bal, _s1, PlaceA(3).Hex));
            // 공중급유: 반경 6 → 연료 12
            _s1.Refuel = true;
            Assert.IsNull(SortieRules.CanLoiter(_w, _bal, _s1, PlaceA(3).Hex));
        }

        [Test]
        public void Loiter_StaysThenReturnsAndCannotSortieNextTurn()
        {
            var a = PlaceA(2);
            var target = a.Hex;
            SortieRules.Plan(_w, _bal, _s1, target);
            var plan = _w.SortiePlan.Single();
            SortieRules.SetLoiter(_w, _bal, plan, true);
            Assert.IsTrue(plan.Loiter);
            SortieRules.ExecuteAll(_w, _bal);

            Assert.AreEqual(target, _s1.LoiterHex);
            Assert.IsTrue(_w.Debriefs[0].Loiter);
            Assert.AreEqual("체공 중", SortieRules.CanSortie(_w, _bal, _s1));
            // 맵 재생: 나가는 비행만 있고 귀환 비행은 없다
            var flights = _w.Traces.Where(t => t.Kind == TraceKind.Squadron).ToList();
            Assert.AreEqual(1, flights.Count);
            Assert.AreEqual(_coast, flights[0].Path[0]);
            Assert.AreEqual(target, flights[0].Path[^1]);

            Economy.EndTurn(_w, _bal);
            Assert.IsNull(_s1.LoiterHex);
            Assert.IsTrue(_s1.ReturnedFromLoiter);
            StringAssert.Contains("체공 후 귀환", SortieRules.CanSortie(_w, _bal, _s1));
            Assert.IsTrue(_w.Traces.Any(t => t.Kind == TraceKind.Squadron && t.Loiter && t.Path[^1] == _coast));

            Economy.EndTurn(_w, _bal);
            Assert.IsFalse(_s1.ReturnedFromLoiter);
        }

        [Test]
        public void Loiter_InterceptsEnemyInWatchArea()
        {
            var a = PlaceA(2);
            var target = a.Hex;
            SortieRules.Plan(_w, _bal, _s1, target);
            SortieRules.SetLoiter(_w, _bal, _w.SortiePlan.Single(), true);
            SortieRules.ExecuteAll(_w, _bal);
            Assert.AreEqual(target, _s1.LoiterHex);

            // A부대는 치웠다고 보고, B부대를 감시 반경 안(목표 옆 칸)에 둔다
            _w.EnemyUnits.Remove(a);
            var b = _w.EnemyUnits[0];
            b.Hex = target.Neighbor(0);
            var before = b.Strength;

            Economy.EndTurn(_w, _bal);
            var rep = _w.Debriefs.SingleOrDefault(r => r.Intercept);
            Assert.IsNotNull(rep, "체공 요격 디브리핑");
            Assert.AreEqual(0, rep.Cost);
            Assert.Greater(rep.Rounds, 0);
            Assert.IsTrue(_w.Traces.Any(t => t.Kind == TraceKind.Event && t.Event == "체공 요격"));
            // 요격당한 부대는 깎이고(또는 격멸) 이번 턴 움직이지 않는다
            if (_w.EnemyUnits.Contains(b))
            {
                Assert.Less(b.Strength, before);
                Assert.AreEqual(target.Neighbor(0), b.Hex);
            }
        }

        [Test]
        public void EnemyMoves_AreRecordedStepByStep()
        {
            Economy.EndTurn(_w, _bal);
            var moves = _w.Traces.Where(t => t.Kind == TraceKind.Enemy).ToList();
            Assert.IsNotEmpty(moves);
            foreach (var m in moves)
            {
                Assert.AreEqual(m.Path.Count, m.Seen.Count);
                for (var i = 1; i < m.Path.Count; i++) Assert.AreEqual(1, Hex.Distance(m.Path[i - 1], m.Path[i]));
                var u = _w.EnemyUnits.FirstOrDefault(x => x.Id == m.UnitId);
                // 거점을 공격한 부대는 격퇴되면 후퇴·점령하면 거점으로 옮기므로 제외
                if (u != null && _w.Intercepts.All(r => r.UnitName != u.Name)) Assert.AreEqual(u.Hex, m.Path[^1]);
            }
        }
    }
}
