using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YARG.Core.Song;
using YARG.Venue.Characters;
using YARG.Venue.Guitars;

namespace YARG.Editor
{
    /// <summary>
    /// Headless test for the guitar mounting system.
    ///
    ///   Unity -batchmode -nographics -projectPath /root/yarg-build \
    ///     -logFile /tmp/test.log -executeMethod YARG.Editor.GuitarMountTests.RunAll -quit
    ///
    /// Exit 0 when every assertion passes.
    /// </summary>
    public static class GuitarMountTests
    {
        private const string TESTBED    = "/root/instrument_samples/testbed";
        private const string IMPORT_DIR = "Assets/GuitarImport";

        private static int _pass;
        private static int _fail;

        public static void RunAll()
        {
            _pass = 0;
            _fail = 0;

            try
            {
                GuitarMountService.GuitarFolderOverride = Path.Combine(TESTBED, "guitars");
                GuitarMountService.SelectedGuitarPath = Path.Combine(TESTBED, "guitars/guitar_01.glb");
                GuitarMountService.ReloadConfig();

                Log($"config path: {GuitarMountService.ConfigPath}");
                Check(GuitarMountService.Config.Entries.Count == 2,
                    $"override table loaded with 2 entries (got {GuitarMountService.Config.Entries.Count})");

                TestCharacterB();
                TestCharacterA();
                TestDropdownWiring();
                ReportHeuristicOnly();
                TestVocalistSelection();
                TestCharacterDropdown();
            }
            catch (Exception e)
            {
                LogFail($"unhandled exception: {e}");
            }
            finally
            {
                GuitarMountService.GuitarFolderOverride = null;
                GuitarMountService.SelectedGuitarPath = null;
            }

            Log($"RESULT: {_pass} passed, {_fail} failed");
            EditorApplication.Exit(_fail == 0 ? 0 : 1);
        }

        // ---- character_b: has a built-in guitar in submeshes 6 and 7 ----
        private static void TestCharacterB()
        {
            Log("=== character_b (built-in guitar, expects 6 & 7 hidden) ===");

            var root = ImportAndInstantiate("characters/character_b.vrm", out var assetPath);
            if (root == null) return;

            try
            {
                var renderer = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .OrderByDescending(r => r.sharedMesh != null ? r.sharedMesh.subMeshCount : 0)
                    .First();

                var before = (Material[]) renderer.sharedMaterials.Clone();
                Log($"renderer '{renderer.name}' submeshes={renderer.sharedMesh.subMeshCount} " +
                    $"materials={before.Length}");

                var result = GuitarMountService.MountFor(root, "character_b");

                // 1. Mount succeeded at all.
                if (!Check(result != null && result.Success,
                    $"mount succeeded (reason: {result?.FailureReason ?? "null result"})"))
                {
                    return;
                }

                // 2. Bone found, by name.
                Check(result.AttachBone != null, "bone_guitar_body found");
                Check(result.AttachBone != null && string.Equals(result.AttachBone.name,
                        GuitarMounter.GUITAR_BODY_BONE, StringComparison.OrdinalIgnoreCase),
                    $"attach bone is '{GuitarMounter.GUITAR_BODY_BONE}' (got '{result.AttachBone?.name}')");

                // 3. Guitar actually attached under that bone.
                Check(result.GuitarRoot != null, "guitar root instantiated");
                Check(result.GuitarRoot != null && result.GuitarRoot.transform.parent == result.AttachBone,
                    "guitar parented to the attach bone");
                int guitarMeshes = result.GuitarRoot == null
                    ? 0
                    : result.GuitarRoot.GetComponentsInChildren<Renderer>(true).Length;
                Check(guitarMeshes > 0, $"guitar has renderers (got {guitarMeshes})");

                // 4. Transform sane.
                var t = result.GuitarRoot.transform;
                Check(t.localPosition == Vector3.zero, $"local position is identity (got {t.localPosition})");
                Check(t.localRotation == Quaternion.identity, "local rotation is identity");
                Check(t.localScale.x > 0f && float.IsFinite(t.localScale.x),
                    $"local scale is finite and positive (got {t.localScale})");
                Log($"scale: measured body length={result.MeasuredBodyLength:F3} m " +
                    $"applied={result.AppliedScale:F3} normalized={result.WasNormalized}");
                Check(result.MeasuredBodyLength >= 0.4f && result.MeasuredBodyLength <= 0.5f,
                    $"body length within 0.4-0.5 m after mount (got {result.MeasuredBodyLength:F3})");

                // 5. Materials nulled at exactly 6 and 7.
                Check(result.HideSource == "override",
                    $"hiding came from the override table (got '{result.HideSource}')");
                Check(result.HiddenSubmeshes.SequenceEqual(new[] { 6, 7 }),
                    $"hid submeshes [6,7] (got [{string.Join(",", result.HiddenSubmeshes)}])");

                var after = renderer.sharedMaterials;
                Check(after.Length == before.Length,
                    $"material array length unchanged ({before.Length})");
                Check(after[6] == null, "submesh 6 material is null");
                Check(after[7] == null, "submesh 7 material is null");

                // 6. Every other material intact.
                bool othersIntact = true;
                for (int i = 0; i < after.Length; i++)
                {
                    if (i == 6 || i == 7) continue;
                    if (after[i] != before[i])
                    {
                        othersIntact = false;
                        LogFail($"submesh {i} material changed: '{before[i]?.name}' -> '{after[i]?.name}'");
                    }
                }
                Check(othersIntact, "all other submesh materials intact");

                // 7. String bones present (reported; not animated - see summary).
                Log($"guitar string bones found on rig: {result.StringBonesFound}/6");

                // 8. Unmount restores.
                GuitarMounter.Unmount(result);
                var restored = renderer.sharedMaterials;
                Check(restored[6] == before[6] && restored[7] == before[7],
                    "unmount restored the blanked materials");
            }
            finally
            {
                Cleanup(root, assetPath);
            }
        }

        // ---- character_a: no built-in guitar, must never be hidden ----
        private static void TestCharacterA()
        {
            Log("=== character_a (no built-in guitar, expects nothing hidden) ===");

            var root = ImportAndInstantiate("characters/character_a.vrm", out var assetPath);
            if (root == null) return;

            try
            {
                var renderer = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .OrderByDescending(r => r.sharedMesh != null ? r.sharedMesh.subMeshCount : 0)
                    .First();

                var before = (Material[]) renderer.sharedMaterials.Clone();

                var result = GuitarMountService.MountFor(root, "character_a");
                if (!Check(result != null && result.Success,
                    $"mount succeeded (reason: {result?.FailureReason ?? "null result"})"))
                {
                    return;
                }

                Check(result.HideSource == "override",
                    $"hiding decision came from the override table (got '{result.HideSource}')");
                Check(result.HiddenSubmeshes.Length == 0,
                    $"hid nothing (got [{string.Join(",", result.HiddenSubmeshes)}])");

                var after = renderer.sharedMaterials;
                bool allIntact = !after.Where((t, i) => t != before[i]).Any();
                Check(allIntact, "every submesh material untouched");

                int handIndex = Array.FindIndex(before, m => m != null &&
                    m.name.ToLowerInvariant().Contains("hand"));
                if (handIndex >= 0)
                {
                    Check(after[handIndex] != null,
                        $"hand submesh {handIndex} ('{before[handIndex].name}') still visible");
                }

                GuitarMounter.Unmount(result);
            }
            finally
            {
                Cleanup(root, assetPath);
            }
        }

        /// <summary>
        /// The settings dropdown wiring: an empty selection must mount nothing at all, and a
        /// selected .glb must go through the same MountFor path the other tests exercise -
        /// including the override table.
        /// </summary>
        private static void TestDropdownWiring()
        {
            Log("=== settings dropdown wiring ===");

            string guitarsFolder = Path.Combine(TESTBED, "guitars");
            var expected = Directory.GetFiles(guitarsFolder, "*.glb")
                .OrderBy(f => f, StringComparer.Ordinal).ToArray();

            // 1. The dropdown enumerates .glb from the customization folder, empty first.
            var setting = new YARG.Settings.Types.CustomGuitarSetting(string.Empty,
                path => GuitarMountService.SelectedGuitarPath = path);

            var values = setting.PossibleValues;
            Check(values.Count == expected.Length + 1,
                $"dropdown lists {expected.Length} guitars plus an empty entry (got {values.Count})");
            Check(values.Count > 0 && values[0] == string.Empty,
                "empty entry is first in the dropdown");
            Check(expected.All(values.Contains), "every .glb in the folder is listed");
            Check(setting.ValueToString(string.Empty) == "None",
                $"empty value labels as 'None' (got '{setting.ValueToString(string.Empty)}')");

            string first = expected.First();
            Check(setting.ValueToString(first) == Path.GetFileNameWithoutExtension(first),
                $"selected value labels from the file name (got '{setting.ValueToString(first)}')");

            var root = ImportAndInstantiate("characters/character_b.vrm", out var assetPath);
            if (root == null) return;

            try
            {
                var renderer = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .OrderByDescending(r => r.sharedMesh != null ? r.sharedMesh.subMeshCount : 0)
                    .First();
                var before = (Material[]) renderer.sharedMaterials.Clone();

                // 2. Empty selection -> no mount, and nothing touched.
                // Value's setter needs a live SettingsMenu, so drive the callback the way
                // the dropdown does.
                setting.OnChange.Invoke(string.Empty);
                Check(string.IsNullOrEmpty(GuitarMountService.SelectedGuitarPath),
                    "empty dropdown value clears the service selection");

                var emptyResult = GuitarMountService.MountFor(root, "character_b");
                Check(emptyResult == null, "empty selection mounts nothing");
                Check(root.GetComponentsInChildren<Renderer>(true)
                        .All(r => r.transform.parent == null ||
                             r.transform.parent.name != GuitarMounter.GUITAR_BODY_BONE),
                    "no guitar was parented to the attach bone");
                Check(!renderer.sharedMaterials.Where((m, i) => m != before[i]).Any(),
                    "empty selection leaves the built-in guitar visible");

                // 3. Selected guitar -> mounts, and the override table still applies.
                string chosen = Path.Combine(guitarsFolder, "guitar_02.glb");
                setting.OnChange.Invoke(chosen);
                Check(GuitarMountService.SelectedGuitarPath == chosen,
                    "dropdown selection reaches the service");

                var result = GuitarMountService.MountFor(root, "character_b");
                if (!Check(result != null && result.Success,
                    $"selected guitar mounts (reason: {result?.FailureReason ?? "null result"})"))
                {
                    return;
                }

                Check(result.GuitarRoot.transform.parent == result.AttachBone,
                    "guitar parented to bone_guitar_body via the dropdown path");
                Check(result.HideSource == "override",
                    $"override table still applied (got '{result.HideSource}')");
                Check(result.HiddenSubmeshes.SequenceEqual(new[] { 6, 7 }),
                    $"submeshes [6,7] hidden via the dropdown path " +
                    $"(got [{string.Join(",", result.HiddenSubmeshes)}])");

                GuitarMounter.Unmount(result);
            }
            finally
            {
                Cleanup(root, assetPath);
                GuitarMountService.SelectedGuitarPath =
                    Path.Combine(TESTBED, "guitars/guitar_01.glb");
            }
        }

        /// <summary>
        /// Exercises the heuristic with the override table bypassed - what a character NOT
        /// in the table would get. Asserts the safety property (it abstains) and records
        /// what the raw position test would have selected, as evidence.
        /// </summary>
        private static void ReportHeuristicOnly()
        {
            Log("=== heuristic-only dry run (override table bypassed) ===");
            Check(GuitarSubmeshDetector.Enabled, "heuristic is enabled by default");
            Check(GuitarSubmeshDetector.Space == GuitarSubmeshDetector.VertexSpace.BindPose &&
                  GuitarSubmeshDetector.Sampling == GuitarSubmeshDetector.SamplingMode.LeadingTriangles,
                "heuristic defaults match the calibration of its thresholds " +
                $"(got {GuitarSubmeshDetector.Space}/{GuitarSubmeshDetector.Sampling})");

            foreach (var character in new[] { "character_a", "character_b" })
            {
                var root = ImportAndInstantiate($"characters/{character}.vrm", out var assetPath);
                if (root == null) continue;

                try
                {
                    var renderer = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                        .OrderByDescending(r => r.sharedMesh != null ? r.sharedMesh.subMeshCount : 0)
                        .First();

                    var detected = GuitarSubmeshDetector.Detect(renderer, out var reason);
                    Log($"{character}: Detect -> [{string.Join(",", detected)}] ({reason})");

                    if (character == "character_a")
                    {
                        // The safety property that matters: a guitar-less character is
                        // never blanked, even with no override entry to protect it.
                        Check(detected.Length == 0,
                            $"character_a: heuristic hides nothing (got [{string.Join(",", detected)}])");
                    }
                    else
                    {
                        Check(detected.SequenceEqual(new[] { 6, 7 }),
                            $"character_b: heuristic finds [6,7] (got [{string.Join(",", detected)}])");
                    }

                    // Evidence: what the raw position test would pick if trusted.
                    foreach (var space in new[]
                    {
                        GuitarSubmeshDetector.VertexSpace.BindPose,
                        GuitarSubmeshDetector.VertexSpace.Posed,
                    })
                    {
                        GuitarSubmeshDetector.Space = space;
                        GuitarSubmeshDetector.Sampling =
                            GuitarSubmeshDetector.SamplingMode.LeadingTriangles;
                        var raw = GuitarSubmeshDetector.Evaluate(renderer, out var rawReason);
                        Log($"{character}: raw position test in {space} -> " +
                            $"[{string.Join(",", raw)}] ({rawReason})");

                        if (character == "character_a" && raw.Length > 0)
                        {
                            Log($"{character}: NOTE - the raw position test alone would blank " +
                                $"[{string.Join(",", raw)}] on a character with NO guitar " +
                                $"(space={space}); the guards are what prevent this");
                        }
                    }

                    GuitarSubmeshDetector.Space = GuitarSubmeshDetector.VertexSpace.BindPose;
                }
                finally
                {
                    Cleanup(root, assetPath);
                }
            }
        }

        /// <summary>
        /// Prints per-submesh averages for a character so override entries can be written
        /// by hand. CLI: -executeMethod YARG.Editor.GuitarMountTests.DumpSubmeshes -character character_b
        /// </summary>
        public static void DumpSubmeshes()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-character");
            string character = i >= 0 && i + 1 < args.Length ? args[i + 1] : "character_b";

            var root = ImportAndInstantiate($"characters/{character}.vrm", out var assetPath);
            if (root == null) { EditorApplication.Exit(2); return; }

            try
            {
                var renderer = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .OrderByDescending(r => r.sharedMesh != null ? r.sharedMesh.subMeshCount : 0)
                    .First();

                var mesh = renderer.sharedMesh;
                var bindPose = mesh.vertices;
                var vertices = GuitarSubmeshDetector.GetPosedVertices(renderer, mesh);
                var materials = renderer.sharedMaterials;

                Log($"{character}: renderer='{renderer.name}' submeshes={mesh.subMeshCount} " +
                    $"verts={vertices.Length} meshBounds={mesh.bounds}");

                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    GuitarSubmeshDetector.TryGetAverage(mesh, vertices, sub, out var avg);
                    GuitarSubmeshDetector.TryGetAverage(mesh, bindPose, sub, out var bind);
                    string mat = sub < materials.Length && materials[sub] != null
                        ? materials[sub].name : "<null>";
                    Log($"  sub[{sub}] tris={mesh.GetTriangles(sub).Length / 3} mat='{mat}' " +
                        $"posed=({avg.x:F2},{avg.y:F2},{avg.z:F2}) " +
                        $"bind=({bind.x:F2},{bind.y:F2},{bind.z:F2})");
                }
            }
            finally
            {
                Cleanup(root, assetPath);
            }

            EditorApplication.Exit(0);
        }

        /// <summary>
        /// Per-song vocalist auto-selection. The rule is a pure function, so it is tested
        /// exhaustively here without a venue or play mode; the spawn path that consumes it
        /// (BackgroundManager.LoadCustomCharacter) still needs play mode to verify.
        /// </summary>
        private static void TestVocalistSelection()
        {
            Log("=== per-song vocalist selection ===");

            string male = Path.Combine(TESTBED, "characters/character_a.vrm");
            string female = Path.Combine(TESTBED, "characters/character_b.vrm");

            Check(File.Exists(male) && File.Exists(female),
                "both test character VRMs are present");

            // Gender -> slot, with both slots configured and auto-select on.
            var cases = new (VocalGender Gender, string Expected, string Label)[]
            {
                (VocalGender.Male,        male,   "Male -> default vocalist"),
                (VocalGender.Female,      female, "Female -> female vocalist"),
                (VocalGender.Nonbinary,   female, "Nonbinary -> female vocalist"),
                (VocalGender.Other,       male,   "Other -> default vocalist"),
                (VocalGender.Unspecified, male,   "Unspecified -> default vocalist"),
            };

            foreach (var (gender, expected, label) in cases)
            {
                var actual = VocalistSelector.SelectVocalistPath(gender, male, female, true);
                Check(actual == expected,
                    $"{label} (got '{Path.GetFileNameWithoutExtension(actual)}')");
            }

            // Auto-select disabled: the default slot wins regardless of tag.
            foreach (var gender in (VocalGender[]) Enum.GetValues(typeof(VocalGender)))
            {
                var actual = VocalistSelector.SelectVocalistPath(gender, male, female, false);
                Check(actual == male,
                    $"auto-select off: {gender} uses the default vocalist " +
                    $"(got '{Path.GetFileNameWithoutExtension(actual)}')");
            }

            // Never blank the stage: a female-tagged song with no female slot configured
            // falls back to the default rather than resolving to nothing.
            Check(VocalistSelector.SelectVocalistPath(VocalGender.Female, male, string.Empty, true) == male,
                "Female song with empty female slot falls back to the default vocalist");
            Check(VocalistSelector.SelectVocalistPath(VocalGender.Nonbinary, male, null, true) == male,
                "Nonbinary song with unset female slot falls back to the default vocalist");

            // Nothing configured at all means "leave the venue's own vocalist alone".
            Check(VocalistSelector.SelectVocalistPath(VocalGender.Female, string.Empty, string.Empty, true)
                    == string.Empty,
                "no slots configured resolves to empty (venue vocalist untouched)");

            // Only a female slot configured: a male song must not silently borrow it.
            Check(VocalistSelector.SelectVocalistPath(VocalGender.Male, string.Empty, female, true)
                    == string.Empty,
                "Male song does not borrow the female slot when the default is empty");

            // Slot mapping is independent of which files are configured.
            Check(VocalistSelector.SlotForGender(VocalGender.Female) == VocalistSelector.Slot.Female &&
                  VocalistSelector.SlotForGender(VocalGender.Nonbinary) == VocalistSelector.Slot.Female &&
                  VocalistSelector.SlotForGender(VocalGender.Male) == VocalistSelector.Slot.Default &&
                  VocalistSelector.SlotForGender(VocalGender.Other) == VocalistSelector.Slot.Default &&
                  VocalistSelector.SlotForGender(VocalGender.Unspecified) == VocalistSelector.Slot.Default,
                "slot mapping covers every VocalGender value");
        }

        /// <summary>
        /// End-to-end check on the custom character dropdown: a rebuilt .yargchar must
        /// actually reach the vocals list. This covers all three bugs at once - the prefab
        /// path (or the bundle loads as null), the stamped Type (or the filter excludes it),
        /// and the filter direction itself.
        /// </summary>
        private static void TestCharacterDropdown()
        {
            Log("=== custom character dropdown ===");

            const string REBUILT = "/root/yargchar_out";
            if (!Directory.Exists(REBUILT))
            {
                LogFail($"rebuilt character bundles not found at {REBUILT}");
                return;
            }

            // PathHelper.Init is [RuntimeInitializeOnLoadMethod], so it never runs in a
            // batchmode edit-mode session and PersistentDataPath stays null. The character
            // dropdown reads it, so prime it the way the runtime would.
            if (!EnsurePathHelperInitialized())
            {
                LogFail("could not initialize PathHelper; character dropdown not testable");
                return;
            }

            var setting = new YARG.Settings.Types.CustomCharacterSetting(
                string.Empty, VenueCharacter.CharacterType.Vocals);

            string folder = setting.CustomCharacterPath;
            var staged = new List<string>();

            try
            {
                // Stage two rebuilt bundles into the real customization folder.
                foreach (var source in Directory.GetFiles(REBUILT, "*.yargchar").OrderBy(f => f).Take(2))
                {
                    string destination = Path.Combine(folder, Path.GetFileName(source));
                    File.Copy(source, destination, true);
                    staged.Add(destination);
                }

                Check(staged.Count == 2, $"staged 2 bundles into {folder} (got {staged.Count})");

                setting.UpdateValues();
                var values = setting.PossibleValues;

                foreach (var path in staged)
                {
                    string label = Path.GetFileNameWithoutExtension(path);
                    Check(values.Contains(path),
                        $"'{label}' is listed in the vocals dropdown");
                    Check(setting.ValueToString(path) != "None",
                        $"'{label}' has a display name (got '{setting.ValueToString(path)}')");
                }

                // A Vocals-typed character must NOT appear in a different slot's dropdown.
                var drumsSetting = new YARG.Settings.Types.CustomCharacterSetting(
                    string.Empty, VenueCharacter.CharacterType.Drums);
                drumsSetting.UpdateValues();

                Check(staged.All(p => !drumsSetting.PossibleValues.Contains(p)),
                    "Vocals characters are excluded from the Drums dropdown");
            }
            finally
            {
                foreach (var path in staged)
                {
                    try { File.Delete(path); } catch { /* best effort */ }
                }
            }
        }

        private static bool EnsurePathHelperInitialized()
        {
            if (!string.IsNullOrEmpty(YARG.Helpers.PathHelper.PersistentDataPath))
            {
                return true;
            }

            var init = typeof(YARG.Helpers.PathHelper).GetMethod("Init",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            if (init == null)
            {
                return false;
            }

            init.Invoke(null, null);
            return !string.IsNullOrEmpty(YARG.Helpers.PathHelper.PersistentDataPath);
        }

        // ---- helpers ----

        private static GameObject ImportAndInstantiate(string relativePath, out string assetPath)
        {
            assetPath = null;
            string source = Path.Combine(TESTBED, relativePath);

            if (!File.Exists(source))
            {
                LogFail($"test asset missing: {source}");
                return null;
            }

            Directory.CreateDirectory(IMPORT_DIR);
            assetPath = $"{IMPORT_DIR}/{Path.GetFileName(source)}";
            File.Copy(source, assetPath, true);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            var prefab = AssetDatabase.LoadAllAssetsAtPath(assetPath)
                .OfType<GameObject>()
                .FirstOrDefault(g => g.GetComponentInChildren<SkinnedMeshRenderer>(true) != null &&
                    g.transform.parent == null);

            if (prefab == null)
            {
                LogFail($"no root GameObject imported from {assetPath}");
                return null;
            }

            return UnityEngine.Object.Instantiate(prefab);
        }

        private static void Cleanup(GameObject root, string assetPath)
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            if (!string.IsNullOrEmpty(assetPath)) AssetDatabase.DeleteAsset(assetPath);
        }

        private static bool Check(bool condition, string description)
        {
            if (condition)
            {
                _pass++;
                Debug.Log($"[GuitarTest] PASS: {description}");
            }
            else
            {
                _fail++;
                Debug.LogError($"[GuitarTest] FAIL: {description}");
            }

            return condition;
        }

        private static void Log(string message) => Debug.Log($"[GuitarTest] {message}");

        private static void LogFail(string message)
        {
            _fail++;
            Debug.LogError($"[GuitarTest] FAIL: {message}");
        }
    }
}
