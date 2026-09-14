using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YARG.Venue;
using YARG.Venue.Characters;

namespace YARG.Editor
{
    /// <summary>
    /// Verifies built .yarground bundles by loading them EXACTLY the way the game does -
    /// AssetBundle.LoadFromFile, then LoadAsset at the fixed background prefab path
    /// (lowercased, as BackgroundManager does) - so a venue the game would fail on
    /// fails here instead, as a build gate rather than a visual surprise.
    ///
    /// Mirrors YargcharVerifier. The contract being asserted is the one in
    /// BackgroundManager.cs:213 and BundleBackgroundManager.BACKGROUND_PREFAB_PATH.
    ///
    /// CLI: -executeMethod YARG.Editor.VenueVerifier.VerifyAll -dir &lt;dir&gt;
    /// </summary>
    public static class VenueVerifier
    {
        public static void VerifyAll()
        {
            var args = Environment.GetCommandLineArgs();
            string dir = GetArg(args, "-dir");

            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                Debug.LogError($"[VenueVerify] Missing -dir: '{dir}'");
                EditorApplication.Exit(2);
                return;
            }

            var files = Directory.GetFiles(dir, "*.yarground").OrderBy(f => f).ToArray();
            int ok = 0, fail = 0;

            foreach (var file in files)
            {
                string name = Path.GetFileName(file);
                var bundle = AssetBundle.LoadFromFile(file);

                if (bundle == null)
                {
                    Debug.LogError($"[VenueVerify] FAIL {name}: bundle would not open");
                    fail++;
                    continue;
                }

                try
                {
                    // KEEP LOWERCASE - BackgroundManager does the same, and a
                    // case-sensitive miss is exactly how a venue silently fails.
                    var prefab = bundle.LoadAsset<GameObject>(
                        BundleBackgroundManager.BACKGROUND_PREFAB_PATH.ToLowerInvariant());

                    // Diagnostic: if the canonical path misses, try the first
                    // GameObject in the bundle - the same fallback LoadCharacterPrefab
                    // already uses for characters. This separates "bundle is broken"
                    // from "bundle is fine, prefab is at a different path".
                    bool viaFallback = false;
                    if (prefab == null)
                    {
                        foreach (var assetName in bundle.GetAllAssetNames())
                        {
                            prefab = bundle.LoadAsset<GameObject>(assetName);
                            if (prefab != null) { viaFallback = true; break; }
                        }
                    }

                    if (prefab == null)
                    {
                        Debug.LogError($"[VenueVerify] FAIL {name}: no prefab at " +
                            $"{BundleBackgroundManager.BACKGROUND_PREFAB_PATH} and no " +
                            $"GameObject anywhere. Contains: [{string.Join(", ", bundle.GetAllAssetNames())}]");
                        fail++;
                        continue;
                    }

                    // The game instantiates the prefab and immediately walks its
                    // renderers (BackgroundManager.cs:215). A prefab that loads but
                    // instantiates to nothing would pass a load-only check and still
                    // give a black stage, so instantiate for real.
                    var instance = UnityEngine.Object.Instantiate(prefab);
                    try
                    {
                        if (instance == null)
                        {
                            Debug.LogError($"[VenueVerify] FAIL {name}: prefab did not instantiate");
                            fail++;
                            continue;
                        }

                        var renderers = instance.GetComponentsInChildren<Renderer>(true);
                        var rends = renderers;
                        var bgManager = instance.GetComponentInChildren<BundleBackgroundManager>(true);
                        var cameras = instance.GetComponentsInChildren<Camera>(true);

                        if (renderers.Length == 0)
                        {
                            Debug.LogError($"[VenueVerify] FAIL {name}: instantiated with 0 renderers " +
                                "- would draw an empty stage");
                            fail++;
                            continue;
                        }

                        // LoadYarground requires this component and now log-and-skips
                        // without it, so the gate treats its absence as fatal too - the
                        // gate and the loader must agree on what "loadable" means.
                        if (bgManager == null)
                        {
                            Debug.LogError($"[VenueVerify] FAIL {name}: no BundleBackgroundManager " +
                                "on the prefab - LoadYarground would skip this venue");
                            fail++;
                            continue;
                        }

                        string warn = cameras.Length == 0 ? " [no camera]" : "";

                        // DRAW-ABILITY, not just load-ability. A venue that loads but is
                        // culled (wrong layer) or on the wrong render pipeline (Built-in
                        // Standard under URP) is invisible in game while passing every
                        // load check - which is exactly how 33 venues shipped unnoticed.
                        int venueLayer = LayerMask.NameToLayer(VenueAssetFixer.VENUE_LAYER);
                        int offLayer = rends.Count(r => r.gameObject.layer != venueLayer);
                        var badPipeline = rends.SelectMany(r => r.sharedMaterials)
                            .Where(m => m != null && m.shader != null)
                            .Select(m => m.shader.name)
                            .Where(n => !n.StartsWith("Universal Render Pipeline/")
                                     && !n.StartsWith("Unlit/"))
                            .Distinct().ToList();

                        if (offLayer > 0)
                        {
                            Debug.LogError($"[VenueVerify] FAIL {name}: {offLayer}/{rends.Length} renderers " +
                                $"not on the '{VenueAssetFixer.VENUE_LAYER}' layer - the venue camera culls them");
                            fail++;
                            continue;
                        }

                        if (badPipeline.Count > 0)
                        {
                            Debug.LogError($"[VenueVerify] FAIL {name}: non-URP shader(s) " +
                                $"[{string.Join(", ", badPipeline)}] - will not render under URP");
                            fail++;
                            continue;
                        }

                        // Character slots. LoadAndReplaceCharacter searches the venue root
                        // for a VenueCharacter of the wanted type; a venue with no Vocals
                        // slot loads fine and then silently has no vocalist, which is the
                        // "Failed to find character of type Vocals in venue root" symptom.
                        var slots = instance.GetComponentsInChildren<VenueCharacter>(true);
                        var slotList = slots.Length == 0
                            ? "NONE"
                            : string.Join("/", slots.Select(c => c.Type.ToString()).Distinct().OrderBy(t => t));
                        warn += $" slots={slotList}";

                        // Both routes are loadable now that LoadYarground carries the
                        // same fallback, so both PASS - but the route is reported so a
                        // drift back to the canonical path stays visible in the log.
                        string route = viaFallback ? "via fallback path" : "at the game's path";
                        Debug.Log($"[VenueVerify] PASS {name}: loads {route}, " +
                            $"{renderers.Length} renderers, {cameras.Length} camera(s){warn}");
                        ok++;
                    }
                    finally
                    {
                        if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
                    }
                }
                finally
                {
                    bundle.Unload(true);
                }
            }

            Debug.Log($"[VenueVerify] RESULT: {ok} passed, {fail} failed, {files.Length} total");
            EditorApplication.Exit(fail == 0 && files.Length > 0 ? 0 : 1);
        }

        private static string GetArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name) return args[i + 1];
            }

            return null;
        }
    }
}
