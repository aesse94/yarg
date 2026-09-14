using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using YARG.Venue;

namespace YARG.Editor
{
    /// <summary>
    /// Builds .yarground venue bundles from .glb files.
    /// CLI: -glbDir /root/venue_glbs -outDir /root/venue_out
    /// Per venue.glb: copy into Assets, import via GlbAssetImporter (UniGLTF scripted
    /// importer -> persistent sub-assets), instantiate, wrap in GO with
    /// BundleBackgroundManager + camera, save prefab at Assets/VenueBundles/<name>/_Background.prefab,
    /// build <name>.yarground AssetBundle.
    /// </summary>
    public static class VenueBundleBuilder
    {
        public static void BuildFromCliArgs()
        {
            var args = Environment.GetCommandLineArgs();
            string glbDir = GetArg(args, "-glbDir");
            // Material conversion target. Unlit by default - see VenueAssetFixer for why.
            string matMode = (GetArg(args, "-materials") ?? "unlit").ToLowerInvariant();
            bool unlit = matMode != "lit";
            string outDir = GetArg(args, "-outDir") ?? "build/yarground";
            if (string.IsNullOrEmpty(glbDir) || !Directory.Exists(glbDir))
            {
                Debug.LogError($"[VenueBundleBuilder] Missing -glbDir: '{glbDir}'");
                EditorApplication.Exit(2);
                return;
            }

            Directory.CreateDirectory(outDir);
            var glbs = Directory.GetFiles(glbDir, "*.glb");
            int ok = 0, fail = 0;
            foreach (var glb in glbs)
            {
                try
                {
                    bool built = BuildVenueBundle(glb, outDir, unlit);
                    if (built) ok++; else fail++;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[VenueBundleBuilder] {Path.GetFileName(glb)}: {e}");
                    fail++;
                }
            }
            Debug.Log($"[VenueBundleBuilder] DONE ok={ok} fail={fail} total={glbs.Length}");
            EditorApplication.Exit(ok == glbs.Length ? 0 : 3);
        }

        private static bool BuildVenueBundle(string glbPath, string outDir, bool unlit)
        {
            string assetName = Path.GetFileNameWithoutExtension(glbPath);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 1. Copy GLB into Assets and import via scripted importer (persistent assets)
            string assetDir = $"Assets/VenueBundles/{assetName}";
            Directory.CreateDirectory(assetDir);
            string glbAssetPath = $"{assetDir}/{assetName}.glb";
            File.Copy(glbPath, glbAssetPath, true);
            AssetDatabase.ImportAsset(glbAssetPath, ImportAssetOptions.ForceUpdate);

            var all = AssetDatabase.LoadAllAssetsAtPath(glbAssetPath);
            GameObject importedRoot = null;
            foreach (var a in all)
            {
                if (a is GameObject go && go.transform.parent == null && go.scene.IsValid() == false)
                {
                    importedRoot = go;
                    break;
                }
            }
            if (importedRoot == null)
            {
                Debug.LogError($"[VenueBundleBuilder] No imported root GameObject for {glbAssetPath}. Assets: {string.Join(", ", all.Select(a => a.name))}");
                return false;
            }
            Debug.Log($"[VenueBundleBuilder] Imported {glbAssetPath}: {all.Length} sub-assets, root={importedRoot.name}");

            // 2. Wrap: root GO with BundleBackgroundManager + main camera
            var venueGo = new GameObject("Venue");
            var bgManager = venueGo.AddComponent<BundleBackgroundManager>();
            var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(importedRoot);
            modelInstance.transform.SetParent(venueGo.transform, false);

            var camGo = new GameObject("VenueCamera");
            camGo.transform.SetParent(venueGo.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 3f, -12f);
            var cam = camGo.AddComponent<Camera>();
            cam.cullingMask = LayerMask.GetMask("Venue");
            cam.clearFlags = CameraClearFlags.Skybox;
            camGo.AddComponent<AudioListener>();

            var so = new SerializedObject(bgManager);
            so.FindProperty("mainCamera").objectReferenceValue = cam;
            so.ApplyModifiedPropertiesWithoutUndo();

            venueGo.transform.position = Vector3.zero;

            // 2b. The two fixes that decide whether this venue is ever visible:
            //     put the geometry on the layer the venue camera actually draws, and
            //     move its materials onto the render pipeline the game actually runs.
            //     Without these the bundle loads, instantiates, counts its renderers,
            //     and draws nothing.
            int layered = VenueAssetFixer.ApplyVenueLayer(venueGo);
            int converted = VenueAssetFixer.ConvertMaterialsToUrp(venueGo, assetDir, unlit);
            Debug.Log($"[VenueBundleBuilder] {assetName}: layer applied to {layered} objects, " +
                $"{converted} material(s) -> {(unlit ? "URP/Unlit" : "URP/Lit")}");

            // 3. Save wrapper prefab (references persistent imported assets)
            string prefabPath = $"{assetDir}/_Background.prefab";
            PrefabUtility.SaveAsPrefabAsset(venueGo, prefabPath);
            Debug.Log($"[VenueBundleBuilder] Prefab saved {prefabPath}");

            // 4. Build the AssetBundle
            var build = new AssetBundleBuild
            {
                assetBundleName = assetName + ".yarground",
                assetNames = new[] { prefabPath },
            };
            BuildPipeline.BuildAssetBundles(outDir,
                new[] { build },
                BuildAssetBundleOptions.ForceRebuildAssetBundle,
                EditorUserBuildSettings.activeBuildTarget);

            string bundlePath = Path.Combine(outDir, assetName + ".yarground");
            bool ok = File.Exists(bundlePath);
            if (ok)
                Debug.Log($"[VenueBundleBuilder] Built {bundlePath} ({new FileInfo(bundlePath).Length} bytes)");
            else
                Debug.LogError($"[VenueBundleBuilder] Build failed for {glbPath}");
            return ok;
        }

        private static string GetArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}