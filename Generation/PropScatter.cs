using GPR202_A3.Scene;
using OpenTK.Mathematics;

namespace GPR202_A3.Generation
{
    /// <summary>
    /// Places props on the terrain with a seeded random number generator, following each prop's ScatterRules:
    /// pick a random spot the right distance from the oasis, and keep it only if it's on the map, above the water,
    /// not too steep, and not too close to anything already placed.
    /// </summary>
    internal static class PropScatter
    {
        #region Constants
        // Random spots tried per prop before giving up on it
        private const int ATTEMPTSPERPROP = 30;

        // Keeps props this far in from the edges of the terrain
        private const float EDGEMARGIN = 40f;

        // Distance either side used to measure the slope
        private const float SLOPESTEP = 2f;
        #endregion

        #region Public API
        /// <summary>
        /// Places one kind of prop.
        /// </summary>
        /// <param name="rules">Where it can go, and how big and rotated it can be.</param>
        /// <param name="random">The seeded random number generator (shared, so the result is repeatable).</param>
        /// <param name="placed">Everything placed so far (x and z, and its spacing). New props are added to it.</param>
        /// <returns>One model matrix per prop placed.</returns>
        public static List<Matrix4> Scatter(ScatterRules rules, Random random, List<(Vector2 Position, float Spacing)> placed)
        {
            var instances = new List<Matrix4>();
            float range = Terrain.Size / 2f - EDGEMARGIN;

            for (int attempt = 0; attempt < rules.Count * ATTEMPTSPERPROP && instances.Count < rules.Count; attempt++)
            {
                // A random spot in the ring between MinDistance and MaxDistance from the oasis
                Vector2 spot = RandomSpotInRing(random, rules.MinDistance, rules.MaxDistance);

                // On the terrain (the outer ring can reach past the edges)
                if (MathF.Abs(spot.X) > range || MathF.Abs(spot.Y) > range)
                    continue;

                // Above the water
                float ground = Terrain.GetHeightAt(spot.X, spot.Y);
                if (ground < Terrain.WaterLevel + rules.MinHeightAboveWater)
                    continue;

                // Not too steep
                if (SlopeAt(spot) > rules.MaxSlope)
                    continue;

                // Not too close to anything already placed (each side keeps half the gap)
                if (!HasSpace(spot, rules.Spacing, placed))
                    continue;

                placed.Add((spot, rules.Spacing));
                instances.Add(CreateTransform(rules, random, spot, ground));
            }

            return instances;
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Builds a prop's model matrix: random size, random turn, a small random lean, sunk slightly into the ground.
        /// </summary>
        private static Matrix4 CreateTransform(ScatterRules rules, Random random, Vector2 spot, float ground)
        {
            float scale = RandomBetween(random, rules.MinScale, rules.MaxScale);

            // Turn around Y, then lean around a random flat axis
            float yaw = RandomBetween(random, 0f, MathHelper.TwoPi);
            float tilt = MathHelper.DegreesToRadians(RandomBetween(random, 0f, rules.MaxTilt));
            float tiltDirection = RandomBetween(random, 0f, MathHelper.TwoPi);
            var tiltAxis = new Vector3(MathF.Cos(tiltDirection), 0f, MathF.Sin(tiltDirection));
            Quaternion rotation = Quaternion.FromAxisAngle(tiltAxis, tilt) * Quaternion.FromAxisAngle(Vector3.UnitY, yaw);

            var position = new Vector3(spot.X, ground - rules.SinkDepth * scale, spot.Y);

            // Row-vector order: scale, then rotate, then move
            return Matrix4.CreateScale(scale) * Matrix4.CreateFromQuaternion(rotation) * Matrix4.CreateTranslation(position);
        }

        /// <summary>
        /// A random spot in a ring around the centre of the map, spread evenly over its area.
        /// </summary>
        private static Vector2 RandomSpotInRing(Random random, float minDistance, float maxDistance)
        {
            float angle = RandomBetween(random, 0f, MathHelper.TwoPi);

            // Picking the squared distance evenly (then taking the root) stops spots bunching near the middle,
            // since the outer part of the ring has more area
            float distance = MathF.Sqrt(RandomBetween(random, minDistance * minDistance, maxDistance * maxDistance));

            return new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
        }

        /// <summary>
        /// The slope of the terrain at a spot, in degrees (0 = flat).
        /// </summary>
        private static float SlopeAt(Vector2 spot)
        {
            float left = Terrain.GetHeightAt(spot.X - SLOPESTEP, spot.Y);
            float right = Terrain.GetHeightAt(spot.X + SLOPESTEP, spot.Y);
            float down = Terrain.GetHeightAt(spot.X, spot.Y - SLOPESTEP);
            float up = Terrain.GetHeightAt(spot.X, spot.Y + SLOPESTEP);

            // The same normal the terrain's vertex shader works out
            Vector3 normal = Vector3.Normalize(new Vector3(left - right, 2f * SLOPESTEP, down - up));
            return MathHelper.RadiansToDegrees(MathF.Acos(normal.Y));
        }

        /// <summary>
        /// Whether a spot is far enough from everything already placed.
        /// </summary>
        private static bool HasSpace(Vector2 spot, float spacing, List<(Vector2 Position, float Spacing)> placed)
        {
            foreach (var other in placed)
            {
                float needed = (spacing + other.Spacing) / 2f;
                if (Vector2.DistanceSquared(spot, other.Position) < needed * needed)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// A random number between min and max.
        /// </summary>
        private static float RandomBetween(Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);
        #endregion
    }
}
