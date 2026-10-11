using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Prototype.Strategy.Editor
{
    /// <summary>
    /// 전략 프로토타입 Windows 빌드 + 테스터 배포용 zip (사용 설명서·테스트 가이드 포함).
    /// 배치 모드: Unity.exe -batchmode -quit -projectPath . -executeMethod Prototype.Strategy.Editor.ProtoBuild.BuildWindowsBatch
    /// </summary>
    public static class ProtoBuild
    {
        const string ScenePath = "Assets/Prototype/Strategy/Scenes/StrategyProto.unity";
        const string OutputFolder = "Builds/StrategyProto_Flight";
        const string OutputPath = OutputFolder + "/StrategyProto.exe";
        const string ZipPath = "Builds/ShootStrategyStrategy_Flight_Test.zip";

        // zip 안 이름은 ASCII로 (압축 도구에 따라 한글 파일 이름이 깨짐). Manual = 사용 설명서, TestGuide = 테스트 가이드
        static readonly (string source, string entry)[] Documents =
        {
            ("DevDocuments/횡스크롤_전략_비행슈팅_전략프로토타입_사용설명서.md", "Manual.md"),
            ("DevDocuments/횡스크롤_전략_비행슈팅_전략프로토타입_테스트가이드.md", "TestGuide.md"),
        };

        [MenuItem("Prototype/Build Strategy (Windows)")]
        public static void BuildWindowsMenu()
        {
            var report = Build();
            var ok = report.summary.result == BuildResult.Succeeded;
            var zip = ok ? Package() : null;
            EditorUtility.DisplayDialog("Strategy Prototype",
                ok ? $"빌드 완료\n{Path.GetFullPath(OutputPath)}\n\n배포용 zip\n{zip}" : $"빌드 실패: {report.summary.result}\n콘솔을 확인하세요.", "확인");
            if (ok) EditorUtility.RevealInFinder(ZipPath);
        }

        /// <summary>-executeMethod용. 실패하면 종료 코드 1.</summary>
        public static void BuildWindowsBatch()
        {
            var report = Build();
            var ok = report.summary.result == BuildResult.Succeeded;
            if (ok) Package();
            EditorApplication.Exit(ok ? 0 : 1);
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

        /// <summary>
        /// 빌드 폴더를 zip으로 묶는다. 배포하면 안 되는 Burst 디버그 정보와, 개발 PC에서 빌드를 실행해 생긴 기록 폴더는 뺀다.
        /// </summary>
        static string Package()
        {
            var zipFull = Path.GetFullPath(ZipPath);
            if (File.Exists(zipFull)) File.Delete(zipFull);
            var root = Path.GetFullPath(OutputFolder);
            using (var zip = ZipFile.Open(zipFull, ZipArchiveMode.Create))
            {
                foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    var rel = file.Substring(root.Length + 1).Replace('\\', '/');
                    if (rel.Contains("_DoNotShip") || rel.StartsWith("PlaytestLogs/")) continue;
                    zip.CreateEntryFromFile(file, rel, System.IO.Compression.CompressionLevel.Optimal);
                }
                foreach (var (source, entry) in Documents)
                {
                    if (File.Exists(source)) zip.CreateEntryFromFile(source, entry, System.IO.Compression.CompressionLevel.Optimal);
                    else Debug.LogWarning($"[StrategyProto] 문서 없음: {source}");
                }
            }
            Debug.Log($"[StrategyProto] 배포용 zip: {zipFull}, {new FileInfo(zipFull).Length / (1024f * 1024f):0.0} MB");
            return zipFull;
        }
    }
}
