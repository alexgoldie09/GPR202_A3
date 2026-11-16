using System.Diagnostics;
using GPR202_A3.Debugging;
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

        // HUD text sizes, as whole-number multiples of the font's 16px (whole numbers keep the pixels even)
        private const int TITLESCALE = 3;
        private const int HUDSCALE = 2;
        private const int DEBUGSCALE = 1;

        // Gap between the HUD and the screen edges, in pixels
        private static readonly Vector2 ScreenMargin = new(12f, 12f);
        
        // Gap between the timed message and the bottom of the screen, in pixels
        private static readonly Vector2 MessageOffset = new(12f, 180f);

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
        private const int VIEWLINE = 2;
        private const int CONTROLSLINE = 3;
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

        // HUD: the game title (top centre), balloons left (bottom right), timed messages (bottom centre),
        // and the debug readouts (top left, F7 hides them)
        private Font? _font;
        private Hud? _hud;
        private TextBlock? _titleText;
        private TextBlock? _balloonText;
        private TextBlock? _messageText;
        private TextBlock? _debugText;

        // Scene
        private Camera? _camera;
        private Skybox? _skybox;
        private Terrain? _terrain;
        private Plane? _plane;
        private Balloon? _balloonModel;
        private Props? _props;
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
            _terrain.CreatePatch(Vector2.Zero, Water.HalfSize); // the oasis is at the centre
            _plane = new Plane(GetStartPosition());
            _balloonModel = new Balloon();
            _props = new Props(); // after the terrain, since placement reads its heights

            // --- Camera ---
            _camera = new Camera(_width, _height);
            UploadProjection();
            CursorState = CursorState.Grabbed; // hide the cursor and keep it in the window

            // --- Start a new game ---
            SetupNewGame();

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

            HandleDebugKeys(kb);
            UpdateDebugText();

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
        /// the sky, and the HUD on top.
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

                // Back to filled polygons for the sky and HUD
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);

                // Last, so the sky is only drawn where nothing else covers it
                _skybox?.Render(view);
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
        /// <param name="terrainPatchOnly">Whether to draw only the terrain under the water.</param>
        private void RenderScene(Matrix4 view, Vector3 cameraPosition, Vector4 clipPlane, bool drawModels, bool terrainPatchOnly = false)
        {
            // Each pass binds its textures again, so materials can't skip themselves based on an earlier pass
            Material.ResetLastApplied();

            if (drawModels && _modelShader != null)
            {
                SetPassUniforms(_modelShader, view, cameraPosition, clipPlane);
                _plane?.Render(_modelShader);
                if (_balloonModel != null)
                {
                    foreach (var balloon in _balloons)
                        _balloonModel.Render(_modelShader, balloon);
                }
            }

            if (drawModels && _instancedShader != null && _props != null)
            {
                SetPassUniforms(_instancedShader, view, cameraPosition, clipPlane);
                _props.Render(_instancedShader);
            }

            // After the models, so terrain pixels hidden behind them are skipped by the depth test
            _terrain?.Render(view, clipPlane, _debugView, _useLighting, terrainPatchOnly);
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
            UpdateBalloonText();

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
                    UpdateBalloonText();
                    _messageText?.ShowFor($"Balloon collected! {_balloonsLeft} remaining", MessageColor, MESSAGEDURATION);
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
        }

        /// <summary>
        /// Makes the HUD's blocks. Each one is anchored to a point of the screen, so it stays put when the window resizes.
        /// </summary>
        private void SetupHud(Hud hud)
        {
            // Game title, top centre
            _titleText = hud.Add(new TextBlock(new RectTransform(Anchor.TopCentre, ScreenMargin), TITLESCALE,
                PanelColor, PANELPADDING));
            _titleText.SetText(_title, TitleColor);

            // Balloons left, bottom right
            _balloonText = hud.Add(new TextBlock(new RectTransform(Anchor.BottomRight, ScreenMargin), HUDSCALE,
                PanelColor, PANELPADDING));

            // Timed messages, bottom centre (hidden until ShowFor is called)
            _messageText = hud.Add(new TextBlock(new RectTransform(Anchor.Centre, MessageOffset), HUDSCALE));
            _messageText.Visible = false;

            // Debug readouts, top left
            _debugText = hud.Add(new TextBlock(new RectTransform(Anchor.BottomLeft, ScreenMargin), DEBUGSCALE,
                PanelColor, PANELPADDING));
        }

        /// <summary>
        /// Shows how many balloons are left. Only called when the count changes.
        /// </summary>
        private void UpdateBalloonText() =>
            _balloonText?.SetText($"Balloons: {_balloonsLeft} / {BALLOONSTOTAL}", BalloonColor);

        /// <summary>
        /// The debug readout lines. The HUD only rebuilds when one of them actually changes
        /// (the frame timings update twice a second).
        /// </summary>
        private void UpdateDebugText()
        {
            if (_debugText == null || !_debugText.Visible)
                return;

            _debugText.SetLine(FRAMELINE, $"Frame: {_frameTimer.AverageFrameMs:0.0} ms ({_frameTimer.AverageFps:0} FPS), " +
                                          $"CPU: {_frameTimer.AverageRenderMs:0.00} ms, GPU: {_frameTimer.AverageGpuMs:0.00} ms",
                ReadoutColor);
            _debugText.SetLine(LOADLINE, $"Load: {_loadMs:0} ms", ReadoutColor);
            _debugText.SetLine(VIEWLINE, $"F1 View: {_debugView}  F2 Lighting: {OnOff(_useLighting)}  " +
                                         $"F3 Culling: {OnOff(_useBackfaceCulling)}  F4 VSync: {OnOff(_useVSync)}",
                ControlsColor);
            _debugText.SetLine(CONTROLSLINE, $"F5 Crater  F6 Water: {OnOff(_useWater)}  F7 Hide debug  Esc: Quit",
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
