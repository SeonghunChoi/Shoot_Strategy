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
            var radar = w.FacilitiesOf(b).Where(x => x.facility.Type == FacilityType.Radar)
                .Select(x => x.facility.Grade).DefaultIfEmpty(0).Max();
            return bal.TerritoryVisionRange + radar * bal.RadarRangePerGrade;
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
