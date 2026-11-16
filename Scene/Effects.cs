using GPR202_A3.Graphics;
using GPR202_A3.Particles;
using OpenTK.Mathematics;

namespace GPR202_A3.Scene
{
    /// <summary>
    /// The game's particle effects: confetti when a balloon pops, fire and smoke when the plane crashes,
    /// and a smoke trail behind the plane. Game calls BalloonPop() and Crash(); the trail follows the plane by itself.
    /// </summary>
    internal class Effects
    {
        #region Constants
        // Gravity, in units per second per second
        private static readonly Vector3 Gravity = new(0f, -9.8f, 0f);

        // Balloon pop: a burst of paper confetti that tumbles down and settles on the sand (or floats on the oasis)
        private static readonly EmitterSettings Confetti = new()
        {
            MaxParticles = 600, BurstCount = 120,
            MinLife = 4f, MaxLife = 6f,
            SpawnRadius = 2f, Direction = Vector3.UnitY, Spread = 70f, MinSpeed = 8f, MaxSpeed = 18f,
            Acceleration = Gravity, Drag = 1.2f,
            MinSize = 0.5f, MaxSize = 0.9f, MaxSpin = 540f,
            Colors = [Color4.Red, Color4.Gold, Color4.DodgerBlue, Color4.LimeGreen, Color4.Magenta, Color4.Orange],
            EndTint = new Color4(1f, 1f, 1f, 0f), FadeStart = 0.7f, // keep the colour, fade out at the end
            Sprites = [ParticleSprite.Square],
            Billboard = false, Additive = false, Ground = GroundContact.Settle
        };

        // Crash: a quick flash of fire, glowing (additive) and fading to dark red
        private static readonly EmitterSettings Fire = new()
        {
            MaxParticles = 300, BurstCount = 80,
            MinLife = 0.6f, MaxLife = 1.2f,
            SpawnRadius = 2f, Direction = Vector3.UnitY, Spread = 90f, MinSpeed = 6f, MaxSpeed = 16f,
            Acceleration = new Vector3(0f, 4f, 0f), Drag = 2f, // hot, so it rises
            MinSize = 2.5f, MaxSize = 4.5f, Growth = 2f, MaxSpin = 90f,
            Colors = [new Color4(1f, 0.9f, 0.5f, 1f), new Color4(1f, 0.6f, 0.2f, 1f)],
            EndTint = new Color4(0.8f, 0.2f, 0.05f, 0f),
            Sprites = [ParticleSprite.SoftDot, ParticleSprite.Spark],
            Billboard = true, Additive = true, Ground = GroundContact.Kill
        };

        // Crash: dark smoke that rises, spreads out and thins
        private static readonly EmitterSettings Smoke = new()
        {
            MaxParticles = 300, BurstCount = 50,
            MinLife = 2.5f, MaxLife = 5f,
            SpawnRadius = 2f, Direction = Vector3.UnitY, Spread = 60f, MinSpeed = 2f, MaxSpeed = 6f,
            Acceleration = new Vector3(0f, 2.5f, 0f), Drag = 0.8f,
            MinSize = 3f, MaxSize = 5f, Growth = 3f, MaxSpin = 30f,
            Colors = [new Color4(0.25f, 0.23f, 0.2f, 0.75f)],
            EndTint = new Color4(1.6f, 1.6f, 1.6f, 0f), // lightens as it thins
            Sprites = [ParticleSprite.Smoke],
            Billboard = true, Additive = false, Ground = GroundContact.None
        };

        // The plane's trail: a steady stream of light smoke from the tail
        private static readonly EmitterSettings Exhaust = new()
        {
            MaxParticles = 300, EmitRate = 30f,
            MinLife = 1.5f, MaxLife = 2.5f,
            Spread = 8f, MinSpeed = 2f, MaxSpeed = 4f, // Direction is set every frame to point backwards
            Acceleration = new Vector3(0f, 0.8f, 0f), Drag = 0.5f,
            MinSize = 1.6f, MaxSize = 1.9f, Growth = 2.5f, MaxSpin = 40f,
            Colors = [new Color4(0.85f, 0.85f, 0.85f, 0.4f)],
            Sprites = [ParticleSprite.Smoke],
            Billboard = true, Additive = false, Ground = GroundContact.None
        };

        // Where the two trails start, on the plane model (its own units, as in Blender; the nose points along +Z):
        // behind the rocket racks under each wing
        private static readonly Vector3 LeftExhaustPoint = new(-3.2f, -0.2f, -1.2f);
        private static readonly Vector3 RightExhaustPoint = new(3.2f, -0.2f, -1.2f);

        // Backwards along the model, the way the smoke is blown
        private static readonly Vector3 ModelBackward = -Vector3.UnitZ;

        // How far above the ground a crash's fire and smoke start, so they don't begin inside the sand
        private const float CRASHLIFT = 1f;
        #endregion

        #region Public Fields
        /// <summary>
        /// Particles alive across every effect.
        /// </summary>
        public int AliveCount => _system.AliveCount;

        /// <summary>
        /// Particle draw calls last frame.
        /// </summary>
        public int DrawCalls => _system.DrawCalls;
        #endregion

        #region Private Fields
        private readonly ParticleSystem _system = new();
        private readonly ParticleEmitter _confetti;
        private readonly ParticleEmitter _fire;
        private readonly ParticleEmitter _smoke;
        private readonly ParticleEmitter _leftExhaust;
        private readonly ParticleEmitter _rightExhaust;
        #endregion

        #region Constructors
        /// <summary>
        /// Constructor that makes an emitter for each effect.
        /// </summary>
        public Effects()
        {
            _confetti = _system.Add(Confetti);
            _fire = _system.Add(Fire);
            _smoke = _system.Add(Smoke);
            _leftExhaust = _system.Add(Exhaust);
            _rightExhaust = _system.Add(Exhaust);
            _leftExhaust.Enabled = true;
            _rightExhaust.Enabled = true;
        }
        #endregion

        #region Public API
        /// <summary>
        /// Confetti bursting out where a balloon was.
        /// </summary>
        public void BalloonPop(Vector3 position) => _confetti.Burst(position);

        /// <summary>
        /// A fireball and a cloud of smoke where the plane crashed.
        /// </summary>
        public void Crash(Vector3 position)
        {
            // A crash point can be slightly inside a dune, and the fire dies on touching the ground,
            // so it starts on the surface (or the water) instead
            float surface = MathF.Max(Terrain.GetHeightAt(position.X, position.Z), Terrain.WaterLevel);
            position.Y = MathF.Max(position.Y, surface + CRASHLIFT);

            _fire.Burst(position);
            _smoke.Burst(position);
        }

        /// <summary>
        /// Keeps the trail at the plane's tail, then updates every particle. Call every update.
        /// </summary>
        /// <param name="plane">The plane to trail behind (null = no trail).</param>
        /// <param name="deltaTime">Seconds since the last update.</param>
        public void Update(Plane? plane, float deltaTime)
        {
            _leftExhaust.Enabled = plane != null;
            _rightExhaust.Enabled = plane != null;
            if (plane != null)
            {
                // Points on the model, so the trails tilt with the wings when the plane banks and pitches
                Vector3 backward = plane.GetWorldDirection(ModelBackward);
                _leftExhaust.Position = plane.GetWorldPoint(LeftExhaustPoint);
                _rightExhaust.Position = plane.GetWorldPoint(RightExhaustPoint);
                _leftExhaust.Direction = backward;
                _rightExhaust.Direction = backward;
            }

            _system.Update(deltaTime);
        }

        /// <summary>
        /// Draws every effect. Call after the scene, the water and the sky.
        /// </summary>
        public void Render(Matrix4 view, Vector3 cameraPosition) => _system.Render(view, cameraPosition);

        /// <summary>
        /// Uploads the projection. Call on load and on resize.
        /// </summary>
        public void SetProjection(Matrix4 projection) => _system.SetProjection(projection);

        /// <summary>
        /// Uploads the fog settings. Call on load, and again when the fog mode changes.
        /// </summary>
        public void SetFog(Fog fog) => _system.SetFog(fog);

        /// <summary>
        /// Removes every particle (e.g. on a new game), and moves the trail to the plane without drawing a line
        /// from where it was.
        /// </summary>
        public void Clear(Plane? plane)
        {
            _system.Clear();
            if (plane != null)
            {
                _leftExhaust.Teleport(plane.GetWorldPoint(LeftExhaustPoint));
                _rightExhaust.Teleport(plane.GetWorldPoint(RightExhaustPoint));
            }
        }

        /// <summary>
        /// Frees the particle system's GPU resources.
        /// </summary>
        public void Delete() => _system.Delete();
        #endregion
    }
}
