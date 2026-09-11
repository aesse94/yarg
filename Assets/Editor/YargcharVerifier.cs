using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using YARG.Venue;
using YARG.Venue.Characters;

namespace YARG.Editor
{
    /// <summary>
    /// Verifies built .yargchar bundles by loading them EXACTLY the way the game does -
    /// AssetBundle.LoadFromFile, then LoadAsset at the fixed character prefab path - so a
    /// bundle that the game would silently skip fails here instead.
    ///
    /// CLI: -executeMethod YARG.Editor.YargcharVerifier.VerifyAll -dir <dir> [-type Vocals]
    /// </summary>
    public static class YargcharVerifier
    {
        public static void VerifyAll()
        {
            var args = Environment.GetCommandLineArgs();
            string dir = GetArg(args, "-dir");
            var expectedType = VrmYargcharBuilder.ParseCharacterType(GetArg(args, "-type"));

            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                Debug.LogError($"[YargcharVerify] Missing -dir: '{dir}'");
                EditorApplication.Exit(2);
                return;
            }

            var files = Directory.GetFiles(dir, "*.yargchar");
            int ok = 0, fail = 0;

            foreach (var file in files)
            {
                string name = Path.GetFileName(file);
                var bundle = AssetBundle.LoadFromFile(file);

                if (bundle == null)
                {
                    Debug.LogError($"[YargcharVerify] FAIL {name}: bundle would not open");
                    fail++;
                    continue;
                }

                try
                {
                    var prefab = bundle.LoadAsset<GameObject>(
                        BundleBackgroundManager.CHARACTER_PREFAB_PATH.ToLowerInvariant());

                    if (prefab == null)
                    {
                        Debug.LogError($"[YargcharVerify] FAIL {name}: no prefab at " +
                            $"{BundleBackgroundManager.CHARACTER_PREFAB_PATH}. " +
                            $"Contains: [{string.Join(", ", bundle.GetAllAssetNames())}]");
                        fail++;
                        continue;
                    }

                    // CustomCharacterSetting requires Vrm10Instance on the ROOT, and
                    // VRMCharacter.Initialize dereferences it without a null check.
                    if (prefab.GetComponent<UniVRM10.Vrm10Instance>() == null)
                    {
                        Debug.LogError($"[YargcharVerify] FAIL {name}: prefab root has no " +
                            "Vrm10Instance; the menu would skip it and loading would throw");
                        fail++;
                        continue;
                    }

                    var character = prefab.GetComponent<VenueCharacter>();
                    if (character == null)
                    {
                        Debug.LogError($"[YargcharVerify] FAIL {name}: prefab has no VenueCharacter");
                        fail++;
                        continue;
                    }

                    if (character.Type != expectedType)
                    {
                        Debug.LogError($"[YargcharVerify] FAIL {name}: Type is {character.Type}, " +
                            $"expected {expectedType}");
                        fail++;
                        continue;
                    }

                    Debug.Log($"[YargcharVerify] PASS {name}: loads at the game's path, " +
                        $"Type={character.Type}, gender={character.CharacterGender}");
                    ok++;
                }
                finally
                {
                    bundle.Unload(true);
                }
            }

            Debug.Log($"[YargcharVerify] RESULT: {ok} passed, {fail} failed, {files.Length} total");
            EditorApplication.Exit(fail == 0 && files.Length > 0 ? 0 : 1);
        }

        private static string GetArg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
