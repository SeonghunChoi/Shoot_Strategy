using System.Linq;
using NUnit.Framework;
using Prototype.Strategy.Rules;

namespace Prototype.Strategy.Tests
{
    public class HexTests
    {
        [Test]
        public void Distance_IsSymmetricAndMatchesSteps()
        {
            var a = new Hex(0, 0);
            Assert.AreEqual(0, Hex.Distance(a, a));
            Assert.AreEqual(3, Hex.Distance(a, new Hex(3, 0)));
            Assert.AreEqual(3, Hex.Distance(a, new Hex(-3, 3)));
            Assert.AreEqual(4, Hex.Distance(new Hex(1, -2), new Hex(-1, 2)));
            Assert.AreEqual(Hex.Distance(new Hex(2, 5), new Hex(-4, 1)), Hex.Distance(new Hex(-4, 1), new Hex(2, 5)));
        }

        [Test]
        public void Neighbors_AreSixDistinctAtDistanceOne()
        {
            var c = new Hex(2, -1);
            var n = c.Neighbors().ToList();
            Assert.AreEqual(6, n.Distinct().Count());
            Assert.IsTrue(n.All(h => Hex.Distance(c, h) == 1));
        }

        [TestCase(0, 1)]
        [TestCase(1, 7)]
        [TestCase(2, 19)]
        [TestCase(3, 37)]
        public void Range_CountIsCentered(int radius, int expected)
        {
            var c = new Hex(1, 1);
            var range = Hex.Range(c, radius).ToList();
            Assert.AreEqual(expected, range.Distinct().Count());
            Assert.IsTrue(range.All(h => Hex.Distance(c, h) <= radius));
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        public void Ring_HasSixRCellsAtExactDistance(int radius)
        {
            var c = new Hex(-2, 3);
            var ring = Hex.Ring(c, radius).ToList();
            Assert.AreEqual(6 * radius, ring.Distinct().Count());
            Assert.IsTrue(ring.All(h => Hex.Distance(c, h) == radius));
        }

        [Test]
        public void Line_IsContiguousFromStartToEnd()
        {
            var a = new Hex(0, 0);
            var b = new Hex(5, -2);
            var line = Hex.Line(a, b);
            Assert.AreEqual(Hex.Distance(a, b) + 1, line.Count);
            Assert.AreEqual(a, line[0]);
            Assert.AreEqual(b, line[^1]);
            for (var i = 1; i < line.Count; i++) Assert.AreEqual(1, Hex.Distance(line[i - 1], line[i]));
        }

        [Test]
        public void StraightDirection_OnlyForAxisAlignedCells()
        {
            var a = new Hex(1, 1);
            for (var d = 0; d < 6; d++)
                Assert.AreEqual(d, Hex.StraightDirection(a, a + Hex.Directions[d] * 3));
            Assert.AreEqual(-1, Hex.StraightDirection(a, a));
            Assert.AreEqual(-1, Hex.StraightDirection(a, a + new Hex(2, -1)));
        }

        [Test]
        public void Offset_RoundTrips()
        {
            for (var row = 0; row < 12; row++)
            for (var col = 0; col < 16; col++)
                Assert.AreEqual((col, row), Hex.FromOffset(col, row).ToOffset());
        }

        [Test]
        public void Round_PicksNearestCell()
        {
            Assert.AreEqual(new Hex(2, -1), Hex.Round(2.2, -0.9));
            Assert.AreEqual(new Hex(0, 0), Hex.Round(0.3, 0.3));
        }
    }

    public class MapAndRngTests
    {
        [Test]
        public void Zone1_ParsesAsSixteenByTwelve()
        {
            var map = HexMap.Parse(MapLayouts.Zone1);
            Assert.AreEqual(16, map.Width);
            Assert.AreEqual(12, map.Height);
            Assert.AreEqual(16 * 12, map.AllHexes().Count());
            Assert.IsTrue(map.AllHexes().Any(h => map[h].Oil));
            Assert.IsFalse(map.Contains(Hex.FromOffset(16, 0)));
            Assert.IsFalse(map.Contains(Hex.FromOffset(0, -1)));
        }

        [Test]
        public void Rng_IsDeterministicPerSeed()
        {
            var a = new Rng(42);
            var b = new Rng(42);
            for (var i = 0; i < 100; i++) Assert.AreEqual(a.NextULong(), b.NextULong());

            var c = new Rng(43);
            Assert.AreNotEqual(new Rng(42).NextULong(), c.NextULong());
        }

        [Test]
        public void Rng_StateRestoresSequence()
        {
            var rng = new Rng(7);
            rng.Next(100);
            var saved = rng.State;
            var expected = Enumerable.Range(0, 10).Select(_ => rng.Next(1000)).ToList();
            rng.State = saved;
            var replay = Enumerable.Range(0, 10).Select(_ => rng.Next(1000)).ToList();
            CollectionAssert.AreEqual(expected, replay);
        }
    }
}
