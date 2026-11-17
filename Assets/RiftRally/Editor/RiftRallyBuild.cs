using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RiftRally.EditorTools
{
    /// <summary>
    /// Command-line build entry points.
    ///
    /// Player settings are applied here rather than left in the project file so a build is
    /// reproducible from a clean checkout: whatever the Editor happens to be set to, a Quest
    /// build always comes out ARM64/IL2CPP/linear, which are the settings the headset
    /// actually requires.
    ///
    /// Usage:
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod RiftRally.EditorTools.RiftRallyBuild.Quest
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod RiftRally.EditorTools.RiftRallyBuild.Windows
    /// </summary>
    public static class RiftRallyBuild
    {
        private const string ProductName = "Rift Rally";
        private const string CompanyName = "MetaVR Hackathon";
        private const string Identifier = "com.metavrhackathon.riftrally";

        private const string OutputRoot = "Build";

        [MenuItem("Rift Rally/Build/Quest (Android)", priority = 20)]
        public static void Quest() => Run(BuildTarget.Android);

        [MenuItem("Rift Rally/Build/Windows (desktop test)", priority = 21)]
        public static void Windows() => Run(BuildTarget.StandaloneWindows64);

        private static void Run(BuildTarget target)
        {
            bool batch = Application.isBatchMode;
            try
            {
                string path = BuildFor(target);
                Debug.Log($"[RiftRally] Build succeeded: {path}");
                if (batch) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[RiftRally] Build failed: {e}");
                if (batch) EditorApplication.Exit(1);
                else throw;
            }
        }

        /// <summary>Builds a player for the target and returns the output path.</summary>
        public static string BuildFor(BuildTarget target)
        {
            string scenePath = RiftRallySceneTool.ScenePath;
            if (!File.Exists(scenePath)) scenePath = RiftRallySceneTool.BuildScene();
            RiftRallySceneTool.RegisterInBuildSettings(scenePath);

            ApplyCommonSettings();

            string output;
            if (target == BuildTarget.Android)
            {
                ApplyAndroidSettings();
                output = Path.Combine(OutputRoot, "Quest", "RiftRally.apk");
            }
            else
            {
                // The desktop player is only a quick way to look at the game without a
                // headset, so it uses Mono: it builds in seconds and needs no IL2CPP module.
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone,
                                                   ScriptingImplementation.Mono2x);
                output = Path.Combine(OutputRoot, "Windows", "RiftRally.exe");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(output));

            var options = new BuildPlayerOptions
            {
                scenes = new[] { scenePath },
                locationPathName = output,
                target = target,
                targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            var log = new StringBuilder();
            log.AppendLine($"[RiftRally] {target} build {summary.result}");
            log.AppendLine($"  output : {summary.outputPath}");
            log.AppendLine($"  size   : {summary.totalSize / (1024 * 1024)} MB");
            log.AppendLine($"  time   : {summary.totalTime}");
            log.AppendLine($"  errors : {summary.totalErrors}, warnings: {summary.totalWarnings}");
            Debug.Log(log.ToString());

            if (summary.result != BuildResult.Succeeded)
                throw new Exception($"{target} build result was {summary.result} " +
                                    $"with {summary.totalErrors} error(s)");

            return summary.outputPath;
        }

        private static void ApplyCommonSettings()
        {
            PlayerSettings.productName = ProductName;
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, Identifier);
        }

        /// <summary>
        /// Quest-specific settings. ARM64 and IL2CPP are mandatory for the Meta store, and
        /// Vulkan is the pipeline Meta's runtime is tuned for on Quest 2 and later.
        /// </summary>
        private static void ApplyAndroidSettings()
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android,
                                               ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[]
            {
                UnityEngine.Rendering.GraphicsDeviceType.Vulkan,
                UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3
            });

            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.Android,
                                                          Il2CppCompilerConfiguration.Release);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android,
                                                    ManagedStrippingLevel.Low);

            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.androidBuildType = AndroidBuildType.Release;
        }

        /// <summary>
        /// Compiles the project and reports, without producing a player. Used as the fast
        /// gate in continuous integration before the slow device build runs.
        /// </summary>
        public static void CompileOnly()
        {
            bool ok = RiftRallySceneTool.ValidateScene(out string message);
            Debug.Log($"[RiftRally] Scene validation: {message}");
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
