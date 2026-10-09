using System.IO;
using System.Linq;
using Prototype.Strategy.Rules;
using UnityEditor;
using UnityEngine;

namespace Prototype.Strategy.Editor
{
    /// <summary>
    /// 헤드리스 시뮬레이션 메뉴. 규칙만 돌리므로 플레이 모드가 필요 없다.
    /// 결과: PlaytestLogs/strategy/sim_*.csv, sim_*.md
    /// </summary>
    public static class StrategySimMenu
    {
        [MenuItem("Prototype/Strategy Sim (50 seeds x 100 turns)")]
        static void RunDefault() => Run(50, 100);

        [MenuItem("Prototype/Strategy Sim (quick: 10 seeds x 60 turns)")]
        static void RunQuick() => Run(10, 60);

        [MenuItem("Prototype/Open Strategy Logs")]
        static void OpenLogs()
        {
            var folder = StrategyBootstrap.LogFolder;
            Directory.CreateDirectory(folder);
            EditorUtility.RevealInFinder(folder);
        }

        static void Run(int seeds, int turns)
        {
            var folder = StrategyBootstrap.LogFolder;
            try
            {
                var md = SimRunner.RunBatch(folder, seeds, turns, new[] { 2, 3, 4 },
                    p => EditorUtility.DisplayProgressBar("Strategy Sim", p, 0.5f));
                Debug.Log($"[StrategyProto] 시뮬레이션 완료: {folder}\n{md}");
                var latest = new DirectoryInfo(folder).GetFiles("sim_*.md").OrderByDescending(f => f.LastWriteTime).First();
                EditorUtility.RevealInFinder(latest.FullName);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
