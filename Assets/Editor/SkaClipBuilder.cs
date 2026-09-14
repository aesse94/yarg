using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YARG.Editor
{
    /// <summary>
    /// Builds Unity humanoid AnimationClips from .ska data exported by
    /// ska-devkit/ska_export.py.
    ///
    /// YARG's vocal clips are humanoid MUSCLE clips (attributes like
    /// "Left Forearm Stretch", classID 95, empty path) rather than transform
    /// clips, so per-bone quaternions cannot be written directly. Muscle values
    /// depend on the Avatar's rig definition, so the conversion has to run inside
    /// Unity: reconstruct the GH skeleton, build a HumanAvatar from it, drive the
    /// bones per frame, and read the resulting HumanPose back out.
    ///
    /// Clips are written over the existing .anim files so their .meta GUIDs - and
    /// therefore the DefaultController's state wiring - are preserved. No state is
    /// renamed and AnimatorParameters.json is never touched.
    /// </summary>
    public static class SkaClipBuilder
    {
        private const string ExportDir = "SkaExport";
        private const string ClipDir   = "Assets/Resources/Animations/Vocals/Default";

        [Serializable] private class BoneJson
        {
            public int index;
            public string name;
            public string parent;
            public float[] restPos;
            public float[] restRot;
            public float[] rot;      // flat, stride 4 (x,y,z,w)
            public bool animated;
        }

        [Serializable] private class ClipJson
        {
            public string name;
            public string source;
            public float duration;
            public int sampleRate;
            public int frameCount;
            public string clipDir;
            // Additive muscle-level correction, applied only when the exporter
            // declares it. GH3 exports never set these, so that path is untouched.
            public float clavicleFBBiasL;
            public float clavicleFBBiasR;
            // Authored shoulder clamp. Declared per clip by the exporter; GH3 exports
            // never set it, so the GH3 v7 freeze holds by construction. An explicit,
            // documented clamp is preferred over leaning on Unity's incidental runtime
            // clamp - the artifact is then intentional and recorded in the manifest.
            public bool clampShoulderMuscles;
            // Arm/forearm clamp (ruling 2026-09-12). Same discipline: exporter
            // declares, builder applies, GH3 never declares it.
            public bool clampArmMuscles;
            // Leg clamp. Authorised only as the fallback for a transient residual the
            // pole fix does not cover (ruling 2026-09-12); declared per take so it can
            // never apply silently or broadly.
            public bool clampLegMuscles;
            public BoneJson[] bones;
        }

        // Authored clamp band for the shoulder muscles on takes that declare it.
        private const float SHOULDER_CLAMP = 2.0f;

        public static void BuildAll()
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), ExportDir);
            if (!Directory.Exists(dir))
            {
                Debug.LogError($"[SkaClip] export dir missing: {dir}");
                EditorApplication.Exit(2);
                return;
            }

            int ok = 0, fail = 0;
            foreach (var file in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories).OrderBy(f => f))
            {
                try
                {
                    if (BuildOne(file)) ok++; else fail++;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[SkaClip] {Path.GetFileName(file)}: {e}");
                    fail++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SkaClip] DONE ok={ok} fail={fail}");
            EditorApplication.Exit(fail == 0 ? 0 : 1);
        }


        /// <summary>
        /// Empirically determines the root-frame correction, and checks the twist
        /// muscles before any clip is regenerated.
        ///
        /// The candidate is applied to the Hips' local rotation, which is what Unity
        /// derives the body frame from. Acceptance: RootT.y carries the height,
        /// RootT.x/z near zero, RootQ a stable constant.
        /// </summary>
        public static void DiagnoseRoot()
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), ExportDir);
            var data = Load(Path.Combine(dir, "VoxMicGrab.json"));

            var candidates = new (string name, Quaternion q)[]
            {
                ("identity",   Quaternion.identity),
                ("-90 Z",      Quaternion.Euler(0, 0, -90)),
                ("+90 Z",      Quaternion.Euler(0, 0,  90)),
                ("-90 X",      Quaternion.Euler(-90, 0, 0)),
                ("+90 X",      Quaternion.Euler( 90, 0, 0)),
                ("-90 Y",      Quaternion.Euler(0, -90, 0)),
                ("+90 Y",      Quaternion.Euler(0,  90, 0)),
                ("180 Z",      Quaternion.Euler(0, 0, 180)),
            };

            int twLUL = Array.IndexOf(HumanTrait.MuscleName, "Left Upper Leg Twist In-Out");
            int twRUL = Array.IndexOf(HumanTrait.MuscleName, "Right Upper Leg Twist In-Out");
            int twLA  = Array.IndexOf(HumanTrait.MuscleName, "Left Arm Twist In-Out");
            int twRA  = Array.IndexOf(HumanTrait.MuscleName, "Right Arm Twist In-Out");
            int shR   = Array.IndexOf(HumanTrait.MuscleName, "Right Shoulder Down-Up");

            Debug.Log("[Diag] candidate | bodyPos (x,y,z) | bodyRot (x,y,z,w) | twist LUL/RUL/LArm/RArm | ShoulderR | OOR");

            foreach (var (cname, corr) in candidates)
            {
                var root = new GameObject("DiagRoot");
                var xf = new Dictionary<string, Transform>();
                try
                {
                    foreach (var b in data.bones) xf[b.name] = new GameObject(b.name).transform;
                    foreach (var b in data.bones)
                    {
                        var t = xf[b.name];
                        var isRoot = string.IsNullOrEmpty(b.parent);
                        t.SetParent(isRoot ? root.transform : xf[b.parent], false);
                        t.localPosition = new Vector3(b.restPos[0], b.restPos[1], b.restPos[2]);
                        var rq = new Quaternion(b.restRot[0], b.restRot[1], b.restRot[2], b.restRot[3]);
                        t.localRotation = isRoot ? corr * rq : rq;
                    }

                    PoseArmsToT(xf);
                    var avatar = BuildAvatar(root, data);
                    foreach (var b in data.bones)
                    {
                        var isRoot = string.IsNullOrEmpty(b.parent);
                        var rq = new Quaternion(b.restRot[0], b.restRot[1], b.restRot[2], b.restRot[3]);
                        xf[b.name].localRotation = isRoot ? corr * rq : rq;
                    }

                    if (avatar == null || !avatar.isValid)
                    {
                        Debug.Log($"[Diag] {cname,-10} AVATAR INVALID");
                        continue;
                    }

                    var handler = new HumanPoseHandler(avatar, root.transform);
                    var pose = new HumanPose();

                    int oor = 0;
                    float lul = 0, rul = 0, la = 0, ra = 0, sr = 0;
                    int frames = Mathf.Min(data.frameCount, 60);
                    for (int f = 0; f < frames; f++)
                    {
                        foreach (var b in data.bones)
                        {
                            int o = Mathf.Min(f, b.rot.Length / 4 - 1) * 4;
                            var q = new Quaternion(b.rot[o], b.rot[o+1], b.rot[o+2], b.rot[o+3]);
                            xf[b.name].localRotation =
                                string.IsNullOrEmpty(b.parent) ? corr * q : q;
                        }
                        handler.GetHumanPose(ref pose);
                        for (int m = 0; m < HumanTrait.MuscleCount; m++)
                            if (Mathf.Abs(pose.muscles[m]) > 1.0001f) oor++;
                        lul = Mathf.Max(lul, Mathf.Abs(pose.muscles[twLUL]));
                        rul = Mathf.Max(rul, Mathf.Abs(pose.muscles[twRUL]));
                        la  = Mathf.Max(la,  Mathf.Abs(pose.muscles[twLA]));
                        ra  = Mathf.Max(ra,  Mathf.Abs(pose.muscles[twRA]));
                        sr  = Mathf.Max(sr,  Mathf.Abs(pose.muscles[shR]));
                    }
                    handler.GetHumanPose(ref pose);
                    Debug.Log($"[Diag] {cname,-10} pos({pose.bodyPosition.x,7:F3},{pose.bodyPosition.y,7:F3},{pose.bodyPosition.z,7:F3}) " +
                        $"rot({pose.bodyRotation.x,6:F3},{pose.bodyRotation.y,6:F3},{pose.bodyRotation.z,6:F3},{pose.bodyRotation.w,6:F3}) " +
                        $"twist {lul:F2}/{rul:F2}/{la:F2}/{ra:F2}  shR {sr:F2}  OOR {oor}");
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            EditorApplication.Exit(0);
        }


        /// <summary>Sweeps ClavicleRoll and reports Shoulder Front-Back behaviour.</summary>
        public static void SweepClavicleRoll()
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), ExportDir);
            int sfbL = Array.IndexOf(HumanTrait.MuscleName, "Left Shoulder Front-Back");
            int sfbR = Array.IndexOf(HumanTrait.MuscleName, "Right Shoulder Front-Back");

            Debug.Log("[Sweep] roll | clip | ShoulderFB max |L|,|R| | shoulder OOR | total OOR");
            foreach (var clip in new[] { "VoxIdleRealtime", "VoxIdle" })
            {
                var data = Load(Path.Combine(dir, clip + ".json"));
                for (float roll = -90f; roll <= 90.5f; roll += 15f)
                {
                    ClavicleRoll = roll;
                    var root = new GameObject("SweepRoot");
                    var xf = new Dictionary<string, Transform>();
                    try
                    {
                        foreach (var b in data.bones) xf[b.name] = new GameObject(b.name).transform;
                        foreach (var b in data.bones)
                        {
                            var t = xf[b.name];
                            var isRoot = string.IsNullOrEmpty(b.parent);
                            t.SetParent(isRoot ? root.transform : xf[b.parent], false);
                            t.localPosition = new Vector3(b.restPos[0], b.restPos[1], b.restPos[2]);
                            t.localRotation = new Quaternion(b.restRot[0], b.restRot[1], b.restRot[2], b.restRot[3]);
                        }
                        PoseArmsToT(xf);
                        var avatar = BuildAvatar(root, data);
                        if (avatar == null || !avatar.isValid) { Debug.Log($"[Sweep] {roll,5:F0} {clip} INVALID"); continue; }
                        foreach (var b in data.bones)
                            xf[b.name].localRotation = new Quaternion(b.restRot[0], b.restRot[1], b.restRot[2], b.restRot[3]);

                        var handler = new HumanPoseHandler(avatar, root.transform);
                        var pose = new HumanPose();
                        float mL = 0, mR = 0; int sOOR = 0, tOOR = 0;
                        for (int f = 0; f < data.frameCount; f++)
                        {
                            foreach (var b in data.bones)
                            {
                                int o = Mathf.Min(f, b.rot.Length / 4 - 1) * 4;
                                xf[b.name].localRotation = new Quaternion(b.rot[o], b.rot[o+1], b.rot[o+2], b.rot[o+3]);
                            }
                            handler.GetHumanPose(ref pose);
                            mL = Mathf.Max(mL, Mathf.Abs(pose.muscles[sfbL]));
                            mR = Mathf.Max(mR, Mathf.Abs(pose.muscles[sfbR]));
                            if (Mathf.Abs(pose.muscles[sfbL]) > 1.0001f) sOOR++;
                            if (Mathf.Abs(pose.muscles[sfbR]) > 1.0001f) sOOR++;
                            for (int m = 0; m < HumanTrait.MuscleCount; m++)
                                if (Mathf.Abs(pose.muscles[m]) > 1.0001f) tOOR++;
                        }
                        Debug.Log($"[Sweep] {roll,5:F0} {clip,-16} FBmax {mL,6:F3}/{mR,6:F3}  shOOR {sOOR,5}  totOOR {tOOR,6}");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(root); }
                }
            }
            ClavicleRoll = 0f;
            EditorApplication.Exit(0);
        }


        /// <summary>
        /// Samples the GENERATED clips back through the humanoid solver and dumps
        /// world-space bone positions. This exercises the same path the game uses
        /// (clip -> muscle curves -> HumanPose -> skeleton), so what it reports is
        /// what will actually be seen, not what was intended.
        /// </summary>
        public static void DumpPoses()
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), ExportDir);
            var outDir = Path.Combine(Directory.GetCurrentDirectory(), "PoseDump");
            Directory.CreateDirectory(outDir);

            foreach (var jsonPath in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories).OrderBy(f => f))
            {
                var data = Load(jsonPath);
                var setName = Path.GetFileName(Path.GetDirectoryName(jsonPath));
                var clipName = setName + "_" + data.name;
                var targetDir = string.IsNullOrEmpty(data.clipDir) ? ClipDir : data.clipDir;
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    Path.Combine(targetDir, data.name + ".anim").Replace("\\", "/"));
                if (clip == null) { Debug.LogError($"[Dump] missing clip {clipName}"); continue; }

                var root = new GameObject("DumpRoot");
                var xf = new Dictionary<string, Transform>();
                try
                {
                    foreach (var b in data.bones) xf[b.name] = new GameObject(b.name).transform;
                    foreach (var b in data.bones)
                    {
                        var t = xf[b.name];
                        var isRoot = string.IsNullOrEmpty(b.parent);
                        t.SetParent(isRoot ? root.transform : xf[b.parent], false);
                        t.localPosition = new Vector3(b.restPos[0], b.restPos[1], b.restPos[2]);
                        t.localRotation = new Quaternion(b.restRot[0], b.restRot[1], b.restRot[2], b.restRot[3]);
                    }
                    PoseArmsToT(xf);
                    var avatar = BuildAvatar(root, data);
                    foreach (var b in data.bones)
                        xf[b.name].localRotation = new Quaternion(b.restRot[0], b.restRot[1], b.restRot[2], b.restRot[3]);

                    var handler = new HumanPoseHandler(avatar, root.transform);
                    var pose = new HumanPose();
                    handler.GetHumanPose(ref pose);   // initialise arrays

                    // pull the curves back out of the generated clip
                    var mcurves = new AnimationCurve[HumanTrait.MuscleCount];
                    for (int m = 0; m < HumanTrait.MuscleCount; m++)
                        mcurves[m] = AnimationUtility.GetEditorCurve(clip,
                            EditorCurveBinding.FloatCurve("", typeof(Animator), HumanTrait.MuscleName[m]));
                    AnimationCurve C(string n) => AnimationUtility.GetEditorCurve(clip,
                        EditorCurveBinding.FloatCurve("", typeof(Animator), n));
                    var tx = C("RootT.x"); var ty = C("RootT.y"); var tz = C("RootT.z");
                    var qx = C("RootQ.x"); var qy = C("RootQ.y"); var qz = C("RootQ.z"); var qw = C("RootQ.w");

                    var sb = new System.Text.StringBuilder();
                    sb.Append("{\"clip\":\"").Append(clipName).Append("\",\"frames\":[");
                    int nSamples = 6;
                    for (int i = 0; i < nSamples; i++)
                    {
                        float t = data.duration * i / (nSamples - 1);
                        for (int m = 0; m < HumanTrait.MuscleCount; m++)
                            if (mcurves[m] != null) pose.muscles[m] = mcurves[m].Evaluate(t);
                        if (tx != null) pose.bodyPosition = new Vector3(tx.Evaluate(t), ty.Evaluate(t), tz.Evaluate(t));
                        if (qx != null) pose.bodyRotation = new Quaternion(qx.Evaluate(t), qy.Evaluate(t), qz.Evaluate(t), qw.Evaluate(t));
                        handler.SetHumanPose(ref pose);

                        if (i > 0) sb.Append(",");
                        sb.Append("{\"t\":").Append(t.ToString("F3")).Append(",\"bones\":{");
                        bool first = true;
                        foreach (var b in data.bones)
                        {
                            var w = xf[b.name].position;
                            if (!first) sb.Append(",");
                            first = false;
                            sb.Append("\"").Append(b.name).Append("\":[")
                              .Append(w.x.ToString("F4")).Append(",")
                              .Append(w.y.ToString("F4")).Append(",")
                              .Append(w.z.ToString("F4")).Append("]");
                        }
                        sb.Append("}}");
                    }
                    sb.Append("]}");
                    File.WriteAllText(Path.Combine(outDir, clipName + ".json"), sb.ToString());
                    Debug.Log($"[Dump] {clipName}: {nSamples} poses written");
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            EditorApplication.Exit(0);
        }


        /// <summary>
        /// Reads back avatar.humanScale and the skeleton proportions Unity derives it
        /// from, for every export set. GH3 is the known-good reference: if its scale is
        /// ~1.0 and GHWT's is not, the scale is the shared root cause of both the
        /// clavicle muscle inflation and the RootT blow-up.
        /// </summary>
        public static void DumpAvatarScale()
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), ExportDir);
            Debug.Log("[Scale] set | humanScale | hips->head | leg(hip->ankle) | arm(sh->hand)");
            foreach (var jsonPath in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories)
                                              .OrderBy(f => f))
            {
                var data = Load(jsonPath);
                var setName = Path.GetFileName(Path.GetDirectoryName(jsonPath));
                var root = new GameObject("ScaleRoot");
                var xf = new Dictionary<string, Transform>();
                try
                {
                    foreach (var b in data.bones) xf[b.name] = new GameObject(b.name).transform;
                    foreach (var b in data.bones)
                    {
                        var t = xf[b.name];
                        var isRoot = string.IsNullOrEmpty(b.parent);
                        t.SetParent(isRoot ? root.transform : xf[b.parent], false);
                        t.localPosition = new Vector3(b.restPos[0], b.restPos[1], b.restPos[2]);
                        t.localRotation = new Quaternion(b.restRot[0], b.restRot[1],
                                                         b.restRot[2], b.restRot[3]);
                    }
                    PoseArmsToT(xf);
                    var avatar = BuildAvatar(root, data);
                    // humanScale is exposed on Animator, not Avatar
                    var anim = root.AddComponent<Animator>();
                    if (avatar != null && avatar.isValid) anim.avatar = avatar;
                    float D(string a, string b) =>
                        (xf.ContainsKey(a) && xf.ContainsKey(b))
                            ? Vector3.Distance(xf[a].position, xf[b].position) : -1f;
                    Debug.Log($"[Scale] {setName}/{data.name,-16} scale={(avatar != null && avatar.isValid ? anim.humanScale.ToString("F5") : "INVALID"),10}  " +
                        $"hips->head {D("Hips", "Head"):F4}  leg {D("Hips", "LeftFoot"):F4}  arm {D("LeftShoulder", "LeftHand"):F4}  " +
                        $"hipsY {(xf.ContainsKey("Hips") ? xf["Hips"].position.y : 0f):F4}");
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            EditorApplication.Exit(0);
        }

        private static ClipJson Load(string path)
        {
            return JsonUtility.FromJson<ClipJson>(File.ReadAllText(path));
        }

        private static bool BuildOne(string jsonPath)
        {
            var data = Load(jsonPath);

            // --- reconstruct the GH skeleton ---
            var root = new GameObject("SkaRoot");
            var xf = new Dictionary<string, Transform>();
            try
            {
                foreach (var b in data.bones)
                {
                    var go = new GameObject(b.name);
                    xf[b.name] = go.transform;
                }
                foreach (var b in data.bones)
                {
                    var t = xf[b.name];
                    // JsonUtility maps a JSON null string to "", not null.
                    var isRoot = string.IsNullOrEmpty(b.parent);
                    t.SetParent(isRoot ? root.transform : xf[b.parent], false);
                    t.localPosition = new Vector3(b.restPos[0], b.restPos[1], b.restPos[2]);
                    t.localRotation = new Quaternion(b.restRot[0], b.restRot[1], b.restRot[2], b.restRot[3]);
                }

                // Bind pose for the Avatar must be a T; the animation itself is still
                // driven from the untouched rest pose, so restore it straight after.
                PoseArmsToT(xf);
                var avatar = BuildAvatar(root, data);
                foreach (var b in data.bones)
                {
                    xf[b.name].localRotation = new Quaternion(
                        b.restRot[0], b.restRot[1], b.restRot[2], b.restRot[3]);
                }
                if (avatar == null || !avatar.isValid)
                {
                    Debug.LogError($"[SkaClip] {data.name}: avatar invalid");
                    return false;
                }

                var handler = new HumanPoseHandler(avatar, root.transform);
                var pose = new HumanPose();

                int muscleCount = HumanTrait.MuscleCount;
                var curves = new AnimationCurve[muscleCount];
                for (int m = 0; m < muscleCount; m++) curves[m] = new AnimationCurve();
                var rootT = new AnimationCurve[3];
                var rootQ = new AnimationCurve[4];
                for (int i = 0; i < 3; i++) rootT[i] = new AnimationCurve();
                for (int i = 0; i < 4; i++) rootQ[i] = new AnimationCurve();

                var shoulderMuscle = new bool[muscleCount];
                var armMuscle = new bool[muscleCount];
                var legMuscle = new bool[muscleCount];
                var preClampPeak = new float[muscleCount];
                var clampedFrames = new int[muscleCount];
                for (int m = 0; m < muscleCount; m++)
                {
                    var mn = HumanTrait.MuscleName[m];
                    shoulderMuscle[m] = mn == "Left Shoulder Front-Back"
                                     || mn == "Right Shoulder Front-Back"
                                     || mn == "Left Shoulder Down-Up"
                                     || mn == "Right Shoulder Down-Up";
                    // Arm and Forearm only - Hand/wrist is out of the ruling's scope.
                    armMuscle[m] = mn.Contains("Arm ") || mn.Contains("Forearm ");
                    legMuscle[m] = mn.Contains("Upper Leg ") || mn.Contains("Lower Leg ");
                }

                for (int f = 0; f < data.frameCount; f++)
                {
                    foreach (var b in data.bones)
                    {
                        int o = Mathf.Min(f, b.rot.Length / 4 - 1) * 4;
                        xf[b.name].localRotation =
                            new Quaternion(b.rot[o], b.rot[o + 1], b.rot[o + 2], b.rot[o + 3]);
                    }

                    handler.GetHumanPose(ref pose);
                    float time = f / (float) data.sampleRate;

                    for (int m = 0; m < muscleCount; m++)
                    {
                        float v = pose.muscles[m];
                        bool clampThis = (data.clampShoulderMuscles && shoulderMuscle[m])
                                      || (data.clampArmMuscles && armMuscle[m])
                                      || (data.clampLegMuscles && legMuscle[m]);
                        if (clampThis)
                        {
                            float a = Mathf.Abs(v);
                            if (a > preClampPeak[m]) preClampPeak[m] = a;
                            if (a > SHOULDER_CLAMP)
                            {
                                clampedFrames[m]++;
                                v = Mathf.Sign(v) * SHOULDER_CLAMP;
                            }
                        }
                        curves[m].AddKey(new Keyframe(time, v));
                    }

                    rootT[0].AddKey(new Keyframe(time, pose.bodyPosition.x));
                    rootT[1].AddKey(new Keyframe(time, pose.bodyPosition.y));
                    rootT[2].AddKey(new Keyframe(time, pose.bodyPosition.z));
                    rootQ[0].AddKey(new Keyframe(time, pose.bodyRotation.x));
                    rootQ[1].AddKey(new Keyframe(time, pose.bodyRotation.y));
                    rootQ[2].AddKey(new Keyframe(time, pose.bodyRotation.z));
                    rootQ[3].AddKey(new Keyframe(time, pose.bodyRotation.w));
                }

                var targetDir = string.IsNullOrEmpty(data.clipDir) ? ClipDir : data.clipDir;
                Directory.CreateDirectory(targetDir);
                var clipPath = Path.Combine(targetDir, data.name + ".anim").Replace('\\', '/');
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                bool existed = clip != null;
                if (!existed) clip = new AnimationClip();

                clip.ClearCurves();
                clip.legacy = false;
                clip.frameRate = data.sampleRate;

                // Plan-B: the GHWT clavicle carries a large near-constant offset in
                // Front-Back (measured +5.89 / -6.30 with only ~1.3 of real motion on
                // top). The residual correlates 0.998 with the clavicle's own rotation
                // and <=0.67 with any parent axis, so it is clavicle-static and can be
                // removed at the consumption layer.
                for (int m = 0; m < muscleCount; m++)
                {
                    var mname = HumanTrait.MuscleName[m];
                    float bias = 0f;
                    if (mname == "Left Shoulder Front-Back")  bias = data.clavicleFBBiasL;
                    if (mname == "Right Shoulder Front-Back") bias = data.clavicleFBBiasR;
                    if (Mathf.Abs(bias) > 1e-6f)
                    {
                        var keys = curves[m].keys;
                        for (int k = 0; k < keys.Length; k++) keys[k].value -= bias;
                        curves[m].keys = keys;
                    }
                    SetAnimatorCurve(clip, mname, curves[m]);
                }

                SetAnimatorCurve(clip, "RootT.x", rootT[0]);
                SetAnimatorCurve(clip, "RootT.y", rootT[1]);
                SetAnimatorCurve(clip, "RootT.z", rootT[2]);
                SetAnimatorCurve(clip, "RootQ.x", rootQ[0]);
                SetAnimatorCurve(clip, "RootQ.y", rootQ[1]);
                SetAnimatorCurve(clip, "RootQ.z", rootQ[2]);
                SetAnimatorCurve(clip, "RootQ.w", rootQ[3]);

                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = data.name != "VoxMicGrab";
                AnimationUtility.SetAnimationClipSettings(clip, settings);

                if (existed) EditorUtility.SetDirty(clip);
                else AssetDatabase.CreateAsset(clip, clipPath);

                Debug.Log($"[SkaClip] {data.name}: {data.frameCount} frames, {data.duration:F2}s, " +
                    $"{muscleCount} muscles, loop={settings.loopTime}, " +
                    $"{(existed ? "overwrote (GUID preserved)" : "created")}");

                if (data.clampShoulderMuscles || data.clampArmMuscles || data.clampLegMuscles)
                {
                    for (int m = 0; m < muscleCount; m++)
                    {
                        if (clampedFrames[m] == 0) continue;
                        Debug.Log($"[SkaClamp] {data.name} | {HumanTrait.MuscleName[m]} | " +
                                  $"CLAMPED_FROM_{preClampPeak[m]:F3}_{clampedFrames[m]}f");
                    }
                }
                return true;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        private static void SetAnimatorCurve(AnimationClip clip, string property, AnimationCurve curve)
        {
            var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), property);
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        /// <summary>
        /// Straightens the arm chains laterally so the Avatar's bind pose is a T.
        ///
        /// GH's rest pose has the arms hanging down. AvatarBuilder takes the supplied
        /// skeleton pose as the muscle zero-point, so an arms-down bind makes every
        /// arm muscle read as though it were already fully retracted - which is what
        /// drove "Forearm Stretch" to -1.98 and "Arm Front-Back" to -1.70.
        ///
        /// The target direction is derived from the rig itself rather than assumed:
        /// whichever lateral side the Left shoulder actually sits on is the side the
        /// left arm is straightened toward. Shoulder pitch is left untouched, since
        /// Unity only needs the arm chain's zero-point to be a straight arm.
        /// </summary>
        /// <summary>
        /// Roll applied to the clavicle in the BIND POSE only. The GH clavicle carries a
        /// baked ~90 degree Y rotation in its rest orientation (|y| 0.63-0.78 of the quat,
        /// in every take), which the arm-chain alignment does not account for, leaving the
        /// Shoulder muscles with a rolled zero-point. Determined empirically by sweep.
        /// </summary>
        public static float ClavicleRoll = 0f;

        private static void PoseArmsToT(Dictionary<string, Transform> xf)
        {
            if (!xf.ContainsKey("LeftShoulder")) return;

            float leftSign = Mathf.Sign(xf["LeftShoulder"].position.x);
            if (Mathf.Approximately(leftSign, 0f)) leftSign = 1f;

            if (!Mathf.Approximately(ClavicleRoll, 0f))
            {
                RollBone(xf, "LeftShoulder",  new Vector3(leftSign, 0f, 0f),  ClavicleRoll);
                RollBone(xf, "RightShoulder", new Vector3(-leftSign, 0f, 0f), ClavicleRoll);
            }

            // Clavicle deliberately excluded: including it in the alignment was tested
            // and made Shoulder Front-Back worse (708 -> 1732 out-of-range samples),
            // so the clavicle keeps its rest orientation.
            AlignChain(xf, leftSign, "LeftUpperArm", "LeftLowerArm", "LeftHand");
            AlignChain(xf, -leftSign, "RightUpperArm", "RightLowerArm", "RightHand");
        }

        private static void RollBone(Dictionary<string, Transform> xf, string name,
            Vector3 axis, float degrees)
        {
            if (!xf.ContainsKey(name)) return;
            var t = xf[name];
            t.rotation = Quaternion.AngleAxis(degrees, axis) * t.rotation;
        }

        private static void AlignChain(Dictionary<string, Transform> xf, float sign,
            params string[] chain)
        {
            var target = new Vector3(sign, 0f, 0f);
            for (int i = 0; i < chain.Length - 1; i++)
            {
                if (!xf.ContainsKey(chain[i]) || !xf.ContainsKey(chain[i + 1])) continue;
                var bone = xf[chain[i]];
                var child = xf[chain[i + 1]];
                var dir = child.position - bone.position;
                if (dir.sqrMagnitude < 1e-10f) continue;
                bone.rotation = Quaternion.FromToRotation(dir.normalized, target) * bone.rotation;
            }
        }

        private static Avatar BuildAvatar(GameObject root, ClipJson data)
        {
            var human = new List<HumanBone>();
            foreach (var b in data.bones)
            {
                human.Add(new HumanBone
                {
                    boneName = b.name,
                    humanName = b.name,          // exporter already uses Unity humanoid names
                    limit = new HumanLimit { useDefaultValues = true },
                });
            }

            var skeleton = new List<SkeletonBone>
            {
                new SkeletonBone
                {
                    name = root.name,
                    position = Vector3.zero,
                    rotation = Quaternion.identity,
                    scale = Vector3.one,
                }
            };
            // Read the live transforms so the skeleton records the T-posed arm chain
            // that PoseArmsToT has just applied, not the raw arms-down rest pose.
            foreach (var b in data.bones)
            {
                var t = root.transform.Find(b.name) ?? FindDeep(root.transform, b.name);
                skeleton.Add(new SkeletonBone
                {
                    name = b.name,
                    position = t != null ? t.localPosition
                        : new Vector3(b.restPos[0], b.restPos[1], b.restPos[2]),
                    rotation = t != null ? t.localRotation
                        : new Quaternion(b.restRot[0], b.restRot[1], b.restRot[2], b.restRot[3]),
                    scale = Vector3.one,
                });
            }

            var desc = new HumanDescription
            {
                human = human.ToArray(),
                skeleton = skeleton.ToArray(),
                upperArmTwist = 0.5f,
                lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f,
                lowerLegTwist = 0.5f,
                armStretch = 0.05f,
                legStretch = 0.05f,
                feetSpacing = 0f,
                hasTranslationDoF = false,
            };

            return AvatarBuilder.BuildHumanAvatar(root, desc);
        }
    }
}
