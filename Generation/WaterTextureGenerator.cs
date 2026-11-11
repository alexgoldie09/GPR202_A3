using OpenTK.Mathematics;

namespace GPR202_A3.Generation
{
    /// <summary>
    /// Generates the water's ripple normal map from periodic noise, so it tiles without a seam.
    /// The water shader scrolls two copies of it in different directions to fake moving ripples.
    /// </summary>
    internal static class WaterTextureGenerator
    {
        #region Constants
        // How many noise cells fit across the texture (larger = smaller ripples). Must be a power of 2.
        private const int NOISECELLS = 8;

        // Layers of noise. NOISECELLS * 2^(OCTAVES - 1) must be 256 or less for the texture to tile.
        private const int OCTAVES = 4;

        // How steep the ripples are (larger = bumpier water)
        private const float NORMALSTRENGTH = 12f;

        // Bytes per pixel (red, green, blue, alpha)
        private const int CHANNELS = 4;
        private const byte OPAQUE = 255;
        #endregion

        #region Public API
        /// <summary>
        /// Generates a square, tileable normal map. Normals point along +Z (blue) when flat, like any normal map.
        /// </summary>
        /// <param name="size">Pixels along each side.</param>
        /// <param name="seed">Random seed.</param>
        /// <returns>size * size pixels of RGBA bytes, row by row.</returns>
        public static byte[] GenerateNormalMap(int size, int seed)
        {
            var noise = new Noise(seed);

            // --- Ripple heights ---
            // The texture covers exactly NOISECELLS cells of noise that repeats every NOISECELLS cells, so it tiles
            float[] heights = new float[size * size];
            float cellsPerPixel = (float)NOISECELLS / size;
            Parallel.For(0, size, y =>
            {
                for (int x = 0; x < size; x++)
                    heights[y * size + x] = noise.FbmPeriodic(x * cellsPerPixel, y * cellsPerPixel, OCTAVES, NOISECELLS);
            });

            // --- Normals from the slopes ---
            byte[] pixels = new byte[size * size * CHANNELS];
            Parallel.For(0, size, y =>
            {
                for (int x = 0; x < size; x++)
                {
                    // Neighbours wrap around the edges, so the slopes tile too
                    float left = heights[y * size + Wrap(x - 1, size)];
                    float right = heights[y * size + Wrap(x + 1, size)];
                    float down = heights[Wrap(y - 1, size) * size + x];
                    float up = heights[Wrap(y + 1, size) * size + x];

                    // The slope across 2 pixels; a rising surface tilts the normal the other way
                    float slopeX = (right - left) / 2f * NORMALSTRENGTH;
                    float slopeY = (up - down) / 2f * NORMALSTRENGTH;
                    Vector3 normal = Vector3.Normalize(new Vector3(-slopeX, -slopeY, 1f));

                    int i = (y * size + x) * CHANNELS;
                    pixels[i] = ToByte(normal.X);
                    pixels[i + 1] = ToByte(normal.Y);
                    pixels[i + 2] = ToByte(normal.Z);
                    pixels[i + 3] = OPAQUE;
                }
            });

            return pixels;
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Wraps an index into 0..size - 1 (so -1 becomes size - 1).
        /// </summary>
        private static int Wrap(int index, int size) => (index % size + size) % size;

        /// <summary>
        /// Stores a -1..1 value as a 0..255 byte, the way normal maps do.
        /// </summary>
        private static byte ToByte(float value) => (byte)MathF.Round((value * 0.5f + 0.5f) * byte.MaxValue);
        #endregion
    }
}
