using YARG.Core.Engine;
using YARG.Core.Replays;
using YARG.Player;
using YARG.Replays;

namespace YARG.Menu.ScoreScreen
{
    public struct PlayerScoreCard
    {
        public bool  IsHighScore;
        public float AverageMultiplier;

        public YargPlayer Player;
        public BaseStats  Stats;
    }

    public struct ScoreScreenStats
    {
        public PlayerScoreCard[] PlayerScores;

        public int BandStars;
        public int BandScore;

        /// <summary>Set when the run ended in a fail, so the results screen can play the
        /// lose stinger instead of the win one.</summary>
        public bool SongFailed;

#nullable enable
        public ReplayInfo? ReplayInfo;
#nullable disable
    }
}