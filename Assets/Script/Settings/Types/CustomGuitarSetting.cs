using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YARG.Venue.Guitars;

namespace YARG.Settings.Types
{
    /// <summary>
    /// Dropdown listing the .glb guitar models in the user's customization folder.
    /// Mirrors <see cref="CustomCharacterSetting"/>.
    ///
    /// The stored value is the absolute file path, so it round-trips through
    /// SettingsManager like any other string setting. The empty value means
    /// "no custom guitar" and must stay first in the list.
    /// </summary>
    public class CustomGuitarSetting : DropdownSetting<string>
    {
        private readonly Dictionary<string, string> _fileToName = new();

        public CustomGuitarSetting(string value, Action<string> onChange = null)
            : base(value, onChange, localizable: false)
        {
        }

        public override void UpdateValues()
        {
            _fileToName.Clear();
            _possibleValues.Clear();

            // Empty first: this is the "leave the character's own guitar alone" option.
            _possibleValues.Add(string.Empty);

            string folder;
            try
            {
                folder = GuitarMountService.GuitarFolder;
            }
            catch (Exception)
            {
                // Customization directory not ready yet (very early startup).
                return;
            }

            if (!Directory.Exists(folder))
            {
                return;
            }

            var files = Directory.GetFiles(folder, "*.glb")
                .OrderBy(f => f, StringComparer.Ordinal);

            foreach (var file in files)
            {
                _possibleValues.Add(file);
                _fileToName[file] = Path.GetFileNameWithoutExtension(file);
            }

            // A previously selected guitar that has since been deleted would otherwise
            // vanish from the list while remaining the stored value, leaving the dropdown
            // with no matching index.
            if (!string.IsNullOrEmpty(Value) && !_possibleValues.Contains(Value))
            {
                _possibleValues.Add(Value);
                _fileToName[Value] = Path.GetFileNameWithoutExtension(Value) + " (missing)";
            }
        }

        public override string ValueToString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "None";
            }

            return _fileToName.GetValueOrDefault(value, Path.GetFileNameWithoutExtension(value));
        }
    }
}
