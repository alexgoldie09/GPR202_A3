namespace GPR202_A3.Generation
{
    /// <summary>
    /// Seeded 2D gradient noise (Perlin's improved noise) and fBm (several layers of it added together).
    /// The same seed always gives the same values.
    /// </summary>
    internal class Noise
    {
        #region Constants
        // Size of the shuffled lookup table (the noise repeats every 256 units, far bigger than anything sampled)
        private const int TABLESIZE = 256;

        // Number of gradient directions to pick from (see Gradient)
        private const int GRADIENTCOUNT = 8;

        // fBm: each layer is twice as detailed (LACUNARITY) and half as strong (GAIN) as the one before
        private const float LACUNARITY = 2f;
        private const float GAIN = 0.5f;
        #endregion

        #region Private Fields
        // The numbers 0..255 shuffled, stored twice so looking up index + 1 never runs off the end
        private readonly int[] _permutation = new int[TABLESIZE * 2];
        #endregion

        #region Constructors
        /// <summary>
        /// Constructor that shuffles the lookup table with the given seed.
        /// </summary>
        /// <param name="seed">Any whole number.</param>
        public Noise(int seed)
        {
            var random = new Random(seed);

            int[] table = new int[TABLESIZE];
            for (int i = 0; i < TABLESIZE; i++)
                table[i] = i;

            // Fisher-Yates shuffle: swap each entry with a random one at or before it
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
        /// Noise at a point: smooth, roughly -1..1, with features about 1 unit apart.
        /// Scale the coordinates to change the feature size.
        /// </summary>
        public float Sample(float x, float y)
        {
            // The grid cell the point is in, and where it is inside the cell (0..1)
            int cellX = (int)MathF.Floor(x);
            int cellY = (int)MathF.Floor(y);
            float fx = x - cellX;
            float fy = y - cellY;

            // Wrap the cell into the table (works for negative cells too)
            int ix = cellX & (TABLESIZE - 1);
            int iy = cellY & (TABLESIZE - 1);

            // Each corner of the cell gets a random gradient, picked by looking its cell up in the table
            int a = _permutation[ix] + iy;
            int b = _permutation[ix + 1] + iy;
            float g00 = Gradient(_permutation[a], fx, fy);
            float g10 = Gradient(_permutation[b], fx - 1f, fy);
            float g01 = Gradient(_permutation[a + 1], fx, fy - 1f);
            float g11 = Gradient(_permutation[b + 1], fx - 1f, fy - 1f);

            // Blend the four corners with a smooth curve, so there are no creases at cell edges
            float u = Fade(fx);
            float v = Fade(fy);
            float bottom = Lerp(g00, g10, u);
            float top = Lerp(g01, g11, u);
            return Lerp(bottom, top, v);
        }

        /// <summary>
        /// fBm: several layers (octaves) of noise added together, each more detailed and weaker than the last.
        /// </summary>
        /// <param name="octaves">How many layers. More = more fine detail, but slower.</param>
        /// <returns>Roughly -1..1.</returns>
        public float Fbm(float x, float y, int octaves)
        {
            float sum = 0f;
            float amplitude = 1f;
            float frequency = 1f;
            float total = 0f; // the biggest possible sum, to scale the result back to -1..1

            for (int i = 0; i < octaves; i++)
            {
                sum += Sample(x * frequency, y * frequency) * amplitude;
                total += amplitude;
                amplitude *= GAIN;
                frequency *= LACUNARITY;
            }

            return sum / total;
        }

        /// <summary>
        /// Like Sample, but repeats every 'period' units in x and y, so textures made from it tile without a seam.
        /// </summary>
        /// <param name="period">How often the noise repeats. Must be a power of 2, up to 256.</param>
        public float SamplePeriodic(float x, float y, int period)
        {
            int cellX = (int)MathF.Floor(x);
            int cellY = (int)MathF.Floor(y);
            float fx = x - cellX;
            float fy = y - cellY;

            // Wrap every corner by the period (not the table size), so the cells after the last one are the first ones again
            int mask = period - 1;
            int x0 = cellX & mask;
            int y0 = cellY & mask;
            int x1 = (cellX + 1) & mask;
            int y1 = (cellY + 1) & mask;

            float g00 = Gradient(_permutation[_permutation[x0] + y0], fx, fy);
            float g10 = Gradient(_permutation[_permutation[x1] + y0], fx - 1f, fy);
            float g01 = Gradient(_permutation[_permutation[x0] + y1], fx, fy - 1f);
            float g11 = Gradient(_permutation[_permutation[x1] + y1], fx - 1f, fy - 1f);

            float u = Fade(fx);
            float v = Fade(fy);
            return Lerp(Lerp(g00, g10, u), Lerp(g01, g11, u), v);
        }

        /// <summary>
        /// Like Fbm, but repeats every 'period' units. Each layer doubles the period along with the detail,
        /// so period * 2^(octaves - 1) must be 256 or less.
        /// </summary>
        public float FbmPeriodic(float x, float y, int octaves, int period)
        {
            float sum = 0f;
            float amplitude = 1f;
            float frequency = 1f;
            float total = 0f;
            int layerPeriod = period;

            for (int i = 0; i < octaves; i++)
            {
                sum += SamplePeriodic(x * frequency, y * frequency, layerPeriod) * amplitude;
                total += amplitude;
                amplitude *= GAIN;
                frequency *= LACUNARITY;
                layerPeriod *= (int)LACUNARITY;
            }

            return sum / total;
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Perlin's fade curve, 6t^5 - 15t^4 + 10t^3: an S-curve that is flat at both ends.
        /// </summary>
        private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        /// <summary>
        /// Linear interpolation from a to b.
        /// </summary>
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        /// <summary>
        /// The dot product of one of 8 gradient directions (picked by the hash) with the offset (x, y).
        /// </summary>
        private static float Gradient(int hash, float x, float y)
        {
            switch (hash % GRADIENTCOUNT)
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
