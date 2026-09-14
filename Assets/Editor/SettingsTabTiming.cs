using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using YARG.Audio.BASS;
using YARG.Core.Audio;
using YARG.Helpers;
using YARG.Settings;
using Debug = UnityEngine.Debug;

namespace YARG.Editor
{
    /// <summary>
    /// Times exactly the calls MetadataTab.OnTabEnter makes, headless, to test whether
    /// opening that Settings tab blocks the main thread long enough to trip Windows'
    /// "not responding" detection (Event 1002, Application Hang).
    ///
    /// Diagnostic only. Setup mirrors SmokeTest so the settings objects are the real ones.
    ///
    /// CLI: -executeMethod YARG.Editor.SettingsTabTiming.Run
    /// </summary>
    public static class SettingsTabTiming
    {
        public static void Run()
        {
            typeof(PathHelper).GetProperty("PersistentDataPath", BindingFlags.Public | BindingFlags.Static)
                .SetValue(null, Path.Combine(Application.persistentDataPath, "dev"));

            GlobalAudioHandler.Initialize<BassAudioManager>();

            var settingsProp = typeof(SettingsManager).GetProperty("Settings", BindingFlags.Public | BindingFlags.Static);
            var s = new SettingsManager.SettingContainer();
            settingsProp.SetValue(null, s);

            // Same order and same eight calls as MetadataTab.OnTabEnter.
            var calls = new (string name, System.Action run)[]
            {
                ("CustomVocalsCharacter",       () => s.CustomVocalsCharacter.UpdateValues()),
                ("CustomVocalsCharacterFemale", () => s.CustomVocalsCharacterFemale.UpdateValues()),
                ("CustomGuitar",                () => s.CustomGuitar.UpdateValues()),
                ("CustomGuitarCharacter",       () => s.CustomGuitarCharacter.UpdateValues()),
                ("CustomBassCharacter",         () => s.CustomBassCharacter.UpdateValues()),
                ("CustomDrumsCharacter",        () => s.CustomDrumsCharacter.UpdateValues()),
                ("CustomKeysCharacter",         () => s.CustomKeysCharacter.UpdateValues()),
                ("CustomVenue",                 () => s.CustomVenue.UpdateValues()),
            };

            // Pass 1 = first open (cold). Pass 2 = re-entering the tab with nothing changed,
            // which is what a player hits every time they come back to Settings.
            // CONTROL: a first pass of ~0 ms would be impossible for a genuinely cold scan, so
            // it would mean the cache was already warm (e.g. filled during container
            // construction) and the number would be lying. Force it cold before pass 1 so the
            // measurement reflects the real first-open cost of the fixed code.
            var sigField = typeof(YARG.Settings.Types.CustomCharacterSetting).GetField(
                "_scanSignature", BindingFlags.NonPublic | BindingFlags.Static);
            Debug.Log($"[TABTIME] cache warm before timing? {(sigField.GetValue(null) != null ? "YES" : "no")}");

            for (int pass = 1; pass <= 2; pass++)
            {
                if (pass == 1) sigField.SetValue(null, null);
                var total = Stopwatch.StartNew();
                foreach (var (name, run) in calls)
                {
                    var sw = Stopwatch.StartNew();
                    run();
                    sw.Stop();
                    Debug.Log($"[TABTIME] pass{pass} {name,-28} {sw.ElapsedMilliseconds,6} ms");
                }
                total.Stop();

                // Windows flags a window "not responding" after 5 s without pumping messages.
                Debug.Log($"[TABTIME] pass{pass} TOTAL OnTabEnter             {total.ElapsedMilliseconds,6} ms " +
                    $"({(total.ElapsedMilliseconds > 5000 ? "OVER" : "under")} the 5000 ms hang threshold)");
            }

            // Correctness, not just speed: every slot must still list every character.
            Debug.Log($"[TABTIME] entries per slot: vocals={s.CustomVocalsCharacter.PossibleValues.Count - 1} " +
                $"guitar={s.CustomGuitarCharacter.PossibleValues.Count - 1} " +
                $"drums={s.CustomDrumsCharacter.PossibleValues.Count - 1} " +
                $"keys={s.CustomKeysCharacter.PossibleValues.Count - 1}");

            EditorApplication.Exit(0);
        }
    }
}
