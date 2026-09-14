using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YARG.Helpers;
using YARG.Venue;

namespace YARG.Settings.Types
{
    /// <summary>
    /// Dropdown listing the venues in the user's venue folder.
    ///
    /// The empty value means "pick at random", which is the behaviour YARG has always had,
    /// so leaving this alone changes nothing. Selecting a file pins that venue for every
    /// song instead.
    /// </summary>
    public class CustomVenueSetting : DropdownSetting<string>
    {
        private static readonly string[] _extensions =
        {
            "*.yarground", "*.mp4", "*.mov", "*.webm", "*.png", "*.jpg", "*.jpeg"
        };

        private readonly Dictionary<string, string> _fileToName = new();

        public CustomVenueSetting(string value, Action<string> onChange = null)
            : base(value, onChange, localizable: false)
        {
        }

        public override void UpdateValues()
        {
            _fileToName.Clear();
            _possibleValues.Clear();

            // Empty first: this is the "random venue per song" option.
            _possibleValues.Add(string.Empty);

            string folder;
            try
            {
                folder = VenueLoader.VenueFolder;
            }
            catch (Exception)
            {
                return;
            }

            if (!Directory.Exists(folder))
            {
                return;
            }

            var files = new List<string>();
            foreach (var ext in _extensions)
            {
                files.AddRange(Directory.EnumerateFiles(folder, ext, PathHelper.SafeSearchOptions_NoRecurse));
            }

            foreach (var file in files.OrderBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase))
            {
                _possibleValues.Add(file);
                _fileToName[file] = Path.GetFileNameWithoutExtension(file);
            }
        }

        public override string ValueToString(string value)
        {
            return string.IsNullOrEmpty(value) ? "Random" : _fileToName.GetValueOrDefault(value, "Random");
        }
    }
}
