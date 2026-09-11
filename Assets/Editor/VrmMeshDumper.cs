using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UniVRM10;

namespace YARG.Editor
{
    public static class VrmMeshDumper
    {
        public static void DumpFromCliArgs()
        {
            var args = Environment.GetCommandLineArgs();
            string vrmPath = GetArg(args, "-vrmPath");
            string outPath = GetArg(args, "-dumpOut") ?? "/tmp/vrm_meshdump.txt";
            if (string.IsNullOrEmpty(vrmPath) || !File.Exists(vrmPath))
            {
                Debug.LogError($"[VrmMeshDumper] Missing -vrmPath: '{vrmPath}'");
                EditorApplication.Exit(2);
                return;
            }
            try
            {
                using var sw = new StreamWriter(outPath, false);
                var data = new UniGLTF.GlbFileParser(vrmPath).Parse();
                var vrmData = UniVRM10.Vrm10Data.Parse(data);
                var context = new UniVRM10.Vrm10Importer(vrmData);
                var instance = context.LoadAsync(new UniGLTF.ImmediateCaller()).GetAwaiter().GetResult();
                var root = instance.Root;
                root.SetActive(true);

                // Locate guitar body bone position for distance reference
                Transform guitarBody = null;
                foreach (var t in root.GetComponentsInChildren<Transform>())
                    if (t.name == "bone_guitar_body") { guitarBody = t; break; }

                foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var mesh = smr.sharedMesh;
                    if (mesh == null) continue;
                    sw.WriteLine($"=== Renderer: {smr.gameObject.name} | Mesh: {mesh.name} | SubMeshes: {mesh.subMeshCount} | verts={mesh.vertexCount} ===");
                    var mats = smr.sharedMaterials;
                    var verts = mesh.vertices;
                    var bws = mesh.boneWeights;
                    var bones = smr.bones;
                    for (int i = 0; i < mesh.subMeshCount; i++)
                    {
                        var sub = mesh.GetSubMesh(i);
                        string matName = (mats != null && i < mats.Length && mats[i] != null) ? mats[i].name : "(none)";
                        // Sample verts of this submesh: avg distance from guitar body bone (bindpose space approximation: use vertex pos)
                        var tris = mesh.GetTriangles(i);
                        int sample = Math.Min(tris.Length / 3, 200);
                        Vector3 sum = Vector3.zero;
                        int guitarW = 0;
                        for (int t = 0; t < sample; t++)
                        {
                            int v = tris[t * 3];
                            sum += verts[v];
                            if (bws[v].weight0 > 0.5f && bws[v].boneIndex0 < bones.Length && bones[bws[v].boneIndex0] != null
                                && bones[bws[v].boneIndex0].name.ToLower().Contains("guitar"))
                                guitarW++;
                        }
                        Vector3 avg = sample > 0 ? sum / sample : Vector3.zero;
                        sw.WriteLine($"  sub[{i}] tris={sub.indexCount/3} mat='{matName}' avgVert=({avg.x:F2},{avg.y:F2},{avg.z:F2}) guitarWeighted={guitarW}/{sample}");
                    }
                }

                if (guitarBody != null)
                    sw.WriteLine($"GUITAR_BODY_BONE localPos={guitarBody.localPosition} worldPos={guitarBody.position}");

                UnityEngine.Object.DestroyImmediate(root);
                sw.WriteLine("[VrmMeshDumper] COMPLETE");
                Debug.Log($"[VrmMeshDumper] DONE -> {outPath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[VrmMeshDumper] FAILED: {e}");
                try { File.AppendAllText(outPath, $"\nERROR: {e}"); } catch {}
                EditorApplication.Exit(1);
                return;
            }
            EditorApplication.Exit(0);
        }

        private static string GetArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
