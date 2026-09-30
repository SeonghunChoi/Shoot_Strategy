using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Prototype.Strategy.Editor
{
    /// <summary>
    /// 전략 프로토타입 씬을 만들고 빌드 씬 목록에 넣는다. 씬 YAML을 직접 편집하지 않기 위한 도구.
    /// 배치 모드: Unity.exe -batchmode -quit -projectPath . -executeMethod Prototype.Strategy.Editor.StrategySceneCreator.CreateBatch
    /// </summary>
    public static class StrategySceneCreator
    {
        const string Root = "Assets/Prototype/Strategy";
        const string ScenePath = Root + "/Scenes/StrategyProto.unity";

        [MenuItem("Prototype/Create Strategy Scene")]
        static void CreateMenu()
        {
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Strategy Prototype", $"{ScenePath}가 이미 있습니다. 덮어쓸까요?", "덮어쓰기", "취소"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Create();
        }

        public static void CreateBatch()
        {
            Create();
            EditorApplication.Exit(0);
        }

        static void Create()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 7f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.1f, 0.12f, 0.15f);
            cam.transform.position = new Vector3(0f, 0f, -50f);

            new GameObject("StrategyBootstrap").AddComponent<StrategyBootstrap>();

            if (!AssetDatabase.IsValidFolder($"{Root}/Scenes")) AssetDatabase.CreateFolder(Root, "Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);

            if (EditorBuildSettings.scenes.All(s => s.path != ScenePath))
                EditorBuildSettings.scenes = EditorBuildSettings.scenes
                    .Append(new EditorBuildSettingsScene(ScenePath, true)).ToArray();

            Debug.Log($"[StrategyProto] 씬 생성: {ScenePath}");
        }
    }
}
