using UnityEngine;

namespace YARG.Venue.Effects
{
    /// <summary>
    /// Procedurally generated flame sprite.
    ///
    /// The original game's flames were engine particles rather than mesh assets, so there is
    /// nothing to extract; this builds an equivalent soft additive blob at runtime. Generated
    /// once and shared by every emitter - it is never written to after creation.
    /// </summary>
    public static class FlameTexture
    {
        private const int SIZE = 64;

        private static Texture2D _texture;

        public static Texture2D Get()
        {
            if (_texture != null)
            {
                return _texture;
            }

            _texture = Generate();
            return _texture;
        }

        private static Texture2D Generate()
        {
            var texture = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false)
            {
                name = "ProceduralFlame",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                // Nothing reads this back, and not keeping it saves the CPU copy.
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color32[SIZE * SIZE];
            const float center = (SIZE - 1) * 0.5f;

            for (int y = 0; y < SIZE; y++)
            {
                for (int x = 0; x < SIZE; x++)
                {
                    // Teardrop: rounder at the base, tapering towards the top, so the
                    // particle reads as a flame rather than a dot even without rotation.
                    float nx = (x - center) / center;
                    float ny = (y - center) / center;

                    float taper = Mathf.Lerp(1.6f, 0.7f, Mathf.InverseLerp(-1f, 1f, ny));
                    float radius = Mathf.Sqrt(nx * nx * taper * taper + ny * ny);

                    // Soft falloff; squared to keep the core bright and the edge gentle.
                    float intensity = Mathf.Clamp01(1f - radius);
                    intensity *= intensity;

                    // White-hot core -> yellow -> orange -> red at the fringe.
                    Color color;
                    if (intensity > 0.75f)
                    {
                        color = Color.Lerp(new Color(1f, 0.85f, 0.4f), Color.white,
                            Mathf.InverseLerp(0.75f, 1f, intensity));
                    }
                    else if (intensity > 0.35f)
                    {
                        color = Color.Lerp(new Color(1f, 0.42f, 0.08f), new Color(1f, 0.85f, 0.4f),
                            Mathf.InverseLerp(0.35f, 0.75f, intensity));
                    }
                    else
                    {
                        color = Color.Lerp(new Color(0.75f, 0.10f, 0.02f), new Color(1f, 0.42f, 0.08f),
                            Mathf.InverseLerp(0f, 0.35f, intensity));
                    }

                    // Additive blending uses alpha as the overall weight.
                    color.a = intensity;
                    pixels[y * SIZE + x] = color;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>
        /// Additive material for the flame particles. This is created by us and owned by us -
        /// no character or venue material is ever modified.
        /// </summary>
        public static Material CreateMaterial()
        {
            // URP first, then the built-in particle shader, then a sprite shader. Sprites/
            // Default is present in every project, so the last step cannot fail silently.
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                ?? Shader.Find("Particles/Standard Unlit")
                ?? Shader.Find("Mobile/Particles/Additive")
                ?? Shader.Find("Sprites/Default");

            var material = new Material(shader)
            {
                name = "ProceduralFlameMaterial",
                hideFlags = HideFlags.HideAndDontSave,
            };

            material.mainTexture = Get();

            // Additive: flames brighten what is behind them and never darken it, which is
            // what makes the glow read without touching scene lighting.
            material.SetFloat("_Surface", 1f);   // transparent (URP)
            material.SetFloat("_Blend", 1f);     // additive (URP)
            material.SetFloat("_ZWrite", 0f);
            material.SetInt("_SrcBlend", (int) UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int) UnityEngine.Rendering.BlendMode.One);
            material.renderQueue = (int) UnityEngine.Rendering.RenderQueue.Transparent;

            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", Get());
            }

            return material;
        }
    }
}
