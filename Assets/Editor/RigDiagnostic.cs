using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YARG.Editor
{
    /// <summary>Dumps the humanoid bone map of a rig so mount targets can be checked.</summary>
    public static class RigDiagnostic
    {
        public static void DumpFromCliArgs()
        {
            var args = Environment.GetCommandLineArgs();
            string rigPath = GetArg(args, "-rig");

            string asset = "Assets/GuitarImport/" + Path.GetFileName(rigPath);
            Directory.CreateDirectory("Assets/GuitarImport");
            File.Copy(rigPath, asset, true);
            AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceUpdate);

            var all = AssetDatabase.LoadAllAssetsAtPath(asset);
            Debug.Log($"[Rig] asset={asset} subassets={all.Length} " +
                $"gameobjects={all.OfType<GameObject>().Count()}");
            foreach (var go in all.OfType<GameObject>())
            {
                Debug.Log($"[Rig] GameObject '{go.name}' animator={(go.GetComponent<Animator>() != null)}");
            }

            var root = all.OfType<GameObject>().FirstOrDefault(g => g.GetComponent<Animator>() != null);
            if (root == null) { Debug.LogError("[Rig] no animator root"); EditorApplication.Exit(1); return; }

            var inst = UnityEngine.Object.Instantiate(root);
            var anim = inst.GetComponent<Animator>();
            Debug.Log($"[Rig] root='{root.name}' isHuman={anim.isHuman} avatar={anim.avatar?.name}");

            foreach (var b in new[] { HumanBodyBones.Hips, HumanBodyBones.Head, HumanBodyBones.LeftHand,
                HumanBodyBones.RightHand, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm })
            {
                var t = anim.GetBoneTransform(b);
                Debug.Log($"[Rig] {b} -> {(t == null ? "<null>" : $"'{t.name}' world={t.position}")}");
            }

            Debug.Log("[Rig] --- renderers ---");
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var mats = string.Join("|", r.sharedMaterials.Select(m => m == null ? "<null>" : m.name));
                int sub = r is SkinnedMeshRenderer smr && smr.sharedMesh != null ? smr.sharedMesh.subMeshCount : -1;
                Debug.Log($"[Rig] renderer '{r.name}' type={r.GetType().Name} submeshes={sub} mats=[{mats}]");
            }

            UnityEngine.Object.DestroyImmediate(inst);
            AssetDatabase.DeleteAsset(asset);
            EditorApplication.Exit(0);
        }

        private static string GetArg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
