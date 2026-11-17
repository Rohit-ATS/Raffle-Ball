using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RiftRally.Setup;

namespace RiftRally.EditorTools
{
    /// <summary>
    /// Generates the playable scene.
    ///
    /// The scene is a generated artefact rather than a hand-authored one: it holds the XR rig
    /// and a single <see cref="GameBootstrap"/>, and everything else is built in code at run
    /// time. That keeps the scene file tiny and merge-friendly, and it means the scene can be
    /// regenerated from scratch after an XR package upgrade instead of being repaired.
    /// </summary>
    public static class RiftRallySceneTool
    {
        public const string ScenePath = "Assets/RiftRally/Scenes/RiftRally.unity";

        private const string HandsRigPrefab =
            "Assets/VRTemplateAssets/Prefabs/Setup/Complete XR Origin Set Up Hands Variant.prefab";
        private const string PermissionsPrefab =
            "Assets/VRTemplateAssets/Prefabs/Setup/Hands Permissions Manager.prefab";

        [MenuItem("Rift Rally/Build Play Scene", priority = 0)]
        public static void BuildSceneMenu()
        {
            string path = BuildScene();
            EditorUtility.DisplayDialog("Rift Rally", $"Scene written to:\n{path}", "OK");
        }

        /// <summary>
        /// Creates (or overwrites) the play scene and makes it the first entry in the build
        /// settings. Returns the asset path. Callable from the command line.
        /// </summary>
        public static string BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                                                    NewSceneMode.Single);

            // ---- XR rig ----
            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(HandsRigPrefab);
            if (rig != null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(rig);
                instance.name = "XR Origin (Hands)";
                instance.transform.position = Vector3.zero;
            }
            else
            {
                Debug.LogWarning($"[RiftRally] XR rig prefab not found at {HandsRigPrefab}. " +
                                 "The scene will fall back to a desktop camera.");
            }

            var permissions = AssetDatabase.LoadAssetAtPath<GameObject>(PermissionsPrefab);
            if (permissions != null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(permissions);
                instance.name = "Hands Permissions Manager";
            }

            // ---- the game ----
            var gameGo = new GameObject("Rift Rally");
            var bootstrap = gameGo.AddComponent<GameBootstrap>();
            bootstrap.Mode = Core.GameMode.SoloVsAi;
            bootstrap.AutoStartMatch = false;
            bootstrap.DemoMatch = false;

            // ---- save ----
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            RegisterInBuildSettings(ScenePath);
            Debug.Log($"[RiftRally] Play scene generated at {ScenePath}");
            return ScenePath;
        }

        /// <summary>Puts the play scene first in the build list, keeping the rest intact.</summary>
        public static void RegisterInBuildSettings(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(scenePath, true)
            };

            foreach (var existing in EditorBuildSettings.scenes)
            {
                if (existing.path == scenePath) continue;
                scenes.Add(existing);
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }

        /// <summary>
        /// Command-line entry point, so continuous integration can regenerate the scene
        /// before building:
        ///   Unity.exe -batchmode -quit -executeMethod
        ///       RiftRally.EditorTools.RiftRallySceneTool.GenerateSceneCli
        /// </summary>
        public static void GenerateSceneCli()
        {
            try
            {
                BuildScene();
                bool ok = ValidateScene(out string message);
                Debug.Log($"[RiftRally] {message}");
                if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[RiftRally] Scene generation failed: {e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        [MenuItem("Rift Rally/Open Play Scene", priority = 1)]
        public static void OpenScene()
        {
            if (!File.Exists(ScenePath)) BuildScene();
            else EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        /// <summary>
        /// Sanity check used by the scene tool and by continuous integration: the generated
        /// scene must contain exactly one bootstrap and a camera to render from.
        /// </summary>
        public static bool ValidateScene(out string message)
        {
            if (!File.Exists(ScenePath))
            {
                message = $"Scene missing at {ScenePath}";
                return false;
            }

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            int bootstraps = 0;
            int cameras = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                bootstraps += root.GetComponentsInChildren<GameBootstrap>(true).Length;
                cameras += root.GetComponentsInChildren<Camera>(true).Length;
            }

            if (bootstraps != 1)
            {
                message = $"Expected exactly 1 GameBootstrap, found {bootstraps}";
                return false;
            }

            message = $"Scene OK: {bootstraps} bootstrap, {cameras} camera(s)";
            return true;
        }
    }
}
