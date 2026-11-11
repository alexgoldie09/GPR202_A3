using OpenTK.Mathematics;

namespace GPR202_A3.Generation
{
    /// <summary>
    /// Generates the desert's heights in code: rolling sand, dunes from several wind directions,
    /// and a crater-shaped oasis basin in the centre.
    /// </summary>
    internal static class DesertGenerator
    {
        #region Constants
        // --- Rolling base: broad, gentle hills under everything ---
        private const float BASESCALE = 700f; // size of the hills, in world units
        private const float BASEHEIGHT = 20f; // tallest hill (never below 0, so dune troughs stay above the water)
        private const int BASEOCTAVES = 3; // layers of noise in the hills

        // --- Main dunes: one ridge pattern per wind, blended by region ---
        private static readonly Vector2[] MainWinds =
        [
            Vector2.Normalize(new Vector2(1f, 0.35f)),
            Vector2.Normalize(new Vector2(-0.25f, 1f)),
            Vector2.Normalize(new Vector2(1f, -0.9f))
        ];
        private const float MAINWAVELENGTH = 170f; // distance between crests (shorter = more dunes)
        private const float MAINHEIGHT = 38f; // tallest crest

        // Wind regions: how big each wind's area is, and how quickly one wind takes over from another
        private const float WINDREGIONSCALE = 650f; // larger = bigger regions
        private const int WINDREGIONOCTAVES = 2;
        private const float WINDREGIONSTART = -0.2f; // region noise below this = this wind has no say
        private const float WINDREGIONFULL = 0.4f; // region noise above this = this wind has full say
        private const float MINWINDWEIGHT = 0.001f; // stops the blend dividing by 0 where no wind has a say

        // Dune fields: which parts of the map have tall dunes and which are nearly flat
        private const float DUNEFIELDSCALE = 500f;
        private const int DUNEFIELDOCTAVES = 2;
        private const float DUNEFIELDSTART = -0.6f; // field noise below this = no dunes
        private const float DUNEFIELDFULL = 0.25f; // field noise above this = full-height dunes

        // --- Small dunes: shorter ridges at another angle, in patches ---
        private static readonly Vector2 SmallWind = Vector2.Normalize(new Vector2(0.6f, -1f));
        private const float SMALLWAVELENGTH = 75f;
        private const float SMALLHEIGHT = 5f;
        private const float SMALLFIELDSCALE = 300f;
        private const int SMALLFIELDOCTAVES = 2;
        private const float SMALLFIELDSTART = -0.2f;
        private const float SMALLFIELDFULL = 0.6f;

        // --- Dune shape ---
        private const float WARPSTRENGTH = 0.7f; // how far the crests meander, in wavelengths
        private const float WARPSCALE = 380f; // size of the meanders, in world units
        private const int WARPOCTAVES = 2;
        private const float WINDWARDFRACTION = 0.75f; // how much of each dune is the gentle side (the rest is the steep slip face)

        // --- Noise offsets ---
        // Each layer reads a different part of the noise, so the layers don't line up with each other.
        // The values are arbitrary; they just need to be different.
        private static readonly Vector2 DuneFieldOffset = new(31.7f, -12.3f);
        private static readonly Vector2 SmallFieldOffset = new(-71.1f, 5.9f);
        private static readonly Vector2 SmallDuneOffset = new(913f, -377f);
        private static readonly Vector2 WindRegionOffset = new(47.3f, -19.1f); // multiplied by the wind's number
        private static readonly Vector2 WindRidgeOffset = new(311f, -173f); // multiplied by the wind's number

        // --- Oasis basin: a crater, just much bigger ---
        private const float BASINRADIUS = 160f; // radius to the rim
        private const float BASINDEPTH = 40f; // depth at the centre
        private const float FLATRADIUS = 240f; // dunes are fully flattened inside this distance
        private const float DUNERADIUS = 400f; // dunes are full height beyond this distance

        // --- Crater shape (as fractions of the crater's radius and depth) ---
        private const float RIMHEIGHT = 0.25f; // the rim is a quarter of the depth high
        private const float RIMWIDTH = 0.3f; // how wide the rim is

        // How many grid points either side are averaged to round off the crests
        private const int SMOOTHRADIUS = 2;
        #endregion

        #region Public API
        /// <summary>
        /// Generates the height of every grid point.
        /// </summary>
        /// <param name="gridSize">Grid points along each side.</param>
        /// <param name="spacing">World units between grid points.</param>
        /// <param name="seed">Random seed (the same seed always gives the same desert).</param>
        /// <returns>The heights row by row (index = z * gridSize + x).</returns>
        public static float[] Generate(int gridSize, float spacing, int seed)
        {
            var noise = new Noise(seed);
            float[] heights = new float[gridSize * gridSize];

            // The grid is centred on the origin, so the oasis is at (0, 0)
            float halfSize = (gridSize - 1) * spacing / 2f;

            // Each row doesn't depend on the others, so the rows are split across the CPU's cores
            Parallel.For(0, gridSize, z =>
            {
                float worldZ = z * spacing - halfSize;
                for (int x = 0; x < gridSize; x++)
                {
                    float worldX = x * spacing - halfSize;
                    heights[z * gridSize + x] = SampleDesert(noise, worldX, worldZ);
                }
            });

            Smooth(heights, gridSize, SMOOTHRADIUS);
            return heights;
        }

        /// <summary>
        /// Averages each point with its neighbours (a box blur), in two passes: along the rows, then down the columns.
        /// Each pass keeps a running total, adding the value entering the window and removing the one leaving it,
        /// so each point costs the same whatever the radius (the original read all 64 neighbours per point).
        /// The window is centred, edges are included, and the result is written back into the array.
        /// </summary>
        /// <param name="heights">The heights, row by row. Smoothed in place.</param>
        /// <param name="gridSize">Grid points along each side.</param>
        /// <param name="radius">How many points either side to average.</param>
        public static void Smooth(float[] heights, int gridSize, int radius)
        {
            if (radius <= 0)
                return;

            float[] rowsBlurred = new float[heights.Length];

            // Rows: each row starts at z * gridSize, and neighbours are 1 apart
            Parallel.For(0, gridSize, z => BlurLine(heights, rowsBlurred, z * gridSize, 1, gridSize, radius));

            // Columns: each column starts at x, and neighbours are a whole row apart
            Parallel.For(0, gridSize, x => BlurLine(rowsBlurred, heights, x, gridSize, gridSize, radius));
        }

        /// <summary>
        /// The shape of a crater as a fraction of its depth: a bowl (-1 at the centre) with a raised rim.
        /// Used for the oasis basin and for runtime craters.
        /// </summary>
        /// <param name="distance">Distance from the centre as a fraction of the radius (1 = the rim).</param>
        public static float CraterShape(float distance)
        {
            // Bowl: (1 - d^2)^2 is flat where it meets the rim, so there is no crease
            float bowl = 0f;
            if (distance < 1f)
            {
                float t = 1f - distance * distance;
                bowl = -t * t;
            }

            // Rim: a bump centred on the edge that fades out either side
            float rimOffset = (distance - 1f) / RIMWIDTH;
            float rim = RIMHEIGHT * MathF.Exp(-rimOffset * rimOffset);

            return bowl + rim;
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// The desert's height at one world position.
        /// </summary>
        private static float SampleDesert(Noise noise, float x, float z)
        {
            // Rolling base
            float height = ToZeroOne(noise.Fbm(x / BASESCALE, z / BASESCALE, BASEOCTAVES)) * BASEHEIGHT;

            // How strong each dune layer is here, so some areas are tall dunes and some are nearly flat
            float duneField = noise.Fbm(x / DUNEFIELDSCALE + DuneFieldOffset.X, z / DUNEFIELDSCALE + DuneFieldOffset.Y, DUNEFIELDOCTAVES);
            float smallField = noise.Fbm(x / SMALLFIELDSCALE + SmallFieldOffset.X, z / SMALLFIELDSCALE + SmallFieldOffset.Y, SMALLFIELDOCTAVES);
            float mainStrength = SmoothStep(DUNEFIELDSTART, DUNEFIELDFULL, duneField);
            float smallStrength = SmoothStep(SMALLFIELDSTART, SMALLFIELDFULL, smallField);

            // Dunes
            height += BlendedDunes(noise, x, z) * MAINHEIGHT * mainStrength;
            height += DuneRidge(noise, x + SmallDuneOffset.X, z + SmallDuneOffset.Y, SmallWind, SMALLWAVELENGTH) * SMALLHEIGHT * smallStrength;

            // Oasis: flatten the dunes towards the centre, then add the basin
            float distance = MathF.Sqrt(x * x + z * z);
            height *= SmoothStep(FLATRADIUS, DUNERADIUS, distance);
            height += CraterShape(distance / BASINRADIUS) * BASINDEPTH;

            return height;
        }

        /// <summary>
        /// The main dunes (0..1), blended between the winds. Each wind has a weight from its own noise,
        /// so one wind wins in each region, and the patterns cross where regions meet.
        /// </summary>
        private static float BlendedDunes(Noise noise, float x, float z)
        {
            float total = 0f;
            float totalWeight = 0f;

            for (int i = 0; i < MainWinds.Length; i++)
            {
                // Each wind reads its own part of the noise, so the regions don't line up
                float regionNoise = noise.Fbm(x / WINDREGIONSCALE + i * WindRegionOffset.X,
                                              z / WINDREGIONSCALE + i * WindRegionOffset.Y, WINDREGIONOCTAVES);
                float weight = SmoothStep(WINDREGIONSTART, WINDREGIONFULL, regionNoise) + MINWINDWEIGHT;

                float ridge = DuneRidge(noise, x + i * WindRidgeOffset.X, z + i * WindRidgeOffset.Y, MainWinds[i], MAINWAVELENGTH);
                total += ridge * weight;
                totalWeight += weight;
            }

            return total / totalWeight;
        }

        /// <summary>
        /// One set of dunes (0..1): parallel ridges across the wind, with a gentle windward side and a steep slip face.
        /// Noise bends the crests so they meander.
        /// </summary>
        /// <param name="wind">The wind direction (normalised).</param>
        /// <param name="wavelength">Distance between crests, in world units.</param>
        private static float DuneRidge(Noise noise, float x, float z, Vector2 wind, float wavelength)
        {
            // How many wavelengths along the wind this point is, bent by noise
            float along = (x * wind.X + z * wind.Y) / wavelength;
            along += noise.Fbm(x / WARPSCALE, z / WARPSCALE, WARPOCTAVES) * WARPSTRENGTH;

            // Position within one dune, 0..1
            float t = along - MathF.Floor(along);

            // Up the windward side to the crest, then down the slip face
            if (t < WINDWARDFRACTION)
                return SmoothStep(0f, 1f, t / WINDWARDFRACTION);
            return SmoothStep(0f, 1f, (1f - t) / (1f - WINDWARDFRACTION));
        }

        /// <summary>
        /// Blurs one row or column with a running total.
        /// </summary>
        /// <param name="start">Index of the line's first value.</param>
        /// <param name="stride">Gap between values on the line (1 for a row, gridSize for a column).</param>
        /// <param name="count">Number of values on the line.</param>
        /// <param name="radius">How many values either side to average.</param>
        private static void BlurLine(float[] source, float[] destination, int start, int stride, int count, int radius)
        {
            int windowSize = 2 * radius + 1;

            // Total for the first window. Indexes past the ends are clamped, so the edge values repeat.
            float sum = 0f;
            for (int k = -radius; k <= radius; k++)
                sum += source[start + Math.Clamp(k, 0, count - 1) * stride];

            for (int i = 0; i < count; i++)
            {
                destination[start + i * stride] = sum / windowSize;

                // Slide the window: add the value coming in, remove the value going out
                int entering = Math.Clamp(i + radius + 1, 0, count - 1);
                int leaving = Math.Clamp(i - radius, 0, count - 1);
                sum += source[start + entering * stride] - source[start + leaving * stride];
            }
        }

        /// <summary>
        /// Converts noise from -1..1 to 0..1.
        /// </summary>
        private static float ToZeroOne(float value) => value * 0.5f + 0.5f;

        /// <summary>
        /// 0 below edge0, 1 above edge1, with a smooth curve between (the same as GLSL's smoothstep).
        /// </summary>
        private static float SmoothStep(float edge0, float edge1, float value)
        {
            float t = Math.Clamp((value - edge0) / (edge1 - edge0), 0f, 1f);
            return t * t * (3f - 2f * t);
        }
        #endregion
    }
}
