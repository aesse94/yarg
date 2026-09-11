using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace YARG.Venue.Guitars
{
    /// <summary>
    /// Detects the submeshes holding a character's built-in guitar, for characters absent
    /// from <see cref="GuitarMountConfig"/>.
    ///
    /// The position thresholds are calibrated against the reference submesh dumps, which
    /// averaged the FIRST 200 triangles of each submesh in index order, in BIND POSE space.
    /// That sample is a localised cluster, not a centroid, so both
    /// <see cref="SamplingMode.LeadingTriangles"/> and <see cref="VertexSpace.BindPose"/>
    /// are required for the thresholds to mean anything. Verified reproduction on
    /// character_b: sub[6] = (-0.50, 1.10), sub[7] = (-0.59, 1.00), matching the dump exactly.
    ///
    /// The position test alone is NOT sufficient. On character_a, which has no guitar at all,
    /// the hand submesh (index 8, 'Ozzy_Hands', avg -0.49 / 1.01) passes it - a slung guitar
    /// and a fretting hand occupy the same region by construction. Two independent guards
    /// reject it: a material-name deny list, and a requirement that a guitar span at least
    /// <see cref="MIN_RUN_LENGTH"/> adjacent submeshes.
    ///
    /// A wrong guess blanks part of a character's body, so an override entry always wins
    /// over this.
    /// </summary>
    public static class GuitarSubmeshDetector
    {
        /// <summary>
        /// Master switch for the heuristic. Turn off to make every character without an
        /// override entry keep its built-in guitar rather than risk a wrong guess.
        /// </summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>Which vertex space <see cref="Evaluate"/> measures in.</summary>
        public enum VertexSpace
        {
            BindPose,
            Posed,
        }

        public static VertexSpace Space { get; set; } = VertexSpace.BindPose;

        /// <summary>How the per-submesh sample is drawn.</summary>
        public enum SamplingMode
        {
            /// <summary>First N triangles in index order - what the reference dumps used.</summary>
            LeadingTriangles,

            /// <summary>Strided across the whole submesh - the true centroid.</summary>
            Strided,
        }

        public static SamplingMode Sampling { get; set; } = SamplingMode.LeadingTriangles;

        // Position test as originally specified. Retained so the thresholds are tunable in
        // one place; see the class remarks for why they are not trusted unvalidated.
        public static float MaxAverageX { get; set; } = -0.4f;
        public static float MinAverageY { get; set; } = 0.8f;
        public static float MaxAverageY { get; set; } = 1.2f;

        // Guard: a guitar spans at least this many adjacent submeshes (body + neck/hardware).
        // A lone match is more likely an arm or hand crossing the body.
        public const int MIN_RUN_LENGTH = 2;

        private const int SAMPLE_LIMIT = 200;

        // Guard: body parts share the guitar's region. Never blank these.
        private static readonly string[] _materialDenyList =
        {
            "hand", "skin", "face", "head", "hair", "eye", "brow", "mouth", "tooth", "teeth",
            "tongue", "arm", "finger", "glove", "nail", "body", "cloth", "shirt", "jacket",
            "boot", "shoe", "pant", "leg",
        };

        /// <summary>
        /// Returns the submesh indices to hide. Returns empty unless <see cref="Enabled"/>
        /// has been explicitly turned on. Never throws.
        /// </summary>
        public static int[] Detect(SkinnedMeshRenderer renderer, out string reason)
        {
            if (!Enabled)
            {
                reason = "heuristic disabled; add a guitar_mount_overrides.json entry " +
                    "for this character";
                return Array.Empty<int>();
            }

            return Evaluate(renderer, out reason);
        }

        /// <summary>
        /// Runs the position test and guards regardless of <see cref="Enabled"/>.
        /// Exposed so tooling can show what the heuristic *would* do without acting on it.
        /// </summary>
        public static int[] Evaluate(SkinnedMeshRenderer renderer, out string reason)
        {
            reason = "no renderer";
            if (renderer == null || renderer.sharedMesh == null)
            {
                return Array.Empty<int>();
            }

            var mesh = renderer.sharedMesh;
            var materials = renderer.sharedMaterials;
            var vertices = Space == VertexSpace.Posed
                ? GetPosedVertices(renderer, mesh)
                : mesh.vertices;

            var matches = new List<int>();
            var rejectedByName = new List<int>();

            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                if (!TryGetAverage(mesh, vertices, sub, out var average))
                {
                    continue;
                }

                if (average.x >= MaxAverageX || average.y <= MinAverageY || average.y >= MaxAverageY)
                {
                    continue;
                }

                string materialName = sub < materials.Length && materials[sub] != null
                    ? materials[sub].name
                    : string.Empty;

                if (IsDeniedMaterial(materialName))
                {
                    rejectedByName.Add(sub);
                    continue;
                }

                matches.Add(sub);
            }

            var run = LongestAdjacentRun(matches);

            if (run.Count < MIN_RUN_LENGTH)
            {
                reason = matches.Count == 0
                    ? $"no submesh passed the position test (name-rejected: [{string.Join(",", rejectedByName)}])"
                    : $"only isolated matches [{string.Join(",", matches)}], need {MIN_RUN_LENGTH} adjacent";
                return Array.Empty<int>();
            }

            reason = $"adjacent run [{string.Join(",", run)}] passed the position test in {Space} space" +
                (rejectedByName.Count > 0 ? $"; name-rejected [{string.Join(",", rejectedByName)}]" : string.Empty);
            return run.ToArray();
        }

        /// <summary>
        /// Vertices with the rig's current pose applied, falling back to bind pose when
        /// baking is unavailable.
        /// </summary>
        public static Vector3[] GetPosedVertices(SkinnedMeshRenderer renderer, Mesh mesh)
        {
            Mesh baked = null;

            try
            {
                baked = new Mesh();
                renderer.BakeMesh(baked, true);

                var vertices = baked.vertices;
                if (vertices != null && vertices.Length == mesh.vertexCount)
                {
                    return vertices;
                }
            }
            catch
            {
                // Fall through to bind pose.
            }
            finally
            {
                if (baked != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(baked);
                    else UnityEngine.Object.DestroyImmediate(baked);
                }
            }

            return mesh.vertices;
        }

        private static bool IsDeniedMaterial(string materialName)
        {
            if (string.IsNullOrEmpty(materialName))
            {
                // Hashed/anonymous names carry no signal; leave it to the run guard.
                return false;
            }

            var lowered = materialName.ToLowerInvariant();
            return _materialDenyList.Any(denied => lowered.Contains(denied));
        }

        private static List<int> LongestAdjacentRun(List<int> indices)
        {
            var best = new List<int>();
            var current = new List<int>();

            foreach (var index in indices)
            {
                if (current.Count > 0 && index != current[^1] + 1)
                {
                    if (current.Count > best.Count) best = new List<int>(current);
                    current.Clear();
                }

                current.Add(index);
            }

            if (current.Count > best.Count) best = new List<int>(current);
            return best;
        }

        /// <summary>
        /// Average position of a submesh's vertices, sampled to <see cref="SAMPLE_LIMIT"/>.
        /// </summary>
        public static bool TryGetAverage(Mesh mesh, Vector3[] vertices, int submesh, out Vector3 average)
        {
            average = Vector3.zero;

            var triangles = mesh.GetTriangles(submesh);
            if (triangles.Length == 0)
            {
                return false;
            }

            // LeadingTriangles reproduces the reference submesh dumps exactly: they averaged
            // the first 200 triangles in index order, which is a localised cluster rather
            // than a centroid. The published thresholds are calibrated to that sample, so
            // changing the sampling silently invalidates them.
            int triangleCount = triangles.Length / 3;
            int sampleCount = Mathf.Min(triangleCount, SAMPLE_LIMIT);
            int step = Sampling == SamplingMode.Strided && sampleCount > 0
                ? Mathf.Max(1, triangleCount / sampleCount)
                : 1;

            int count = 0;
            var sum = Vector3.zero;

            for (int t = 0; t < sampleCount; t++)
            {
                int vertexIndex = triangles[t * step * 3];
                if (vertexIndex < 0 || vertexIndex >= vertices.Length)
                {
                    continue;
                }

                sum += vertices[vertexIndex];
                count++;
            }

            if (count == 0)
            {
                return false;
            }

            average = sum / count;
            return true;
        }
    }
}
