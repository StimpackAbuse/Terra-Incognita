using UnityEngine;

namespace TerraIncognita.World
{
    public sealed class PeriodicTerrainNoise
    {
        private static readonly Vector2[] Gradients =
        {
            new Vector2(1f, 0f),
            new Vector2(-1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(0f, -1f),
            new Vector2(0.70710678f, 0.70710678f),
            new Vector2(-0.70710678f, 0.70710678f),
            new Vector2(0.70710678f, -0.70710678f),
            new Vector2(-0.70710678f, -0.70710678f)
        };

        private readonly int[] permutation = new int[512];
        private readonly float worldWidth;
        private readonly int baseCycles;
        private readonly int octaveCount;
        private readonly float persistence;
        private readonly float heightOffset;
        private readonly float heightAmplitude;
        private readonly float seaLevel;

        public PeriodicTerrainNoise(
            int seed,
            float worldWidth,
            int baseCycles,
            int octaveCount,
            float persistence,
            float heightOffset,
            float heightAmplitude,
            float seaLevel)
        {
            this.worldWidth = Mathf.Max(1f, worldWidth);
            this.baseCycles = Mathf.Max(1, baseCycles);
            this.octaveCount = Mathf.Clamp(octaveCount, 1, 8);
            this.persistence = Mathf.Clamp01(persistence);
            this.heightOffset = heightOffset;
            this.heightAmplitude = Mathf.Max(0f, heightAmplitude);
            this.seaLevel = seaLevel;

            InitializePermutation(seed);
        }

        public float GetHeight(float worldX, float worldZ)
        {
            float periodicX = Mathf.Repeat(
                worldX + worldWidth * 0.5f,
                worldWidth);
            float accumulatedNoise = 0f;
            float amplitude = 1f;
            float totalAmplitude = 0f;

            for (int octave = 0; octave < octaveCount; octave++)
            {
                int cycles = baseCycles * (1 << octave);
                float x = periodicX / worldWidth * cycles;
                float z = worldZ / worldWidth * cycles;

                accumulatedNoise += SamplePeriodicPerlin(x, z, cycles) * amplitude;
                totalAmplitude += amplitude;
                amplitude *= persistence;
            }

            float normalizedNoise = totalAmplitude > 0f
                ? accumulatedNoise / totalAmplitude
                : 0f;

            return heightOffset + normalizedNoise * heightAmplitude;
        }

        private void InitializePermutation(int seed)
        {
            int[] values = new int[256];
            for (int i = 0; i < values.Length; i++)
                values[i] = i;

            uint state = unchecked((uint)seed);
            if (state == 0)
                state = 0x6D2B79F5u;

            for (int i = values.Length - 1; i > 0; i--)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;

                int swapIndex = (int)(state % (uint)(i + 1));
                int value = values[i];
                values[i] = values[swapIndex];
                values[swapIndex] = value;
            }

            for (int i = 0; i < permutation.Length; i++)
                permutation[i] = values[i & 255];
        }

        private float SamplePeriodicPerlin(float x, float z, int periodX)
        {
            int x0 = Mathf.FloorToInt(x);
            int z0 = Mathf.FloorToInt(z);
            int x1 = x0 + 1;
            int z1 = z0 + 1;

            float localX = x - x0;
            float localZ = z - z0;
            float fadeX = Fade(localX);
            float fadeZ = Fade(localZ);

            float bottom = Mathf.Lerp(
                GradientDot(x0, z0, localX, localZ, periodX),
                GradientDot(x1, z0, localX - 1f, localZ, periodX),
                fadeX);
            float top = Mathf.Lerp(
                GradientDot(x0, z1, localX, localZ - 1f, periodX),
                GradientDot(x1, z1, localX - 1f, localZ - 1f, periodX),
                fadeX);

            return Mathf.Clamp(Mathf.Lerp(bottom, top, fadeZ) * 1.41421356f, -1f, 1f);
        }

        private float GradientDot(int x, int z, float offsetX, float offsetZ, int periodX)
        {
            int wrappedX = Modulo(x, periodX);
            int hash = permutation[(permutation[wrappedX] + z) & 255];
            Vector2 gradient = Gradients[hash & 7];
            return gradient.x * offsetX + gradient.y * offsetZ;
        }

        private static float Fade(float value)
        {
            return value * value * value *
                   (value * (value * 6f - 15f) + 10f);
        }

        private static int Modulo(int value, int divisor)
        {
            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }
    }
}
