using OpenTK.Mathematics;

namespace GPR202_A3.Generation
{
    /// <summary>
    /// Generates the desert's height field in code: rolling sand, wind-blown dunes, and a bowl-shaped oasis
    /// basin in the centre of the map for the water to sit in.
    ///
    /// Heights are generated as floats, so there is none of the stepping (terracing) an 8-bit height map image has,
    /// and only a light smoothing pass is needed to round off the dune crests.
    /// </summary>
    internal static class DesertGenerator
    {
        #region Constants
        // --- Rolling base: broad, gentle hills under everything ---
        private const float BASESCALE = 700f; // feature size, in world units
        private const float BASEHEIGHT = 20f; // 0 up to this much (never below 0, so dune troughs stay above the water)

        // --- Main dunes: long ridges lying across the wind ---
        // Each region of the map has its own prevailing wind, so the dunes face different ways in different places.
        // Noise decides how strongly each wind counts at each point, and the dunes blend between them.
        private static readonly Vector2[] MainWinds =
        [
            Vector2.Normalize(new Vector2(1f, 0.35f)),
            Vector2.Normalize(new Vector2(-0.25f, 1f)),
            Vector2.Normalize(new Vector2(1f, -0.9f))
        ];
        private const float WINDREGIONSCALE = 650f; // size of each wind's region, in world units (larger = bigger regions)
        private const float MAINWAVELENGTH = 170f; // distance between crests, in world units (shorter = more dunes)
        private const float MAINHEIGHT = 38f; // tallest crest height

        // --- Small dunes: shorter ridges at a different angle, to break up the main pattern ---
        private static readonly Vector2 SmallWind = Vector2.Normalize(new Vector2(0.6f, -1f));
        private const float SMALLWAVELENGTH = 75f;
        private const float SMALLHEIGHT = 5f;

        // How much the crests meander (in wavelengths), and the size of those bends (in world units)
        private const float WARPSTRENGTH = 0.7f;
        private const float WARPSCALE = 380f;

        // How much of each dune is the gentle windward slope; the rest is the steep slip face on the far side
        private const float WINDWARDFRACTION = 0.75f;

        // How much of the map the main dunes cover: the dune-field noise (-1..1) is turned into a 0..1 strength
        // between these two values. Lowering both covers more of the map with dunes and leaves fewer flat areas.
        private const float DUNEFIELDSTART = -0.6f; // noise below this = no dunes
        private const float DUNEFIELDFULL = 0.25f; // noise above this = full-height dunes

        // --- Oasis basin in the centre: the same shape as a crater (a bowl with a raised rim), just much bigger ---
        private const float BASINRADIUS = 160f; // the bowl's radius (where the rim peaks), in world units
        private const float BASINDEPTH = 40f; // how deep the bowl's centre is (deep for its size, like the craters, so the rim stands out)
        private const float FLATRADIUS = 240f; // inside this distance the dunes are completely flattened (clear of the rim)
        private const float DUNERADIUS = 400f; // beyond this distance the dunes are at full height

        // Smoothing: how many grid points either side are averaged, to round off the dune crests
        private const int SMOOTHRADIUS = 2;
        #endregion

        #region Public API
        /// <summary>
        /// Generates the height of every grid point.
        /// </summary>
        /// <param name="gridSize">Grid points along each side.</param>
        /// <param name="spacing">World units between neighbouring grid points.</param>
        /// <param name="seed">Random seed: the same seed always gives the same desert.</param>
        /// <returns>gridSize * gridSize heights in world units, row by row (index = z * gridSize + x).</returns>
        public static float[] Generate(int gridSize, float spacing, int seed)
        {
            var noise = new Noise(seed);
            float[] heights = new float[gridSize * gridSize];

            // The grid is centred on the origin, so the oasis sits at world (0, 0)
            float halfSize = (gridSize - 1) * spacing * 0.5f;

            // Every row is independent of the others, so the rows are shared between the CPU's cores
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
        /// Box blur that averages each point with the (2 * radius + 1) x (2 * radius + 1) square around it,
        /// done in two passes (rows, then columns) with a running sum.
        ///
        /// This replaces the original neighbour loop, which re-read the whole 8x8 window for every vertex
        /// (64 reads each, over 268 million for a 2048 x 2048 map). Here:
        ///  - Blurring rows then columns gives the same result as the square, but each pass only looks along one line.
        ///  - The running sum slides along the line: each step adds the value entering the window and removes the one
        ///    leaving it. That is 4 reads per point in total, whatever the radius.
        ///  - The window is centred (-radius..+radius), unlike the original's -4..+3, so the terrain doesn't shift.
        ///  - Edges repeat their last value instead of being skipped, so the border is smoothed too.
        ///  - The result is written back into the same array, so collision and placement use the smoothed surface.
        /// </summary>
        /// <param name="heights">The heights, row by row. Smoothed in place.</param>
        /// <param name="gridSize">Grid points along each side.</param>
        /// <param name="radius">How many points either side to average. 0 does nothing.</param>
        public static void Smooth(float[] heights, int gridSize, int radius)
        {
            if (radius <= 0)
                return;

            float[] rowsBlurred = new float[heights.Length];

            // Pass 1, along each row (x): row z starts at z * gridSize, and neighbours are 1 apart
            Parallel.For(0, gridSize, z => BlurLine(heights, rowsBlurred, z * gridSize, 1, gridSize, radius));

            // Pass 2, down each column (z): column x starts at x, and neighbours are a whole row (gridSize) apart
            Parallel.For(0, gridSize, x => BlurLine(rowsBlurred, heights, x, gridSize, gridSize, radius));
        }

        /// <summary>
        /// The shape of a crater across its radius, as a fraction of its depth: a smooth bowl (-1 at the centre,
        /// 0 at the edge) plus a raised rim of sand around the edge. Used for the oasis basin and for runtime craters.
        /// </summary>
        /// <param name="distance">Distance from the centre as a fraction of the radius (1 = the bowl's edge).</param>
        public static float CraterShape(float distance)
        {
            float bowl = 0f;
            if (distance < 1f)
            {
                float t = 1f - distance * distance;
                bowl = -t * t; // (1 - d^2)^2 has zero slope at the edge, so the bowl meets the rim without a crease
            }

            // A bump centred on the edge, a quarter of the depth high, fading out to almost nothing by 2x the radius
            float rimOffset = (distance - 1f) / 0.3f;
            float rim = 0.25f * MathF.Exp(-rimOffset * rimOffset);

            return bowl + rim;
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// The desert's height at one world position: rolling base + dunes, flattened and sunk around the oasis.
        /// </summary>
        private static float SampleDesert(Noise noise, float x, float z)
        {
            // Rolling base, moved from -1..1 to 0..1 so the open desert never dips below the water level
            float height = (noise.Fbm(x / BASESCALE, z / BASESCALE, 3) * 0.5f + 0.5f) * BASEHEIGHT;

            // Dune fields: how strong each dune layer is rises and falls across the map (0..1), so some areas have
            // tall dunes, some small ripples, and some are nearly flat, instead of one even pattern everywhere
            float mainStrength = SmoothStep(DUNEFIELDSTART, DUNEFIELDFULL, noise.Fbm(x / 500f + 31.7f, z / 500f - 12.3f, 2));
            float smallStrength = SmoothStep(-0.2f, 0.6f, noise.Fbm(x / 300f - 71.1f, z / 300f + 5.9f, 2));
            height += BlendedDunes(noise, x, z) * MAINHEIGHT * mainStrength;
            height += DuneRidge(noise, x + 913f, z - 377f, SmallWind, SMALLWAVELENGTH) * SMALLHEIGHT * smallStrength;

            // Oasis: fade the dunes out towards the centre, then sink a giant crater into the flattened sand
            float distance = MathF.Sqrt(x * x + z * z);
            height *= SmoothStep(FLATRADIUS, DUNERADIUS, distance);
            height += CraterShape(distance / BASINRADIUS) * BASINDEPTH;

            return height;
        }

        /// <summary>
        /// One layer of dunes, 0..1: parallel ridges lying across the wind direction, with a gentle windward slope
        /// and a steep slip face, like real sand dunes. The crests are bent by noise so they meander.
        /// </summary>
        /// <param name="noise">The noise used to bend the crests.</param>
        /// <param name="x">World x.</param>
        /// <param name="z">World z.</param>
        /// <param name="wind">Normalised direction the wind blows (the dunes face across it).</param>
        /// <param name="wavelength">Distance between crests, in world units.</param>
        private static float DuneRidge(Noise noise, float x, float z, Vector2 wind, float wavelength)
        {
            // How many wavelengths along the wind this point is, bent by noise so the crests aren't straight lines
            float along = (x * wind.X + z * wind.Y) / wavelength;
            along += noise.Fbm(x / WARPSCALE, z / WARPSCALE, 2) * WARPSTRENGTH;

            // Where this point is within one dune, 0..1
            float t = along - MathF.Floor(along);

            // Up the long windward side to the crest, then down the short, steep slip face
            if (t < WINDWARDFRACTION)
                return SmoothStep(0f, 1f, t / WINDWARDFRACTION);
            return SmoothStep(0f, 1f, (1f - t) / (1f - WINDWARDFRACTION));
        }

        /// <summary>
        /// Blurs one line (a row or a column) of the grid with a running sum.
        /// </summary>
        /// <param name="source">Values to read.</param>
        /// <param name="destination">Where to write the blurred values.</param>
        /// <param name="start">Index of the line's first value.</param>
        /// <param name="stride">Distance between neighbouring values on the line (1 for a row, gridSize for a column).</param>
        /// <param name="count">Number of values on the line.</param>
        /// <param name="radius">How many values either side to average.</param>
        private static void BlurLine(float[] source, float[] destination, int start, int stride, int count, int radius)
        {
            int windowSize = 2 * radius + 1;

            // Fill the window for the first value. Indexes past the ends are clamped, so edge values repeat.
            float sum = 0f;
            for (int k = -radius; k <= radius; k++)
                sum += source[start + Math.Clamp(k, 0, count - 1) * stride];

            for (int i = 0; i < count; i++)
            {
                destination[start + i * stride] = sum / windowSize;

                // Slide the window one step: add the value coming in at the front, remove the one leaving at the back
                int entering = Math.Clamp(i + radius + 1, 0, count - 1);
                int leaving = Math.Clamp(i - radius, 0, count - 1);
                sum += source[start + entering * stride] - source[start + leaving * stride];
            }
        }

        /// <summary>
        /// 0 below edge0, 1 above edge1, and a smooth S-curve between (the same as GLSL's smoothstep).
        /// </summary>
        private static float SmoothStep(float edge0, float edge1, float value)
        {
            float t = Math.Clamp((value - edge0) / (edge1 - edge0), 0f, 1f);
            return t * t * (3f - 2f * t);
        }
        
        /// <summary>
        /// The main dunes, 0..1, blended between several wind directions.
        /// Each wind gets a weight from its own noise pattern (0 in some regions, 1 in others), and the dunes are the
        /// weighted average of one ridge pattern per wind. Inside a region one wind dominates, so the dunes face its way;
        /// where regions meet, the patterns cross over, which looks like the crossing dunes real deserts have.
        /// </summary>
        private static float BlendedDunes(Noise noise, float x, float z)
        {
            float total = 0f;
            float totalWeight = 0f;

            for (int i = 0; i < MainWinds.Length; i++)
            {
                // A different noise pattern per wind (offset by i), so the regions don't line up
                float regionNoise = noise.Fbm(x / WINDREGIONSCALE + i * 47.3f, z / WINDREGIONSCALE - i * 19.1f, 2);
                float weight = SmoothStep(-0.2f, 0.4f, regionNoise) + 0.001f; // the tiny extra stops dividing by 0

                total += DuneRidge(noise, x + i * 311f, z - i * 173f, MainWinds[i], MAINWAVELENGTH) * weight;
                totalWeight += weight;
            }

            return total / totalWeight;
        }
        #endregion
    }
}
