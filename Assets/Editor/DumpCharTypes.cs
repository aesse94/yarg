using System.IO;
using UnityEngine;
using UnityEditor;
using YARG.Venue;
using YARG.Venue.Characters;
using UniVRM10;

namespace YARG.Editor
{
    public static class DumpCharTypes
    {
        public static void Run()
        {
            string folder = null;
            var cli = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < cli.Length - 1; i++) if (cli[i] == "-dir") folder = cli[i + 1];
            folder ??= Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
                "AppData", "LocalLow", "YARC", "YARG", "dev", "custom", "characters");

            Debug.Log($"[DUMP] folder={folder} exists={Directory.Exists(folder)}");
            if (!Directory.Exists(folder)) { EditorApplication.Exit(1); return; }

            foreach (var file in Directory.GetFiles(folder, "*.yargchar"))
            {
                var name = Path.GetFileName(file);
                var bundle = AssetBundle.LoadFromFile(file);
                if (bundle == null) { Debug.Log($"[DUMP] {name}: BUNDLE NULL"); continue; }

                var assets = bundle.GetAllAssetNames();
                var prefab = BundleBackgroundManager.LoadCharacterPrefab(bundle);

                if (prefab == null)
                {
                    Debug.Log($"[DUMP] {name}: PREFAB NULL at '{BundleBackgroundManager.CHARACTER_PREFAB_PATH.ToLowerInvariant()}' | assets=[{string.Join(", ", assets)}]");
                    bundle.Unload(true);
                    continue;
                }

                var vc  = prefab.GetComponent<VenueCharacter>();
                var vrm = prefab.GetComponent<Vrm10Instance>();
                Debug.Log($"[DUMP] {name}: Type={(vc == null ? "NO VenueCharacter" : vc.Type.ToString())} Vrm10Instance={(vrm != null)}");
                bundle.Unload(true);
            }
            EditorApplication.Exit(0);
        }
    }
}
