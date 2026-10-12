using System;
using System.Collections.Generic;
using System.Linq;

namespace Prototype.Strategy.Rules
{
    public enum SiteType
    {
        Refinery, // 정유
        Mine,     // 광산
        City,     // 도시
    }

    /// <summary>
    /// 자원 타일 (도시판): 맵 위의 정유·광산·도시. 등급과 소유 세력이 있고, 소유 세력이 등급에 비례해 수입을 얻는다.
    /// 지상군(아군·적 모두)이 칸에 들어가면 점령, 폭격이 성공하면 등급 −1 (0이면 중립 폐허).
    /// </summary>
    public sealed class ResourceSite
    {
        public const int Neutral = -1;

        public int Id;
        public SiteType Type;
        public string Name;
        public Hex Hex;
        public int Grade;
        public int Owner = Neutral; // World.Player(0), 적 세력 Id, 중립 −1
    }

    public static class SiteRules
    {
        public static string TypeName(SiteType t) => t switch
        {
            SiteType.Refinery => "정유",
            SiteType.Mine => "광산",
            _ => "도시",
        };

        public static int IncomePerGrade(Balance bal, SiteType t) => t switch
        {
            SiteType.Refinery => bal.RefineryIncome,
            SiteType.Mine => bal.MineIncome,
            _ => bal.CityIncome,
        };

        public static int BaseCost(Balance bal, SiteType t) => t switch
        {
            SiteType.Refinery => bal.RefineryCost,
            SiteType.Mine => bal.MineCost,
            _ => bal.CityCost,
        };

        public static int Income(Balance bal, ResourceSite s) => IncomePerGrade(bal, s.Type) * s.Grade;

        public static IEnumerable<ResourceSite> OwnedBy(World w, int owner) => w.Sites.Where(s => s.Owner == owner);

        // ---- 등급 올리기 (0 → 1은 폐허 복구) ----

        public static int UpgradeCost(Balance bal, ResourceSite s) =>
            (int)(BaseCost(bal, s.Type) * Math.Max(1, s.Grade + 1) * bal.FacilityUpgradeFactor);

        public static string CanUpgrade(World w, Balance bal, ResourceSite s)
        {
            if (s.Owner != World.Player) return "아군 소유가 아님";
            if (s.Grade >= bal.MaxSiteGrade) return "최고 등급";
            if (w.Funds < UpgradeCost(bal, s)) return "자금 부족";
            return null;
        }

        public static void Upgrade(World w, Balance bal, ResourceSite s)
        {
            if (CanUpgrade(w, bal, s) != null) return;
            w.Funds -= UpgradeCost(bal, s);
            s.Grade++;
            w.Log("자원 등급", $"{s.Name} → {s.Grade}등급");
        }

        // ---- 점령 ----

        /// <summary>owner 쪽 지상 부대가 칸에 들어옴: 다른 소유면 점령 (수비대 없음). 폐허(0등급)도 소유만 바뀐다.</summary>
        public static bool Capture(World w, ResourceSite s, int owner, string by)
        {
            if (s.Owner == owner) return false;
            var before = s.Owner;
            s.Owner = owner;
            var ownerName = owner == World.Player ? "아군" : w.FactionById(owner)?.Name ?? "적";
            w.Log("자원 점령", $"{s.Name} ← {by} ({OwnerName(w, before)} → {ownerName})");
            if (owner == World.Player || before == World.Player || Vision.IsVisible(w, w.VisionBalance, s.Hex))
                w.EnemyEvents.Add($"{s.Name} 점령: {by} ({OwnerName(w, before)} → {ownerName})");
            w.AddEvent(s.Hex, "점령", owner == World.Player || before == World.Player || Vision.IsVisible(w, w.VisionBalance, s.Hex));
            return true;
        }

        public static string OwnerName(World w, int owner) =>
            owner == World.Player ? "아군" : owner == ResourceSite.Neutral ? "중립" : w.FactionById(owner)?.Name ?? "적";

        // ---- 폭격 ----

        /// <summary>폭격으로 시설을 모두 깨면 등급 −1. 0이 되면 중립 폐허.</summary>
        public static string Damage(World w, ResourceSite s)
        {
            s.Grade = Math.Max(0, s.Grade - 1);
            if (s.Grade == 0)
            {
                s.Owner = ResourceSite.Neutral;
                w.Log("자원 파괴", $"{s.Name} 폐허");
                return $"{s.Name} 폐허 (중립, 수입 없음)";
            }
            w.Log("자원 파괴", $"{s.Name} → {s.Grade}등급");
            return $"{s.Name} 등급 {s.Grade + 1} → {s.Grade}";
        }
    }
}
