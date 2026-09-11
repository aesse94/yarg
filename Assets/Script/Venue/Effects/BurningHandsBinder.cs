using System;
using System.Collections.Generic;
using YARG.Core;
using YARG.Gameplay.Player;
using YARG.Venue.Characters;
using PerformanceState = YARG.Venue.Effects.BurningHands.PerformanceState;

namespace YARG.Venue.Effects
{
    /// <summary>
    /// Connects a venue character to the player whose performance its flames reflect.
    /// </summary>
    public static class BurningHandsBinder
    {
        /// <summary>
        /// Which character an instrument is played by. Rhythm, co-op and pro variants all
        /// map onto the same on-stage performer.
        /// </summary>
        public static VenueCharacter.CharacterType? CharacterTypeFor(Instrument instrument)
        {
            return instrument switch
            {
                Instrument.FiveFretGuitar     => VenueCharacter.CharacterType.Guitar,
                Instrument.SixFretGuitar      => VenueCharacter.CharacterType.Guitar,
                Instrument.FiveFretRhythm     => VenueCharacter.CharacterType.Guitar,
                Instrument.SixFretRhythm      => VenueCharacter.CharacterType.Guitar,
                Instrument.FiveFretCoopGuitar => VenueCharacter.CharacterType.Guitar,
                Instrument.SixFretCoopGuitar  => VenueCharacter.CharacterType.Guitar,
                Instrument.ProGuitar_17Fret   => VenueCharacter.CharacterType.Guitar,
                Instrument.ProGuitar_22Fret   => VenueCharacter.CharacterType.Guitar,

                Instrument.FiveFretBass       => VenueCharacter.CharacterType.Bass,
                Instrument.SixFretBass        => VenueCharacter.CharacterType.Bass,
                Instrument.ProBass_17Fret     => VenueCharacter.CharacterType.Bass,
                Instrument.ProBass_22Fret     => VenueCharacter.CharacterType.Bass,

                Instrument.FourLaneDrums      => VenueCharacter.CharacterType.Drums,
                Instrument.ProDrums           => VenueCharacter.CharacterType.Drums,
                Instrument.FiveLaneDrums      => VenueCharacter.CharacterType.Drums,
                Instrument.EliteDrums         => VenueCharacter.CharacterType.Drums,

                Instrument.Keys               => VenueCharacter.CharacterType.Keys,
                Instrument.ProKeys            => VenueCharacter.CharacterType.Keys,

                Instrument.Vocals             => VenueCharacter.CharacterType.Vocals,
                Instrument.Harmony            => VenueCharacter.CharacterType.Vocals,

                _                             => null,
            };
        }

        /// <summary>
        /// Reads the performance of the player matching <paramref name="characterType"/>.
        ///
        /// Deliberately no cross-instrument fallback: a guitarist's hands must not catch
        /// fire because the drummer is at full multiplier. A character with no matching
        /// player stays idle.
        /// </summary>
        public static Func<PerformanceState> ForCharacter(
            VenueCharacter.CharacterType characterType,
            Func<IReadOnlyList<BasePlayer>> playerSource)
        {
            return () =>
            {
                var players = playerSource?.Invoke();
                if (players == null)
                {
                    return PerformanceState.Idle;
                }

                foreach (var player in players)
                {
                    if (player?.Player?.Profile == null)
                    {
                        continue;
                    }

                    if (CharacterTypeFor(player.Player.Profile.CurrentInstrument) != characterType)
                    {
                        continue;
                    }

                    var stats = player.BaseStats;
                    var parameters = player.BaseParameters;
                    if (stats == null || parameters == null)
                    {
                        continue;
                    }

                    return new PerformanceState(stats.ScoreMultiplier, stats.IsStarPowerActive,
                        parameters.MaxMultiplier);
                }

                return PerformanceState.Idle;
            };
        }
    }
}
