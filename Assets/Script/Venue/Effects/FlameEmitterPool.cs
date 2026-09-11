using System.Collections.Generic;
using UnityEngine;

namespace YARG.Venue.Effects
{
    /// <summary>
    /// Reuses flame ParticleSystems across characters and songs.
    ///
    /// Building a ParticleSystem allocates a GameObject, a renderer and a material, so
    /// characters swapping between songs would otherwise churn them every load.
    /// </summary>
    public static class FlameEmitterPool
    {
        private static readonly Stack<ParticleSystem> _available = new();
        private static Material _sharedMaterial;
        private static Transform _parkingSpot;

        public static int AvailableCount => _available.Count;

        /// <summary>Total emitters created since startup; a pool hit does not increase it.</summary>
        public static int CreatedCount { get; private set; }

        public static ParticleSystem Acquire(Transform parent)
        {
            ParticleSystem system = null;

            while (_available.Count > 0 && system == null)
            {
                // Pooled objects can be destroyed with their scene; skip the dead ones.
                system = _available.Pop();
            }

            if (system == null)
            {
                system = Create();
            }

            var t = system.transform;
            t.SetParent(parent, false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;

            system.gameObject.SetActive(true);
            return system;
        }

        public static void Release(ParticleSystem system)
        {
            if (system == null)
            {
                return;
            }

            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            system.gameObject.SetActive(false);
            system.transform.SetParent(GetParkingSpot(), false);

            _available.Push(system);
        }

        /// <summary>Drops every pooled emitter. Used between tests.</summary>
        public static void Clear()
        {
            while (_available.Count > 0)
            {
                var system = _available.Pop();
                if (system != null)
                {
                    DestroyObject(system.gameObject);
                }
            }

            if (_parkingSpot != null)
            {
                DestroyObject(_parkingSpot.gameObject);
                _parkingSpot = null;
            }

            CreatedCount = 0;
        }

        private static Transform GetParkingSpot()
        {
            if (_parkingSpot == null)
            {
                var holder = new GameObject("FlameEmitterPool")
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                holder.SetActive(false);
                _parkingSpot = holder.transform;
            }

            return _parkingSpot;
        }

        private static ParticleSystem Create()
        {
            var go = new GameObject("StarPowerFlame")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };

            var system = go.AddComponent<ParticleSystem>();

            // Configure before the first play so nothing emits on creation.
            var main = system.main;
            main.duration = 1f;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = 0.36f;
            main.startSpeed = 0.45f;
            main.startSize = 0.10f;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.72f, 0.28f, 1f), new Color(1f, 0.35f, 0.06f, 1f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 48;

            var emission = system.emission;
            emission.rateOverTime = 26f;

            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.045f;

            // Flames rise regardless of how the hand is oriented.
            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.y = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);

            // Fade out rather than popping, and shrink as they rise.
            var color = system.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(new Gradient
            {
                colorKeys = new[]
                {
                    new GradientColorKey(new Color(1f, 0.85f, 0.45f), 0f),
                    new GradientColorKey(new Color(1f, 0.35f, 0.05f), 1f),
                },
                alphaKeys = new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.85f, 0.18f),
                    new GradientAlphaKey(0f, 1f),
                },
            });

            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                AnimationCurve.EaseInOut(0f, 1f, 1f, 0.15f));

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = _sharedMaterial ??= FlameTexture.CreateMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            go.SetActive(false);

            CreatedCount++;
            return system;
        }

        private static void DestroyObject(Object target)
        {
            if (Application.isPlaying)
            {
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}
