using System.Diagnostics;
using GPR202_A3.Debugging;
using GPR202_A3.Gameplay;
using GPR202_A3.Graphics;
using GPR202_A3.Scene;
using GPR202_A3.UI;
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

        // HUD text sizes, as whole-number multiples of the font's 16px (whole numbers keep the pixels even)
        private const int TITLESCALE = 3;
        private const int HUDSCALE = 2;
        private const int DEBUGSCALE = 1;

        // HUD backgrounds: dark and see-through, with a few pixels around the text
        private static readonly Color4 PanelColor = new(0f, 0f, 0f, 0.45f);
        private const float PANELPADDING = 3f;

        // HUD text colours
        private static readonly Color4 TitleColor = Color4.White;
        private static readonly Color4 BalloonColor = Color4.Gold;
        private static readonly Color4 MessageColor = Color4.Yellow;
        private static readonly Color4 ReadoutColor = Color4.White;
        private static readonly Color4 ControlsColor = Color4.Plum;

        // Seconds a message like "Balloon collected!" stays on screen
        private const float MESSAGEDURATION = 3f;

        // Texture unit for the HUD font (the HUD draws last, so it can share the albedo unit)
        private const int HUDFONTUNIT = 0;

        // Lines of the debug block
        private const int FRAMELINE = 0;
        private const int LOADLINE = 1;
        private const int TERRAINLINE = 2;
        private const int VIEWLINE = 3;
        private const int CONTROLSLINE = 4;

        // For showing triangle counts in thousands
        private const float THOUSAND = 1000f;

        // Seconds between debug readout refreshes (the frame timings only change this often anyway,
        // and the particle count would otherwise change, and rebuild the HUD, every frame)
        private const float READOUTINTERVAL = 0.5f;

        // Every debug key (pressing one refreshes the debug readout straight away)
        private static readonly Keys[] DebugKeys = [Keys.F1, Keys.F2, Keys.F3, Keys.F4, Keys.F5, Keys.F6, Keys.F7, Keys.F8, Keys.F9, Keys.F10, Keys.F12];

        // How far ahead of the plane the F8 and F9 test effects go off, so they're in view
        private const float TESTEFFECTDISTANCE = 25f;
        #endregion

        #region Private Fields
        // Window size in screen coordinates (the same space as the mouse position)
        private int _width;
        private int _height;

        // Title of the game
        private readonly string _title;

        // Render pipeline
        private ShaderProgram? _modelShader;
        private ShaderProgram? _instancedShader; // same lighting as _modelShader, but one draw places many copies
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

        // HUD: the game title (top centre), balloons left (bottom right), timed messages (below the middle),
        // and the debug readouts (bottom left, F7 hides them)
        private Font? _font;
        private Hud? _hud;
        private TextBlock? _titleText;
        private TextBlock? _balloonText;
        private TextBlock? _messageText;
        private TextBlock? _debugText;
        
        // Gap between the HUD and the screen edges, in pixels
        private Vector2 _screenMarginOffset = new(12f, 12f);
        // Where the timed message sits: centred across the screen, 180 pixels below the middle
        private Vector2 _messageOffset = new(0f, 180f);

        // Counts down to the next debug readout refresh
        private float _readoutTimer;

        // Scene
        private Camera? _camera;
        private Skybox? _skybox;
        private Terrain? _terrain;
        private Plane? _plane;
        private Balloon? _balloonModel;
        private Props? _props;
        private Effects? _effects;

        // Fog settings, shared by every world shader (F11 changes the mode)
        private Fog? _fog;
        private Water? _water;
        
        // Particles
        private Vector3 _balloonConfettiOffset = new(0f, 10f, 0f);

        // Game state
        private readonly GameManager _gameManager = new();
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

            // --- Debugging and HUD Setup ---
            _frameTimer.Initialize();
            _font = new Font("UI/BitPapAtlas.png", HUDFONTUNIT);
            _hud = new Hud(_font, _width, _height);
            SetupHud(_hud);

            // --- Render state ---
            GL.ClearColor(ClearColor);
            GL.Enable(EnableCap.DepthTest);
            if (_useBackfaceCulling)
                GL.Enable(EnableCap.CullFace);
            // Front faces are counter-clockwise (OpenGL's default, and what Blender exports)
            GL.FrontFace(FrontFaceDirection.Ccw);
            VSync = VSyncMode.On;

            // --- Model shaders (both share Model.frag, only the vertex shader differs) ---
            _modelShader = new ShaderProgram("Model.vert", "Model.frag");
            SetupModelShader(_modelShader);
            _instancedShader = new ShaderProgram("ModelInstanced.vert", "Model.frag");
            SetupModelShader(_instancedShader);

            // --- Scene ---
            // Six faces in OpenGL's cube map order: +X, -X, +Y, -Y, +Z, -Z
            _skybox = new Skybox(["Skybox/right.jpg", "Skybox/left.jpg", "Skybox/top.jpg",
                                  "Skybox/bottom.jpg", "Skybox/front.jpg", "Skybox/back.jpg"]);
            _terrain = new Terrain(); // first, so things can be placed on it
            _terrain.SetLighting(SunDirection, SunColor, AMBIENTSTRENGTH);
            _framebufferSize = FramebufferSize;
            _water = new Water(_framebufferSize.X, _framebufferSize.Y);
            _water.SetLighting(SunDirection, SunColor);
            _terrain.SetArea(Vector2.Zero, Water.HalfSize); // the refraction pass only needs the oasis, at the centre
            _plane = new Plane(GameManager.GetStartPosition());
            _balloonModel = new Balloon();
            _props = new Props(); // after the terrain, since placement reads its heights
            _effects = new Effects();
            _fog = new Fog(SunDirection, SunColor, Terrain.WaterLevel, Skybox.CUBEMAPUNIT);
            UploadFog();

            // --- Camera ---
            _camera = new Camera(_width, _height);
            UploadProjection();
            CursorState = CursorState.Grabbed; // hide the cursor and keep it in the window

            // --- Start a new game ---
            _gameManager.BalloonCollected += OnBalloonCollected;
            StartNewGame();

            loadTimer.Stop();
            _loadMs = loadTimer.Elapsed.TotalMilliseconds;
            Log.Info($"Load time: {_loadMs:0} ms");
            Log.CheckGLError("OnLoad");
        }

        /// <summary>
        /// Frees every GPU resource.
        /// </summary>
        protected override void OnUnload()
        {
            base.OnUnload();

            _plane?.Delete();
            _balloonModel?.Delete();
            _props?.Delete();
            _effects?.Delete();
            _terrain?.Delete();
            _water?.Delete();
            _skybox?.Delete();
            Material.DeleteDefaults();
            _modelShader?.Delete();
            _instancedShader?.Delete();
            _frameTimer.Delete();
            _hud?.Delete();
            _font?.Delete();
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
            _hud?.SetScreenSize(_width, _height);
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
            _hud?.Update(deltaTime);

            KeyboardState kb = KeyboardState;
            if (kb.IsKeyPressed(Keys.Escape))
            {
                Close();
                return;
            }

            // A debug key refreshes the readout straight away, so the toggles respond instantly
            if (HandleDebugKeys(kb))
                _readoutTimer = 0f;
            UpdateDebugText(deltaTime);

            // Pause while the window isn't focused
            if (!IsFocused || _plane == null || _camera == null)
                return;

            _plane.Update(MouseState, _width, _height, deltaTime);
            _gameManager.Update(_plane, deltaTime);
            _water?.Update(deltaTime);
            _effects?.Update(_plane, deltaTime);
            _camera.Follow(_plane.Position, _plane.Forward, deltaTime);
        }

        /// <summary>
        /// Draws the water's reflection and refraction (if the water is on screen), then the scene, the water,
        /// the sky, and the HUD on top.
        /// </summary>
        protected override void OnRenderFrame(FrameEventArgs args)
        {
            // Timing stops before SwapBuffers, which is where VSync waits
            _frameTimer.BeginFrame();

            if (_camera != null)
            {
                Matrix4 view = _camera.GetViewMatrix();

                // Terrain detail levels for this frame, from the real camera (every pass uses the same ones)
                _terrain?.SelectLevels(_camera.Position);

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

                // Back to filled polygons for the sky and HUD
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);

                // Last of the solid things, so the sky is only drawn where nothing else covers it
                _skybox?.Render(view);

                // See-through, so after everything they can be seen through (including the sky)
                _effects?.Render(view, _camera.Position);
            }

            _hud?.Render();

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
        /// <param name="drawModels">Whether to draw the plane, balloons and props (the refraction pass skips them).</param>
        /// <param name="terrainAreaOnly">Whether to draw only the terrain under the water.</param>
        private void RenderScene(Matrix4 view, Vector3 cameraPosition, Vector4 clipPlane, bool drawModels, bool terrainAreaOnly = false)
        {
            // Each pass binds its textures again, so materials can't skip themselves based on an earlier pass
            Material.ResetLastApplied();

            if (drawModels && _modelShader != null)
            {
                SetPassUniforms(_modelShader, view, cameraPosition, clipPlane);
                _plane?.Render(_modelShader);
                if (_balloonModel != null)
                {
                    foreach (var balloon in _gameManager.Balloons)
                        _balloonModel.Render(_modelShader, balloon);
                }
            }

            if (drawModels && _instancedShader != null && _props != null)
            {
                SetPassUniforms(_instancedShader, view, cameraPosition, clipPlane);
                _props.Render(_instancedShader);
            }

            // After the models, so terrain pixels hidden behind them are skipped by the depth test
            _terrain?.Render(view, cameraPosition, clipPlane, _debugView, _useLighting, terrainAreaOnly);
        }

        /// <summary>
        /// Uploads the model shader values that never change (lighting and texture units).
        /// </summary>
        private static void SetupModelShader(ShaderProgram shader)
        {
            shader.Bind();
            shader.SetVector3("lightDir", SunDirection);
            shader.SetVector3("lightColor", SunColor);
            shader.SetFloat("ambientStrength", AMBIENTSTRENGTH);
            shader.SetInt("AlbedoMap", Material.ALBEDOMAPUNIT);
            shader.SetInt("NormalMap", Material.NORMALMAPUNIT);
            shader.SetInt("MetallicMap", Material.METALLICMAPUNIT);
            shader.SetInt("RoughnessMap", Material.ROUGHNESSMAPUNIT);
            shader.SetInt("SkyboxMap", Skybox.CUBEMAPUNIT);
        }

        /// <summary>
        /// Binds a model shader and uploads the values that change each pass.
        /// </summary>
        private void SetPassUniforms(ShaderProgram shader, Matrix4 view, Vector3 cameraPosition, Vector4 clipPlane)
        {
            shader.Bind();
            shader.SetMatrix4("view", view);
            shader.SetVector3("cameraPos", cameraPosition);
            shader.SetVector4("clipPlane", clipPlane);
            shader.SetInt("DebugView", (int)_debugView);
            shader.SetInt("UseLighting", _useLighting ? 1 : 0);
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
            RenderScene(camera.GetViewMatrix(), camera.Position, water.RefractionClipPlane, drawModels: false, terrainAreaOnly: true);

            GL.Disable(EnableCap.ClipDistance0);

            // Back to drawing into the window
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Viewport(0, 0, _framebufferSize.X, _framebufferSize.Y);
        }

        /// <summary>
        /// Starts a new game: the GameManager resets the balloons and the plane, and Game resets what's on screen.
        /// </summary>
        private void StartNewGame()
        {
            if (_plane == null)
                return;

            _gameManager.StartNewGame(_plane);
            UpdateBalloonText();
            _effects?.Clear(_plane);
            _camera?.SnapTo(_plane.Position, _plane.Forward);
        }

        /// <summary>
        /// What happens on screen when the GameManager says a balloon was collected.
        /// </summary>
        private void OnBalloonCollected(BalloonState balloon)
        {
            UpdateBalloonText();
            _messageText?.ShowFor($"Balloon collected! {_gameManager.BalloonsLeft} remaining", MessageColor, MESSAGEDURATION);
            _effects?.BalloonPop(balloon.Position + _balloonConfettiOffset); // a bit above the balloon's centre
        }

        /// <summary>
        /// Debug toggles, on the function keys so the gameplay keys stay free.
        /// </summary>
        /// <returns>Whether any debug key was pressed.</returns>
        private bool HandleDebugKeys(KeyboardState kb)
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
                Material.CullingEnabled = _useBackfaceCulling; // so double-sided materials put back the right state
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

            // F7: debug readouts on/off (for a clean HUD in screenshots)
            if (kb.IsKeyPressed(Keys.F7) && _debugText != null)
                _debugText.Visible = !_debugText.Visible;

            // F8 / F9: test the balloon pop and crash effects, ahead of the plane (until collisions trigger them)
            if (_plane != null && _effects != null)
            {
                Vector3 ahead = _plane.Position + _plane.Forward * TESTEFFECTDISTANCE;
                if (kb.IsKeyPressed(Keys.F8))
                    _effects.BalloonPop(ahead);
                if (kb.IsKeyPressed(Keys.F9))
                    _effects.Crash(ahead);
            }

            // F10: terrain level of detail and culling on/off (off draws every patch at full detail, to measure the saving)
            if (kb.IsKeyPressed(Keys.F10) && _terrain != null)
                _terrain.LodEnabled = !_terrain.LodEnabled;

            // F12: next fog mode (off, linear, exponential, exponential squared, height layer)
            if (kb.IsKeyPressed(Keys.F12) && _fog != null)
            {
                _fog.NextMode();
                UploadFog();
            }

            foreach (Keys key in DebugKeys)
            {
                if (kb.IsKeyPressed(key))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Makes the HUD's blocks. Each one is anchored to a point of the screen, so it stays put when the window resizes.
        /// </summary>
        private void SetupHud(Hud hud)
        {
            // Game title, top centre
            _titleText = hud.Add(new TextBlock(new RectTransform(Anchor.TopCentre, _screenMarginOffset), TITLESCALE,
                PanelColor, PANELPADDING));
            _titleText.SetText(_title, TitleColor);

            // Balloons left, bottom right
            _balloonText = hud.Add(new TextBlock(new RectTransform(Anchor.BottomRight, _screenMarginOffset), HUDSCALE,
                PanelColor, PANELPADDING));

            // Timed messages, below the middle of the screen (hidden until ShowFor is called)
            _messageText = hud.Add(new TextBlock(new RectTransform(Anchor.Centre, _messageOffset), HUDSCALE));
            _messageText.Visible = false;

            // Debug readouts, bottom left
            _debugText = hud.Add(new TextBlock(new RectTransform(Anchor.BottomLeft, _screenMarginOffset), DEBUGSCALE,
                PanelColor, PANELPADDING));
        }

        /// <summary>
        /// Shows how many balloons are left. Only called when the count changes.
        /// </summary>
        private void UpdateBalloonText() =>
            _balloonText?.SetText($"Balloons: {_gameManager.BalloonsLeft} / {_gameManager.BalloonsTotal}", BalloonColor);

        /// <summary>
        /// The debug readout lines, refreshed every READOUTINTERVAL seconds. The HUD still only rebuilds
        /// when one of them actually changes.
        /// </summary>
        private void UpdateDebugText(float deltaTime)
        {
            if (_debugText == null || !_debugText.Visible)
                return;

            // Only every READOUTINTERVAL seconds (or straight after a debug key)
            _readoutTimer -= deltaTime;
            if (_readoutTimer > 0f)
                return;
            _readoutTimer = READOUTINTERVAL;

            _debugText.SetLine(FRAMELINE, $"Frame: {_frameTimer.AverageFrameMs:0.0} ms ({_frameTimer.AverageFps:0} FPS), " +
                                          $"CPU: {_frameTimer.AverageRenderMs:0.00} ms, GPU: {_frameTimer.AverageGpuMs:0.00} ms",
                ReadoutColor);
            int particles = 0;
            int particleDrawCalls = 0;
            if (_effects != null)
            {
                particles = _effects.AliveCount;
                particleDrawCalls = _effects.DrawCalls;
            }
            _debugText.SetLine(LOADLINE, $"Load: {_loadMs:0} ms  Particles: {particles} ({particleDrawCalls} draw calls)", ReadoutColor);
            int patchesDrawn = 0;
            int patchCount = 0;
            float trianglesK = 0f;
            bool lodOn = false;
            if (_terrain != null)
            {
                patchesDrawn = _terrain.PatchesDrawn;
                patchCount = _terrain.PatchCount;
                trianglesK = _terrain.TrianglesDrawn / THOUSAND;
                lodOn = _terrain.LodEnabled;
            }
            _debugText.SetLine(TERRAINLINE, $"Terrain: {patchesDrawn}/{patchCount} patches, {trianglesK:0}k triangles  F10 LOD: {OnOff(lodOn)}",
                ReadoutColor);
            _debugText.SetLine(VIEWLINE, $"F1 View: {_debugView}  F2 Lighting: {OnOff(_useLighting)}  " +
                                         $"F3 Culling: {OnOff(_useBackfaceCulling)}  F4 VSync: {OnOff(_useVSync)}",
                ControlsColor);
            _debugText.SetLine(CONTROLSLINE, $"F5 Crater  F6 Water: {OnOff(_useWater)}  F7 Hide debug  F8 Pop  F9 Crash  F12 Fog: {FogModeName()}  Esc: Quit",
                ControlsColor);
        }

        /// <summary>
        /// Uploads the projection matrix to every shader that uses it (on load and on resize).
        /// </summary>
        private void UploadProjection()
        {
            if (_camera == null)
                return;

            Matrix4 projection = _camera.GetProjectionMatrix();

            foreach (ShaderProgram? shader in new[] { _modelShader, _instancedShader })
            {
                if (shader == null)
                    continue;
                shader.Bind();
                shader.SetMatrix4("projection", projection);
            }

            _terrain?.SetProjection(projection);
            _effects?.SetProjection(projection);
            _skybox?.SetProjection(projection);
            _water?.SetProjection(projection, _camera.NearPlane, _camera.FarPlane);
        }

        /// <summary>
        /// Uploads the fog settings to every shader that uses them (on load, and when F11 changes the mode).
        /// </summary>
        private void UploadFog()
        {
            if (_fog == null)
                return;

            foreach (ShaderProgram? shader in new[] { _modelShader, _instancedShader })
            {
                if (shader != null)
                    _fog.Upload(shader);
            }

            _terrain?.SetFog(_fog);
            _water?.SetFog(_fog);
            _skybox?.SetFog(_fog);
            _effects?.SetFog(_fog);
        }

        /// <summary>
        /// The fog mode's name for the debug readout.
        /// </summary>
        private string FogModeName()
        {
            if (_fog == null)
                return OnOff(false);
            return _fog.Mode.ToString();
        }

        /// <summary>
        /// "ON" or "OFF" for the debug readout.
        /// </summary>
        private static string OnOff(bool value) => value ? "ON" : "OFF";
        #endregion
    }
}
