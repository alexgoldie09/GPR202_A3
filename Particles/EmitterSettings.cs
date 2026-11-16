using OpenTK.Mathematics;

namespace GPR202_A3.Particles
{
    /// <summary>
    /// Everything that makes one kind of particle effect look the way it does. Most values are a range,
    /// and each new particle picks a random value inside it, so no two particles are the same.
    /// </summary>
    internal class EmitterSettings
    {
        #region Public Fields
        // --- How many ---

        /// <summary>
        /// The most particles alive at once. New ones are skipped while it's full.
        /// </summary>
        public int MaxParticles { get; init; } = 100;

        /// <summary>
        /// Particles per second while the emitter is enabled (0 = bursts only).
        /// </summary>
        public float EmitRate { get; init; }

        /// <summary>
        /// Particles per Burst().
        /// </summary>
        public int BurstCount { get; init; }

        // --- Life ---

        /// <summary>
        /// Shortest life, in seconds.
        /// </summary>
        public float MinLife { get; init; } = 1f;

        /// <summary>
        /// Longest life, in seconds.
        /// </summary>
        public float MaxLife { get; init; } = 1f;

        // --- Movement ---

        /// <summary>
        /// Random distance from the emitter they're born at (inside a sphere this size).
        /// </summary>
        public float SpawnRadius { get; init; }

        /// <summary>
        /// The main direction they're thrown in. The emitter can change it (the exhaust points backwards).
        /// </summary>
        public Vector3 Direction { get; init; } = Vector3.UnitY;

        /// <summary>
        /// How far from Direction they can be thrown, in degrees (0 = exactly along it, 180 = any direction).
        /// </summary>
        public float Spread { get; init; }

        /// <summary>
        /// Slowest starting speed, in units per second.
        /// </summary>
        public float MinSpeed { get; init; }

        /// <summary>
        /// Fastest starting speed.
        /// </summary>
        public float MaxSpeed { get; init; }

        /// <summary>
        /// Constant push, in units per second per second: gravity (down) or hot air rising (up).
        /// </summary>
        public Vector3 Acceleration { get; init; }

        /// <summary>
        /// How quickly they slow down from air resistance (0 = never, larger = sooner).
        /// </summary>
        public float Drag { get; init; }

        // --- Look ---

        /// <summary>
        /// Smallest starting size, in world units.
        /// </summary>
        public float MinSize { get; init; } = 1f;

        /// <summary>
        /// Largest starting size.
        /// </summary>
        public float MaxSize { get; init; } = 1f;

        /// <summary>
        /// Size change per second (smoke spreads out as it rises).
        /// </summary>
        public float Growth { get; init; }

        /// <summary>
        /// Fastest spin, in degrees per second (each particle picks a random speed and direction up to this).
        /// </summary>
        public float MaxSpin { get; init; }

        /// <summary>
        /// Starting colours. Each particle picks one at random (confetti uses several, smoke just one).
        /// </summary>
        public Color4[] Colors { get; init; } = [Color4.White];

        /// <summary>
        /// The colour each particle ends at, as a multiple of its starting colour.
        /// Alpha 0 fades it out; (1, 1, 1, 0) keeps the colour and only fades.
        /// </summary>
        public Color4 EndTint { get; init; } = new(1f, 1f, 1f, 0f);

        /// <summary>
        /// How far through its life the change to the end colour starts (0 = from birth, 0.7 = the last 30%).
        /// </summary>
        public float FadeStart { get; init; }

        /// <summary>
        /// Shapes to pick from at random.
        /// </summary>
        public ParticleSprite[] Sprites { get; init; } = [ParticleSprite.SoftDot];

        // --- Drawing ---

        /// <summary>
        /// True: the quads always face the camera (fire, smoke). False: they tumble in 3D (confetti).
        /// </summary>
        public bool Billboard { get; init; } = true;

        /// <summary>
        /// True: colours add up and glow, and no sorting is needed (fire). False: normal see-through blending,
        /// sorted back to front (smoke, confetti).
        /// </summary>
        public bool Additive { get; init; }

        /// <summary>
        /// What happens when they reach the ground or the water.
        /// </summary>
        public GroundContact Ground { get; init; }
        #endregion
    }
}
