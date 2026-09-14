using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UniVRM10;
using YARG.Settings.Customization;
using YARG.Venue;
using YARG.Venue.Characters;

namespace YARG.Settings.Types
{
    public class CustomCharacterSetting : DropdownSetting<string>
    {
        /// <summary>The venue slot this dropdown fills, regardless of the picked
        /// character's exported type.</summary>
        public VenueCharacter.CharacterType Slot { get; }
        private Dictionary<string, string>   _fileToName = new();

        private const string CHARACTER_FOLDER = "characters";

        public string CustomCharacterPath
        {
            get
            {
                var folder = Path.Combine(CustomContentManager.CustomizationDirectory, CHARACTER_FOLDER);

                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                return folder;
            }
        }

        public CustomCharacterSetting(string value, VenueCharacter.CharacterType characterType, Action<string> onChange = null) :
            base(value, onChange, localizable: false)
        {
            Slot = characterType;
        }

        // Shared across every CustomCharacterSetting instance. MetadataTab.OnTabEnter calls
        // UpdateValues on SIX of these (vocals, vocals-female, guitar, bass, drums, keys), all
        // scanning the same folder and - because every character is offered for every slot -
        // all producing the identical list. Each scan does a synchronous
        // AssetBundle.LoadFromFile plus a full prefab deserialise per character, on the main
        // thread. Measured headless with 10 characters: ~1.72 s per instance, 10.35 s total -
        // over twice the 5 s Windows allows before declaring a window hung (Event 1002).
        // That was the Settings "crash".
        //
        // So scan at most once per folder state and let every instance reuse the result. The
        // signature covers path + size + write time, so adding, removing or rebuilding a
        // bundle still triggers a rescan - the reason the rescan exists is preserved.
        private static string _scanSignature;
        private static readonly List<(string file, string name)> _scanResult = new();

        private static string BuildSignature(string[] files)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var f in files)
            {
                var info = new FileInfo(f);
                sb.Append(f).Append('|').Append(info.Length).Append('|')
                  .Append(info.LastWriteTimeUtc.Ticks).Append(';');
            }

            return sb.ToString();
        }

        public override void UpdateValues()
        {
            _fileToName.Clear();
            _possibleValues.Clear();
            _possibleValues.Add(string.Empty);

            var folder = CustomCharacterPath;
            string[] files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.yargchar") : Array.Empty<string>();
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            string signature = BuildSignature(files);
            if (signature != _scanSignature)
            {
                ScanCharacters(files);
                _scanSignature = signature;
            }

            foreach (var (file, name) in _scanResult)
            {
                _possibleValues.Add(file);
                _fileToName[file] = name;
            }
        }

        private static void ScanCharacters(string[] files)
        {
            _scanResult.Clear();

            // Load the AssetBundles and pull the character names from the VrmInstance (and use the filename as a fallback for the display name)
            foreach (var file in files)
            {
                var bundle = AssetBundle.LoadFromFile(file);
                if (bundle == null)
                {
                    continue;
                }

                var character = BundleBackgroundManager.LoadCharacterPrefab(bundle);
                if (character == null)
                {
                    bundle.Unload(true);
                    continue;
                }

                var vrmInstance = character.GetComponent<Vrm10Instance>();
                if (vrmInstance == null)
                {
                    bundle.Unload(true);
                    continue;
                }

                string name;

                if (vrmInstance.Vrm != null && vrmInstance.Vrm.Meta != null && string.IsNullOrEmpty(vrmInstance.Vrm.Meta.Name))
                {
                    name = vrmInstance.Vrm.Meta.Name;
                }
                else
                {
                    name = Path.GetFileNameWithoutExtension(file);
                }

                // Every character is offered for every slot. Which slot a character fills is
                // decided by the dropdown it was picked from, not by the type baked in at
                // export - that is what lets a vocalist be put on guitar, or a drummer sing.
                if (character.GetComponent<VenueCharacter>() != null)
                {
                    _scanResult.Add((file, name));
                }

                bundle.Unload(true);
            }
        }

        public override string ValueToString(string value)
        {
            return _fileToName.GetValueOrDefault(value, "None");
        }
    }
}