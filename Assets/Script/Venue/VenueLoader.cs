using System.IO;
using System.Collections.Generic;
using UnityEngine;
using YARG.Helpers;
using YARG.Settings;
using YARG.Core.Song;
using YARG.Core.Venue;
using YARG.Core.IO;
using YARG.Core.Logging;

namespace YARG.Venue
{
    public enum VenueSource
    {
        Global,
        Song,
    }

    public static class VenueLoader
    {
        private static readonly string _venueFolder = Path.Combine(PathHelper.PersistentDataPath, "venue");
        private static readonly string _defaultVenue = Path.Combine(Application.streamingAssetsPath, "venue", "default.yarground");
        public static string VenueFolder
        {
            get
            {
                if (!Directory.Exists(_venueFolder))
                {
                    Directory.CreateDirectory(_venueFolder);
                }
                return _venueFolder;
            }
        }

#nullable enable
        public static BackgroundResult? GetVenue(SongEntry song, out VenueSource source)
        {
            BackgroundResult? result = null;
#nullable disable
            source = VenueSource.Song;
            if (!SettingsManager.Settings.DisablePerSongBackgrounds.Value)
            {
                result = song.LoadBackground();
            }

            if (!SettingsManager.Settings.DisableGlobalBackgrounds.Value && result == null)
            {
                source = VenueSource.Global;
                result = GetVenuePathFromGlobal();
            }

            if (!SettingsManager.Settings.DisableDefaultBackground.Value && result == null)
            {
                source = VenueSource.Global;
                result = LoadDefaultVenue();
                if (result != null)
                {
                    YargLogger.LogInfo("Venue: fell through to the built-in default venue");
                }
            }

            YargLogger.LogFormatInfo<VenueSource, string>("Venue: source={0}, type={1}", source,
                result == null ? "NONE" : result.Type.ToString());
            return result;
        }

#nullable enable
        private static BackgroundResult? GetVenuePathFromGlobal()
#nullable disable
        {
            string[] validExtensions =
            {
                "*.yarground", "*.mp4", "*.mov", "*.webm", "*.png", "*.jpg", "*.jpeg"
            };

            string venueFolder = VenueFolder;
            string launcherVenueFolder = PathHelper.VenuePath;
            var filePaths = new List<string>();

            // A pinned venue short-circuits the random pick. If the file has since been
            // deleted we fall through to random rather than showing nothing.
            string pinned = SettingsManager.Settings.CustomVenue.Value;
            if (!string.IsNullOrEmpty(pinned) && File.Exists(pinned))
            {
                YargLogger.LogFormatInfo("Venue: using pinned '{0}'", pinned);
                filePaths.Add(pinned);
            }
            else
            {
                if (!string.IsNullOrEmpty(pinned))
                {
                    YargLogger.LogFormatWarning("Venue: pinned '{0}' does not exist; falling back to random", pinned);
                }

                foreach (var ext in validExtensions)
                {
                    filePaths.AddRange(Directory.EnumerateFiles(venueFolder, ext, PathHelper.SafeSearchOptions));
                }

                if (launcherVenueFolder != null && Directory.Exists(launcherVenueFolder))
                {
                    // We limit ourselves to yarground here because that's all that will be downloaded by the launcher
                    filePaths.AddRange(Directory.EnumerateFiles(launcherVenueFolder, "*.yarground", PathHelper.SafeSearchOptions));
                }
            }

            while (filePaths.Count > 0)
            {
                int index = Random.Range(0, filePaths.Count);
                var file = filePaths[index];
                switch (Path.GetExtension(file))
                {
                    case ".png":
                    case ".jpg":
                    case ".jpeg":
                        var image = YARGImage.Load(file);
                        if (image != null)
                        {
                            return new BackgroundResult(image);
                        }
                        break;
                    case ".mp4":
                    case ".mov":
                    case ".webm":
                        return new BackgroundResult(BackgroundType.Video, File.OpenRead(file));
                    case ".yarground":
                        YargLogger.LogFormatInfo("Venue: selected yarground '{0}'", file);
                        return new BackgroundResult(BackgroundType.Yarground, File.OpenRead(file));
                    default:
                        filePaths.RemoveAt(index);
                        break;
                }
            }

            YargLogger.LogWarning("Venue: global scan produced no usable venue");
            return null;
        }

#nullable enable
        private static BackgroundResult? LoadDefaultVenue()
#nullable disable
        {
            if (!File.Exists(_defaultVenue))
            {
                YargLogger.LogWarning("Default venue not found. Build error?");
                return null;
            }

            return new BackgroundResult(BackgroundType.Yarground, File.OpenRead(_defaultVenue));
        }
    }
}