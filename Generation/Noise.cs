namespace GPR202_A3.Generation
{
    /// <summary>
    /// Seeded 2D gradient noise (Perlin's "improved noise", 2002) and fractal Brownian motion (fBm) built on it.
    ///
    /// Gradient noise gives a smooth, random-looking value for any point: a grid of random slopes is laid over the
    /// plane, and each point blends the slopes of the four grid corners around it. The same seed always gives
    /// the same values, so the same desert is generated every run.
    /// </summary>
    internal class Noise
    {
        #region Constants
        // Size of the permutation table (the grid repeats every 256 cells, far larger than anything sampled here)
        private const int TABLESIZE = 256;
        #endregion

        #region Private Fields
        // A shuffled list of 0..255, stored twice so lookups never need wrapping (index + 1 is always valid)
        private readonly int[] _permutation = new int[TABLESIZE * 2];
        #endregion

        #region Constructors
        /// <summary>
        /// Constructor that shuffles the permutation table with the given seed.
        /// </summary>
        /// <param name="seed">Any whole number. The same seed always gives the same noise.</param>
        public Noise(int seed)
        {
            var random = new Random(seed);

            int[] table = new int[TABLESIZE];
            for (int i = 0; i < TABLESIZE; i++)
                table[i] = i;

            // Fisher-Yates shuffle: swap each entry with a random entry at or before it
            for (int i = TABLESIZE - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (table[i], table[j]) = (table[j], table[i]);
            }

            for (int i = 0; i < TABLESIZE * 2; i++)
                _permutation[i] = table[i % TABLESIZE];
        }
        #endregion

        #region Public API
        /// <summary>
        /// Gradient noise at a point. Smooth, roughly -1..1, with features about 1 unit apart.
        /// </summary>
        /// <param name="x">The x coordinate (scale it to choose the feature size).</param>
        /// <param name="y">The y coordinate.</param>
        public float Sample(float x, float y)
        {
            // The grid cell the point is in, and where it is inside that cell (0..1)
            int cellX = (int)MathF.Floor(x);
            int cellY = (int)MathF.Floor(y);
            float fx = x - cellX;
            float fy = y - cellY;

            // Wrap the cell to the table size (& 255 keeps negative cells positive too)
            int ix = cellX & (TABLESIZE - 1);
            int iy = cellY & (TABLESIZE - 1);

            // A random gradient for each corner, picked by hashing the corner's cell through the table
            int a = _permutation[ix] + iy;
            int b = _permutation[ix + 1] + iy;
            float g00 = Gradient(_permutation[a], fx, fy);
            float g10 = Gradient(_permutation[b], fx - 1f, fy);
            float g01 = Gradient(_permutation[a + 1], fx, fy - 1f);
            float g11 = Gradient(_permutation[b + 1], fx - 1f, fy - 1f);

            // Blend the four corners with an S-curve, so the result has no visible creases at cell edges
            float u = Fade(fx);
            float v = Fade(fy);
            float bottom = Lerp(g00, g10, u);
            float top = Lerp(g01, g11, u);
            return Lerp(bottom, top, v);
        }

        /// <summary>
        /// Fractal Brownian motion: several layers (octaves) of noise added together, each twice as detailed
        /// and half as strong as the last. Gives natural-looking variation at many sizes at once.
        /// </summary>
        /// <param name="x">The x coordinate (the first octave has features about 1 unit apart).</param>
        /// <param name="y">The y coordinate.</param>
        /// <param name="octaves">How many layers to add. More = more fine detail, but slower.</param>
        /// <returns>Roughly -1..1.</returns>
        public float Fbm(float x, float y, int octaves)
        {
            float sum = 0f;
            float amplitude = 1f;
            float frequency = 1f;
            float total = 0f; // the largest possible sum, to bring the result back to -1..1

            for (int i = 0; i < octaves; i++)
            {
                sum += Sample(x * frequency, y * frequency) * amplitude;
                total += amplitude;
                amplitude *= 0.5f;
                frequency *= 2f;
            }

            return sum / total;
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Perlin's fade curve, 6t^5 - 15t^4 + 10t^3: an S-curve whose slope and curvature are 0 at both ends.
        /// </summary>
        private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        /// <summary>
        /// Linear interpolation from a to b.
        /// </summary>
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        /// <summary>
        /// The dot product of one of 8 fixed gradient directions (picked by the hash) with the offset (x, y).
        /// </summary>
        private static float Gradient(int hash, float x, float y)
        {
            switch (hash & 7)
            {
                case 0: return x + y;
                case 1: return -x + y;
                case 2: return x - y;
                case 3: return -x - y;
                case 4: return x;
                case 5: return -x;
                case 6: return y;
                default: return -y;
            }
        }
        #endregion
    }
}
