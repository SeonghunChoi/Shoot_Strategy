using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Prototype.Combat
{
    /// <summary>
    /// 검증계획 7.2 수집 로그. 출격(리셋 → 종료·전멸·중단) 1건마다 runs.csv에 1행,
    /// 사건마다 events.csv에 1행을 프로젝트 루트의 PlaytestLogs/에 쓴다 (Git 제외, 로컬 전용).
    /// Excel에서 한글이 깨지지 않도록 UTF-8 BOM으로 쓴다.
    /// </summary>
    public sealed class PlaytestLogger
    {
        public static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "PlaytestLogs"));

        readonly CombatContext _ctx;
        readonly List<string> _events = new();

        public string RunId { get; private set; }
        public bool Running { get; private set; }

        // 출격 누적 값 (결과 창에서도 사용)
        public int SwitchTap, SwitchRing, SwitchAuto, RingCancel;
        public int TurnAuto, TurnManual;
        public int VulnerableHits;

        public PlaytestLogger(CombatContext ctx) => _ctx = ctx;

        public void BeginRun()
        {
            if (Running) EndRun(_ctx.StageTime > 1f ? "중단" : null);
            RunId = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            Running = true;
            SwitchTap = SwitchRing = SwitchAuto = RingCancel = 0;
            TurnAuto = TurnManual = 0;
            VulnerableHits = 0;
            _events.Clear();
        }

        /// <param name="result">null이면 기록하지 않고 버린다 (시작 직후 리셋 등)</param>
        public void EndRun(string result)
        {
            if (!Running) return;
            Running = false;
            if (result == null) return;
            try
            {
                Directory.CreateDirectory(Folder);
                AppendCsv("events.csv", "run_id,stage_time,type,detail,ground_x,y,aircraft", _events);
                AppendCsv("runs.csv", RunHeader, new[] { RunRow(result) });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CombatProto] 로그 기록 실패: {e.Message}");
            }
        }

        // ---- 사건 ----

        public void Switch(string method, SquadronMember from, SquadronMember to)
        {
            if (method == "탭") SwitchTap++;
            else if (method == "링") SwitchRing++;
            else SwitchAuto++;
            Event("전환", $"{method}:{from.Def.Name}>{to.Def.Name}");
        }

        public void RingCanceled()
        {
            RingCancel++;
            Event("링 취소", "");
        }

        public void Turn(bool manual)
        {
            if (manual) TurnManual++;
            else TurnAuto++;
            Event("선회", manual ? "임의" : "자동");
        }

        public void TerrainHit(string section) => Event("지형 충돌", section);

        public void PlayerHit(string source, float damage, bool lost)
        {
            var vulnerable = _ctx.Player.Vulnerable;
            if (vulnerable) VulnerableHits++;
            Event("피격", $"{source}:{damage:0}{(vulnerable ? ":무방비" : "")}");
            if (lost) Event("기체 손실", _ctx.Player.Member.Def.Name);
        }

        public void Event(string type, string detail)
        {
            if (!Running) return;
            var p = _ctx.Player;
            _events.Add(string.Join(",", RunId, F(_ctx.StageTime), Csv(type), Csv(detail),
                F(p.Position.x + _ctx.Scroll.Distance), F(p.Y), Csv(p.Member?.Def.Name ?? "")));
        }

        // ---- 출격 1행 ----

        const string RunHeader =
            "run_id,tester,result,stage_time,distance,mode,pad_layout,base_scroll,offset_speed,offset_range,decel_range,accel_range," +
            "vulnerable_time,ring_slow,ring_slow_scale,hold_threshold,turn_duration,manual_turn,camera,radar,terrain_damage,terrain_bounce,lead_accuracy," +
            "air_kills,ground_kills,boss_defeated,boss_parts,aircraft_lost,terrain_hits,vulnerable_hits," +
            "switch_tap,switch_ring,switch_auto,ring_cancel,turn_auto,turn_manual,fg_1,fg_2,fg_3";

        string RunRow(string result)
        {
            var t = _ctx.Tuning;
            var members = _ctx.Squadron.Members;
            string Fg(int i) => i < members.Count ? F(members[i].ForegroundTime) : "";
            return string.Join(",",
                RunId, Csv(t.testerName), Csv(result), F(_ctx.StageTime), F(_ctx.Scroll.Distance),
                t.horizontalMode, t.padLayout, F(t.baseScrollSpeed), F(t.offsetSpeed), F(t.offsetRange), F(t.decelRange), F(t.accelRange),
                F(t.switchVulnerableTime), t.ringSlowMotion ? 1 : 0, F(t.ringSlowScale), F(t.switchHoldThreshold), F(t.turnDuration),
                t.manualTurnEnabled ? 1 : 0, t.cameraFollow, t.radarMode, F(t.terrainDamage), F(t.terrainBounce), F(t.enemyLeadAccuracy),
                _ctx.AirKills, _ctx.GroundKills, _ctx.Boss.Defeated ? 1 : 0, _ctx.BossPartKills, _ctx.PlayerDowns, _ctx.TerrainHits, VulnerableHits,
                SwitchTap, SwitchRing, SwitchAuto, RingCancel, TurnAuto, TurnManual, Fg(0), Fg(1), Fg(2));
        }

        // ---- 튜닝 값 내보내기 (플레이테스트 기록 §4용) ----

        public static string ExportTuning(CombatTuning tuning, IReadOnlyList<SquadronMember> members)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# 튜닝 값 ({DateTime.Now:yyyy-MM-dd HH:mm})").AppendLine();
            sb.AppendLine("## CombatTuning").AppendLine();
            sb.AppendLine("| 구분 | 항목 | 값 |").AppendLine("|---|---|---|");
            var group = "";
            foreach (var field in typeof(CombatTuning).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var header = field.GetCustomAttribute<HeaderAttribute>();
                if (header != null) group = header.header;
                var label = field.GetCustomAttribute<TooltipAttribute>()?.tooltip ?? field.Name;
                sb.AppendLine($"| {group} | {label} | {Value(field.GetValue(tuning))} |");
            }

            sb.AppendLine().AppendLine("## 기체 정의 (배율은 공격기 기준값에 곱함)").AppendLine();
            var defFields = typeof(AircraftDefinition).GetFields(BindingFlags.Public | BindingFlags.Instance);
            sb.Append("| 항목 |");
            foreach (var m in members) sb.Append($" {m.Def.Name} |");
            sb.AppendLine().Append("|---|");
            foreach (var _ in members) sb.Append("---|");
            sb.AppendLine();
            foreach (var f in defFields)
            {
                if (f.Name is nameof(AircraftDefinition.Name) or nameof(AircraftDefinition.Color)) continue;
                sb.Append($"| {f.Name} |");
                foreach (var m in members) sb.Append($" {Value(f.GetValue(m.Def))} |");
                sb.AppendLine();
            }

            Directory.CreateDirectory(Folder);
            var path = Path.Combine(Folder, $"tuning_{DateTime.Now:yyyyMMdd_HHmmss}.md");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return path;
        }

        public static void OpenFolder()
        {
            Directory.CreateDirectory(Folder);
            Application.OpenURL("file:///" + Folder.Replace('\\', '/'));
        }

        // ---- 도우미 ----

        static void AppendCsv(string file, string header, IEnumerable<string> rows)
        {
            var path = Path.Combine(Folder, file);
            var sb = new StringBuilder();
            if (!File.Exists(path)) sb.AppendLine(header);
            foreach (var row in rows) sb.AppendLine(row);
            File.AppendAllText(path, sb.ToString(), new UTF8Encoding(true)); // 새 파일이면 BOM 포함
        }

        static string Value(object v) => v switch
        {
            float f => f.ToString("0.###", CultureInfo.InvariantCulture),
            bool b => b ? "켜기" : "끄기",
            Enum e => e.GetType().GetField(e.ToString())?.GetCustomAttribute<InspectorNameAttribute>()?.displayName ?? e.ToString(),
            null => "",
            _ => v.ToString(),
        };

        static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        static string Csv(string s) => s.IndexOfAny(new[] { ',', '"', '\n' }) < 0 ? s : $"\"{s.Replace("\"", "\"\"")}\"";
    }
}
