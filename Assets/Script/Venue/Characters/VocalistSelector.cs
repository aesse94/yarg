using YARG.Core.Song;

namespace YARG.Venue.Characters
{
    /// <summary>
    /// Chooses which custom vocalist slot a song should use.
    ///
    /// Kept as a pure function of its inputs so the rule can be tested without entering
    /// play mode or loading a venue.
    /// </summary>
    public static class VocalistSelector
    {
        /// <summary>
        /// Which configured slot a gender maps to, before any availability fallback.
        /// </summary>
        public enum Slot
        {
            Default,
            Female,
        }

        /// <summary>
        /// Maps a song's tagged gender to a slot.
        ///
        /// Female and Nonbinary take the female slot. Male, Other and Unspecified take the
        /// default slot: Other is deliberately grouped with the default rather than guessed
        /// at, and Unspecified is the common case for charts with no gender metadata.
        /// </summary>
        public static Slot SlotForGender(VocalGender gender)
        {
            return gender switch
            {
                VocalGender.Female    => Slot.Female,
                VocalGender.Nonbinary => Slot.Female,
                _                     => Slot.Default,
            };
        }

        /// <summary>
        /// Resolves the character file to load.
        ///
        /// Falls back to the default slot whenever the gender-matched slot is empty, so a
        /// song can never end up with no vocalist that would otherwise have had one. When
        /// <paramref name="autoSelectEnabled"/> is false the default slot is always used.
        ///
        /// Returns an empty string when nothing is configured, which means "leave the
        /// venue's own vocalist alone" - not an error.
        /// </summary>
        public static string SelectVocalistPath(VocalGender gender, string defaultPath,
            string femalePath, bool autoSelectEnabled)
        {
            defaultPath ??= string.Empty;
            femalePath ??= string.Empty;

            if (!autoSelectEnabled)
            {
                return defaultPath;
            }

            if (SlotForGender(gender) == Slot.Female && !string.IsNullOrEmpty(femalePath))
            {
                return femalePath;
            }

            return defaultPath;
        }
    }
}
