using GPR202_A3.Generation;
using GPR202_A3.Graphics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GPR202_A3.Particles
{
    /// <summary>
    /// Owns everything the emitters share (the shader, the generated sprite atlas and the quad),
    /// updates every emitter, and draws them with the right blending:
    /// see-through ones first, then the glowing additive ones on top.
    /// </summary>
    internal class ParticleSystem
    {
        #region Constants
        // The atlas: pixels per sprite cell (a power of 2), and the texture unit it binds to
        private const int ATLASCELLSIZE = 64;
        private const int ATLASUNIT = 0;

        // The quad every particle is drawn with, as a triangle strip of corners from -0.5 to 0.5
        private const int CORNERLOCATION = 0;
        private static readonly float[] QuadCorners =
        [
            -0.5f, -0.5f,
             0.5f, -0.5f,
            -0.5f,  0.5f,
             0.5f,  0.5f
        ];
        #endregion

        #region Public Fields
        /// <summary>
        /// Particles alive across every emitter.
        /// </summary>
        public int AliveCount
        {
            get
            {
                int total = 0;
                foreach (ParticleEmitter emitter in _emitters)
                    total += emitter.AliveCount;
                return total;
            }
        }

        /// <summary>
        /// Draw calls last frame (one per emitter with particles alive).
        /// </summary>
        public int DrawCalls { get; private set; }
        #endregion

        #region Private Fields
        private readonly ShaderProgram _shader;
        private readonly Texture _atlas;
        private readonly int _quadVbo;
        private readonly List<ParticleEmitter> _emitters = new();
        private readonly Random _random = new();
        #endregion

        #region Constructors
        /// <summary>
        /// Constructor that makes the shader, generates the atlas and uploads the quad.
        /// </summary>
        public ParticleSystem()
        {
            _shader = new ShaderProgram("Particle.vert", "Particle.frag");

            int atlasWidth = ATLASCELLSIZE * ParticleTextureGenerator.COLUMNS;
            int atlasHeight = ATLASCELLSIZE * ParticleTextureGenerator.ROWS;
            _atlas = new Texture(ParticleTextureGenerator.GenerateAtlas(ATLASCELLSIZE), atlasWidth, atlasHeight, ATLASUNIT);

            _quadVbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _quadVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, QuadCorners.Length * sizeof(float), QuadCorners, BufferUsageHint.StaticDraw);

            // Values that never change
            _shader.Bind();
            _shader.SetInt("ParticleAtlas", ATLASUNIT);
            _shader.SetVector2("atlasGrid", new Vector2(ParticleTextureGenerator.COLUMNS, ParticleTextureGenerator.ROWS));
        }
        #endregion

        #region Public API
        /// <summary>
        /// Adds an emitter. Returns it, so it can be kept to Burst() or move later.
        /// </summary>
        public ParticleEmitter Add(EmitterSettings settings)
        {
            var emitter = new ParticleEmitter(settings, _quadVbo, CORNERLOCATION, _random);
            _emitters.Add(emitter);
            return emitter;
        }

        /// <summary>
        /// Updates every emitter. Call every update.
        /// </summary>
        public void Update(float deltaTime)
        {
            foreach (ParticleEmitter emitter in _emitters)
                emitter.Update(deltaTime);
        }

        /// <summary>
        /// Uploads the projection. Call on load and on resize.
        /// </summary>
        public void SetProjection(Matrix4 projection)
        {
            _shader.Bind();
            _shader.SetMatrix4("projection", projection);
        }

        /// <summary>
        /// Draws every emitter. Call after everything solid and the water, so particles can be seen through.
        /// </summary>
        /// <param name="view">The camera's view matrix.</param>
        /// <param name="cameraPosition">The camera's position (for sorting).</param>
        public void Render(Matrix4 view, Vector3 cameraPosition)
        {
            DrawCalls = 0;
            if (AliveCount == 0) return;

            _shader.Bind();
            _shader.SetMatrix4("view", view);

            // The camera's right and up directions are the first two columns of the view matrix's rotation.
            // Billboards are built from them, so they always face the camera.
            _shader.SetVector3("cameraRight", new Vector3(view.M11, view.M21, view.M31));
            _shader.SetVector3("cameraUp", new Vector3(view.M12, view.M22, view.M32));
            _atlas.Bind();

            // Hidden behind solid things (depth test on), but they don't hide each other (depth writes off),
            // and both sides of the quads show
            bool blendWasOn = GL.IsEnabled(EnableCap.Blend);
            GL.Enable(EnableCap.Blend);
            GL.DepthMask(false);
            GL.Disable(EnableCap.CullFace);

            // See-through first (each sorted far to near), then the additive glow on top
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            RenderEmitters(cameraPosition, additive: false);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
            RenderEmitters(cameraPosition, additive: true);

            // Back to the normal state for whatever draws next
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.DepthMask(true);
            Material.RestoreCulling();
            if (!blendWasOn) GL.Disable(EnableCap.Blend);
        }

        /// <summary>
        /// Removes every particle (e.g. on a new game).
        /// </summary>
        public void Clear()
        {
            foreach (ParticleEmitter emitter in _emitters)
                emitter.Clear();
        }

        /// <summary>
        /// Frees the shader, atlas, quad and every emitter's buffers.
        /// </summary>
        public void Delete()
        {
            foreach (ParticleEmitter emitter in _emitters)
                emitter.Delete();
            _emitters.Clear();
            GL.DeleteBuffer(_quadVbo);
            _atlas.Delete();
            _shader.Delete();
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Draws the emitters with one kind of blending.
        /// </summary>
        private void RenderEmitters(Vector3 cameraPosition, bool additive)
        {
            foreach (ParticleEmitter emitter in _emitters)
            {
                if (emitter.Settings.Additive != additive || emitter.AliveCount == 0) continue;

                emitter.Render(_shader, cameraPosition);
                DrawCalls++;
            }
        }
        #endregion
    }
}
