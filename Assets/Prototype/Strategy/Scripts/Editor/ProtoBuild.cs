using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Prototype.Strategy.Editor
{
    /// <summary>
    /// 전략 프로토타입 Windows 빌드.
    /// 배치 모드: Unity.exe -batchmode -quit -projectPath . -executeMethod Prototype.Strategy.Editor.ProtoBuild.BuildWindowsBatch
    /// </summary>
    public static class ProtoBuild
    {
        const string ScenePath = "Assets/Prototype/Strategy/Scenes/StrategyProto.unity";
        const string OutputPath = "Builds/StrategyProto/StrategyProto.exe";

        [MenuItem("Prototype/Build Strategy (Windows)")]
        public static void BuildWindowsMenu()
        {
            var report = Build();
            var ok = report.summary.result == BuildResult.Succeeded;
            EditorUtility.DisplayDialog("Strategy Prototype",
                ok ? $"빌드 완료\n{Path.GetFullPath(OutputPath)}" : $"빌드 실패: {report.summary.result}\n콘솔을 확인하세요.", "확인");
            if (ok) EditorUtility.RevealInFinder(OutputPath);
        }

        /// <summary>-executeMethod용. 실패하면 종료 코드 1.</summary>
        public static void BuildWindowsBatch()
        {
            var report = Build();
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        static BuildReport Build()
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            });
            var s = report.summary;
            Debug.Log($"[StrategyProto] 빌드 {s.result}: {Path.GetFullPath(OutputPath)}, " +
                      $"{s.totalSize / (1024f * 1024f):0.0} MB, 오류 {s.totalErrors}, 경고 {s.totalWarnings}, {s.totalTime.TotalSeconds:0}초");
            return report;
        }
    }
}
