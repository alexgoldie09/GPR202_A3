using OpenTK.Mathematics;

namespace GPR202_A3.Particles
{
    /// <summary>
    /// One particle. A struct kept in a fixed array by its emitter and changed in place every frame,
    /// so spawning and updating particles never allocates memory.
    /// Fields rather than properties, so the emitter can change them directly in the array.
    /// </summary>
    internal struct Particle
    {
        #region Public Fields
        /// <summary>
        /// World position of the particle's centre.
        /// </summary>
        public Vector3 Position;

        /// <summary>
        /// Units per second.
        /// </summary>
        public Vector3 Velocity;

        /// <summary>
        /// Width of the quad, in world units.
        /// </summary>
        public float Size;

        /// <summary>
        /// How much the size changes per second (smoke spreads out).
        /// </summary>
        public float Growth;

        /// <summary>
        /// Current rotation, in radians.
        /// </summary>
        public float Angle;

        /// <summary>
        /// Radians per second.
        /// </summary>
        public float Spin;

        /// <summary>
        /// The axis it tumbles around. Only used by emitters that don't billboard (confetti);
        /// billboards always spin around the direction the camera is looking.
        /// </summary>
        public Vector3 SpinAxis;

        /// <summary>
        /// Colour when it's born.
        /// </summary>
        public Color4 StartColor;

        /// <summary>
        /// Colour when it dies (usually with alpha 0, so it fades out).
        /// </summary>
        public Color4 EndColor;

        /// <summary>
        /// Seconds since it was born.
        /// </summary>
        public float Age;

        /// <summary>
        /// Seconds it lives for.
        /// </summary>
        public float Lifespan;

        /// <summary>
        /// Which shape it's drawn with.
        /// </summary>
        public ParticleSprite Sprite;

        /// <summary>
        /// Whether it has landed (GroundContact.Settle): it stops moving but keeps fading.
        /// </summary>
        public bool Grounded;
        #endregion
    }
}
