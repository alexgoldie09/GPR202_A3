using GPR202_A3.Graphics;
using GPR202_A3.Scene;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GPR202_A3.Particles
{
    /// <summary>
    /// Spawns, moves and draws one kind of particle (confetti, fire, smoke, exhaust).
    /// It emits continuously while Enabled, or all at once with Burst().
    /// Live particles are kept packed at the front of a fixed array, and only those are uploaded and drawn,
    /// as instances of one quad (one draw call per emitter).
    /// </summary>
    internal class ParticleEmitter
    {
        #region Constants
        // Per-instance data sent to the GPU: position and size, colour, spin axis and angle, sprite
        private const int POSITIONSIZELOCATION = 1;
        private const int COLORLOCATION = 2;
        private const int ROTATIONLOCATION = 3;
        private const int SPRITELOCATION = 4;
        private const int VEC4SIZE = 4;
        private const int SPRITESIZE = 1;
        private const int FLOATSPERINSTANCE = VEC4SIZE * 3 + SPRITESIZE;

        // Attribute divisor: move to the next instance's values once per particle (not per vertex)
        private const int ONEPERINSTANCE = 1;

        // The quad is a 4-corner triangle strip, each corner an (x, y) pair
        private const int QUADCORNERS = 4;
        private const int CORNERSIZE = 2;

        // Spread that allows any direction (a full sphere)
        private const float ANYDIRECTION = 180f;

        // Above this, a direction counts as straight up or down (so a different helper axis is used)
        private const float NEARLYVERTICAL = 0.99f;

        // Landed particles sit this far above the ground, so they don't flicker into the sand
        private const float GROUNDLIFT = 0.05f;

        // A landed tumbling quad turns to lie flat (its quad is drawn in the XY plane, so 90 degrees around X)
        private static readonly Vector3 FlatAxis = Vector3.UnitX;
        private const float FLATANGLE = MathHelper.PiOver2;
        #endregion

        #region Public Fields
        /// <summary>
        /// The settings this emitter was made with.
        /// </summary>
        public EmitterSettings Settings { get; }

        /// <summary>
        /// Where new particles are born (move it to follow something, like the plane's tail).
        /// </summary>
        public Vector3 Position { get; set; }


        /// <summary>
        /// The direction new particles are thrown in. Starts as Settings.Direction.
        /// </summary>
        public Vector3 Direction { get; set; }

        /// <summary>
        /// Whether it emits continuously (at Settings.EmitRate).
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// How many particles are alive.
        /// </summary>
        public int AliveCount { get; private set; }
        #endregion

        #region Private Fields
        // Live particles are [0, AliveCount); the rest are free slots
        private readonly Particle[] _particles;

        // Reused every frame for the upload (it never grows, so there are no per-frame allocations)
        private readonly float[] _instanceData;

        private readonly Random _random;
        private readonly int _vao;
        private readonly int _instanceVbo;

        // Time carried over between frames, so the emit rate is exact even when the frame rate isn't
        private float _emitTimer;

        // Where the emitter was last update (continuous particles are spread along the way it moved)
        private Vector3 _previousPosition;

        // Sorts far to near for see-through blending
        private readonly DistanceComparer _comparer = new();
        #endregion

        #region Constructors
        /// <summary>
        /// Constructor that makes the particle array and this emitter's GPU buffers.
        /// </summary>
        /// <param name="settings">How the particles look and move.</param>
        /// <param name="quadVbo">The shared quad corners (made by ParticleSystem).</param>
        /// <param name="quadLocation">The attribute location of the quad corners.</param>
        /// <param name="random">The random number generator to pick values with.</param>
        public ParticleEmitter(EmitterSettings settings, int quadVbo, int quadLocation, Random random)
        {
            Settings = settings;
            Direction = settings.Direction;
            _random = random;
            _particles = new Particle[settings.MaxParticles];
            _instanceData = new float[settings.MaxParticles * FLOATSPERINSTANCE];

            _vao = GL.GenVertexArray();
            GL.BindVertexArray(_vao);

            // The quad's corners, the same for every particle
            GL.BindBuffer(BufferTarget.ArrayBuffer, quadVbo);
            GL.VertexAttribPointer(quadLocation, CORNERSIZE, VertexAttribPointerType.Float, false, CORNERSIZE * sizeof(float), 0);
            GL.EnableVertexAttribArray(quadLocation);

            // One set of instance values per particle, sized for the most particles there can be
            _instanceVbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _instanceVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, _instanceData.Length * sizeof(float), IntPtr.Zero, BufferUsageHint.StreamDraw);

            int stride = FLOATSPERINSTANCE * sizeof(float);
            LinkInstanceAttribute(POSITIONSIZELOCATION, VEC4SIZE, stride, 0);
            LinkInstanceAttribute(COLORLOCATION, VEC4SIZE, stride, VEC4SIZE);
            LinkInstanceAttribute(ROTATIONLOCATION, VEC4SIZE, stride, VEC4SIZE * 2);
            LinkInstanceAttribute(SPRITELOCATION, SPRITESIZE, stride, VEC4SIZE * 3);

            GL.BindVertexArray(0);
        }
        #endregion

        #region Public API
        /// <summary>
        /// Moves the emitter without a trail between the old and new positions (e.g. the plane resetting).
        /// </summary>
        public void Teleport(Vector3 position)
        {
            Position = position;
            _previousPosition = position;
        }

        /// <summary>
        /// Emits Settings.BurstCount particles at once (a balloon pop, a crash).
        /// </summary>
        public void Burst(Vector3 position) => Burst(position, Settings.BurstCount);

        /// <summary>
        /// Emits a number of particles at once.
        /// </summary>
        public void Burst(Vector3 position, int count)
        {
            for (int i = 0; i < count; i++)
                Spawn(position);
        }

        /// <summary>
        /// Emits (if enabled), then moves and ages every particle, and removes the dead ones.
        /// </summary>
        public void Update(float deltaTime)
        {
            // --- Continuous emission ---
            if (Enabled && Settings.EmitRate > 0f)
            {
                float secondsPerParticle = 1f / Settings.EmitRate;
                _emitTimer += deltaTime;
                int count = (int)(_emitTimer / secondsPerParticle);
                _emitTimer -= count * secondsPerParticle;

                // Spread this frame's particles along the path the emitter moved since last frame,
                // so a moving emitter leaves an even trail instead of a clump per frame
                for (int k = 0; k < count; k++)
                    Spawn(Vector3.Lerp(_previousPosition, Position, (k + 1f) / count));
            }
            _previousPosition = Position;

            // --- Move and age ---
            // Air resistance: lose a fraction of the speed each second
            float dragFactor = 1f / (1f + Settings.Drag * deltaTime);

            int i = 0;
            while (i < AliveCount)
            {
                ref Particle p = ref _particles[i];
                p.Age += deltaTime;

                // Dead: move the last live particle into this slot, so the live ones stay packed at the front
                if (p.Age >= p.Lifespan)
                {
                    AliveCount--;
                    _particles[i] = _particles[AliveCount];
                    continue; // check the particle that was just moved here
                }

                p.Size = MathF.Max(0f, p.Size + p.Growth * deltaTime);

                if (!p.Grounded)
                {
                    p.Velocity = (p.Velocity + Settings.Acceleration * deltaTime) * dragFactor;
                    p.Position += p.Velocity * deltaTime;
                    p.Angle += p.Spin * deltaTime;

                    if (Settings.Ground != GroundContact.None)
                        CheckGround(ref p);
                }

                i++;
            }
        }

        /// <summary>
        /// Uploads the live particles and draws them. The ParticleSystem sets the shader and the blending first.
        /// </summary>
        /// <param name="shader">The bound particle shader.</param>
        /// <param name="cameraPosition">For sorting see-through particles far to near.</param>
        public void Render(ShaderProgram shader, Vector3 cameraPosition)
        {
            if (AliveCount == 0) return;

            // See-through particles have to be drawn far to near, or near ones hide the ones behind them.
            // Additive ones just add up, so their order doesn't matter.
            if (!Settings.Additive)
            {
                _comparer.CameraPosition = cameraPosition;
                Array.Sort(_particles, 0, AliveCount, _comparer);
            }

            // --- Pack the live particles ---
            int f = 0;
            for (int i = 0; i < AliveCount; i++)
            {
                ref Particle p = ref _particles[i];
                Color4 color = GetColor(p);

                _instanceData[f++] = p.Position.X;
                _instanceData[f++] = p.Position.Y;
                _instanceData[f++] = p.Position.Z;
                _instanceData[f++] = p.Size;
                _instanceData[f++] = color.R;
                _instanceData[f++] = color.G;
                _instanceData[f++] = color.B;
                _instanceData[f++] = color.A;
                _instanceData[f++] = p.SpinAxis.X;
                _instanceData[f++] = p.SpinAxis.Y;
                _instanceData[f++] = p.SpinAxis.Z;
                _instanceData[f++] = p.Angle;
                _instanceData[f++] = (float)p.Sprite;
            }

            // Only the live particles are uploaded and drawn
            GL.BindBuffer(BufferTarget.ArrayBuffer, _instanceVbo);
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, f * sizeof(float), _instanceData);

            shader.SetInt("Billboard", Settings.Billboard ? 1 : 0);
            GL.BindVertexArray(_vao);
            GL.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, QUADCORNERS, AliveCount);
            GL.BindVertexArray(0);
        }

        /// <summary>
        /// Removes every particle (e.g. on a new game).
        /// </summary>
        public void Clear()
        {
            AliveCount = 0;
            _emitTimer = 0f;
        }

        /// <summary>
        /// Frees the GPU buffers (the shared quad belongs to the ParticleSystem).
        /// </summary>
        public void Delete()
        {
            GL.DeleteBuffer(_instanceVbo);
            GL.DeleteVertexArray(_vao);
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Gives a new particle random starting values from the settings. Skipped if the array is full.
        /// </summary>
        private void Spawn(Vector3 position)
        {
            if (AliveCount >= _particles.Length) return;

            EmitterSettings s = Settings;
            Color4 start = s.Colors[_random.Next(s.Colors.Length)];

            _particles[AliveCount++] = new Particle
            {
                Position = position + RandomInsideSphere() * s.SpawnRadius,
                Velocity = RandomDirection(Direction, s.Spread) * RandomBetween(s.MinSpeed, s.MaxSpeed),
                Size = RandomBetween(s.MinSize, s.MaxSize),
                Growth = s.Growth,
                Angle = RandomBetween(0f, MathHelper.TwoPi),
                Spin = MathHelper.DegreesToRadians(RandomBetween(-s.MaxSpin, s.MaxSpin)),
                SpinAxis = RandomDirection(Vector3.UnitY, ANYDIRECTION),
                StartColor = start,
                EndColor = new Color4(start.R * s.EndTint.R, start.G * s.EndTint.G, start.B * s.EndTint.B, start.A * s.EndTint.A),
                Lifespan = RandomBetween(s.MinLife, s.MaxLife),
                Sprite = s.Sprites[_random.Next(s.Sprites.Length)]
            };
        }

        /// <summary>
        /// What a particle does if it has gone below the ground (or the water, which is higher in the oasis).
        /// </summary>
        private void CheckGround(ref Particle p)
        {
            float ground = MathF.Max(Terrain.GetHeightAt(p.Position.X, p.Position.Z), Terrain.WaterLevel);
            if (p.Position.Y > ground) return;

            // Kill: age it out, so it's removed next update
            if (Settings.Ground == GroundContact.Kill)
            {
                p.Age = p.Lifespan;
                return;
            }

            // Settle: stop on the surface and keep fading there
            p.Position.Y = ground + GROUNDLIFT;
            p.Velocity = Vector3.Zero;
            p.Spin = 0f;
            p.Grounded = true;

            // Lie flat on the ground
            p.SpinAxis = FlatAxis;
            p.Angle = FLATANGLE;
        }

        /// <summary>
        /// A particle's colour at its age: its start colour until FadeStart, then blending to its end colour.
        /// </summary>
        private Color4 GetColor(in Particle p)
        {
            float life = p.Age / p.Lifespan;
            float fade = Math.Clamp((life - Settings.FadeStart) / (1f - Settings.FadeStart), 0f, 1f);
            return new Color4(
                MathHelper.Lerp(p.StartColor.R, p.EndColor.R, fade),
                MathHelper.Lerp(p.StartColor.G, p.EndColor.G, fade),
                MathHelper.Lerp(p.StartColor.B, p.EndColor.B, fade),
                MathHelper.Lerp(p.StartColor.A, p.EndColor.A, fade));
        }

        /// <summary>
        /// A random direction within a cone around a main direction.
        /// </summary>
        /// <param name="direction">The middle of the cone.</param>
        /// <param name="spreadDegrees">The cone's half-angle (180 = any direction).</param>
        private Vector3 RandomDirection(Vector3 direction, float spreadDegrees)
        {
            // Picking the cosine of the angle evenly spreads the directions evenly over the cone's cap
            float minCos = MathF.Cos(MathHelper.DegreesToRadians(spreadDegrees));
            float cosAngle = RandomBetween(minCos, 1f);
            float sinAngle = MathF.Sqrt(1f - cosAngle * cosAngle);
            float around = RandomBetween(0f, MathHelper.TwoPi);

            // Two directions at right angles to the main one, to build the cone around it
            Vector3 forward = direction.Normalized();
            Vector3 helper = MathF.Abs(forward.Y) < NEARLYVERTICAL ? Vector3.UnitY : Vector3.UnitX;
            Vector3 side = Vector3.Cross(forward, helper).Normalized();
            Vector3 up = Vector3.Cross(side, forward);

            return forward * cosAngle + (side * MathF.Cos(around) + up * MathF.Sin(around)) * sinAngle;
        }

        /// <summary>
        /// A random point inside a sphere of radius 1.
        /// </summary>
        private Vector3 RandomInsideSphere()
        {
            // Cube root of the distance, so points spread evenly through the volume instead of bunching in the middle
            float distance = MathF.Cbrt((float)_random.NextDouble());
            return RandomDirection(Vector3.UnitY, ANYDIRECTION) * distance;
        }

        /// <summary>
        /// A random number between min and max.
        /// </summary>
        private float RandomBetween(float min, float max) => min + (float)_random.NextDouble() * (max - min);

        /// <summary>
        /// Links one per-instance attribute of the interleaved instance buffer.
        /// </summary>
        private static void LinkInstanceAttribute(int location, int size, int stride, int floatOffset)
        {
            GL.VertexAttribPointer(location, size, VertexAttribPointerType.Float, false, stride, floatOffset * sizeof(float));
            GL.EnableVertexAttribArray(location);
            GL.VertexAttribDivisor(location, ONEPERINSTANCE);
        }
        #endregion

        #region Helper Classes
        /// <summary>
        /// Orders particles from furthest to nearest the camera. Reused every frame, so sorting doesn't allocate.
        /// </summary>
        private sealed class DistanceComparer : IComparer<Particle>
        {
            public Vector3 CameraPosition;

            public int Compare(Particle a, Particle b) =>
                Vector3.DistanceSquared(b.Position, CameraPosition).CompareTo(Vector3.DistanceSquared(a.Position, CameraPosition));
        }
        #endregion
    }
}
