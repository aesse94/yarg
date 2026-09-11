using System;
using UnityEngine;
using YARG.Core.Logging;

namespace YARG.Venue.Effects
{
    /// <summary>
    /// Flames on a character's hands while the player is performing at full multiplier.
    ///
    /// Trigger: the BASE multiplier (before Star Power doubling) has reached its cap. For
    /// guitar that cap is 4, so flames burn at 4x, and at 8x once Star Power doubles it.
    /// Reading the base rather than the displayed multiplier keeps the behaviour honest on
    /// instruments with a different cap - bass caps at 6, so it burns at 6x and 12x - and
    /// stops a mid-combo 2x from lighting up just because Star Power doubled it to 4x.
    ///
    /// Attaches by BONE NAME. The humanoid avatar maps on these rigs are scrambled
    /// (HumanBodyBones.LeftHand resolves to the right forearm), so GetBoneTransform is not
    /// usable here.
    /// </summary>
    public class BurningHands : MonoBehaviour
    {
        public const string LEFT_HAND_BONE = "leftHand";
        public const string RIGHT_HAND_BONE = "rightHand";

        /// <summary>
        /// What the flames read each frame. Injected so the rule can be driven by a test
        /// without a running song.
        /// </summary>
        public readonly struct PerformanceState
        {
            /// <summary>Total multiplier as shown to the player, Star Power included.</summary>
            public readonly int ScoreMultiplier;

            public readonly bool IsStarPowerActive;

            /// <summary>The instrument's base multiplier cap, before Star Power.</summary>
            public readonly int MaxMultiplier;

            public PerformanceState(int scoreMultiplier, bool isStarPowerActive, int maxMultiplier)
            {
                ScoreMultiplier = scoreMultiplier;
                IsStarPowerActive = isStarPowerActive;
                MaxMultiplier = maxMultiplier;
            }

            /// <summary>Multiplier with Star Power's doubling removed.</summary>
            public int BaseMultiplier =>
                IsStarPowerActive ? ScoreMultiplier / 2 : ScoreMultiplier;

            public bool ShouldBurn => MaxMultiplier > 0 && BaseMultiplier >= MaxMultiplier;

            public static PerformanceState Idle => new(1, false, 4);
        }

        /// <summary>
        /// Source of truth for the flames. Defaults to "never burning" so a character with
        /// no player attached is inert rather than permanently alight.
        /// </summary>
        public Func<PerformanceState> StateSource { get; set; } = () => PerformanceState.Idle;

        private ParticleSystem _leftFlame;
        private ParticleSystem _rightFlame;

        private bool _burning;
        private bool _initialized;

        public bool IsBurning => _burning;
        public int  EmitterCount => (_leftFlame != null ? 1 : 0) + (_rightFlame != null ? 1 : 0);

        public ParticleSystem LeftFlame => _leftFlame;
        public ParticleSystem RightFlame => _rightFlame;

        /// <summary>
        /// Finds both hand bones and attaches pooled emitters. Returns false when the rig
        /// has neither hand bone, in which case this component does nothing at all.
        /// </summary>
        public bool Initialize()
        {
            if (_initialized)
            {
                return EmitterCount > 0;
            }

            _initialized = true;

            var left = FindBone(LEFT_HAND_BONE);
            var right = FindBone(RIGHT_HAND_BONE);

            if (left == null && right == null)
            {
                YargLogger.LogFormatWarning(
                    "Character '{0}' has no hand bones; burning hands disabled", name);
                return false;
            }

            if (left != null) _leftFlame = FlameEmitterPool.Acquire(left);
            if (right != null) _rightFlame = FlameEmitterPool.Acquire(right);

            // Acquired emitters start idle; nothing burns until the multiplier says so.
            SetBurning(false, force: true);
            return true;
        }

        private void LateUpdate()
        {
            if (!_initialized)
            {
                return;
            }

            SetBurning(StateSource().ShouldBurn);
        }

        /// <summary>
        /// Drives the flames directly. Exposed so a test can step the state machine without
        /// waiting for Unity's update loop.
        /// </summary>
        public void Evaluate()
        {
            SetBurning(StateSource().ShouldBurn);
        }

        private void SetBurning(bool burning, bool force = false)
        {
            if (_burning == burning && !force)
            {
                return;
            }

            _burning = burning;

            Apply(_leftFlame, burning);
            Apply(_rightFlame, burning);
        }

        private static void Apply(ParticleSystem system, bool burning)
        {
            if (system == null)
            {
                return;
            }

            var emission = system.emission;
            emission.enabled = burning;

            if (burning)
            {
                system.Play(true);
            }
            else
            {
                // Let the particles already in flight finish rather than vanishing.
                system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        /// <summary>Returns the emitters to the pool. Safe to call more than once.</summary>
        public void ReleaseEmitters()
        {
            if (_leftFlame != null)
            {
                FlameEmitterPool.Release(_leftFlame);
                _leftFlame = null;
            }

            if (_rightFlame != null)
            {
                FlameEmitterPool.Release(_rightFlame);
                _rightFlame = null;
            }

            _burning = false;
            _initialized = false;
        }

        private void OnDestroy()
        {
            ReleaseEmitters();
        }

        private Transform FindBone(string boneName)
        {
            foreach (var candidate in GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(candidate.name, boneName, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
