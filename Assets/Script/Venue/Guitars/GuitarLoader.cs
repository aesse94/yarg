using System;
using UniGLTF;
using UnityEngine;
using YARG.Core.Logging;

namespace YARG.Venue.Guitars
{
    /// <summary>
    /// Runtime .glb import via UniGLTF (com.vrmc.gltf). Plain glTF only - .vrm files go
    /// through Vrm10Data instead, and are not what this loads.
    ///
    /// Import runs synchronously on the calling thread via <see cref="ImmediateCaller"/>,
    /// so this MUST be called from the Unity main thread.
    /// </summary>
    public static class GuitarLoader
    {
        /// <summary>
        /// Imports a .glb and returns its root GameObject. The returned
        /// <paramref name="instance"/> owns the created meshes/materials/textures and must be
        /// disposed (or destroyed with the root) to release them.
        /// </summary>
        public static GameObject Load(string path, out RuntimeGltfInstance instance)
        {
            instance = null;

            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            {
                YargLogger.LogFormatWarning("Guitar GLB not found: {0}", path);
                return null;
            }

            GltfData data = null;

            try
            {
                data = new GlbFileParser(path).Parse();

                // ImmediateCaller resolves every await inline, so the returned task is already
                // complete when LoadAsync returns - no blocking wait is introduced here.
                var context = new ImporterContext(data);
                var task = context.LoadAsync(new ImmediateCaller());

                if (!task.IsCompleted)
                {
                    YargLogger.LogFormatWarning(
                        "Guitar GLB import did not complete synchronously: {0}", path);
                    context.Dispose();
                    return null;
                }

                if (task.IsFaulted)
                {
                    throw task.Exception ?? new Exception("unknown import fault");
                }

                instance = task.Result;

                // UniGLTF imports with renderers disabled until the caller opts in.
                instance.ShowMeshes();

                // Static props parented to an animated bone can be culled incorrectly
                // when their bounds are not refreshed; this keeps them drawn.
                instance.EnableUpdateWhenOffscreen();

                return instance.Root;
            }
            catch (Exception e)
            {
                YargLogger.LogFormatWarning<string, string>(
                    "Failed to import guitar GLB {0}: {1}", path, e.Message);
                return null;
            }
            finally
            {
                // GltfData holds the raw file bytes; the imported objects no longer need it.
                data?.Dispose();
            }
        }
    }
}
