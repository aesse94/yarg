using System.IO;
using UnityEditor;
using UnityEngine;
using YARG.Settings.Types;
using YARG.Venue.Characters;
using YARG.Helpers;
using System.Reflection;

namespace YARG.Editor
{
    /// <summary>
    /// Exercises the real settings objects the UI binds to, so the result is what the
    /// dropdowns will actually show - not a re-implementation of the filter.
    /// </summary>
    public static class VerifyDropdowns
    {
        public static void Run()
        {
            // PathHelper.Init runs via RuntimeInitializeOnLoadMethod in the player only,
            // so seed the same value the YARG_TEST_BUILD player would compute.
            typeof(PathHelper).GetProperty("PersistentDataPath",
                    BindingFlags.Public | BindingFlags.Static)
                .SetValue(null, Path.Combine(Application.persistentDataPath, "dev"));
            Debug.Log($"[VERIFY] PersistentDataPath={PathHelper.PersistentDataPath}");

            foreach (VenueCharacter.CharacterType type in
                System.Enum.GetValues(typeof(VenueCharacter.CharacterType)))
            {
                var setting = new CustomCharacterSetting(string.Empty, type);
                setting.UpdateValues();

                Debug.Log($"[VERIFY] {type} dropdown -> {setting.PossibleValues.Count} entries");
                foreach (var v in setting.PossibleValues)
                {
                    if (string.IsNullOrEmpty(v)) { Debug.Log("[VERIFY]     (None)"); continue; }
                    Debug.Log($"[VERIFY]     {Path.GetFileName(v)}  label='{setting.ValueToString(v)}'");
                }
            }

            var guitars = new CustomGuitarSetting(string.Empty);
            guitars.UpdateValues();
            Debug.Log($"[VERIFY] GUITAR dropdown -> {guitars.PossibleValues.Count} entries");
            foreach (var g in guitars.PossibleValues)
            {
                if (!string.IsNullOrEmpty(g)) Debug.Log($"[VERIFY]     {Path.GetFileName(g)}");
            }

            var venues = new CustomVenueSetting(string.Empty);
            venues.UpdateValues();
            Debug.Log($"[VERIFY] VENUE dropdown -> {venues.PossibleValues.Count} entries");
            int shown = 0;
            foreach (var v in venues.PossibleValues)
            {
                Debug.Log($"[VERIFY]     '{venues.ValueToString(v)}'");
                if (++shown >= 8) break;
            }

            EditorApplication.Exit(0);
        }
    }
}
