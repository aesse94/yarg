using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// Batchmode-safe build entry point.
    /// <see cref="MakeTestBuild"/> goes through BuildPlayerWindow, which opens an
    /// interactive folder picker and so cannot run under -batchmode.
    /// </summary>
    public static class BatchBuild
    {
        private const string YARG_TEST_BUILD = "YARG_TEST_BUILD";

        /// <summary>
        /// Explicit Addressables content build — must run BEFORE BuildWindows64 so the
        /// StreamingAssets/aa catalog and bundles match the player that embeds them.
        /// </summary>
        public static void BuildAddressables()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                throw new Exception("No AddressableAssetSettings found in project.");
            }

            Debug.Log("[BatchBuild] Building Addressables content start");
            settings.BuildPlayerContent();
            Debug.Log("[BatchBuild] Building Addressables content done");
            EditorApplication.Exit(0);
        }

        public static void BuildWindows64()
        {
            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                throw new Exception("No enabled scenes in EditorBuildSettings.");
            }

            var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "Builds", "Windows");
            Directory.CreateDirectory(outputDir);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(outputDir, "YARG.exe"),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
                extraScriptingDefines = new[] { YARG_TEST_BUILD },
            };

            Debug.Log($"[BatchBuild] Building {scenes.Length} scenes to {options.locationPathName}");

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            Debug.Log($"[BatchBuild] Result={summary.result} Errors={summary.totalErrors} " +
                $"Warnings={summary.totalWarnings} Size={summary.totalSize} Time={summary.totalTime}");

            if (summary.result != BuildResult.Succeeded)
            {
                foreach (var step in report.steps)
                {
                    foreach (var message in step.messages.Where(m =>
                        m.type is LogType.Error or LogType.Exception))
                    {
                        Debug.LogError($"[BatchBuild] {step.name}: {message.content}");
                    }
                }

                EditorApplication.Exit(1);
            }

            EditorApplication.Exit(0);
        }
    }
}
