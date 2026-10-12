using System.Collections.Generic;
using System.Linq;

namespace Prototype.Strategy.Rules
{
    /// <summary>마지막 목격 정보 (기술 설계 4.5).</summary>
    public sealed class Sighting
    {
        public int UnitId;
        public string Name;
        public int Owner;
        public Hex Hex;
        public int Turn;
        public int Air;
        public int Ground;
    }

    /// <summary>
    /// 플레이어 시야 (기획서 4장: 지형·거점 공개, 적 부대만 숨김).
    /// 탐지 = 아군 영역에서 (기본 1 + 그 거점 레이더 등급)칸 + 이번 턴 출격한 작전 지역.
    /// </summary>
    public static class Vision
    {
        /// <summary>거점 영역 밖으로 보이는 칸 수 = 기본 + 레이더 등급 × 등급당 칸.</summary>
        public static int VisionRange(World w, Balance bal, Base b)
        {
            var radar = BaseRules.GradeOf(b, FacilityType.Radar);
            return bal.TerritoryVisionRange + radar * bal.RadarRangePerGrade;
        }

        /// <summary>미확인 접촉 범위 = 시야 + 추가 칸 (레이더 등급만큼 시야와 함께 넓어진다).</summary>
        public static int ContactRange(World w, Balance bal, Base b) => VisionRange(w, bal, b) + bal.ContactExtraRange;

        /// <summary>레이더 미확인 접촉: 표시 위치와 지난 위치(이동 방향). 정체·전력·부대 수는 알 수 없다 (한 칸에 하나).</summary>
        public sealed class Contact
        {
            public Hex Hex;
            public Hex? From;
        }

        /// <summary>
        /// 시야 밖이지만 아군 거점의 접촉 범위 안인 적 부대. 잡아낸 거점에 레이더가 있으면 정확한 위치,
        /// 없으면 부대·턴마다 정해지는 최대 1칸 오차 (같은 턴 안에서는 흔들리지 않음, 시야 안 칸으로는 어긋나지 않음).
        /// </summary>
        public static List<Contact> Contacts(World w, Balance bal, HashSet<Hex> visible)
        {
            var result = new List<Contact>();
            if (!bal.VisionEnabled) return result;
            var bases = w.Bases.Where(b => b.Owner == World.Player).Select(b => (b, range: ContactRange(w, bal, b), radar: BaseRules.GradeOf(b, FacilityType.Radar), hexes: w.TerritoryOf(b).ToList())).ToList();
            foreach (var u in w.EnemyUnits)
            {
                if (visible.Contains(u.Hex)) continue;
                var radar = -1;
                foreach (var x in bases)
                    if (x.hexes.Any(h => Hex.Distance(h, u.Hex) <= x.range)) radar = System.Math.Max(radar, x.radar);
                if (radar < 0) continue;
                var shown = radar >= 1 ? u.Hex : Jitter(w, visible, u.Id, w.Turn, u.Hex);
                if (result.Any(c => c.Hex == shown)) continue;
                Hex? from = null;
                if (u.PrevHex is { } p && p != u.Hex) from = radar >= 1 ? p : Jitter(w, visible, u.Id, w.Turn - 1, p);
                result.Add(new Contact { Hex = shown, From = from });
            }
            return result;
        }

        static Hex Jitter(World w, HashSet<Hex> visible, int id, int turn, Hex h)
        {
            var k = (int)(((uint)id * 73856093u ^ (uint)turn * 19349663u) % 7u);
            if (k == 0) return h;
            var n = h.Neighbor(k - 1);
            return w.Map.Contains(n) && !visible.Contains(n) ? n : h;
        }

        /// <summary>플레이어가 지금 볼 수 있는 칸.</summary>
        public static HashSet<Hex> VisibleHexes(World w, Balance bal)
        {
            var set = new HashSet<Hex>();
            if (!bal.VisionEnabled)
            {
                foreach (var h in w.Map.AllHexes()) set.Add(h);
                return set;
            }
            foreach (var b in w.Bases.Where(x => x.Owner == World.Player))
            {
                var range = VisionRange(w, bal, b);
                foreach (var h in w.TerritoryOf(b))
                    foreach (var r in Hex.Range(h, range)) if (w.Map.Contains(r)) set.Add(r);
            }
            foreach (var s in SiteRules.OwnedBy(w, World.Player))
                foreach (var r in Hex.Range(s.Hex, bal.SiteVisionRange)) if (w.Map.Contains(r)) set.Add(r);
            foreach (var g in w.GroundUnits.Where(x => x.Deployed))
                foreach (var r in Hex.Range(g.Hex, bal.GroundVisionRange)) if (w.Map.Contains(r)) set.Add(r);
            foreach (var s in w.Squadrons)
                if (s.AirHex is { } ah)
                    foreach (var r in Hex.Range(ah, bal.FlightVisionRange)) if (w.Map.Contains(r)) set.Add(r);
            foreach (var t in w.SortiedTargets)
                foreach (var r in SortieRules.OperationArea(t)) if (w.Map.Contains(r)) set.Add(r);
            return set;
        }

        public static bool IsVisible(World w, Balance bal, Hex h) => !bal.VisionEnabled || VisibleHexes(w, bal).Contains(h);

        public static IEnumerable<EnemyUnit> VisibleUnits(World w, Balance bal)
        {
            var vis = VisibleHexes(w, bal);
            return w.EnemyUnits.Where(u => vis.Contains(u.Hex));
        }

        /// <summary>턴 종료 시 목격 정보 갱신: 보이는 부대는 기록, 오래된 기록은 삭제.</summary>
        public static void UpdateSightings(World w, Balance bal)
        {
            var vis = VisibleHexes(w, bal);
            foreach (var u in w.EnemyUnits)
            {
                if (!vis.Contains(u.Hex)) continue;
                w.Sightings[u.Id] = new Sighting { UnitId = u.Id, Name = u.Name, Owner = u.Owner, Hex = u.Hex, Turn = w.Turn, Air = u.Air, Ground = u.Ground };
            }
            foreach (var id in w.Sightings.Keys.ToList())
            {
                var s = w.Sightings[id];
                var alive = w.EnemyUnits.Any(u => u.Id == id);
                if (w.Turn - s.Turn > bal.SightingMemoryTurns || (!alive && vis.Contains(s.Hex))) w.Sightings.Remove(id);
            }
        }

        /// <summary>지금은 보이지 않지만 기억에 남은 목격 (유령 표시용).</summary>
        public static IEnumerable<Sighting> StaleSightings(World w, Balance bal)
        {
            var vis = VisibleHexes(w, bal);
            return w.Sightings.Values.Where(s => !vis.Contains(s.Hex) || w.EnemyUnits.All(u => u.Id != s.UnitId || u.Hex != s.Hex));
        }
    }
}
