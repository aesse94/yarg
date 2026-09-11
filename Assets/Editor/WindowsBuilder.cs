using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace YARG.Editor
{
    /// <summary>
    /// Headless Windows player build.
    ///
    ///   Unity -batchmode -nographics -projectPath /root/yarg-build \
    ///     -buildTarget Win64 -logFile build.log \
    ///     -executeMethod YARG.Editor.WindowsBuilder.Build -quit \
    ///     [-buildOut /root/yarg-build/Builds/Windows] [-define YARG_TEST_BUILD]
    /// </summary>
    public static class WindowsBuilder
    {
        private const string DEFAULT_OUT = "Builds/Windows";
        private const string EXE_NAME = "YARG.exe";

        public static void Build()
        {
            var args = Environment.GetCommandLineArgs();

            string outDir = GetArg(args, "-buildOut");
            if (string.IsNullOrEmpty(outDir)) outDir = DEFAULT_OUT;

            string define = GetArg(args, "-define");

            // Fail loudly rather than silently producing nothing.
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone,
                    BuildTarget.StandaloneWindows64))
            {
                Debug.LogError("[WindowsBuild] StandaloneWindows64 is not supported by this " +
                    "editor install. Is the Windows Build Support module present under " +
                    "Editor/Data/PlaybackEngines/WindowsStandaloneSupport?");
                EditorApplication.Exit(2);
                return;
            }

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
            {
                Debug.Log("[WindowsBuild] Switching active build target to StandaloneWindows64");
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone,
                        BuildTarget.StandaloneWindows64))
                {
                    Debug.LogError("[WindowsBuild] Failed to switch build target");
                    EditorApplication.Exit(3);
                    return;
                }
            }

            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Debug.LogError("[WindowsBuild] No enabled scenes in EditorBuildSettings");
                EditorApplication.Exit(4);
                return;
            }

            Debug.Log($"[WindowsBuild] {scenes.Length} scene(s): {string.Join(", ", scenes)}");

            Directory.CreateDirectory(outDir);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(outDir, EXE_NAME),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };

            if (!string.IsNullOrEmpty(define))
            {
                options.extraScriptingDefines = new[] { define };
                Debug.Log($"[WindowsBuild] Extra scripting define: {define}");
            }

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            Debug.Log($"[WindowsBuild] result={summary.result} " +
                $"errors={summary.totalErrors} warnings={summary.totalWarnings} " +
                $"size={summary.totalSize / (1024 * 1024)} MB " +
                $"time={summary.totalTime}");

            if (summary.result != BuildResult.Succeeded)
            {
                foreach (var step in report.steps)
                {
                    foreach (var message in step.messages)
                    {
                        if (message.type is LogType.Error or LogType.Exception)
                        {
                            Debug.LogError($"[WindowsBuild] {step.name}: {message.content}");
                        }
                    }
                }

                EditorApplication.Exit(5);
                return;
            }

            string exe = Path.Combine(outDir, EXE_NAME);
            Debug.Log($"[WindowsBuild] SUCCESS {exe} ({new FileInfo(exe).Length} bytes)");
            EditorApplication.Exit(0);
        }

        private static string GetArg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
