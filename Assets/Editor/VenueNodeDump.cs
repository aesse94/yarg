using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YARG.Venue;

namespace YARG.Editor
{
    /// <summary>
    /// Diagnostic: loads a built .yarground and reports, per renderer, its world position,
    /// rotation, layer and shader - to check whether a multi-node GLB keeps its per-node
    /// transforms through VenueBundleBuilder, or collapses to one mesh at the origin.
    ///
    /// CLI: -executeMethod YARG.Editor.VenueNodeDump.Run -file &lt;bundle&gt;
    /// </summary>
    public static class VenueNodeDump
    {
        public static void Run()
        {
            string file = GetArg(Environment.GetCommandLineArgs(), "-file");
            var bundle = AssetBundle.LoadFromFile(file);
            if (bundle == null)
            {
                Debug.LogError($"[NodeDump] bundle would not open: {file}");
                EditorApplication.Exit(2);
                return;
            }

            try
            {
                var instance = UnityEngine.Object.Instantiate(BundleBackgroundManager.LoadBackgroundPrefab(bundle));
                instance.transform.position = Vector3.zero;

                var rends = instance.GetComponentsInChildren<Renderer>(true);
                int venueLayer = LayerMask.NameToLayer("Venue");
                var meshes = rends.Select(r => r.GetComponent<MeshFilter>()?.sharedMesh)
                    .Where(m => m != null).Distinct().Count();

                Debug.Log($"[NodeDump] renderers={rends.Length} distinctMeshes={meshes} " +
                    $"allOnVenueLayer={rends.All(r => r.gameObject.layer == venueLayer)} " +
                    $"shaders=[{string.Join(",", rends.SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.shader.name).Distinct())}]");

                var ps = rends.Select(r => r.transform.position).ToArray();
                Debug.Log($"[NodeDump] span x={ps.Max(p => p.x) - ps.Min(p => p.x):F2} " +
                    $"y={ps.Max(p => p.y) - ps.Min(p => p.y):F2} z={ps.Max(p => p.z) - ps.Min(p => p.z):F2} " +
                    $"distinctPositions={ps.Select(p => (Mathf.Round(p.x * 100), Mathf.Round(p.y * 100), Mathf.Round(p.z * 100))).Distinct().Count()} " +
                    $"distinctRotations={rends.Select(r => Mathf.Round(r.transform.rotation.eulerAngles.y)).Distinct().Count()}");

                foreach (var r in rends.OrderBy(r => r.name).Take(4))
                {
                    Debug.Log($"[NodeDump]   {r.name,-10} depth={Depth(r.transform, instance.transform)} " +
                        $"pos={r.transform.position} rotY={r.transform.rotation.eulerAngles.y:F0}");
                }

                UnityEngine.Object.DestroyImmediate(instance);
            }
            finally
            {
                bundle.Unload(true);
            }

            EditorApplication.Exit(0);
        }

        private static int Depth(Transform t, Transform root)
        {
            int d = 0;
            for (; t != null && t != root; t = t.parent) d++;
            return d;
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
