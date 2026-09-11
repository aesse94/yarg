using System;
using System.IO;
using UnityEditor;

namespace YARG.Editor
{
    /// <summary>
    /// Builds .yargchar bundles for every .vrm in a directory, in one Unity session.
    /// CLI: -vrmDir /root/vrms -outDir /root/yargchar_out [-type Vocals]
    /// </summary>
    public static class VrmBatchBuilder
    {
        public static void BuildAll()
        {
            var args = Environment.GetCommandLineArgs();
            string vrmDir = GetArg(args, "-vrmDir");
            var characterType = VrmYargcharBuilder.ParseCharacterType(GetArg(args, "-type"));
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
                    bool built = VrmYargcharBuilder.BuildBundle(vrm, outDir, characterType);
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