using System;
using System.IO;
using UnityEditor;

namespace YARG.Editor
{
    /// <summary>
    /// Builds .yargchar bundles for every .vrm in a directory, in one Unity session.
    /// CLI: -vrmDir /root/vrms -outDir /root/yargchar_out [-type Vocals] [-map map.json]
    /// </summary>
    public static class VrmBatchBuilder
    {
        public static void BuildAll()
        {
            var args = Environment.GetCommandLineArgs();
            string vrmDir = GetArg(args, "-vrmDir");
            var characterType = VrmYargcharBuilder.ParseCharacterType(GetArg(args, "-type"));

            // A map makes the export per-character; without one every character gets -type.
            string mapPath = GetArg(args, "-map");
            var map = YargcharExportMap.Load(mapPath);
            if (!string.IsNullOrEmpty(mapPath) && map == null)
            {
                UnityEngine.Debug.LogError($"[VrmBatchBuilder] -map could not be loaded: '{mapPath}'");
                EditorApplication.Exit(2);
                return;
            }
            string outDir = GetArg(args, "-outDir") ?? "build/yargchar";
            if (string.IsNullOrEmpty(vrmDir) || !Directory.Exists(vrmDir))
            {
                UnityEngine.Debug.LogError($"[VrmBatchBuilder] Missing -vrmDir: '{vrmDir}'");
                EditorApplication.Exit(2);
                return;
            }

            Directory.CreateDirectory(outDir);
            var vrms = Directory.GetFiles(vrmDir, "*.vrm");
            int ok = 0, fail = 0;
            foreach (var vrm in vrms)
            {
                try
                {
                    var type = characterType;
                    var gender = YARG.Core.Song.VocalGender.Unspecified;

                    if (map != null)
                    {
                        string stem = Path.GetFileNameWithoutExtension(vrm);
                        if (!map.TryGet(stem, out var settings))
                        {
                            // Defaulting here would export as Bass and hijack the venue's
                            // bassist, so an unmapped character is a failure.
                            UnityEngine.Debug.LogError($"[VrmBatchBuilder] '{stem}' has no entry " +
                                "in the export map; skipping rather than guessing its type");
                            fail++;
                            continue;
                        }

                        type = settings.Type;
                        gender = settings.Gender;
                    }

                    bool built = VrmYargcharBuilder.BuildBundle(vrm, outDir, type, gender);
                    if (built) ok++; else fail++;
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError($"[VrmBatchBuilder] {Path.GetFileName(vrm)}: {e}");
                    fail++;
                }
            }
            UnityEngine.Debug.Log($"[VrmBatchBuilder] DONE ok={ok} fail={fail} total={vrms.Length}");
            EditorApplication.Exit(ok == vrms.Length ? 0 : 3);
        }

        private static string GetArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}