using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using YARG.Helpers;
using YARG.Settings;
using YARG.Venue;
using YARG.Core.Audio;
using YARG.Audio.BASS;

namespace YARG.Editor
{
    /// <summary>
    /// End-to-end checks for the custom character / guitar / venue settings, run headless so
    /// the result does not depend on driving the game's UI.
    /// </summary>
    public static class SmokeTest
    {
        private static int _pass;
        private static int _fail;

        private static void Check(bool condition, string what)
        {
            if (condition) { _pass++; Debug.Log($"[SMOKE] PASS  {what}"); }
            else           { _fail++; Debug.LogError($"[SMOKE] FAIL  {what}"); }
        }

        /// <summary>
        /// Assigns the backing field directly. The public Value setter calls
        /// SettingsMenu.Instance.OnSettingChanged(), and there is no menu in batchmode.
        /// </summary>
        private static void SetDirect(object setting, string value)
        {
            var type = setting.GetType();
            FieldInfo field = null;
            while (type != null && field == null)
            {
                field = type.GetField("_value", BindingFlags.Instance | BindingFlags.NonPublic);
                type = type.BaseType;
            }
            field.SetValue(setting, value);
        }

        public static void Run()
        {
            typeof(PathHelper).GetProperty("PersistentDataPath",
                    BindingFlags.Public | BindingFlags.Static)
                .SetValue(null, Path.Combine(Application.persistentDataPath, "dev"));

            // SettingsManager.LoadSettings() fires audio callbacks that need the audio
            // engine running, which batchmode has not started. Build the container directly
            // and install it, so the settings objects under test are the real ones.
            // SettingContainer's constructor reads GlobalAudioHandler.MaximumBufferLength,
            // so audio must be up before the container can exist.
            try
            {
                GlobalAudioHandler.Initialize<BassAudioManager>();
                Debug.Log("[SMOKE] audio initialized");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SMOKE] audio init failed: {e.Message}");
            }

            var settingsProp = typeof(SettingsManager).GetProperty("Settings",
                BindingFlags.Public | BindingFlags.Static);
            var container = new SettingsManager.SettingContainer();
            settingsProp.SetValue(null, container);
            var s = container;

            // ---- 1. every slot is populated and offers all characters ----
            s.CustomVocalsCharacter.UpdateValues();
            s.CustomGuitarCharacter.UpdateValues();
            s.CustomBassCharacter.UpdateValues();
            s.CustomDrumsCharacter.UpdateValues();
            s.CustomKeysCharacter.UpdateValues();
            s.CustomGuitar.UpdateValues();
            s.CustomVenue.UpdateValues();

            Check(s.CustomVocalsCharacter.PossibleValues.Count == 11, "Vocals slot lists 10 characters + None");
            Check(s.CustomGuitarCharacter.PossibleValues.Count == 11, "Guitar slot lists 10 characters + None");
            Check(s.CustomBassCharacter.PossibleValues.Count   == 11, "Bass slot lists 10 characters + None");
            Check(s.CustomDrumsCharacter.PossibleValues.Count  == 11, "Drums slot lists 10 characters + None");
            Check(s.CustomKeysCharacter.PossibleValues.Count   == 11, "Keys slot lists 10 characters + None");
            Check(s.CustomGuitar.PossibleValues.Count > 50, "Guitar model list populated");
            Check(s.CustomVenue.PossibleValues.Count > 30, "Venue list populated");
            Check(s.CustomVenue.ValueToString(string.Empty) == "Random", "Empty venue reads as 'Random'");

            // A character exported as Vocals must be offered for Guitar - this is the whole
            // point of dropping the type filter.
            string ozzy = s.CustomGuitarCharacter.PossibleValues
                .FirstOrDefault(v => !string.IsNullOrEmpty(v) && v.Contains("ozzy"));
            Check(ozzy != null, "Vocals-exported character (ozzy) is selectable for Guitar");

            // ---- 2. settings survive a save/load round trip ----
            string venue = s.CustomVenue.PossibleValues.First(v => !string.IsNullOrEmpty(v));
            string drummer = s.CustomDrumsCharacter.PossibleValues.First(v => !string.IsNullOrEmpty(v));

            SetDirect(s.CustomGuitarCharacter, ozzy);
            SetDirect(s.CustomDrumsCharacter, drummer);
            SetDirect(s.CustomVenue, venue);
            var jsonSettings = (JsonSerializerSettings)typeof(SettingsManager)
                .GetField("JsonSettings", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);

            string raw = JsonConvert.SerializeObject(container, jsonSettings);
            Check(!string.IsNullOrEmpty(raw), "settings serialize");
            Check(raw.Contains("CustomGuitarCharacter"), "CustomGuitarCharacter serialized");
            Check(raw.Contains("CustomBassCharacter"), "CustomBassCharacter serialized");
            Check(raw.Contains("CustomDrumsCharacter"), "CustomDrumsCharacter serialized");
            Check(raw.Contains("CustomKeysCharacter"), "CustomKeysCharacter serialized");
            Check(raw.Contains("CustomVenue"), "CustomVenue serialized");

            var reloaded = JsonConvert.DeserializeObject<SettingsManager.SettingContainer>(
                raw, jsonSettings);
            settingsProp.SetValue(null, reloaded);
            Check(reloaded.CustomGuitarCharacter.Value == ozzy, "Guitar character survives restart");
            Check(reloaded.CustomDrumsCharacter.Value == drummer, "Drums character survives restart");
            Check(reloaded.CustomVenue.Value == venue, "Pinned venue survives restart");

            // ---- 3. venue pinning actually changes what the loader returns ----
            var method = typeof(VenueLoader).GetMethod("GetVenuePathFromGlobal",
                BindingFlags.NonPublic | BindingFlags.Static);
            Check(method != null, "VenueLoader.GetVenuePathFromGlobal reachable");

            if (method != null)
            {
                SetDirect(SettingsManager.Settings.CustomVenue, venue);
                var pinnedResult = method.Invoke(null, null);
                Check(pinnedResult != null, $"Pinned venue loads ({Path.GetFileNameWithoutExtension(venue)})");

                SetDirect(SettingsManager.Settings.CustomVenue, string.Empty);
                var randomResult = method.Invoke(null, null);
                Check(randomResult != null, "Random venue still loads when nothing pinned");
            }

            Debug.Log($"[SMOKE] RESULT {_pass} passed, {_fail} failed");
            EditorApplication.Exit(_fail == 0 ? 0 : 1);
        }
    }
}
