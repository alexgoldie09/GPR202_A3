using System.Diagnostics;
using GPR202_A3.Debugging;
using GPR202_A3.Graphics;
using GPR202_A3.Scene;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace GPR202_A3
{
    /// <summary>
    /// The game window: loads the scene, runs the game in OnUpdateFrame and draws it in OnRenderFrame.
    /// Balloon Hunt: fly the plane with the mouse and collect every balloon.
    /// </summary>
    internal class Game : GameWindow
    {
        #region Constants
        // How many balloons each game starts with
        private const int BALLOONSTOTAL = 8;

        // Where the plane starts: above the oasis, facing +Z
        private const float STARTHEIGHT = 60f;
        private const float STARTHEADING = 0f;

        // Sunlight for every lit shader. The direction is the way the light travels,
        // from the sun in the skybox's left (-X) face, about 24 degrees above the horizon.
        private static readonly Vector3 SunDirection = new(1f, -0.45f, -0.37f);
        private static readonly Vector3 SunColor = new(1f, 0.95f, 0.85f); // warm desert sunlight
        private const float AMBIENTSTRENGTH = 0.45f;

        // Size of the F5 debug crater (later, crashes and missiles make craters instead)
        private const float CRATERRADIUS = 18f;
        private const float CRATERDEPTH = 8f;

        // Clip plane for the main pass. Clipping is off then, so it isn't used; this just gives the uniform a value.
        private static readonly Vector4 NoClipPlane = Vector4.Zero;

        // Shown where nothing is drawn (normally covered by the skybox)
        private static readonly Color4 ClearColor = new(0.3f, 0.4f, 0.5f, 1f);

        // Debug text size (1 = the font's 16px)
        private const float TEXTSCALE = 1.4f;

        // Keys for the debug readout lines, so each one updates in place
        private const int FRAMEREADOUT = 1;
        private const int LOADREADOUT = 2;
        private const int BALLOONREADOUT = 3;
        private const int CONTROLSREADOUT = 4;
        #endregion

        #region Private Fields
        // Window size in screen coordinates (the same space as the mouse position)
        private int _width;
        private int _height;

        // Title of the game
        private readonly string _title;

        // Render pipeline
        private ShaderProgram? _modelShader;
        private bool _useBackfaceCulling = true;
        private bool _useLighting = true;
        private bool _useVSync = true;
        private bool _useWater = true;

        // The window's size in pixels (larger than _width/_height on a Retina display)
        private Vector2i _framebufferSize;

        // What the shaders output (texture, normals, UVs, wireframe, metallic, roughness)
        private static readonly int DebugViewCount = Enum.GetValues<DebugView>().Length;
        private DebugView _debugView;

        // Frame, CPU and GPU timing
        private readonly FrameTimer _frameTimer = new();

        // How long OnLoad took
        private double _loadMs;

        // Scene
        private Camera? _camera;
        private Skybox? _skybox;
        private Terrain? _terrain;
        private Plane? _plane;
        private Balloon? _balloonModel;
        private Water? _water;

        // Game state
        private readonly List<BalloonState> _balloons = new();
        private int _balloonsLeft;
        #endregion

        #region Constructors
        /// <summary>
        /// Constructor that sets up the window.
        /// </summary>
        /// <param name="width">The window's width.</param>
        /// <param name="height">The window's height.</param>
        /// <param name="title">The window's title.</param>
        public Game(int width, int height, string title)
            : base(GameWindowSettings.Default, NativeWindowSettings.Default)
        {
            _width = width;
            _height = height;
            _title = title;

            Title = _title;
            CenterWindow(new Vector2i(_width, _height));
        }
        #endregion

        #region OpenGL Overrides
        /// <summary>
        /// Creates every GPU resource and starts the first game.
        /// </summary>
        protected override void OnLoad()
        {
            base.OnLoad();
            var loadTimer = Stopwatch.StartNew();

            // --- Debugging Setup ---
            DebugText.Initialize(_width, _height);
            DebugText.TextScale = TEXTSCALE;
            _frameTimer.Initialize();

            // --- Render state ---
            GL.ClearColor(ClearColor);
            GL.Enable(EnableCap.DepthTest);
            if (_useBackfaceCulling)
                GL.Enable(EnableCap.CullFace);
            // Front faces are counter-clockwise (OpenGL's default, and what Blender exports)
            GL.FrontFace(FrontFaceDirection.Ccw);
            VSync = VSyncMode.On;

            // --- Model shader (values that never change are uploaded once) ---
            _modelShader = new ShaderProgram("Model.vert", "Model.frag");
            _modelShader.Bind();
            _modelShader.SetVector3("lightDir", SunDirection);
            _modelShader.SetVector3("lightColor", SunColor);
            _modelShader.SetFloat("ambientStrength", AMBIENTSTRENGTH);
            _modelShader.SetInt("AlbedoMap", Material.ALBEDOMAPUNIT);
            _modelShader.SetInt("NormalMap", Material.NORMALMAPUNIT);
            _modelShader.SetInt("MetallicMap", Material.METALLICMAPUNIT);
            _modelShader.SetInt("RoughnessMap", Material.ROUGHNESSMAPUNIT);
            _modelShader.SetInt("SkyboxMap", Skybox.CUBEMAPUNIT);

            // --- Scene ---
            // Six faces in OpenGL's cube map order: +X, -X, +Y, -Y, +Z, -Z
            _skybox = new Skybox(["Skybox/right.jpg", "Skybox/left.jpg", "Skybox/top.jpg",
                                  "Skybox/bottom.jpg", "Skybox/front.jpg", "Skybox/back.jpg"]);
            _terrain = new Terrain(); // first, so things can be placed on it
            _terrain.SetLighting(SunDirection, SunColor, AMBIENTSTRENGTH);
            _framebufferSize = FramebufferSize;
            _water = new Water(_framebufferSize.X, _framebufferSize.Y);
            _water.SetLighting(SunDirection, SunColor);
            _terrain.CreatePatch(Vector2.Zero, Water.HalfSize); // the oasis is at the centre
            _plane = new Plane(GetStartPosition());
            _balloonModel = new Balloon();

            // --- Camera ---
            _camera = new Camera(_width, _height);
            UploadProjection();
            CursorState = CursorState.Grabbed; // hide the cursor and keep it in the window

            // --- Start a new game ---
            SetupNewGame();

            loadTimer.Stop();
            _loadMs = loadTimer.Elapsed.TotalMilliseconds;
            Console.WriteLine($"Load time: {_loadMs:0} ms");
            DebugText.CheckGLError("OnLoad");
        }

        /// <summary>
        /// Frees every GPU resource.
        /// </summary>
        protected override void OnUnload()
        {
            base.OnUnload();

            _plane?.Delete();
            _balloonModel?.Delete();
            _terrain?.Delete();
            _water?.Delete();
            _skybox?.Delete();
            Material.DeleteDefaults();
            _modelShader?.Delete();
            _frameTimer.Delete();
            DebugText.Shutdown();
        }

        /// <summary>
        /// Keeps the camera and debug text in step with the window size.
        /// </summary>
        protected override void OnResize(ResizeEventArgs e)
        {
            base.OnResize(e);

            // Window size in screen coordinates. On a Retina display this is smaller than the framebuffer
            // (in pixels), so the viewport is set in OnFramebufferResize instead.
            _width = e.Width;
            _height = e.Height;

            _camera?.SetScreenSize(_width, _height);
            UploadProjection();
            DebugText.SetScreenSize(_width, _height);
        }

        /// <summary>
        /// Sets the viewport to the framebuffer's size in pixels.
        /// </summary>
        protected override void OnFramebufferResize(FramebufferResizeEventArgs e)
        {
            base.OnFramebufferResize(e);
            _framebufferSize = new Vector2i(e.Width, e.Height);
            GL.Viewport(0, 0, e.Width, e.Height);
            _water?.Resize(e.Width, e.Height);
        }

        /// <summary>
        /// Game logic: input, the plane, the balloons and the camera.
        /// </summary>
        protected override void OnUpdateFrame(FrameEventArgs args)
        {
            base.OnUpdateFrame(args);

            // Seconds since the last update, so movement doesn't depend on the frame rate
            float deltaTime = (float)args.Time;
            DebugText.Update(deltaTime);

            KeyboardState kb = KeyboardState;
            if (kb.IsKeyPressed(Keys.Escape))
            {
                Close();
                return;
            }

            HandleDebugKeys(kb);
            ShowDebugReadouts();

            // Pause while the window isn't focused
            if (!IsFocused || _plane == null || _camera == null)
                return;

            _plane.Update(MouseState, _width, _height, deltaTime);
            UpdateBalloons(deltaTime);
            _water?.Update(deltaTime);
            _camera.Follow(_plane.Position, _plane.Forward, deltaTime);
        }

        /// <summary>
        /// Draws the water's reflection and refraction (if the water is on screen), then the scene, the water,
        /// the sky, and the debug text on top.
        /// </summary>
        protected override void OnRenderFrame(FrameEventArgs args)
        {
            // Timing stops before SwapBuffers, which is where VSync waits
            _frameTimer.BeginFrame();

            if (_camera != null)
            {
                Matrix4 view = _camera.GetViewMatrix();
                bool drawWater = _useWater && _water != null && _water.IsVisible(view, _camera.GetProjectionMatrix());

                if (drawWater && _water != null)
                    RenderWaterPasses(_water, _camera);

                // --- Main pass ---
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

                // Wireframe: draw only the triangle edges
                if (_debugView == DebugView.Wireframe)
                    GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);

                RenderScene(view, _camera.Position, NoClipPlane, drawModels: true);

                // After the opaque scene, so the shallow edges can fade into the sand underneath
                if (drawWater)
                    _water?.Render(view, _camera.Position, _debugView, _useLighting);

                // Back to filled polygons for the sky and debug text
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);

                // Last, so the sky is only drawn where nothing else covers it
                _skybox?.Render(view);
            }

            DebugText.Render();

            _frameTimer.EndFrame(args.Time);
            Context.SwapBuffers();
            base.OnRenderFrame(args);
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Draws the models (if asked) and the terrain. Used by the main pass and both water passes.
        /// </summary>
        /// <param name="view">The view matrix for this pass.</param>
        /// <param name="cameraPosition">The camera position for this pass (for highlights and reflections).</param>
        /// <param name="clipPlane">The clip plane for this pass (only used while clipping is enabled).</param>
        /// <param name="terrainPatchOnly">Whether to draw only the terrain under the water.</param>
        private void RenderScene(Matrix4 view, Vector3 cameraPosition, Vector4 clipPlane, bool drawModels, bool terrainPatchOnly = false)
        {
            if (drawModels && _modelShader != null)
            {
                // Only the values that change each pass are uploaded here
                _modelShader.Bind();
                _modelShader.SetMatrix4("view", view);
                _modelShader.SetVector3("cameraPos", cameraPosition);
                _modelShader.SetVector4("clipPlane", clipPlane);
                _modelShader.SetInt("DebugView", (int)_debugView);
                _modelShader.SetInt("UseLighting", _useLighting ? 1 : 0);

                // Each pass binds its textures again, so materials can't skip themselves based on an earlier pass
                Material.ResetLastApplied();
                _plane?.Render(_modelShader);
                if (_balloonModel != null)
                {
                    foreach (var balloon in _balloons)
                        _balloonModel.Render(_modelShader, balloon);
                }
            }

            // After the models, so terrain pixels hidden behind them are skipped by the depth test
            _terrain?.Render(view, clipPlane, _debugView, _useLighting, terrainPatchOnly);
        }

        /// <summary>
        /// Draws the scene into the water's two framebuffers:
        /// the reflection (from a camera mirrored below the water, keeping only what's above it)
        /// and the refraction (from the normal camera, keeping only what's below the water).
        /// </summary>
        private void RenderWaterPasses(Water water, Camera camera)
        {
            // Clip distance 0 is what the shaders' clipPlane feeds; it's only on for these two passes
            GL.Enable(EnableCap.ClipDistance0);

            // Reflection: models, terrain above the water, and the sky
            water.BeginReflection();
            Matrix4 reflectionView = camera.GetReflectionViewMatrix(Terrain.WaterLevel);
            RenderScene(reflectionView, camera.GetReflectionPosition(Terrain.WaterLevel), water.ReflectionClipPlane, drawModels: true);
            _skybox?.Render(reflectionView);

            // Refraction: only the terrain under the water (the plane and balloons are never under it)
            water.BeginRefraction();
            RenderScene(camera.GetViewMatrix(), camera.Position, water.RefractionClipPlane, drawModels: false, terrainPatchOnly: true);

            GL.Disable(EnableCap.ClipDistance0);

            // Back to drawing into the window
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Viewport(0, 0, _framebufferSize.X, _framebufferSize.Y);
        }

        /// <summary>
        /// Resets the balloons and the plane for a new game.
        /// </summary>
        private void SetupNewGame()
        {
            _balloons.Clear();
            for (int i = 0; i < BALLOONSTOTAL; i++)
            {
                var balloon = new BalloonState();
                balloon.Spawn();
                _balloons.Add(balloon);
            }

            _balloonsLeft = BALLOONSTOTAL;
            UpdateTitle();

            _plane?.Reset(GetStartPosition(), STARTHEADING);
            if (_plane != null)
                _camera?.SnapTo(_plane.Position, _plane.Forward);
        }

        /// <summary>
        /// Updates the balloons, counts new collections, and removes balloons that have finished animating.
        /// </summary>
        private void UpdateBalloons(float deltaTime)
        {
            if (_plane == null)
                return;

            // Backwards so removing doesn't skip entries
            for (int i = _balloons.Count - 1; i >= 0; i--)
            {
                BalloonState balloon = _balloons[i];
                bool wasCollected = balloon.Collected;

                balloon.Update(_plane.Position, deltaTime);

                if (!wasCollected && balloon.Collected)
                {
                    _balloonsLeft--;
                    UpdateTitle();
                    DebugText.Log($"Balloon collected! {_balloonsLeft} remaining.", Color4.Yellow, DebugText.EVENTDURATION);
                }

                // The original never removed them, and kept drawing them at scale 0
                if (balloon.Finished)
                    _balloons.RemoveAt(i);
            }
        }

        /// <summary>
        /// The plane's starting point, above the oasis.
        /// </summary>
        private static Vector3 GetStartPosition()
        {
            float ground = MathF.Max(Terrain.GetHeightAt(0f, 0f), Terrain.WaterLevel);
            return new Vector3(0f, ground + STARTHEIGHT, 0f);
        }

        /// <summary>
        /// Debug toggles, on the function keys so the gameplay keys stay free.
        /// </summary>
        private void HandleDebugKeys(KeyboardState kb)
        {
            // F1: next debug view
            if (kb.IsKeyPressed(Keys.F1))
                _debugView = (DebugView)(((int)_debugView + 1) % DebugViewCount);

            // F2: lighting on/off
            if (kb.IsKeyPressed(Keys.F2))
                _useLighting = !_useLighting;

            // F3: backface culling on/off
            if (kb.IsKeyPressed(Keys.F3))
            {
                _useBackfaceCulling = !_useBackfaceCulling;
                if (_useBackfaceCulling)
                    GL.Enable(EnableCap.CullFace);
                else
                    GL.Disable(EnableCap.CullFace);
            }

            // F4: VSync on/off (off shows the real frame rate, for measuring)
            if (kb.IsKeyPressed(Keys.F4))
            {
                _useVSync = !_useVSync;
                VSync = _useVSync ? VSyncMode.On : VSyncMode.Off;
            }

            // F5: crater under the plane
            if (kb.IsKeyPressed(Keys.F5) && _plane != null && _terrain != null)
                _terrain.AddCrater(_plane.Position, CRATERRADIUS, CRATERDEPTH);

            // F6: water on/off (to measure what the reflection and refraction passes cost)
            if (kb.IsKeyPressed(Keys.F6))
                _useWater = !_useWater;
        }

        /// <summary>
        /// The debug readout lines, updated in place.
        /// </summary>
        private void ShowDebugReadouts()
        {
            DebugText.Log($"Frame: {_frameTimer.AverageFrameMs:0.0} ms ({_frameTimer.AverageFps:0} FPS), " +
                          $"CPU: {_frameTimer.AverageRenderMs:0.00} ms, GPU: {_frameTimer.AverageGpuMs:0.00} ms",
                Color4.White, DebugText.READOUTDURATION, FRAMEREADOUT);
            DebugText.Log($"Load: {_loadMs:0} ms", Color4.White, DebugText.READOUTDURATION, LOADREADOUT);
            DebugText.Log($"Balloons remaining: {_balloonsLeft}", Color4.DarkGreen, DebugText.READOUTDURATION, BALLOONREADOUT);
            DebugText.Log($"F1 Debug View: {_debugView}  F2 Lighting: {OnOff(_useLighting)}  " +
                          $"F3 Culling: {OnOff(_useBackfaceCulling)}  F4 VSync: {OnOff(_useVSync)}  F5 Crater  F6 Water: {OnOff(_useWater)}  Esc: Quit",
                Color4.DarkMagenta, DebugText.READOUTDURATION, CONTROLSREADOUT);
        }

        /// <summary>
        /// Shows the balloons left in the window title, like the original. Only called when the count changes.
        /// </summary>
        private void UpdateTitle() => Title = $"{_title}  -  Balloons remaining: {_balloonsLeft}";

        /// <summary>
        /// Uploads the projection matrix to every shader that uses it (on load and on resize).
        /// </summary>
        private void UploadProjection()
        {
            if (_camera == null)
                return;

            Matrix4 projection = _camera.GetProjectionMatrix();

            if (_modelShader != null)
            {
                _modelShader.Bind();
                _modelShader.SetMatrix4("projection", projection);
            }

            _terrain?.SetProjection(projection);
            _skybox?.SetProjection(projection);
            _water?.SetProjection(projection, _camera.NearPlane, _camera.FarPlane);
        }

        /// <summary>
        /// "ON" or "OFF" for the debug readout.
        /// </summary>
        private static string OnOff(bool value) => value ? "ON" : "OFF";
        #endregion
    }
}
