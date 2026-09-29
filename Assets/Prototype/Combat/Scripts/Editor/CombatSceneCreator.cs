using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Prototype.Combat.Editor
{
    /// <summary>
    /// 전투 프로토타입 씬과 튜닝 에셋을 생성한다. 씬 YAML을 직접 편집하지 않기 위한 도구.
    /// </summary>
    public static class CombatSceneCreator
    {
        const string Root = "Assets/Prototype/Combat";
        const string ScenePath = Root + "/Scenes/CombatProto.unity";
        const string TuningPath = Root + "/Settings/CombatTuning.asset";

        [MenuItem("Prototype/Open Playtest Logs")]
        static void OpenLogs() => PlaytestLogger.OpenFolder();

        [MenuItem("Prototype/Create Combat Scene")]
        static void Create()
        {
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Combat Prototype", $"{ScenePath}가 이미 있습니다. 덮어쓸까요?", "덮어쓰기", "취소"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var tuning = AssetDatabase.LoadAssetAtPath<CombatTuning>(TuningPath);
            if (tuning == null)
            {
                EnsureFolder(Root, "Settings");
                tuning = ScriptableObject.CreateInstance<CombatTuning>();
                AssetDatabase.CreateAsset(tuning, TuningPath);
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 9f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.45f, 0.62f, 0.8f);
            cam.transform.position = new Vector3(0f, 0f, -50f);

            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var boot = new GameObject("CombatBootstrap").AddComponent<CombatBootstrap>();
            var so = new SerializedObject(boot);
            so.FindProperty("tuning").objectReferenceValue = tuning;
            so.ApplyModifiedPropertiesWithoutUndo();

            EnsureFolder(Root, "Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[CombatProto] 씬 생성: {ScenePath}");
        }

        static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{name}")) AssetDatabase.CreateFolder(parent, name);
        }
    }
}
