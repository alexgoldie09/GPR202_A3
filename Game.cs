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
    /// The game window: loads the scene, runs the game logic in OnUpdateFrame and draws it in OnRenderFrame.
    ///
    /// Balloon Hunt: fly the plane with the mouse and collect every balloon hidden around the landscape.
    /// </summary>
    internal class Game : GameWindow
    {
        #region Constants
        // How many balloons each game starts with
        private const int BALLOONSTOTAL = 8;

        // How high above the centre of the terrain the plane starts, in units
        private const float STARTHEIGHT = 60f;
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

        // Debug view: what the shaders output to the screen (texture, normals, UVs, wireframe, metallic, roughness)
        private static readonly int DebugViewCount = Enum.GetValues<DebugView>().Length;
        private DebugView _debugView;

        // Frame timing readout (frame time, CPU render time and GPU render time)
        private readonly FrameTimer _frameTimer = new();

        // How long OnLoad took, shown on screen to compare optimisations
        private double _loadMs;

        // Scene
        private Camera? _camera;
        private Skybox? _skybox;
        private Terrain? _terrain;
        private Plane? _plane;
        private Balloon? _balloonModel;

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
            DebugText.TextScale = 1.4f;
            _frameTimer.Initialize();

            // --- Render state ---
            GL.ClearColor(new Color4(0.3f, 0.4f, 0.5f, 1f));
            GL.Enable(EnableCap.DepthTest);
            if (_useBackfaceCulling)
                GL.Enable(EnableCap.CullFace);
            // Front faces are wound counter-clockwise (OpenGL's default, and what Blender exports)
            GL.FrontFace(FrontFaceDirection.Ccw);
            VSync = VSyncMode.On;

            // --- Model shader and the uniforms that never change ---
            // Uniforms keep their value until changed, so values that stay the same for the whole run are uploaded once.
            _modelShader = new ShaderProgram("Model.vert", "Model.frag");
            _modelShader.Bind();
            _modelShader.SetVector3("lightDir", new Vector3(-0.4f, -1f, -0.3f)); // sun from above, a little to one side
            _modelShader.SetVector3("lightColor", new Vector3(1f, 0.97f, 0.9f)); // warm daylight
            _modelShader.SetFloat("ambientStrength", 0.5f);
            _modelShader.SetInt("AlbedoMap", Material.ALBEDOMAPUNIT);
            _modelShader.SetInt("NormalMap", Material.NORMALMAPUNIT);
            _modelShader.SetInt("MetallicMap", Material.METALLICMAPUNIT);
            _modelShader.SetInt("RoughnessMap", Material.ROUGHNESSMAPUNIT);
            _modelShader.SetInt("SkyboxMap", Skybox.CUBEMAPUNIT);

            // --- Scene ---
            // Six faces in OpenGL's cube map order: +X, -X, +Y, -Y, +Z, -Z
            _skybox = new Skybox(["Skybox/right.jpg", "Skybox/left.jpg", "Skybox/top.jpg",
                                  "Skybox/bottom.jpg", "Skybox/front.jpg", "Skybox/back.jpg"]);
            _terrain = new Terrain(); // before anything that is placed on the terrain
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
            _skybox?.Delete();
            Material.DeleteDefaults();
            _modelShader?.Delete();
            _frameTimer.Delete();
            DebugText.Shutdown();
        }

        /// <summary>
        /// Keeps the camera's aspect ratio and the debug text layout in step with the window size.
        /// </summary>
        protected override void OnResize(ResizeEventArgs e)
        {
            base.OnResize(e);

            // Window size in screen coordinates. On a Retina/HiDPI display this is smaller than the framebuffer
            // (in pixels), which is why the viewport is set in OnFramebufferResize instead.
            _width = e.Width;
            _height = e.Height;

            _camera?.SetScreenSize(_width, _height);
            UploadProjection();
            DebugText.SetScreenSize(_width, _height);
        }

        /// <summary>
        /// Sets the viewport to the framebuffer's size in pixels (which differs from the window size on Retina displays).
        /// </summary>
        protected override void OnFramebufferResize(FramebufferResizeEventArgs e)
        {
            base.OnFramebufferResize(e);
            GL.Viewport(0, 0, e.Width, e.Height);
        }

        /// <summary>
        /// Game logic: input, the plane, the balloons and the camera.
        /// </summary>
        protected override void OnUpdateFrame(FrameEventArgs args)
        {
            base.OnUpdateFrame(args);

            // Seconds since the last update, which keeps movement frame-rate independent
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

            // Pause the game while the window isn't focused (the cursor is elsewhere)
            if (!IsFocused || _plane == null || _camera == null)
                return;

            _plane.Update(MouseState, _width, _height, deltaTime);
            UpdateBalloons(deltaTime);
            _camera.Follow(_plane.Position, _plane.Forward, deltaTime);
        }

        /// <summary>
        /// Draws the scene: opaque models first, then the terrain, then the sky, then the debug text on top.
        /// </summary>
        protected override void OnRenderFrame(FrameEventArgs args)
        {
            // Time the work this frame issues (stopped before SwapBuffers, which is where VSync waits)
            _frameTimer.BeginFrame();

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            if (_modelShader != null && _camera != null)
            {
                Matrix4 view = _camera.GetViewMatrix(); // shared by every shader this frame

                // Wireframe: rasterise only the triangle edges
                if (_debugView == DebugView.Wireframe)
                    GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);

                // --- Models (plane and balloons) ---
                // Only what can change between frames is uploaded here: the camera and the debug toggles
                _modelShader.Bind();
                _modelShader.SetMatrix4("view", view);
                _modelShader.SetVector3("cameraPos", _camera.Position);
                _modelShader.SetInt("DebugView", (int)_debugView);
                _modelShader.SetInt("UseLighting", _useLighting ? 1 : 0);

                Material.ResetLastApplied();
                _plane?.Render(_modelShader);
                if (_balloonModel != null)
                {
                    foreach (var balloon in _balloons)
                        _balloonModel.Render(_modelShader, balloon);
                }

                // --- Terrain ---
                // Drawn after the small models, so the depth test skips terrain pixels hidden behind them
                _terrain?.Render(view, _debugView);

                // Back to filled polygons, so the sky and debug text aren't drawn as lines
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);

                // --- Skybox ---
                // Drawn last at the far plane: pixels already covered by the scene fail the depth test,
                // so the cube map is only sampled where the sky is actually visible
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

            _plane?.Reset(GetStartPosition(), 0f);
            if (_plane != null)
                _camera?.SnapTo(_plane.Position, _plane.Forward);
        }

        /// <summary>
        /// Updates every balloon, counts newly collected ones, and removes those whose animation has finished.
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
                    DebugText.Log($"Balloon collected! {_balloonsLeft} remaining.", Color4.Yellow, 3f);
                }

                // The original left finished balloons in the list and kept drawing them at scale 0
                if (balloon.Finished)
                    _balloons.RemoveAt(i);
            }
        }

        /// <summary>
        /// The plane's starting point: above the middle of the terrain.
        /// </summary>
        private static Vector3 GetStartPosition() =>
            Terrain.WorldPositionFromHeightMap(Terrain.Width / 2, Terrain.Length / 2) + Vector3.UnitY * STARTHEIGHT;

        /// <summary>
        /// Debug toggles on the function keys, so the gameplay keys stay free.
        /// </summary>
        private void HandleDebugKeys(KeyboardState kb)
        {
            // F1: cycle the debug view (texture - normals - UVs - wireframe - metallic - roughness)
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

            // F4: VSync on/off. Off shows the real frame rate, for measuring optimisations.
            if (kb.IsKeyPressed(Keys.F4))
            {
                _useVSync = !_useVSync;
                VSync = _useVSync ? VSyncMode.On : VSyncMode.Off;
            }
        }

        /// <summary>
        /// Keyed debug lines, updated in place every frame.
        /// </summary>
        private void ShowDebugReadouts()
        {
            DebugText.Log($"Frame: {_frameTimer.AverageFrameMs:0.0} ms ({_frameTimer.AverageFps:0} FPS), " +
                          $"CPU: {_frameTimer.AverageRenderMs:0.00} ms, GPU: {_frameTimer.AverageGpuMs:0.00} ms",
                Color4.White, 0.5f, key: 1);
            DebugText.Log($"Load: {_loadMs:0} ms", Color4.White, 0.5f, key: 2);
            DebugText.Log($"Balloons remaining: {_balloonsLeft}", Color4.DarkGreen, 0.5f, key: 3);
            DebugText.Log($"F1 Debug View: {_debugView}  F2 Lighting: {OnOff(_useLighting)}  " +
                          $"F3 Culling: {OnOff(_useBackfaceCulling)}  F4 VSync: {OnOff(_useVSync)}  Esc: Quit",
                Color4.DarkMagenta, 0.5f, key: 4);
        }

        /// <summary>
        /// Shows the balloons left in the window title (as the original did), only when the count changes.
        /// </summary>
        private void UpdateTitle() => Title = $"{_title}  -  Balloons remaining: {_balloonsLeft}";

        /// <summary>
        /// Uploads the camera's projection matrix to every shader that uses it. Called on load and on resize.
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
        }

        /// <summary>
        /// "ON" or "OFF" for the debug readout.
        /// </summary>
        private static string OnOff(bool value) => value ? "ON" : "OFF";
        #endregion
    }
}
