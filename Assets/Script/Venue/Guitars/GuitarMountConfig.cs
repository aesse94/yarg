using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using YARG.Core.Logging;

namespace YARG.Venue.Guitars
{
    /// <summary>
    /// Per-character override table for built-in guitar hiding.
    ///
    /// A character's built-in guitar lives in submeshes of the character's single
    /// SkinnedMeshRenderer, so it cannot be hidden by disabling a GameObject. This table
    /// records which submesh indices to blank out on mount.
    ///
    /// An entry is authoritative: when a character is listed, the heuristic never runs.
    /// That is how a character with no built-in guitar is protected - list it with an
    /// empty HideSubmeshes array.
    /// </summary>
    [Serializable]
    public class GuitarMountConfig
    {
        public const string FILE_NAME = "guitar_mount_overrides.json";

        [Serializable]
        public class CharacterEntry
        {
            [Tooltip("Character identifier, matched case-insensitively against the VRM meta name, " +
                     "the root GameObject name, and the source file name.")]
            public string Character;

            [Tooltip("SkinnedMeshRenderer submesh indices whose materials are blanked on mount. " +
                     "An empty array means 'this character has no built-in guitar; hide nothing'.")]
            public int[] HideSubmeshes = Array.Empty<int>();

            [Tooltip("Free-text note for whoever maintains this table.")]
            public string Note;
        }

        public List<CharacterEntry> Entries = new();

        /// <summary>
        /// Looks up a character by any of its identifiers. Returns false when the character
        /// is not listed, which is the only case where the heuristic is allowed to run.
        /// </summary>
        public bool TryGetEntry(out CharacterEntry entry, params string[] identifiers)
        {
            entry = null;

            if (identifiers == null)
            {
                return false;
            }

            foreach (var candidate in Entries)
            {
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.Character))
                {
                    continue;
                }

                foreach (var identifier in identifiers)
                {
                    if (string.IsNullOrWhiteSpace(identifier))
                    {
                        continue;
                    }

                    // Match on the whole identifier or its file-name stem, so
                    // "character_b" matches "/path/character_b.vrm".
                    if (string.Equals(candidate.Character, identifier, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(candidate.Character, Path.GetFileNameWithoutExtension(identifier),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        entry = candidate;
                        return true;
                    }
                }
            }

            return false;
        }

        public static GuitarMountConfig Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                YargLogger.LogFormatInfo("No guitar mount override table at {0}; using heuristic only", path);
                return new GuitarMountConfig();
            }

            try
            {
                var config = JsonUtility.FromJson<GuitarMountConfig>(File.ReadAllText(path));
                if (config == null)
                {
                    YargLogger.LogFormatWarning("Guitar mount override table at {0} parsed to null", path);
                    return new GuitarMountConfig();
                }

                config.Entries ??= new List<CharacterEntry>();
                YargLogger.LogFormatInfo("Loaded {0} guitar mount override entries", config.Entries.Count);
                return config;
            }
            catch (Exception e)
            {
                // A broken table must not take the whole character load down with it.
                YargLogger.LogFormatWarning<string, string>(
                    "Failed to parse guitar mount override table {0}: {1}", path, e.Message);
                return new GuitarMountConfig();
            }
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllText(path, JsonUtility.ToJson(this, true));
        }
    }
}
