using System;
using System.IO;
using System.Linq;
using UnityEngine;
using YARG.Core.Logging;
using YARG.Settings;
using YARG.Settings.Customization;

namespace YARG.Venue.Guitars
{
    /// <summary>
    /// Entry point used by the character load path. Owns the override table (loaded once)
    /// and decides which guitar a character gets.
    /// </summary>
    public static class GuitarMountService
    {
        public const string GUITAR_FOLDER = "guitars";

        private static string _selectedGuitarPath;
        private static bool   _selectionAssigned;

        /// <summary>
        /// The guitar to mount, as an absolute .glb path. Assigned by the settings dropdown's
        /// callback, or directly by tests.
        ///
        /// Until something assigns it, this reads back the persisted setting, so a selection
        /// made in a previous run is in effect from the first character load - the callback
        /// only fires when the value CHANGES, never on load.
        ///
        /// Empty or null means no custom guitar, which is a normal state, not an error.
        /// </summary>
        public static string SelectedGuitarPath
        {
            get => _selectionAssigned ? _selectedGuitarPath : ReadPersistedSelection();
            set
            {
                _selectedGuitarPath = value;
                _selectionAssigned = true;
            }
        }

        /// <summary>Drops an assigned selection so the persisted setting is read again.</summary>
        public static void ClearSelection()
        {
            _selectedGuitarPath = null;
            _selectionAssigned = false;
        }

        private static string ReadPersistedSelection()
        {
            try
            {
                return SettingsManager.Settings?.CustomGuitar?.Value;
            }
            catch (Exception)
            {
                // Settings are not available this early, or at all in a bare test fixture.
                return null;
            }
        }

        /// <summary>Set by tests to point the service at a fixture folder.</summary>
        public static string GuitarFolderOverride { get; set; }

        private static GuitarMountConfig _config;
        private static bool              _configLoaded;

        public static string GuitarFolder
        {
            get
            {
                if (!string.IsNullOrEmpty(GuitarFolderOverride))
                {
                    return GuitarFolderOverride;
                }

                var folder = Path.Combine(CustomContentManager.CustomizationDirectory, GUITAR_FOLDER);
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                return folder;
            }
        }

        public static string ConfigPath => Path.Combine(GuitarFolder, GuitarMountConfig.FILE_NAME);

        public static GuitarMountConfig Config
        {
            get
            {
                if (!_configLoaded)
                {
                    _config = GuitarMountConfig.Load(ConfigPath);
                    _configLoaded = true;
                }

                return _config;
            }
        }

        /// <summary>Forces the override table to be re-read (after an edit, or between tests).</summary>
        public static void ReloadConfig()
        {
            _configLoaded = false;
            _config = null;
        }

        /// <summary>
        /// Mounts the selected guitar on a character root. Returns null when no guitar is
        /// selected or available - that is the normal case, not an error.
        /// </summary>
        public static GuitarMounter.MountResult MountFor(GameObject characterRoot,
            params string[] characterIdentifiers)
        {
            string guitarPath = ResolveGuitarPath();
            if (string.IsNullOrEmpty(guitarPath))
            {
                return null;
            }

            var result = GuitarMounter.Mount(characterRoot, guitarPath, Config, characterIdentifiers);

            if (!result.Success)
            {
                YargLogger.LogFormatWarning<string, string>(
                    "Guitar mount failed for '{0}': {1}",
                    characterRoot != null ? characterRoot.name : "<null>", result.FailureReason);
                return null;
            }

            YargLogger.LogFormatInfo<string, string>("Mounted guitar '{0}' on '{1}'",
                Path.GetFileName(guitarPath), characterRoot.name);

            return result;
        }

        /// <summary>
        /// The selected guitar, or null when there is nothing to mount. There is deliberately
        /// no "pick the first file in the folder" fallback: an empty selection must leave the
        /// character exactly as it shipped.
        /// </summary>
        private static string ResolveGuitarPath()
        {
            var selected = SelectedGuitarPath;

            if (string.IsNullOrWhiteSpace(selected))
            {
                return null;
            }

            if (!File.Exists(selected))
            {
                YargLogger.LogFormatWarning("Selected guitar no longer exists: {0}", selected);
                return null;
            }

            return selected;
        }
    }
}
