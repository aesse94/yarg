using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YARG.Venue;

namespace YARG.Editor
{
    /// <summary>
    /// Read-only survey of built .yarground bundles for transforms that could serve as
    /// stage/performer anchors, so the venue character-slot placement strategy can be
    /// chosen from evidence rather than assumption.
    ///
    /// Deliberately SEPARATE from VenueVerifier: that one is a CI gate with a stable
    /// pass/fail contract, and this is a diagnostic. Keeping them apart stops a
    /// survey from ever changing a gate result.
    ///
    /// CLI: -executeMethod YARG.Editor.VenueMarkerScan.Run -dir &lt;dir&gt;
    /// </summary>
    public static class VenueMarkerScan
    {
        // Substrings that plausibly name a performance anchor. Matched case-insensitively
        // against transform names. Kept broad on purpose - a false positive is cheap to
        // dismiss by eye, a missed convention costs a wrong anchor implementation.
        private static readonly string[] Keywords =
        {
            "stage", "band", "singer", "vocal", "mic", "guitar", "bass", "drum",
            "player", "performer", "spawn", "anchor", "marker", "slot", "char",
        };

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            string dir = GetArg(args, "-dir");

            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                Debug.LogError($"[MarkerScan] Missing -dir: '{dir}'");
                EditorApplication.Exit(2);
                return;
            }

            var files = Directory.GetFiles(dir, "*.yarground").OrderBy(f => f).ToArray();
            int withMarkers = 0, pureGeometry = 0;
            var keywordTotals = new Dictionary<string, int>();

            foreach (var file in files)
            {
                string name = Path.GetFileNameWithoutExtension(file);
                var bundle = AssetBundle.LoadFromFile(file);
                if (bundle == null)
                {
                    Debug.LogError($"[MarkerScan] {name}: bundle would not open");
                    continue;
                }

                try
                {
                    var prefab = BundleBackgroundManager.LoadBackgroundPrefab(bundle);
                    if (prefab == null)
                    {
                        Debug.LogError($"[MarkerScan] {name}: no prefab");
                        continue;
                    }

                    var instance = UnityEngine.Object.Instantiate(prefab);
                    try
                    {
                        var all = instance.GetComponentsInChildren<Transform>(true);

                        // An "empty" here means a transform carrying no visible geometry -
                        // the shape a hand-placed anchor takes once exported through GLB.
                        var empties = all.Where(t =>
                            t.GetComponent<Renderer>() == null &&
                            t.GetComponent<MeshFilter>() == null).ToList();

                        var hits = all
                            .Where(t => Keywords.Any(k =>
                                t.name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                            .Select(t => t.name)
                            .Distinct()
                            .OrderBy(n => n)
                            .ToList();

                        foreach (var h in hits)
                        {
                            foreach (var k in Keywords)
                            {
                                if (h.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    keywordTotals.TryGetValue(k, out int c);
                                    keywordTotals[k] = c + 1;
                                }
                            }
                        }

                        // The decisive question is not "does any mesh have 'stage' in its
                        // name" - scenery does - but "is there an empty transform that
                        // could be a performer anchor". Name every empty, so the answer
                        // rests on what they ARE rather than on keyword luck.
                        // LimitVenueLights culls every venue light to the "Venue" layer,
                        // but nothing in the codebase puts venue GEOMETRY on that layer.
                        // If that is so, the venue's own lights illuminate nothing and the
                        // stage renders ambient-only - which looks exactly like "sky and
                        // ground, no building". Report layers and lights to settle it.
                        var rends = instance.GetComponentsInChildren<Renderer>(true);
                        var layerCounts = rends.GroupBy(r => r.gameObject.layer)
                            .OrderByDescending(g => g.Count())
                            .Select(g => $"{LayerMask.LayerToName(g.Key)}({g.Key})={g.Count()}");
                        var lights = instance.GetComponentsInChildren<Light>(true);
                        // Player.log reports "Desired shader compiler platform 4 is not
                        // available in shader blob" (platform 4 = D3D11). If these bundles
                        // were built without D3D11 variants their materials cannot render,
                        // which would look exactly like invisible geometry.
                        var shaders = rends.SelectMany(r => r.sharedMaterials)
                            .Where(m => m != null && m.shader != null)
                            .Select(m => m.shader)
                            .Distinct().ToList();
                        int unsupported = shaders.Count(sh => !sh.isSupported);
                        Debug.Log($"[MarkerScan] SHADERS {name}: {shaders.Count} distinct, " +
                            $"{unsupported} UNSUPPORTED | " +
                            string.Join(", ", shaders.Take(4).Select(sh => $"{sh.name}[{(sh.isSupported ? "ok" : "BAD")}]")));

                        Debug.Log($"[MarkerScan] RENDER {name}: layers[{string.Join(" ", layerCounts)}] " +
                            $"lights={lights.Length} venueLayerIndex={LayerMask.NameToLayer("Venue")}");

                        Debug.Log($"[MarkerScan] EMPTIES {name}: " +
                            string.Join(" | ", empties.Select(t =>
                                $"{t.name}@({t.position.x:F1},{t.position.y:F1},{t.position.z:F1})")));

                        if (hits.Count > 0)
                        {
                            withMarkers++;
                            Debug.Log($"[MarkerScan] {name}: {all.Length} transforms, " +
                                $"{empties.Count} empty | CANDIDATES: {string.Join(", ", hits.Take(12))}" +
                                (hits.Count > 12 ? $" (+{hits.Count - 12} more)" : ""));
                        }
                        else
                        {
                            pureGeometry++;
                            Debug.Log($"[MarkerScan] {name}: {all.Length} transforms, " +
                                $"{empties.Count} empty | PURE GEOMETRY - no named candidates");
                        }
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

            Debug.Log($"[MarkerScan] RESULT: {withMarkers} with candidates, " +
                $"{pureGeometry} pure geometry, {files.Length} total");

            if (keywordTotals.Count > 0)
            {
                var ranked = keywordTotals.OrderByDescending(kv => kv.Value)
                    .Select(kv => $"{kv.Key}={kv.Value}");
                Debug.Log($"[MarkerScan] KEYWORDS: {string.Join("  ", ranked)}");
            }

            EditorApplication.Exit(0);
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
