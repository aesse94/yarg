using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YARG.Venue;

namespace YARG.Editor
{
    /// <summary>
    /// Renders a .yarground's own VenueCamera to a PNG, headless, and reports where the
    /// venue geometry lands on screen. Answers "does it render, how big, where" from the
    /// exact viewpoint the game uses, without driving the game UI.
    ///
    /// Limits, stated so the image is not over-read: this is the raw camera, not the in-game
    /// compositor - no note highway overlay, no VenueCameraRenderer post-processing, and no
    /// SetYargroundOrigin reposition. That last one does not change framing, because the
    /// camera is a child of the venue root and moves with it.
    ///
    /// Must run WITHOUT -nographics, or there is no GPU to render with.
    /// CLI: -executeMethod YARG.Editor.VenueSnapshot.Run -file &lt;bundle&gt; -out &lt;png&gt;
    ///
    /// Optional camera pose, overriding the builder's static VenueCamera (0,3,-12):
    ///   -camPos x,y,z              camera position
    ///   -camLookAt x,y,z           PREFERRED: point to look at (e.g. the next keyframe).
    ///                              Convention-free - no angle definitions to get wrong.
    ///   -camYaw deg -camPitch deg  fallback, with the convention declared below
    ///   -fov deg                   vertical field of view (default: keep camera's, 60)
    ///   -space gltf|unity          coordinate space of the numbers above (default gltf)
    ///
    /// SPACE MATTERS. The venue geometry arrives as glTF and the importer negates Z on the
    /// way into Unity (verified: authored z=-7.5 lands at +7.5, +20 deg yaw lands at 340).
    /// A camera pose given in the same space as the exported placements must go through
    /// the same conversion, or the opening-shot orientation check is mirrored by the test
    /// harness itself - and a mirror is exactly what that check exists to detect.
    ///
    /// Yaw/pitch convention (source space): forward = (sin(yaw)cos(pitch), sin(pitch),
    /// cos(yaw)cos(pitch)). yaw 0 = +Z, yaw 90 = +X; pitch negative = looking down.
    /// If a yaw was computed with a different zero or handedness, use -camLookAt instead.
    /// </summary>
    public static class VenueSnapshot
    {
        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            string file = GetArg(args, "-file");
            string outPng = GetArg(args, "-out");

            var bundle = AssetBundle.LoadFromFile(file);
            if (bundle == null)
            {
                Debug.LogError($"[Snapshot] bundle would not open: {file}");
                EditorApplication.Exit(2);
                return;
            }

            try
            {
                var prefab = BundleBackgroundManager.LoadBackgroundPrefab(bundle);
                var instance = UnityEngine.Object.Instantiate(prefab);
                instance.transform.position = Vector3.zero;

                var cam = instance.GetComponentInChildren<Camera>(true);
                if (cam == null)
                {
                    Debug.LogError("[Snapshot] venue has no camera");
                    EditorApplication.Exit(3);
                    return;
                }

                ApplyCameraPose(cam, args);

                // Combined world bounds of everything that draws.
                var rends = instance.GetComponentsInChildren<Renderer>(true);
                var b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);

                const int W = 1920, H = 1080;
                cam.aspect = (float) W / H;

                // Project all 8 bounding-box corners to find the on-screen footprint.
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                int behind = 0;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3(
                        (i & 1) == 0 ? b.min.x : b.max.x,
                        (i & 2) == 0 ? b.min.y : b.max.y,
                        (i & 4) == 0 ? b.min.z : b.max.z);
                    var vp = cam.WorldToViewportPoint(c);
                    if (vp.z <= 0) { behind++; continue; }
                    minX = Mathf.Min(minX, vp.x); maxX = Mathf.Max(maxX, vp.x);
                    minY = Mathf.Min(minY, vp.y); maxY = Mathf.Max(maxY, vp.y);
                }

                // -probe <name>: where a named renderer lands on screen. This is the
                // NON-circular test of the pose conversion - the renderer's position came
                // through the glTF importer's own RH->LH conversion, independently of
                // ApplyCameraPose's. If the two conversions agree, a camera aimed at a known
                // authored position centres that piece.
                string probe = GetArg(args, "-probe");
                if (!string.IsNullOrEmpty(probe))
                {
                    var target = rends.FirstOrDefault(r => r.name == probe);
                    if (target == null)
                    {
                        Debug.LogError($"[Snapshot] probe '{probe}' not found");
                    }
                    else
                    {
                        var vp = cam.WorldToViewportPoint(target.bounds.center);
                        Debug.Log($"[Snapshot] probe {probe}: world={target.bounds.center} " +
                            $"viewport=({vp.x:F3},{vp.y:F3}) depth={vp.z:F2} " +
                            $"{(Mathf.Abs(vp.x - 0.5f) < 0.02f && Mathf.Abs(vp.y - 0.5f) < 0.02f && vp.z > 0 ? "CENTRED" : "NOT CENTRED")}");
                    }
                }

                float dist = Vector3.Distance(cam.transform.position, b.center);
                Debug.Log($"[Snapshot] camera pos={cam.transform.position} fwd={cam.transform.forward} " +
                    $"fov={cam.fieldOfView:F1} cullingMask={cam.cullingMask}");
                Debug.Log($"[Snapshot] geometry center={b.center} size={b.size} distance={dist:F2}");
                Debug.Log($"[Snapshot] on-screen viewport x[{minX:F3},{maxX:F3}] y[{minY:F3},{maxY:F3}] " +
                    $"=> {(maxX - minX) * 100:F0}% wide x {(maxY - minY) * 100:F0}% tall, corners behind camera={behind}");

                var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.2f, 0.2f, 0.25f); // distinct from grey geometry
                cam.Render();

                RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                cam.targetTexture = null;

                // Count pixels that differ from the clear colour, so "rendered" is a number,
                // not an impression.
                var px = tex.GetPixels32();
                int drawn = 0;
                foreach (var p in px)
                {
                    if (Mathf.Abs(p.r - 51) > 6 || Mathf.Abs(p.g - 51) > 6 || Mathf.Abs(p.b - 64) > 6) drawn++;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(outPng));
                File.WriteAllBytes(outPng, tex.EncodeToPNG());
                Debug.Log($"[Snapshot] drawn pixels={drawn}/{px.Length} ({100.0 * drawn / px.Length:F2}%) -> {outPng}");

                UnityEngine.Object.DestroyImmediate(instance);
            }
            finally
            {
                bundle.Unload(true);
            }

            EditorApplication.Exit(0);
        }

        private static void ApplyCameraPose(Camera cam, string[] args)
        {
            string pos = GetArg(args, "-camPos");
            if (string.IsNullOrEmpty(pos))
            {
                Debug.Log("[Snapshot] pose: builder default VenueCamera (no -camPos given)");
                return;
            }

            bool gltf = (GetArg(args, "-space") ?? "gltf").ToLowerInvariant() != "unity";

            // glTF -> Unity: negate Z. The same conversion the importer applies to geometry.
            Vector3 ToUnity(Vector3 v) => gltf ? new Vector3(v.x, v.y, -v.z) : v;

            var srcPos = ParseVec(pos);
            cam.transform.SetParent(null, true);
            cam.transform.position = ToUnity(srcPos);

            Vector3 srcFwd;
            string mode;
            string look = GetArg(args, "-camLookAt");
            if (!string.IsNullOrEmpty(look))
            {
                srcFwd = ParseVec(look) - srcPos;
                mode = $"lookAt {ParseVec(look)}";
            }
            else
            {
                float yaw = float.Parse(GetArg(args, "-camYaw") ?? "0", System.Globalization.CultureInfo.InvariantCulture) * Mathf.Deg2Rad;
                float pitch = float.Parse(GetArg(args, "-camPitch") ?? "0", System.Globalization.CultureInfo.InvariantCulture) * Mathf.Deg2Rad;
                srcFwd = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch));
                mode = $"yaw/pitch {GetArg(args, "-camYaw")}/{GetArg(args, "-camPitch")}";
            }

            // Forward is a direction, so it takes the same axis flip as positions.
            cam.transform.rotation = Quaternion.LookRotation(ToUnity(srcFwd).normalized, Vector3.up);

            string fov = GetArg(args, "-fov");
            if (!string.IsNullOrEmpty(fov))
            {
                cam.fieldOfView = float.Parse(fov, System.Globalization.CultureInfo.InvariantCulture);
            }

            Debug.Log($"[Snapshot] pose: {mode} space={(gltf ? "gltf (Z negated)" : "unity")} " +
                $"srcPos={srcPos} srcFwd={srcFwd.normalized} -> unityPos={cam.transform.position} " +
                $"unityFwd={cam.transform.forward}");
        }

        private static Vector3 ParseVec(string s)
        {
            var p = s.Split(',');
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            return new Vector3(float.Parse(p[0], ic), float.Parse(p[1], ic), float.Parse(p[2], ic));
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
