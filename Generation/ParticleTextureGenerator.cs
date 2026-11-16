using GPR202_A3.Particles;
using OpenTK.Mathematics;

namespace GPR202_A3.Generation
{
    /// <summary>
    /// Generates the particle atlas: a 2x2 grid of white shapes (soft dot, spark, smoke puff, square),
    /// in ParticleSprite order. They're white so each particle's colour tints them, and only alpha holds the shape.
    /// </summary>
    internal static class ParticleTextureGenerator
    {
        #region Constants
        // Cells across and down (must match ParticleSprite's 4 shapes)
        public const int COLUMNS = 2;
        public const int ROWS = 2;

        // Bytes per pixel (red, green, blue, alpha)
        private const int CHANNELS = 4;
        private const byte WHITE = 255;

        // Empty border around each shape, as a fraction of the cell, so neighbouring cells never bleed in
        private const float CELLMARGIN = 0.06f;

        // Spark: a small bright core plus a fainter glow
        private const float SPARKCORE = 0.15f; // core radius, as a fraction of the shape's radius
        private const float SPARKGLOW = 0.6f; // glow brightness
        private const float SPARKGLOWFALLOFF = 4f; // larger = the glow fades faster

        // Smoke: a soft blob, made lumpy with noise
        private const float SMOKENOISESCALE = 3f; // noise features across the puff
        private const int SMOKEOCTAVES = 3;
        private const float SMOKENOISESTRENGTH = 0.45f; // 0 = smooth blob, 1 = very lumpy
        private const float SMOKEDENSITY = 2f; // thickens the middle (alpha is capped at 1)

        // Square: how much of the cell it fills (the rest is the margin)
        private const float SQUARESIZE = 0.8f;

        // Seed for the smoke noise
        private const int SEED = 11;
        #endregion

        #region Public API
        /// <summary>
        /// Generates the atlas.
        /// </summary>
        /// <param name="cellSize">Pixels per cell (a power of 2, so mipmaps halve cleanly).</param>
        /// <returns>(cellSize * COLUMNS) by (cellSize * ROWS) RGBA pixels, row by row.</returns>
        public static byte[] GenerateAtlas(int cellSize)
        {
            int width = cellSize * COLUMNS;
            int height = cellSize * ROWS;
            byte[] pixels = new byte[width * height * CHANNELS];
            var noise = new Noise(SEED);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Which cell this pixel is in, and where it is inside the cell (-1..1 from the centre)
                    var sprite = (ParticleSprite)(y / cellSize * COLUMNS + x / cellSize);
                    float u = ((x % cellSize) + 0.5f) / cellSize * 2f - 1f;
                    float v = ((y % cellSize) + 0.5f) / cellSize * 2f - 1f;

                    // Shapes fill the cell minus its margin
                    float scale = 1f - CELLMARGIN * 2f;
                    float alpha = GetShapeAlpha(sprite, u / scale, v / scale, noise);

                    int i = (y * width + x) * CHANNELS;
                    pixels[i] = WHITE;
                    pixels[i + 1] = WHITE;
                    pixels[i + 2] = WHITE;
                    pixels[i + 3] = (byte)(Math.Clamp(alpha, 0f, 1f) * WHITE);
                }
            }

            return pixels;
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// How opaque a shape is at a point (-1..1 across the shape, 0 = its centre).
        /// </summary>
        private static float GetShapeAlpha(ParticleSprite sprite, float u, float v, Noise noise)
        {
            float distance = MathF.Sqrt(u * u + v * v); // 0 at the centre, 1 at the edge
            float edge = MathF.Max(0f, 1f - distance); // 1 at the centre, 0 at the edge and beyond

            switch (sprite)
            {
                case ParticleSprite.SoftDot:
                    return edge * edge;

                case ParticleSprite.Spark:
                {
                    float core = MathF.Exp(-(distance / SPARKCORE) * (distance / SPARKCORE));
                    float glow = MathF.Pow(edge, SPARKGLOWFALLOFF) * SPARKGLOW;
                    return core + glow;
                }

                case ParticleSprite.Smoke:
                {
                    // Our Perlin noise (-1..1) turned into 0..1, making the soft blob lumpy
                    float lumps = noise.Fbm(u * SMOKENOISESCALE, v * SMOKENOISESCALE, SMOKEOCTAVES) * 0.5f + 0.5f;
                    return edge * edge * MathHelper.Lerp(1f, lumps, SMOKENOISESTRENGTH) * SMOKEDENSITY;
                }

                default: // Square
                    return MathF.Abs(u) <= SQUARESIZE && MathF.Abs(v) <= SQUARESIZE ? 1f : 0f;
            }
        }
        #endregion
    }
}
