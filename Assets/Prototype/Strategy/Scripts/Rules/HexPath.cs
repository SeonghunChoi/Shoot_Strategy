using System.Collections.Generic;

namespace Prototype.Strategy.Rules
{
    /// <summary>헥스 A* (기술 설계 4.5). 지형별 이동 비용, 해상 통과 불가.</summary>
    public static class HexPath
    {
        public static int MoveCost(Balance bal, TerrainType t) => t switch
        {
            TerrainType.Mountain => bal.MountainMoveCost,
            TerrainType.Sea => -1,
            _ => 1,
        };

        /// <summary>from에서 to까지의 경로 (from 제외, to 포함). 못 가면 null.</summary>
        public static List<Hex> Find(HexMap map, Balance bal, Hex from, Hex to)
        {
            if (from == to) return new List<Hex>();
            var open = new SortedSet<(int f, int order, Hex h)>(Comparer<(int f, int order, Hex h)>.Create((a, b) =>
                a.f != b.f ? a.f.CompareTo(b.f) : a.order.CompareTo(b.order)));
            var g = new Dictionary<Hex, int> { [from] = 0 };
            var came = new Dictionary<Hex, Hex>();
            var closed = new HashSet<Hex>();
            var order = 0;
            open.Add((Hex.Distance(from, to), order++, from));

            while (open.Count > 0)
            {
                var cur = open.Min;
                open.Remove(cur);
                var h = cur.h;
                if (h == to)
                {
                    var path = new List<Hex>();
                    for (var c = to; c != from; c = came[c]) path.Add(c);
                    path.Reverse();
                    return path;
                }
                if (!closed.Add(h)) continue;
                foreach (var n in h.Neighbors())
                {
                    if (!map.Contains(n) || closed.Contains(n)) continue;
                    var cost = MoveCost(bal, map[n].Terrain);
                    if (cost < 0) continue;
                    var ng = g[h] + cost;
                    if (g.TryGetValue(n, out var old) && old <= ng) continue;
                    g[n] = ng;
                    came[n] = h;
                    open.Add((ng + Hex.Distance(n, to), order++, n));
                }
            }
            return null;
        }
    }
}
