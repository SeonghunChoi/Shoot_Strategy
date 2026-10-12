using System;
using System.Collections.Generic;
using System.Linq;

namespace Prototype.Strategy.Rules
{
    /// <summary>
    /// 거점·시설 명령 (도시판: 거점 = 칸 하나, 시설은 거점 안의 목록). 무상태 서비스.
    /// Can*은 불가 사유를 돌려주고(가능하면 null), 실행 함수는 Can*이 통과했을 때만 상태를 바꾼다.
    /// 영역은 직접 넓히지 않고 거점 등급에 따라 자동으로 정해진다 (RefreshTerritory).
    /// </summary>
    public static class BaseRules
    {
        // ---- 조회 ----

        public static Facility Get(Base b, FacilityType type) => b.Facilities.FirstOrDefault(f => f.Type == type);

        public static bool Has(World w, Base b, FacilityType type) => Get(b, type) != null;

        public static int GradeOf(Base b, FacilityType type) => Get(b, type)?.Grade ?? 0;

        /// <summary>시설 규모 = 등급 합 (등급 없는 시설은 1). 거점 등급별 상한과 비교한다.</summary>
        public static int FacilityPoints(Base b) => b.Facilities.Sum(f => f.Grade);

        public static int FacilityCap(Balance bal, Base b) => bal.FacilityCap[Math.Min(b.Grade, bal.FacilityCap.Length - 1)];

        /// <summary>활주로 등급 = 운용 가능한 최대 기체 크기 (0 없음, 1 소형, 2 중형, 3 대형).</summary>
        public static int MaxAircraftSize(Base b) => GradeOf(b, FacilityType.Runway);

        public static string SizeName(int size) => size switch
        {
            0 => "없음",
            1 => "소형",
            2 => "중형",
            _ => "대형",
        };

        // ---- 영역 (자동) ----

        public static int TerritoryRadius(Balance bal, Base b) => bal.TerritoryRadiusByGrade[Math.Min(b.Grade, bal.TerritoryRadiusByGrade.Length - 1)];

        /// <summary>
        /// 거점 등급에 따라 영역을 다시 정한다: 거점마다 반경 안의 칸, 겹치면 더 가까운 거점, 같으면 먼저 세운 거점.
        /// 영역은 시야·적 영역 격추 판정·전초기지 거리에 쓴다.
        /// </summary>
        public static void RefreshTerritory(World w, Balance bal)
        {
            w.Territory.Clear();
            foreach (var b in w.Bases.OrderBy(x => x.Id))
            {
                foreach (var h in Hex.Range(b.Hex, TerritoryRadius(bal, b)))
                {
                    if (!w.Map.Contains(h)) continue;
                    if (w.BaseAt(h) is { } other && other.Id != b.Id) continue;
                    if (w.Territory.TryGetValue(h, out var id))
                    {
                        var cur = w.BaseById(id);
                        if (cur.Hex == h || Hex.Distance(cur.Hex, h) <= Hex.Distance(b.Hex, h)) continue;
                    }
                    w.Territory[h] = b.Id;
                }
            }
        }

        // ---- 시설 ----

        public static string CanBuild(World w, Balance bal, Base b, FacilityType type)
        {
            if (b == null || b.Owner != World.Player) return "아군 거점이 아님";
            var def = bal.Def(type);
            if (Has(w, b, type)) return def.Graded ? "이미 있음 (등급을 올리세요)" : "이미 있음";
            if (b.Grade < def.UnlockGrade) return $"거점 {def.UnlockGrade}등급 필요";
            if (bal.HardFacilityCap && FacilityPoints(b) >= FacilityCap(bal, b)) return "시설 상한";
            if (w.Funds < BuildCost(w, bal, b, type)) return "자금 부족";
            return null;
        }

        /// <summary>이 거점에 시설 규모 1을 더하면 상한을 몇 번째로 넘는지 (0이면 상한 안).</summary>
        public static int NextOverCap(World w, Balance bal, Base b) => Math.Max(0, FacilityPoints(b) + 1 - FacilityCap(bal, b));

        static float OverCapFactor(World w, Balance bal, Base b) => 1f + bal.FacilityOverCapCostRatio * NextOverCap(w, bal, b);

        /// <summary>건설비 = 기본 × (1 + 할증 비율 × 상한 초과 순번). 상한 안이면 기본 그대로.</summary>
        public static int BuildCost(World w, Balance bal, Base b, FacilityType type) =>
            (int)Math.Round(bal.Def(type).Cost * OverCapFactor(w, bal, b));

        public static void Build(World w, Balance bal, Base b, FacilityType type)
        {
            if (CanBuild(w, bal, b, type) != null) return;
            w.Funds -= BuildCost(w, bal, b, type);
            b.Facilities.Add(new Facility { Type = type });
        }

        /// <summary>등급 상승 비용 = 건설비 × 다음 등급 × 계수, 상한을 넘으면 건설과 같은 할증.</summary>
        public static int FacilityUpgradeCost(World w, Balance bal, Base b, Facility f) =>
            (int)Math.Round(bal.Def(f.Type).Cost * (f.Grade + 1) * bal.FacilityUpgradeFactor * OverCapFactor(w, bal, b));

        public static string CanUpgradeFacility(World w, Balance bal, Base b, FacilityType type)
        {
            var f = Get(b, type);
            if (f == null) return "시설 없음";
            var def = bal.Def(type);
            if (!def.Graded) return "등급 없는 시설";
            if (f.Grade >= def.MaxGrade) return "최고 등급";
            if (bal.HardFacilityCap && FacilityPoints(b) >= FacilityCap(bal, b)) return "시설 상한";
            if (w.Funds < FacilityUpgradeCost(w, bal, b, f)) return "자금 부족";
            return null;
        }

        public static void UpgradeFacility(World w, Balance bal, Base b, FacilityType type)
        {
            if (CanUpgradeFacility(w, bal, b, type) != null) return;
            var f = Get(b, type);
            w.Funds -= FacilityUpgradeCost(w, bal, b, f);
            f.Grade++;
        }

        /// <summary>철거 (환불 없음).</summary>
        public static void Demolish(World w, Base b, FacilityType type) => b.Facilities.RemoveAll(f => f.Type == type);

        /// <summary>거점을 잃거나 빼앗았을 때: 시설 규모가 상한 안으로 들어올 때까지 나중에 지은 시설부터 등급을 깎고 없앤다. 깎인 규모를 돌려준다.</summary>
        public static int TrimToCap(Balance bal, Base b)
        {
            var cap = FacilityCap(bal, b);
            var cut = 0;
            for (var i = b.Facilities.Count - 1; i >= 0 && FacilityPoints(b) > cap; i--)
            {
                var f = b.Facilities[i];
                while (f.Grade > 1 && FacilityPoints(b) > cap)
                {
                    f.Grade--;
                    cut++;
                }
                if (FacilityPoints(b) > cap)
                {
                    b.Facilities.RemoveAt(i);
                    cut++;
                }
            }
            return cut;
        }

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
            RefreshTerritory(w, bal);
            w.Log("거점 등급", $"{b.Name} → {b.Grade}등급");
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
        /// 어떤 편대든 공중급유를 포함해 닿을 수 있는 칸. 자원 타일 칸은 안 됨.
        /// </summary>
        public static string CanBuildOutpost(World w, Balance bal, Hex h)
        {
            if (!w.Map.Contains(h)) return "맵 밖";
            if (w.Map[h].Terrain == TerrainType.Sea) return "해상";
            var owner = w.BaseOf(h);
            if (owner != null && owner.Owner != World.Player) return "적 영역";
            if (w.BaseAt(h) != null) return "거점 본부 칸";
            if (w.SiteAt(h) != null) return "자원 타일";
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

        /// <summary>새 등급 1 거점의 본부로 삼는다. 영역은 자동으로 다시 정한다.</summary>
        public static Base BuildOutpost(World w, Balance bal, Hex h)
        {
            if (CanBuildOutpost(w, bal, h) != null) return null;
            var cost = OutpostCost(w, bal, h);
            w.Funds -= cost;
            var b = new Base
            {
                Id = w.Bases.Max(x => x.Id) + 1,
                Name = $"전초기지 {w.Bases.Count(x => x.Owner == World.Player) + 1}",
                Hex = h,
                Grade = 1,
                Owner = World.Player,
            };
            w.Bases.Add(b);
            RefreshTerritory(w, bal);
            HqRules.AddTrust(w, bal, bal.TrustOutpost, $"{b.Name} 건설");
            w.Log("전초기지", $"{b.Name} {h.ToOffset()} · 비용 {cost}");
            return b;
        }
    }
}
