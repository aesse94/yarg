using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace YARG.Editor
{
    /// <summary>
    /// The two content defects that made every imported-GLB venue invisible, fixed in one
    /// place so the builder and any future venue path cannot drift apart.
    ///
    /// 1. LAYER. VenueBundleBuilder sets the venue camera's cullingMask to the "Venue"
    ///    layer but never puts the imported geometry ON that layer, so every renderer is
    ///    culled: loaded, instantiated, counted, never drawn. The stock default.yarground
    ///    sits on Venue(9); all 33 imported venues sat on Default(0).
    ///
    /// 2. PIPELINE. The GLB importer produces Built-in RP "Standard" materials. This game
    ///    runs URP, where Standard does not render correctly - it reports isSupported
    ///    (it compiles) while drawing blank.
    /// </summary>
    public static class VenueAssetFixer
    {
        public const string VENUE_LAYER = "Venue";

        // Unlit is the default on purpose. All 33 venues ship ZERO lights, so a Lit shader
        // resolves to ambient-only and renders far darker than the author intended. The
        // stock venue - the one venue that renders correctly in this engine today - uses
        // an unlit shader (Unlit/MenuBackgroundVenue), which is the best available evidence
        // of what these venues are meant to look like. Lit stays available for A/B.
        public const string SHADER_UNLIT = "Universal Render Pipeline/Unlit";
        public const string SHADER_LIT   = "Universal Render Pipeline/Lit";

        /// <summary>Puts the whole hierarchy on the venue layer so the venue camera draws it.</summary>
        public static int ApplyVenueLayer(GameObject root)
        {
            int layer = LayerMask.NameToLayer(VENUE_LAYER);
            if (layer == -1)
            {
                Debug.LogError($"[VenueFix] Layer '{VENUE_LAYER}' is not defined in this project; " +
                    "geometry would be culled. Aborting layer assignment.");
                return 0;
            }

            int n = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = layer;
                n++;
            }

            return n;
        }

        /// <summary>
        /// Replaces non-URP materials with URP equivalents saved as assets under
        /// <paramref name="assetDir"/>, carrying base colour and main texture across.
        /// Materials are written as real assets because the AssetBundle is built from the
        /// prefab, and a material that is not a persistent asset would not be included.
        /// </summary>
        public static int ConvertMaterialsToUrp(GameObject root, string assetDir, bool unlit)
        {
            var target = Shader.Find(unlit ? SHADER_UNLIT : SHADER_LIT);
            if (target == null)
            {
                Debug.LogError($"[VenueFix] Target shader not found: " +
                    $"'{(unlit ? SHADER_UNLIT : SHADER_LIT)}'. Is URP installed?");
                return 0;
            }

            var remapped = new Dictionary<Material, Material>();
            int converted = 0;

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    if (src == null || src.shader == null) continue;

                    // Already a URP shader - leave it alone.
                    if (src.shader.name.StartsWith("Universal Render Pipeline/")) continue;

                    if (!remapped.TryGetValue(src, out var dst))
                    {
                        dst = new Material(target) { name = src.name + "_URP" };

                        // Built-in -> URP property names differ; carry what exists.
                        if (src.HasProperty("_Color"))   dst.SetColor("_BaseColor", src.GetColor("_Color"));
                        if (src.HasProperty("_MainTex")) dst.SetTexture("_BaseMap", src.GetTexture("_MainTex"));

                        string path = AssetDatabase.GenerateUniqueAssetPath(
                            $"{assetDir}/{SanitizeName(dst.name)}.mat");
                        AssetDatabase.CreateAsset(dst, path);
                        remapped[src] = dst;
                        converted++;
                    }

                    mats[i] = dst;
                }

                r.sharedMaterials = mats;
            }

            if (converted > 0)
            {
                AssetDatabase.SaveAssets();
            }

            return converted;
        }

        private static string SanitizeName(string name)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return name;
        }
    }
}
