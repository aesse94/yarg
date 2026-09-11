using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UniVRM10;
using YARG.Core.Song;
using YARG.Venue;
using YARG.Venue.Characters;

namespace YARG.Editor
{
    /// <summary>
    /// Builds .yargchar AssetBundles from .vrm files.
    /// Usage (headless):
    ///   Unity -batchmode -nographics -projectPath /root/yarg-build \
    ///     -executeMethod YARG.Editor.VrmYargcharBuilder.BuildFromCliArgs -quit
    /// CLI args (after the usual ones): -vrm <path.vrm> [-outDir <dir>]
    ///   [-type Vocals|Guitar|Bass|Drums|Keys]  (default Vocals)
    /// </summary>
    public static class VrmYargcharBuilder
    {
        public static void BuildFromCliArgs()
        {
            var args = Environment.GetCommandLineArgs();
            string vrmPath = GetArg(args, "-vrm");
            if (string.IsNullOrEmpty(vrmPath) || !File.Exists(vrmPath))
            {
                Debug.LogError($"[VrmYargcharBuilder] Missing or invalid -vrm path: '{vrmPath}'");
                EditorApplication.Exit(2);
                return;
            }

            string outDir = GetArg(args, "-outDir");
            if (string.IsNullOrEmpty(outDir)) outDir = "build/yargchar";
            Directory.CreateDirectory(outDir);

            var characterType = ParseCharacterType(GetArg(args, "-type"));

            if (!BuildBundle(vrmPath, outDir, characterType)) EditorApplication.Exit(3);
            EditorApplication.Exit(0);
        }

        public static bool BuildBundle(string vrmPath, string outDir,
            VenueCharacter.CharacterType characterType = VenueCharacter.CharacterType.Vocals,
            VocalGender characterGender = VocalGender.Unspecified)
        {
            string prefabPath = null;
            string vrmAssetPath = null;

            try
            {
                string fileName = Path.GetFileName(vrmPath);
                string assetName = Path.GetFileNameWithoutExtension(vrmPath);
                string vrmAsset = "Assets/VrmImport/" + fileName;
                vrmAssetPath = vrmAsset;
                Directory.CreateDirectory("Assets/VrmImport");
                File.Copy(vrmPath, vrmAsset, true);
                AssetDatabase.ImportAsset(vrmAsset, ImportAssetOptions.ForceUpdate);

                // UniVRM's scripted importer generates the VRM instance prefab at import.
                var all = AssetDatabase.LoadAllAssetsAtPath(vrmAsset);
                GameObject vrmRoot = all.OfType<GameObject>()
                    .FirstOrDefault(g => g.GetComponent<Vrm10Instance>() != null);
                if (vrmRoot == null)
                {
                    Debug.LogError($"[VrmYargcharBuilder] No Vrm10Instance found in {vrmAsset}. Assets: {string.Join(", ", all.Select(a => a.name))}");
                    return false;
                }

                // The VenueCharacter component must sit on the SAME GameObject as the
                // Vrm10Instance, not on a wrapper above it. CustomCharacterSetting looks for
                // Vrm10Instance on the prefab root, and VRMCharacter.Initialize does
                // GetComponent<Vrm10Instance>() on itself and dereferences .Runtime without
                // a null check - a wrapper makes the character invisible in the menu and
                // throws at load.
                var go = (GameObject)PrefabUtility.InstantiatePrefab(vrmRoot);
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
                go.name = assetName;

                if (go.GetComponent<Vrm10Instance>() == null)
                {
                    Debug.LogError($"[VrmYargcharBuilder] {assetName}: root has no Vrm10Instance " +
                        "after instantiation; the character would not load.");
                    return false;
                }

                var venueCharacter = go.AddComponent<VRMCharacter>();

                // Type drives BOTH which venue character this replaces at load and which
                // settings dropdown lists it. Left at its default it is Bass, so a custom
                // vocalist would replace the bassist and never appear in the vocals list.
                venueCharacter.Type = characterType;
                venueCharacter.CharacterGender = characterGender;

                go.SetActive(false);

                // MUST be this exact path: the game loads the prefab by a fixed name
                // (BundleBackgroundManager.CHARACTER_PREFAB_PATH, lowercased), so a bundle
                // built under any other path loads as null and is silently skipped.
                prefabPath = BundleBackgroundManager.CHARACTER_PREFAB_PATH;
                PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
                UnityEngine.Object.DestroyImmediate(go);

                var build = new AssetBundleBuild
                {
                    assetBundleName = assetName + ".yargchar",
                    assetNames = new[] { prefabPath },
                };
                Directory.CreateDirectory(outDir);
                BuildPipeline.BuildAssetBundles(outDir,
                    new[] { build },
                    BuildAssetBundleOptions.ForceRebuildAssetBundle,
                    EditorUserBuildSettings.activeBuildTarget);

                string bundle = Path.Combine(outDir, assetName + ".yargchar");
                Debug.Log($"[VrmYargcharBuilder] Built {bundle} as {characterType}/" +
                    $"{characterGender} ({new FileInfo(bundle).Length} bytes)");
                return File.Exists(bundle);
            }
            catch (Exception e)
            {
                Debug.LogError($"[VrmYargcharBuilder] FAILED for {vrmPath}: {e}");
                return false;
            }
            finally
            {
                // The prefab path is shared by every build, so it must not survive into
                // the next one.
                if (prefabPath != null) AssetDatabase.DeleteAsset(prefabPath);
                if (vrmAssetPath != null) AssetDatabase.DeleteAsset(vrmAssetPath);
            }
        }

        /// <summary>Character type to stamp on the export; defaults to Vocals.</summary>
        public static VenueCharacter.CharacterType ParseCharacterType(string value)
        {
            if (!string.IsNullOrWhiteSpace(value) &&
                Enum.TryParse<VenueCharacter.CharacterType>(value, true, out var parsed))
            {
                return parsed;
            }

            return VenueCharacter.CharacterType.Vocals;
        }

        private static string GetArg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
