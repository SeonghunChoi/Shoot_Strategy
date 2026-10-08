using System;
using System.Collections.Generic;
using System.Linq;

namespace Prototype.Strategy.Rules
{
    /// <summary>
    /// 거점·영역·시설 명령 (기획서 5장). 무상태 서비스.
    /// Can*은 불가 사유를 돌려주고(가능하면 null), 실행 함수는 Can*이 통과했을 때만 상태를 바꾼다.
    /// </summary>
    public static class BaseRules
    {
        // ---- 조회 ----

        public static int FacilityCount(World w, Base b) => w.FacilitiesOf(b).Count();

        public static int FacilityCap(Balance bal, Base b) => bal.FacilityCap[Math.Min(b.Grade, bal.FacilityCap.Length - 1)];

        public static bool Has(World w, Base b, FacilityType type) => w.FacilitiesOf(b).Any(f => f.facility.Type == type);

        /// <summary>거점 영역 안 활주로의 가장 긴 직선 연속 칸 수 (기획서 5.4.2).</summary>
        public static int RunwayLength(World w, Base b)
        {
            bool IsRunway(Hex h) =>
                w.Territory.TryGetValue(h, out var id) && id == b.Id &&
                w.Facilities.TryGetValue(h, out var f) && f.Type == FacilityType.Runway;

            var best = 0;
            foreach (var (h, f) in w.FacilitiesOf(b))
            {
                if (f.Type != FacilityType.Runway) continue;
                best = Math.Max(best, 1);
                // 세 축 방향마다 줄의 시작 칸에서만 센다
                for (var dir = 0; dir < 3; dir++)
                {
                    if (IsRunway(h.Neighbor(dir + 3))) continue;
                    var n = 1;
                    for (var c = h.Neighbor(dir); IsRunway(c); c = c.Neighbor(dir)) n++;
                    best = Math.Max(best, n);
                }
            }
            return best;
        }

        /// <summary>활주로 길이로 운용 가능한 최대 기체 크기 (0 없음, 1 소형, 2 중형, 3 대형).</summary>
        public static int MaxAircraftSize(Balance bal, int runwayLength)
        {
            var size = 0;
            for (var s = 1; s < bal.RunwayForSize.Length; s++)
                if (runwayLength >= bal.RunwayForSize[s]) size = s;
            return size;
        }

        public static string SizeName(int size) => size switch
        {
            0 => "없음",
            1 => "소형",
            2 => "중형",
            _ => "대형",
        };

        // ---- 영역 확장 ----

        public static int ExpandCost(World w, Balance bal, Base b) =>
            bal.ExpandBaseCost + bal.ExpandCostPerHex * w.TerritoryOf(b).Count();

        /// <summary>b의 영역에 맞닿은 빈 칸 (다른 영역이 아닌 맵 안의 칸).</summary>
        public static IEnumerable<Hex> ExpandCandidates(World w, Base b) =>
            w.TerritoryOf(b)
                .SelectMany(h => h.Neighbors())
                .Distinct()
                .Where(h => w.Map.Contains(h) && !w.Territory.ContainsKey(h));

        public static string CanExpand(World w, Balance bal, Base b, Hex h)
        {
            if (!w.Map.Contains(h)) return "맵 밖";
            if (w.Territory.ContainsKey(h)) return "이미 영역";
            if (!h.Neighbors().Any(n => w.Territory.TryGetValue(n, out var id) && id == b.Id)) return "영역에 붙어 있지 않음";
            if (w.Funds < ExpandCost(w, bal, b)) return "자금 부족";
            return null;
        }

        public static void Expand(World w, Balance bal, Base b, Hex h)
        {
            if (CanExpand(w, bal, b, h) != null) return;
            w.Funds -= ExpandCost(w, bal, b);
            w.Territory[h] = b.Id;
        }

        // ---- 시설 ----

        /// <summary>칸 자체가 시설을 받을 수 있는지 (종류와 무관한 조건).</summary>
        public static string CanBuildHere(World w, Hex h)
        {
            var b = w.BaseOf(h);
            if (b == null || b.Owner != World.Player) return "아군 영역이 아님";
            if (b.Hex == h) return "거점 본부 칸";
            if (w.Facilities.ContainsKey(h)) return "이미 시설 있음";
            if (w.Map[h].Terrain == TerrainType.Sea) return "해상";
            return null;
        }

        public static string CanBuild(World w, Balance bal, Hex h, FacilityType type)
        {
            var here = CanBuildHere(w, h);
            if (here != null) return here;
            var b = w.BaseOf(h);
            var def = bal.Def(type);
            if (b.Grade < def.UnlockGrade) return $"거점 {def.UnlockGrade}등급 필요";
            if (FacilityCount(w, b) >= FacilityCap(bal, b)) return "시설 상한";
            var cell = w.Map[h];
            if (def.Site == SiteRequirement.Oil && !cell.Oil) return "유전 칸 필요";
            if (def.Site == SiteRequirement.Mountain && cell.Terrain != TerrainType.Mountain) return "산악 칸 필요";
            if (w.Funds < def.Cost) return "자금 부족";
            return null;
        }

        public static void Build(World w, Balance bal, Hex h, FacilityType type)
        {
            if (CanBuild(w, bal, h, type) != null) return;
            w.Funds -= bal.Def(type).Cost;
            w.Facilities[h] = new Facility { Type = type };
        }

        public static int FacilityUpgradeCost(Balance bal, Facility f) =>
            (int)(bal.Def(f.Type).Cost * (f.Grade + 1) * bal.FacilityUpgradeFactor);

        public static string CanUpgradeFacility(World w, Balance bal, Hex h)
        {
            if (!w.Facilities.TryGetValue(h, out var f)) return "시설 없음";
            if (!bal.Def(f.Type).Graded) return "등급 없는 시설";
            if (f.Grade >= bal.MaxFacilityGrade) return "최고 등급";
            if (w.Funds < FacilityUpgradeCost(bal, f)) return "자금 부족";
            return null;
        }

        public static void UpgradeFacility(World w, Balance bal, Hex h)
        {
            if (CanUpgradeFacility(w, bal, h) != null) return;
            var f = w.Facilities[h];
            w.Funds -= FacilityUpgradeCost(bal, f);
            f.Grade++;
        }

        /// <summary>철거 (환불 없음).</summary>
        public static void Demolish(World w, Hex h) => w.Facilities.Remove(h);

        // ---- 거점 등급 ----

        public static bool IsMaxGrade(Balance bal, Base b) => b.Grade >= bal.BaseUpgradeCost.Length - 1;

        public static IEnumerable<(FacilityType type, bool met)> UpgradeRequirements(World w, Balance bal, Base b) =>
            IsMaxGrade(bal, b)
                ? Enumerable.Empty<(FacilityType, bool)>()
                : bal.BaseUpgradeRequires[b.Grade + 1].Select(t => (t, Has(w, b, t)));

        public static string CanUpgradeBase(World w, Balance bal, Base b)
        {
            if (IsMaxGrade(bal, b)) return "최고 등급";
            var missing = UpgradeRequirements(w, bal, b).Where(r => !r.met).Select(r => bal.Def(r.type).Name).ToList();
            if (missing.Count > 0) return $"필요: {string.Join(", ", missing)}";
            if (w.Funds < bal.BaseUpgradeCost[b.Grade + 1]) return "자금 부족";
            return null;
        }

        public static void UpgradeBase(World w, Balance bal, Base b)
        {
            if (CanUpgradeBase(w, bal, b) != null) return;
            w.Funds -= bal.BaseUpgradeCost[b.Grade + 1];
            b.Grade++;
        }

        // ---- 전초기지 (기획서 5.5) ----

        /// <summary>가장 가까운 아군 거점까지의 거리 (거점이 없으면 int.MaxValue).</summary>
        public static int NearestOwnBaseDistance(World w, Hex h) =>
            w.Bases.Where(b => b.Owner == World.Player).Select(b => Hex.Distance(b.Hex, h)).DefaultIfEmpty(int.MaxValue).Min();

        /// <summary>전초기지 비용 = 기본 + 거리 × 거리당 비용.</summary>
        public static int OutpostCost(World w, Balance bal, Hex h) =>
            bal.OutpostCost + bal.OutpostCostPerDistance * NearestOwnBaseDistance(w, h);

        /// <summary>
        /// 전초기지 건설 조건: 적 영역이 아니고, 가장 가까운 아군 거점에서 OutpostMinDistance 이상,
        /// 어떤 편대든 공중급유를 포함해 닿을 수 있는 칸. (기획서 5.5 "영역 가장자리" 규칙을 플레이테스트 후 개정)
        /// </summary>
        public static string CanBuildOutpost(World w, Balance bal, Hex h)
        {
            if (!w.Map.Contains(h)) return "맵 밖";
            if (w.Map[h].Terrain == TerrainType.Sea) return "해상";
            var owner = w.BaseOf(h);
            if (owner != null && owner.Owner != World.Player) return "적 영역";
            if (w.BaseAt(h) != null) return "거점 본부 칸";
            if (w.Facilities.ContainsKey(h)) return "시설이 있는 칸";
            if (w.EnemyUnitAt(h) != null) return "적 부대가 있는 칸";
            var d = NearestOwnBaseDistance(w, h);
            if (d < bal.OutpostMinDistance) return $"아군 거점에서 {bal.OutpostMinDistance}칸 이상 떨어져야 함 (현재 {d})";
            if (!SquadronRules.ReachableByAir(w, bal, h) && !w.GroundUnits.Any(g => Hex.Distance(g.Hex, h) <= bal.GroundOutpostReach))
                return "어떤 편대(급유 포함)·지상군도 닿지 못함";
            if (w.Funds < OutpostCost(w, bal, h)) return "자금 부족";
            return null;
        }

        public static IEnumerable<Hex> OutpostCandidates(World w, Balance bal) =>
            w.Map.AllHexes().Where(h => CanBuildOutpost(w, bal, h) == null);

        /// <summary>새 등급 1 거점의 본부로 삼는다. 아군 영역 안이면 그 칸을 떼어 온다.</summary>
        public static Base BuildOutpost(World w, Balance bal, Hex h)
        {
            if (CanBuildOutpost(w, bal, h) != null) return null;
            w.Funds -= OutpostCost(w, bal, h);
            var b = new Base
            {
                Id = w.Bases.Max(x => x.Id) + 1,
                Name = $"전초기지 {w.Bases.Count(x => x.Owner == World.Player) + 1}",
                Hex = h,
                Grade = 1,
                Owner = World.Player,
            };
            w.Bases.Add(b);
            w.Territory[h] = b.Id;
            HqRules.AddTrust(w, bal, bal.TrustOutpost, $"{b.Name} 건설");
            return b;
        }

        static bool OwnedByPlayer(World w, Hex h) => w.BaseOf(h)?.Owner == World.Player;
    }
}
