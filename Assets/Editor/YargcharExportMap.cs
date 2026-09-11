using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using YARG.Core.Song;
using YARG.Venue.Characters;

namespace YARG.Editor
{
    /// <summary>
    /// Per-character export settings, so a batch export is type- and gender-accurate
    /// instead of stamping one value across every character.
    ///
    /// Type drives both which venue character is replaced at load and which settings
    /// dropdown lists the character, so a wrong value here is not cosmetic.
    /// </summary>
    [Serializable]
    public class YargcharExportMap
    {
        [Serializable]
        public class Entry
        {
            public string Name;
            public string Type;
            public string Gender;
        }

        public List<Entry> Entries = new();

        public readonly struct Settings
        {
            public readonly VenueCharacter.CharacterType Type;
            public readonly VocalGender                  Gender;

            public Settings(VenueCharacter.CharacterType type, VocalGender gender)
            {
                Type = type;
                Gender = gender;
            }
        }

        private readonly Dictionary<string, Settings> _byName =
            new(StringComparer.OrdinalIgnoreCase);

        public static YargcharExportMap Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return null;
            }

            var map = JsonUtility.FromJson<YargcharExportMap>(File.ReadAllText(path));
            if (map?.Entries == null)
            {
                Debug.LogError($"[YargcharExportMap] {path} parsed to nothing");
                return null;
            }

            foreach (var entry in map.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry?.Name))
                {
                    continue;
                }

                if (!Enum.TryParse<VenueCharacter.CharacterType>(entry.Type, true, out var type))
                {
                    Debug.LogError($"[YargcharExportMap] '{entry.Name}': unknown Type " +
                        $"'{entry.Type}'");
                    return null;
                }

                if (!Enum.TryParse<VocalGender>(entry.Gender, true, out var gender))
                {
                    Debug.LogError($"[YargcharExportMap] '{entry.Name}': unknown Gender " +
                        $"'{entry.Gender}'");
                    return null;
                }

                map._byName[entry.Name] = new Settings(type, gender);
            }

            Debug.Log($"[YargcharExportMap] Loaded {map._byName.Count} entries from {path}");
            return map;
        }

        /// <summary>
        /// Settings for a character, by VRM file stem. Missing entries are a hard failure
        /// rather than a silent default: an unmapped character would export as Bass and
        /// replace the venue's bassist.
        /// </summary>
        public bool TryGet(string name, out Settings settings)
        {
            return _byName.TryGetValue(name, out settings);
        }

        public int Count => _byName.Count;
    }
}
