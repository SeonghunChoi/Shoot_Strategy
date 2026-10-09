using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Prototype.Strategy.Rules
{
    /// <summary>
    /// 플레이 로그 CSV (검증계획 6.1). runs.csv(판) · turns.csv(턴) · events.csv(사건), UTF-8 BOM.
    /// 실제 시간은 화면 쪽이 재서 넘긴다 (S8). 규칙 쪽에 두어 시뮬레이션도 같은 형식을 쓸 수 있다.
    /// </summary>
    public sealed class PlaytestLog
    {
        const string RunsHeader = "run_id,tester,seed,started,result,turns,minutes,avg_turn_seconds,sorties,attacked,bases_lost,captured,directives_done,directives_failed,directives_refused,final_funds,final_trust,tuning_changed";
        const string TurnsHeader = "run_id,tester,turn,seconds,funds,net,income,expense,lines,deficit_streak,trust,sorties,defense_gaps,attacks,bases_lost,bases,hexes,squadrons,aircraft,pilots,ground_units,directive";
        const string EventsHeader = "run_id,turn,kind,detail";

        readonly string _folder;
        int _eventsFlushed;
        string _started;
        ulong _seed;

        public string RunId { get; private set; }
        public string Tester { get; set; } = "";
        public bool Running { get; private set; }
        public float RunSeconds { get; private set; }
        public int TurnsLogged { get; private set; }

        public PlaytestLog(string folder) => _folder = folder;

        public string Folder => _folder;

        public void BeginRun(World w, ulong seed)
        {
            RunId = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            _started = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            _seed = seed;
            _eventsFlushed = w.Events.Count;
            RunSeconds = 0f;
            TurnsLogged = 0;
            Running = true;
        }

        /// <summary>턴 종료 직후 호출. seconds = 그 턴에 플레이어가 실제로 쓴 시간.</summary>
        public void LogTurn(World w, TurnReport rep, float seconds, int defenseGaps)
        {
            if (!Running || rep == null) return;
            RunSeconds += seconds;
            TurnsLogged++;
            var income = rep.Lines.Where(l => l.Amount > 0).Sum(l => l.Amount);
            var expense = rep.Lines.Where(l => l.Amount < 0).Sum(l => l.Amount);
            var lines = string.Join("; ", rep.Lines.Select(l => $"{l.Label} {l.Amount}"));
            var sorties = w.Events.Count(e => e.Turn == rep.Turn && e.Kind == "출격");
            var player = w.Bases.Where(b => b.Owner == World.Player).ToList();
            Append("turns.csv", TurnsHeader, Row(
                RunId, Tester, rep.Turn, seconds.ToString("0.0", CultureInfo.InvariantCulture), rep.FundsAfter, rep.Net, income, expense, lines,
                rep.DeficitStreak, w.Trust, sorties, defenseGaps, w.Intercepts.Count, w.Intercepts.Count(i => i.Captured),
                player.Count, w.OwnedHexes(World.Player).Count(), w.Squadrons.Count, w.Aircraft.Count, w.Pilots.Count, w.GroundUnits.Count,
                w.Directive?.Text ?? ""));
            FlushEvents(w);
        }

        public void FlushEvents(World w)
        {
            if (!Running) return;
            for (; _eventsFlushed < w.Events.Count; _eventsFlushed++)
            {
                var e = w.Events[_eventsFlushed];
                Append("events.csv", EventsHeader, Row(RunId, e.Turn, e.Kind, e.Detail));
            }
        }

        /// <summary>판 종료 (승리·해임·상실·파산·중단). 1턴도 안 넘긴 판은 기록하지 않는다.</summary>
        public void EndRun(World w, Balance bal, string result, float extraSeconds = 0f)
        {
            if (!Running) return;
            Running = false;
            RunSeconds += extraSeconds;
            FlushEventsAfterEnd(w);
            if (TurnsLogged == 0) return;
            var st = w.Stats;
            Append("runs.csv", RunsHeader, Row(
                RunId, Tester, _seed, _started, result, w.Turn,
                (RunSeconds / 60f).ToString("0.0", CultureInfo.InvariantCulture),
                (RunSeconds / Math.Max(1, TurnsLogged)).ToString("0.0", CultureInfo.InvariantCulture),
                st.Sorties, st.BasesAttacked, st.BasesLost, st.BasesCaptured, st.DirectivesDone, st.DirectivesFailed, st.DirectivesRefused,
                w.Funds, w.Trust, Tuning.ChangedCount(bal)));
        }

        void FlushEventsAfterEnd(World w)
        {
            Running = true;
            FlushEvents(w);
            Running = false;
        }

        /// <summary>폴더의 내보낸 튜닝 파일 (최신순).</summary>
        public string[] TuningFiles() =>
            Directory.Exists(_folder) ? Directory.GetFiles(_folder, "tuning_*.md").OrderByDescending(f => f).ToArray() : new string[0];

        public (int applied, System.Collections.Generic.List<string> unknown) ImportTuning(Balance bal, string path) =>
            Tuning.FromMarkdown(bal, File.ReadAllText(path, Encoding.UTF8));

        public string ExportTuning(Balance bal)
        {
            Directory.CreateDirectory(_folder);
            var path = Path.Combine(_folder, $"tuning_{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.md");
            File.WriteAllText(path, Tuning.ToMarkdown(bal, Tester, RunId != null ? $"판 {RunId}" : null), new UTF8Encoding(true));
            return path;
        }

        void Append(string file, string header, string row)
        {
            Directory.CreateDirectory(_folder);
            var path = Path.Combine(_folder, file);
            var isNew = !File.Exists(path);
            using var sw = new StreamWriter(path, append: true, new UTF8Encoding(isNew));
            if (isNew) sw.WriteLine(header);
            sw.WriteLine(row);
        }

        public static string Row(params object[] cells) => string.Join(",", cells.Select(Csv));

        static string Csv(object v)
        {
            var s = v switch
            {
                float f => f.ToString("0.###", CultureInfo.InvariantCulture),
                double d => d.ToString("0.###", CultureInfo.InvariantCulture),
                _ => v?.ToString() ?? "",
            };
            return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
        }
    }
}
