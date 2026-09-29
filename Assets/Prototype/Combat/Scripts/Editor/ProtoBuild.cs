using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Prototype.Combat.Editor
{
    /// <summary>
    /// 테스터 배포용 Windows 빌드.
    /// 런타임에 만드는 재질이 빌드에서 셰이더 누락으로 깨지지 않도록 Resources에 템플릿 재질을 둔다.
    /// 배치 모드: Unity.exe -batchmode -quit -projectPath . -executeMethod Prototype.Combat.Editor.ProtoBuild.BuildWindowsBatch
    /// </summary>
    public static class ProtoBuild
    {
        const string ResourcesFolder = "Assets/Prototype/Combat/Resources";
        const string ScenePath = "Assets/Prototype/Combat/Scenes/CombatProto.unity";
        const string OutputPath = "Builds/CombatProto/ShootStrategyCombat.exe";
        const string TuningPath = "Assets/Prototype/Combat/Settings/CombatTuning.asset";

        [MenuItem("Prototype/Create Build Materials")]
        public static void EnsureMaterials()
        {
            if (!AssetDatabase.IsValidFolder(ResourcesFolder))
                AssetDatabase.CreateFolder("Assets/Prototype/Combat", "Resources");

            EnsureMaterial(Shapes.LitTemplatePath, "Universal Render Pipeline/Lit");
            EnsureMaterial(Shapes.UnlitTemplatePath, "Universal Render Pipeline/Unlit");
            AssetDatabase.SaveAssets();
        }

        static void EnsureMaterial(string name, string shaderName)
        {
            var path = $"{ResourcesFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find(shaderName));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.enableInstancing = true; // 탄 인스턴싱 변형도 빌드에 포함
            EditorUtility.SetDirty(mat);
        }

        [MenuItem("Prototype/Build Windows")]
        public static void BuildWindowsMenu()
        {
            var report = Build();
            var ok = report.summary.result == BuildResult.Succeeded;
            EditorUtility.DisplayDialog("Combat Prototype",
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
            EnsureMaterials();

            // 테스터 이름은 개발자 PC의 값이 배포본에 들어가지 않도록 빌드하는 동안만 비운다
            var tuning = AssetDatabase.LoadAssetAtPath<CombatTuning>(TuningPath);
            var tester = tuning != null ? tuning.testerName : null;
            SetTester(tuning, "");

            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = OutputPath,
                    target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.None,
                });
            }
            finally
            {
                // 빌드 중 에셋이 다시 로드될 수 있으므로 복원 전에 다시 불러온다
                SetTester(AssetDatabase.LoadAssetAtPath<CombatTuning>(TuningPath), tester);
            }

            var s = report.summary;
            Debug.Log($"[CombatProto] 빌드 {s.result}: {Path.GetFullPath(OutputPath)}, " +
                      $"{s.totalSize / (1024f * 1024f):0.0} MB, 오류 {s.totalErrors}, 경고 {s.totalWarnings}, {s.totalTime.TotalSeconds:0}초");
            return report;
        }

        static void SetTester(CombatTuning tuning, string name)
        {
            if (tuning == null || name == null) return;
            tuning.testerName = name;
            EditorUtility.SetDirty(tuning);
            AssetDatabase.SaveAssets();
        }
    }
}
