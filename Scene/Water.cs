using GPR202_A3.Debugging;
using GPR202_A3.Generation;
using GPR202_A3.Graphics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GPR202_A3.Scene
{
    /// <summary>
    /// The oasis water: a flat quad at the water level that shows the scene reflected above it and refracted below it.
    /// Each frame the scene is drawn twice into half-size framebuffers (once mirrored for the reflection, once
    /// clipped to below the water for the refraction), then the water shader mixes the two with ripples,
    /// Fresnel and depth-based colour.
    /// </summary>
    internal class Water
    {
        #region Constants
        // Width of the water quad. The pond is about 215 across; the corners sit under the sand and are hidden.
        private const float QUADSIZE = 300f;

        // The reflection and refraction are drawn at this fraction of the window's resolution (half = a quarter of the pixels)
        private const float RESOLUTIONSCALE = 0.5f;

        // The clip planes sit this far past the water surface, so no gap shows where the surface meets the sand
        private const float CLIPOFFSET = 0.5f;

        // Ripple normal map
        private const int NORMALMAPSIZE = 512;
        private const int NORMALMAPSEED = 7;
        private const float RIPPLETILESIZE = 70f; // world units per repeat of the ripples
        private const float SECONDLAYERSCALE = 1.6f; // the second ripple layer repeats this many times more often

        // Each ripple layer drifts in its own direction, in texture repeats per second
        private static readonly Vector2 FirstRippleVelocity = new(0.05f, 0.02f);
        private static readonly Vector2 SecondRippleVelocity = new(-0.03f, 0.045f);

        // Texture units
        private const int REFLECTIONUNIT = 0;
        private const int REFRACTIONUNIT = 1;
        private const int DEPTHUNIT = 2;
        private const int NORMALUNIT = 3;

        // Shader location of the quad's corners
        private const int POSITIONLOCATION = 0;
        private const int POSITIONSIZE = 3;

        // Corners of the quad's clip-space box, for the visibility check (the six sides of the view)
        private const int CORNERCOUNT = 4;
        #endregion

        #region Public Fields
        /// <summary>
        /// Half the width of the water quad. The refraction pass only needs the terrain inside this.
        /// </summary>
        public static float HalfSize => QUADSIZE / 2f;
        
        /// <summary>
        /// For the reflection pass: keeps everything above the water (cuts away what's below).
        /// </summary>
        public Vector4 ReflectionClipPlane => new(0f, 1f, 0f, -(Terrain.WaterLevel - CLIPOFFSET));

        /// <summary>
        /// For the refraction pass: keeps everything below the water (cuts away what's above).
        /// </summary>
        public Vector4 RefractionClipPlane => new(0f, -1f, 0f, Terrain.WaterLevel + CLIPOFFSET);
        #endregion

        #region Private Fields
        // GPU resources this class owns, deleted in Delete()
        private readonly ShaderProgram _shader;
        private readonly Texture _normalMap;
        private readonly Framebuffer _reflection;
        private readonly Framebuffer _refraction;
        private readonly VAO _vao;
        private readonly VBO _positionVbo;
        private readonly IBO _ibo;
        private readonly int _indexCount;

        // The quad's corners, for the visibility check
        private readonly Vector3[] _corners;

        // How far each ripple layer has drifted (wrapped to 0..1, so it never loses precision)
        private Vector2 _firstRippleOffset;
        private Vector2 _secondRippleOffset;
        #endregion

        #region Constructors
        /// <summary>
        /// Constructor that builds the quad, the ripple normal map, the framebuffers and the shader.
        /// </summary>
        /// <param name="framebufferWidth">The window's framebuffer width in pixels.</param>
        /// <param name="framebufferHeight">The window's framebuffer height in pixels.</param>
        public Water(int framebufferWidth, int framebufferHeight)
        {
            // --- Quad at the water level, wound counter-clockwise from above ---
            float half = QUADSIZE / 2f;
            float y = Terrain.WaterLevel;
            _corners =
            [
                new(-half, y, -half),
                new(-half, y, half),
                new(half, y, -half),
                new(half, y, half)
            ];
            List<uint> indices = [0, 1, 2, 1, 3, 2];

            _vao = new VAO(); // binds itself, so the IBO below is stored in it
            _positionVbo = new VBO(new List<Vector3>(_corners));
            _vao.LinkToVao(POSITIONLOCATION, POSITIONSIZE, _positionVbo);
            _vao.Bind();
            _ibo = new IBO(indices);
            _vao.Unbind();
            _indexCount = indices.Count;

            // --- Ripples, generated from noise ---
            byte[] pixels = WaterTextureGenerator.GenerateNormalMap(NORMALMAPSIZE, NORMALMAPSEED);
            _normalMap = new Texture(pixels, NORMALMAPSIZE, NORMALMAPSIZE, NORMALUNIT);

            // --- Framebuffers: the refraction keeps its depth as a texture, for the depth-based colour ---
            (int width, int height) = ScaledSize(framebufferWidth, framebufferHeight);
            _reflection = new Framebuffer(width, height, depthAsTexture: false);
            _refraction = new Framebuffer(width, height, depthAsTexture: true);

            // --- Shader (values that never change are uploaded once) ---
            _shader = new ShaderProgram("Water.vert", "Water.frag");
            _shader.Bind();
            _shader.SetInt("ReflectionMap", REFLECTIONUNIT);
            _shader.SetInt("RefractionMap", REFRACTIONUNIT);
            _shader.SetInt("RefractionDepthMap", DEPTHUNIT);
            _shader.SetInt("NormalMap", NORMALUNIT);
            _shader.SetFloat("RippleTileSize", RIPPLETILESIZE);
            _shader.SetFloat("SecondLayerScale", SECONDLAYERSCALE);

            DebugText.Log($"Water: {width}x{height} reflection and refraction", Color4.LightGreen, DebugText.INFODURATION);
            DebugText.CheckGLError("Water setup");
        }
        #endregion

        #region Public API
        /// <summary>
        /// Drifts the ripples.
        /// </summary>
        public void Update(float deltaTime)
        {
            _firstRippleOffset = WrapOffset(_firstRippleOffset + FirstRippleVelocity * deltaTime);
            _secondRippleOffset = WrapOffset(_secondRippleOffset + SecondRippleVelocity * deltaTime);
        }

        /// <summary>
        /// Whether any of the water is inside the camera's view. If not, the reflection and refraction passes are skipped.
        /// </summary>
        public bool IsVisible(Matrix4 view, Matrix4 projection)
        {
            // The corners in clip space. A point is inside the view if -w <= x, y, z <= w.
            Matrix4 viewProjection = view * projection;
            var clip = new Vector4[CORNERCOUNT];
            for (int i = 0; i < CORNERCOUNT; i++)
                clip[i] = new Vector4(_corners[i], 1f) * viewProjection;

            // The water is hidden only if every corner is outside the same side of the view
            return !AllOutside(clip, c => c.X < -c.W) && !AllOutside(clip, c => c.X > c.W) &&
                   !AllOutside(clip, c => c.Y < -c.W) && !AllOutside(clip, c => c.Y > c.W) &&
                   !AllOutside(clip, c => c.Z < -c.W) && !AllOutside(clip, c => c.Z > c.W);
        }

        /// <summary>
        /// Starts drawing into the reflection framebuffer.
        /// </summary>
        public void BeginReflection() => BeginPass(_reflection);

        /// <summary>
        /// Starts drawing into the refraction framebuffer.
        /// </summary>
        public void BeginRefraction() => BeginPass(_refraction);

        /// <summary>
        /// Resizes the framebuffers to match the window. Call from OnFramebufferResize.
        /// </summary>
        public void Resize(int framebufferWidth, int framebufferHeight)
        {
            (int width, int height) = ScaledSize(framebufferWidth, framebufferHeight);
            _reflection.Resize(width, height);
            _refraction.Resize(width, height);
        }

        /// <summary>
        /// Uploads the sunlight settings. Call once on load.
        /// </summary>
        public void SetLighting(Vector3 lightDirection, Vector3 lightColor)
        {
            _shader.Bind();
            _shader.SetVector3("lightDir", lightDirection);
            _shader.SetVector3("lightColor", lightColor);
        }

        /// <summary>
        /// Uploads the projection matrix and its near/far planes (used to turn depth values into distances).
        /// Call when the window is resized.
        /// </summary>
        public void SetProjection(Matrix4 projection, float nearPlane, float farPlane)
        {
            _shader.Bind();
            _shader.SetMatrix4("projection", projection);
            _shader.SetFloat("nearPlane", nearPlane);
            _shader.SetFloat("farPlane", farPlane);
        }

        /// <summary>
        /// Draws the water surface. Call after the opaque scene, so the sand under the shallow edges shows through.
        /// </summary>
        public void Render(Matrix4 view, Vector3 cameraPosition, DebugView debugView, bool useLighting)
        {
            _shader.Bind();
            _shader.SetMatrix4("view", view);
            _shader.SetVector3("cameraPos", cameraPosition);
            _shader.SetVector2("FirstRippleOffset", _firstRippleOffset);
            _shader.SetVector2("SecondRippleOffset", _secondRippleOffset);
            _shader.SetInt("DebugView", (int)debugView);
            _shader.SetInt("UseLighting", useLighting ? 1 : 0);

            Framebuffer.BindTexture(_reflection.ColorTexture, REFLECTIONUNIT);
            Framebuffer.BindTexture(_refraction.ColorTexture, REFRACTIONUNIT);
            Framebuffer.BindTexture(_refraction.DepthTexture, DEPTHUNIT);
            _normalMap.Bind();

            // The edges fade out (alpha), so blend with the sand already drawn underneath
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            _vao.Bind();
            GL.DrawElements(PrimitiveType.Triangles, _indexCount, DrawElementsType.UnsignedInt, 0);
            _vao.Unbind();

            GL.Disable(EnableCap.Blend);
        }

        /// <summary>
        /// Frees the water's GPU resources.
        /// </summary>
        public void Delete()
        {
            _shader.Delete();
            _normalMap.Delete();
            _reflection.Delete();
            _refraction.Delete();
            _vao.Delete();
            _positionVbo.Delete();
            _ibo.Delete();
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Binds a framebuffer and clears it, ready for a pass.
        /// </summary>
        private static void BeginPass(Framebuffer framebuffer)
        {
            framebuffer.Bind();
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        }

        /// <summary>
        /// The framebuffer size at RESOLUTIONSCALE of the window.
        /// </summary>
        private static (int Width, int Height) ScaledSize(int width, int height) =>
            ((int)(width * RESOLUTIONSCALE), (int)(height * RESOLUTIONSCALE));

        /// <summary>
        /// Whether every corner fails the same test (is outside the same side of the view).
        /// </summary>
        private static bool AllOutside(Vector4[] corners, Func<Vector4, bool> isOutside)
        {
            foreach (Vector4 corner in corners)
            {
                if (!isOutside(corner))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Keeps a texture offset in 0..1 (the texture repeats, so only the fraction matters).
        /// </summary>
        private static Vector2 WrapOffset(Vector2 offset) =>
            new(offset.X - MathF.Floor(offset.X), offset.Y - MathF.Floor(offset.Y));
        #endregion
    }
}
