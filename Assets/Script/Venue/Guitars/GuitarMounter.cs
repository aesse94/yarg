using System;
using System.Collections.Generic;
using System.Linq;
using UniGLTF;
using UnityEngine;
using YARG.Core.Logging;

namespace YARG.Venue.Guitars
{
    /// <summary>
    /// Mounts an imported guitar onto a YARG character.
    ///
    /// The character rigs carry a guitar skeleton (bone_guitar_body, bone_guitar_string_1..6,
    /// BONE_GUITAR_FRET_POS, bone_ik_hand_guitar_l/r). Parenting to bone_guitar_body by NAME
    /// makes the prop inherit that bone's existing strum animation for free.
    ///
    /// Attaching by HumanBodyBones is deliberately not supported: the humanoid avatar maps on
    /// these rigs are scrambled (Head resolves to a torso bone, LeftHand to the right forearm).
    /// </summary>
    public static class GuitarMounter
    {
        public const string GUITAR_BODY_BONE = "bone_guitar_body";
        public const string GUITAR_STRING_BONE_PREFIX = "bone_guitar_string_";
        public const int    GUITAR_STRING_COUNT = 6;

        // A real guitar body is ~0.45 m on a ~1.7 m player. The imported GLBs are whole
        // guitars, so we measure the overall longest axis and treat the body as this
        // fraction of it - the reference GLBs are ~2.2 units long and need ~0.45x.
        private const float BODY_FRACTION_OF_LENGTH = 0.45f;
        private const float MIN_BODY_LENGTH = 0.4f;
        private const float MAX_BODY_LENGTH = 0.5f;
        private const float TARGET_BODY_LENGTH = 0.45f;

        public class MountResult
        {
            public bool          Success;
            public string        FailureReason;
            public Transform     AttachBone;
            public GameObject    GuitarRoot;
            public RuntimeGltfInstance Instance;

            public float  MeasuredBodyLength;
            public float  AppliedScale;
            public bool   WasNormalized;

            public int[]  HiddenSubmeshes = Array.Empty<int>();
            public string HideSource = "none";
            public string HideReason = string.Empty;

            public int    StringBonesFound;

            /// <summary>Materials replaced during hiding, for <see cref="Unmount"/>.</summary>
            public SkinnedMeshRenderer HiddenRenderer;
            public Material[]          OriginalMaterials;
        }

        /// <summary>
        /// Loads <paramref name="glbPath"/> and mounts it on <paramref name="characterRoot"/>.
        /// Never throws; inspect <see cref="MountResult.Success"/>.
        /// </summary>
        public static MountResult Mount(GameObject characterRoot, string glbPath,
            GuitarMountConfig config, params string[] characterIdentifiers)
        {
            var result = new MountResult();

            if (characterRoot == null)
            {
                result.FailureReason = "character root is null";
                return result;
            }

            // 1. Attach point, by name only.
            var bone = FindBoneByName(characterRoot, GUITAR_BODY_BONE);
            if (bone == null)
            {
                result.FailureReason = $"rig has no '{GUITAR_BODY_BONE}' bone";
                return result;
            }

            result.AttachBone = bone;
            result.StringBonesFound = CountStringBones(characterRoot);

            // 2. Import the GLB.
            var guitar = GuitarLoader.Load(glbPath, out var instance);
            if (guitar == null)
            {
                result.FailureReason = $"failed to import '{glbPath}'";
                return result;
            }

            result.GuitarRoot = guitar;
            result.Instance = instance;

            // 3. Parent at identity local transform, so the bone's animation drives it directly.
            var t = guitar.transform;
            t.SetParent(bone, false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;

            // 4. Scale sanity.
            NormalizeScale(result);

            // 5. Hide the character's built-in guitar, if it has one.
            ApplyHiding(characterRoot, config, result, characterIdentifiers);

            result.Success = true;
            return result;
        }

        /// <summary>Reverses a mount: restores blanked materials and destroys the prop.</summary>
        public static void Unmount(MountResult result)
        {
            if (result == null)
            {
                return;
            }

            if (result.HiddenRenderer != null && result.OriginalMaterials != null)
            {
                result.HiddenRenderer.sharedMaterials = result.OriginalMaterials;
            }

            if (result.GuitarRoot != null)
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(result.GuitarRoot);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(result.GuitarRoot);
                }
            }
        }

        public static Transform FindBoneByName(GameObject root, string boneName)
        {
            foreach (var candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(candidate.name, boneName, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static int CountStringBones(GameObject root)
        {
            int found = 0;
            for (int i = 1; i <= GUITAR_STRING_COUNT; i++)
            {
                if (FindBoneByName(root, GUITAR_STRING_BONE_PREFIX + i) != null)
                {
                    found++;
                }
            }

            return found;
        }

        /// <summary>
        /// Measures the mounted prop and rescales it when its body length falls outside
        /// the plausible range. Measurement is in world space, so any scale already on the
        /// attach bone is accounted for.
        /// </summary>
        private static void NormalizeScale(MountResult result)
        {
            if (!TryMeasureWorldBounds(result.GuitarRoot, out var bounds))
            {
                result.MeasuredBodyLength = -1f;
                result.AppliedScale = 1f;
                return;
            }

            float longestAxis = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            float bodyLength = longestAxis * BODY_FRACTION_OF_LENGTH;

            result.MeasuredBodyLength = bodyLength;
            result.AppliedScale = 1f;

            if (bodyLength <= Mathf.Epsilon)
            {
                return;
            }

            if (bodyLength >= MIN_BODY_LENGTH && bodyLength <= MAX_BODY_LENGTH)
            {
                YargLogger.LogFormatInfo("Guitar body length {0:F3} m is in range; no rescale",
                    bodyLength);
                return;
            }

            float factor = TARGET_BODY_LENGTH / bodyLength;
            result.GuitarRoot.transform.localScale *= factor;
            result.AppliedScale = factor;
            result.WasNormalized = true;

            // Re-measure so the reported figure is the post-scale one.
            if (TryMeasureWorldBounds(result.GuitarRoot, out var scaled))
            {
                float scaledLongest = Mathf.Max(scaled.size.x, Mathf.Max(scaled.size.y, scaled.size.z));
                result.MeasuredBodyLength = scaledLongest * BODY_FRACTION_OF_LENGTH;
            }

            YargLogger.LogFormatInfo<float, float>(
                "Guitar body length {0:F3} m out of range; rescaled by {1:F3}",
                bodyLength, factor);
        }

        private static bool TryMeasureWorldBounds(GameObject root, out Bounds bounds)
        {
            bounds = default;
            bool any = false;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!any)
                {
                    bounds = renderer.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return any;
        }

        /// <summary>
        /// Blanks the submeshes holding the character's built-in guitar. A submesh cannot be
        /// disabled individually, so its entry in sharedMaterials is set to null; Unity then
        /// skips drawing it while every other submesh keeps its material.
        /// </summary>
        private static void ApplyHiding(GameObject characterRoot, GuitarMountConfig config,
            MountResult result, string[] identifiers)
        {
            var renderer = characterRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .OrderByDescending(r => r.sharedMesh != null ? r.sharedMesh.subMeshCount : 0)
                .FirstOrDefault();

            if (renderer == null || renderer.sharedMesh == null)
            {
                result.HideSource = "none";
                result.HideReason = "character has no skinned mesh renderer";
                return;
            }

            int[] indices;

            // The override table is authoritative. A listed character never reaches the
            // heuristic - including one listed with an empty array, which is how a
            // guitar-less character is protected.
            if (config != null && config.TryGetEntry(out var entry, identifiers))
            {
                indices = entry.HideSubmeshes ?? Array.Empty<int>();
                result.HideSource = "override";
                result.HideReason = indices.Length == 0
                    ? $"override lists '{entry.Character}' with no submeshes to hide"
                    : $"override for '{entry.Character}'";
            }
            else
            {
                indices = GuitarSubmeshDetector.Detect(renderer, out var reason);
                result.HideSource = "heuristic";
                result.HideReason = reason;

                if (indices.Length > 0)
                {
                    // The heuristic firing is worth a manual override entry.
                    YargLogger.LogFormatWarning<string, string>(
                        "Guitar hiding fell back to the heuristic for [{0}] ({1}). " +
                        "Add an override entry to make this deterministic.",
                        string.Join(",", identifiers ?? Array.Empty<string>()), reason);
                }
            }

            if (indices.Length == 0)
            {
                return;
            }

            var materials = renderer.sharedMaterials;
            result.HiddenRenderer = renderer;
            result.OriginalMaterials = (Material[]) materials.Clone();

            var applied = new List<int>();
            foreach (var index in indices)
            {
                if (index < 0 || index >= materials.Length)
                {
                    YargLogger.LogFormatWarning("Guitar hide index {0} is out of range; skipped", index);
                    continue;
                }

                materials[index] = null;
                applied.Add(index);
            }

            renderer.sharedMaterials = materials;
            result.HiddenSubmeshes = applied.ToArray();
        }
    }
}
