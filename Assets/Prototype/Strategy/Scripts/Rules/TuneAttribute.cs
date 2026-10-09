using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Prototype.Strategy.Rules
{
    /// <summary>
    /// 튜닝 메뉴에 노출할 수치 (int·float·bool 필드). 규칙 어셈블리는 Unity를 참조하지 않으므로 속성을 직접 정의한다.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class TuneAttribute : Attribute
    {
        public readonly string Group;
        public readonly string Label;
        public readonly float Min;
        public readonly float Max;
        /// <summary>시작할 때만 읽는 값 (바꾼 뒤 재시작해야 적용).</summary>
        public readonly bool Restart;

        public TuneAttribute(string group, string label, float min, float max, bool restart = false)
        {
            Group = group;
            Label = label;
            Min = min;
            Max = max;
            Restart = restart;
        }
    }

    /// <summary>튜닝 항목 열거·초기화·내보내기 (화면 없이도 쓰도록 규칙 쪽에 둔다).</summary>
    public static class Tuning
    {
        public sealed class Item
        {
            public object Target;
            public FieldInfo Field;
            public TuneAttribute Tune;
            public string Group;
            public string Label;
            public object Value => Field.GetValue(Target);
        }

        /// <summary>Balance와 기체 정의의 튜닝 항목 (그룹 순서는 선언 순서).</summary>
        public static List<Item> Items(Balance bal)
        {
            var list = new List<Item>();
            foreach (var f in typeof(Balance).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var t = f.GetCustomAttribute<TuneAttribute>();
                if (t != null) list.Add(new Item { Target = bal, Field = f, Tune = t, Group = t.Group, Label = t.Label });
            }
            foreach (var def in bal.Aircraft.Values)
            foreach (var f in typeof(AircraftDef).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var t = f.GetCustomAttribute<TuneAttribute>();
                if (t != null) list.Add(new Item { Target = def, Field = f, Tune = t, Group = $"기체 {def.Name}", Label = t.Label });
            }
            return list;
        }

        /// <summary>모든 튜닝 항목을 기본값으로 되돌린다 (같은 Balance 인스턴스를 유지해 World가 계속 참조하게).</summary>
        public static void ResetToDefaults(Balance bal)
        {
            var fresh = new Balance();
            var cur = Items(bal);
            var def = Items(fresh);
            for (var i = 0; i < cur.Count; i++) cur[i].Field.SetValue(cur[i].Target, def[i].Value);
        }

        /// <summary>기본값과 다른 항목 수.</summary>
        public static int ChangedCount(Balance bal)
        {
            var cur = Items(bal);
            var def = Items(new Balance());
            return cur.Where((t, i) => !Equals(t.Value, def[i].Value)).Count();
        }

        public static string ToMarkdown(Balance bal, string tester, string note)
        {
            var cur = Items(bal);
            var def = Items(new Balance());
            var sb = new StringBuilder();
            sb.AppendLine("# 전략 프로토타입 튜닝 값");
            sb.AppendLine();
            sb.AppendLine($"- 테스터: {(string.IsNullOrEmpty(tester) ? "—" : tester)}");
            if (!string.IsNullOrEmpty(note)) sb.AppendLine($"- {note}");
            sb.AppendLine($"- 기본값과 다른 항목: {ChangedCount(bal)}개 (굵게)");
            string group = null;
            for (var i = 0; i < cur.Count; i++)
            {
                var it = cur[i];
                if (it.Group != group)
                {
                    group = it.Group;
                    sb.AppendLine();
                    sb.AppendLine($"## {group}");
                    sb.AppendLine();
                    sb.AppendLine("| 항목 | 값 | 기본값 |");
                    sb.AppendLine("|---|---|---|");
                }
                var changed = !Equals(it.Value, def[i].Value);
                var v = Format(it.Value);
                sb.AppendLine($"| {it.Label} | {(changed ? $"**{v}**" : v)} | {Format(def[i].Value)} |");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 내보낸 Markdown(ToMarkdown 형식)을 읽어 값을 적용한다. "## 그룹" 아래 "| 항목 | 값 | 기본값 |" 줄을 그룹·항목 이름으로 맞춘다.
        /// 파일에 없는 항목은 그대로 둔다 (먼저 기본값으로 되돌린 뒤 적용하려면 resetFirst).
        /// </summary>
        public static (int applied, List<string> unknown) FromMarkdown(Balance bal, string text, bool resetFirst = true)
        {
            if (resetFirst) ResetToDefaults(bal);
            var items = Items(bal).ToDictionary(i => (i.Group, i.Label));
            var unknown = new List<string>();
            var applied = 0;
            string group = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("## "))
                {
                    group = line.Substring(3).Trim();
                    continue;
                }
                if (group == null || !line.StartsWith("|") || line.StartsWith("|---") || line.StartsWith("| 항목")) continue;
                var cells = line.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
                if (cells.Length < 2) continue;
                var label = cells[0];
                var value = cells[1].Replace("**", "").Trim();
                if (!items.TryGetValue((group, label), out var it))
                {
                    unknown.Add($"{group} / {label}");
                    continue;
                }
                if (TryParse(it.Value, value, out var parsed))
                {
                    it.Field.SetValue(it.Target, parsed);
                    applied++;
                }
                else unknown.Add($"{group} / {label} = {value} (읽을 수 없음)");
            }
            return (applied, unknown);
        }

        static bool TryParse(object current, string s, out object value)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            value = null;
            switch (current)
            {
                case bool _:
                    if (s == "켜짐") value = true;
                    else if (s == "꺼짐") value = false;
                    return value != null;
                case int _:
                    if (!int.TryParse(s, System.Globalization.NumberStyles.Integer, inv, out var i)) return false;
                    value = i;
                    return true;
                case float _:
                    if (!float.TryParse(s, System.Globalization.NumberStyles.Float, inv, out var f)) return false;
                    value = f;
                    return true;
            }
            return false;
        }

        public static string Format(object v) => v switch
        {
            float f => f.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            bool b => b ? "켜짐" : "꺼짐",
            _ => v?.ToString() ?? "",
        };
    }
}
