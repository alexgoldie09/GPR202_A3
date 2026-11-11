using OpenTK.Mathematics;

namespace GPR202_A3.Scene
{
    /// <summary>
    /// One balloon in the game: where it is, how it is spinning, and whether it has been collected.
    /// The model itself is shared by every balloon (see Balloon).
    ///
    /// A collected balloon plays a short animation (it shrinks, rises and tumbles) and then reports Finished,
    /// so the game can remove it.
    /// </summary>
    internal class BalloonState
    {
        #region Constants
        private const float DEFAULTSCALE = 2f;
        private const float ROTATESPEED = 1f; // idle spin, radians per second

        // Spawn height above the terrain (or the water), in units
        private const int MINSPAWNHEIGHT = 15;
        private const int MAXSPAWNHEIGHT = 65;

        // How far in from the terrain's edges balloons may spawn, in units
        private const float SPAWNMARGIN = 100f;

        // Collection: the plane collects a balloon when it comes within COLLECTRADIUS of the balloon's centre,
        // which sits CENTREHEIGHT above its base (the model's origin is at the basket)
        private const float COLLECTRADIUS = 15f;
        private const float CENTREHEIGHT = 5f;

        // Collection animation
        private const float COLLECTDURATION = 0.25f; // seconds (the original animated at 4x speed over 0..1)
        private const float RISESPEED = 64f; // units per second
        private const float TUMBLESPEED = 64f; // radians per second around X and Z
        #endregion

        #region Public Fields
        /// <summary>
        /// Shared random number generator, so balloons spawned in the same frame don't get the same seed.
        /// </summary>
        public static readonly Random Random = new();

        /// <summary>
        /// Where the balloon's base is in the world.
        /// </summary>
        public Vector3 Position { get; private set; }

        /// <summary>
        /// The balloon's rotation as Euler angles, in radians.
        /// </summary>
        public Vector3 Rotation { get; private set; }

        /// <summary>
        /// The balloon's size (DEFAULTSCALE when idle, shrinking to 0 once collected).
        /// </summary>
        public float Scale { get; private set; } = DEFAULTSCALE;

        /// <summary>
        /// Whether the plane has collected this balloon.
        /// </summary>
        public bool Collected { get; private set; }

        /// <summary>
        /// Whether the collection animation has finished, so the balloon can be removed.
        /// </summary>
        public bool Finished { get; private set; }
        #endregion

        #region Private Fields
        // Progress through the collection animation, 0 to 1
        private float _animate;
        #endregion

        #region Public API
        /// <summary>
        /// Resets the balloon and places it at a random spot above the terrain (or above the water, over the oasis).
        /// </summary>
        public void Spawn()
        {
            // A random world position, kept SPAWNMARGIN in from the edges
            float range = Terrain.Size * 0.5f - SPAWNMARGIN;
            float x = (float)(Random.NextDouble() * 2.0 - 1.0) * range;
            float z = (float)(Random.NextDouble() * 2.0 - 1.0) * range;
            float height = Random.Next(MINSPAWNHEIGHT, MAXSPAWNHEIGHT);

            float ground = MathF.Max(Terrain.GetHeightAt(x, z), Terrain.WaterLevel);
            Position = new Vector3(x, ground + height, z);

            // The original passed a whole number of degrees (0..358) straight in as radians;
            // this picks a random angle in radians instead.
            Rotation = new Vector3(0f, (float)(Random.NextDouble() * MathHelper.TwoPi), 0f);

            Scale = DEFAULTSCALE;
            Collected = false;
            Finished = false;
            _animate = 0f;
        }

        /// <summary>
        /// Spins the balloon, checks whether the plane has reached it, and plays the collection animation.
        /// </summary>
        /// <param name="planePosition">The plane's position.</param>
        /// <param name="deltaTime">Seconds since the last update.</param>
        public void Update(Vector3 planePosition, float deltaTime)
        {
            if (Finished)
                return;

            if (!Collected)
            {
                Rotation += new Vector3(0f, deltaTime * ROTATESPEED, 0f);

                // Distance to the balloon's centre rather than its base
                Vector3 centre = Position + new Vector3(0f, CENTREHEIGHT, 0f);
                if (Vector3.Distance(centre, planePosition) < COLLECTRADIUS)
                    Collected = true;

                return;
            }

            // Collected: shrink, rise and tumble over COLLECTDURATION seconds
            _animate = MathHelper.Clamp(_animate + deltaTime / COLLECTDURATION, 0f, 1f);
            Scale = DEFAULTSCALE * (1f - _animate);
            Position += new Vector3(0f, deltaTime * RISESPEED, 0f);
            Rotation += new Vector3(deltaTime * TUMBLESPEED, 0f, deltaTime * TUMBLESPEED);

            if (_animate >= 1f)
                Finished = true;
        }
        #endregion
    }
}
