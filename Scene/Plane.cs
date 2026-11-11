using GPR202_A3.Debugging;
using GPR202_A3.Graphics;
using GPR202_A3.Loading;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace GPR202_A3.Scene
{
    /// <summary>
    /// The player's plane, flown with the mouse. Mouse movement moves a virtual stick:
    /// left/right turns, and up/down climbs or dives. The plane always flies forward,
    /// banks and pitches with the stick, and its propeller spins.
    /// </summary>
    internal class Plane : IRenderable
    {
        #region Constants
        // Name of the propeller object in plane.obj
        private const string PROPELLERPART = "Propeller";

        // Flight
        private const float FLYSPEED = 35f; // forward speed, units per second
        private const float TURNRATE = 45f; // degrees per second at full stick
        private const float CLIMBRATE = 25.6f; // units per second at full stick (the same as the original game)

        // Stick
        private const float STICKRANGE = 0.5f; // moving the mouse this fraction of the window gives full stick
        private const float DEADZONE = 0.05f; // stick values smaller than this are ignored

        // true: moving the mouse down (pulling back, like a flight stick) climbs. false: moving it up climbs.
        // (static readonly instead of const, so the compiler doesn't warn that one branch never runs)
        private static readonly bool PullBackToClimb = true;

        // Visual tilt
        private const float MAXBANK = 30f; // roll at full turn, degrees
        private const float MAXPITCH = 15f; // nose up/down at full climb, degrees
        private const float TILTRESPONSE = 4f; // how quickly the tilt catches up (higher = snappier)

        // Propeller spin, radians per second
        private const float PROPELLERSPEED = 30f;

        // Material: Metallic and Roughness multiply their maps, so 1 uses the maps as they are
        private const float METALLIC = 1f;
        private const float ROUGHNESS = 1f;
        private const float DETAILSCALE = 2f; // how many times the metal maps repeat across the body
        #endregion

        #region Public Fields
        /// <summary>
        /// Where the plane is in the world.
        /// </summary>
        public Vector3 Position => _root.Position;

        /// <summary>
        /// Which way the plane faces, in degrees. 0 = towards +Z.
        /// </summary>
        public float Heading { get; private set; }

        /// <summary>
        /// The direction the plane is flying (level). The model's nose points along +Z.
        /// </summary>
        public Vector3 Forward
        {
            get
            {
                float heading = MathHelper.DegreesToRadians(Heading);
                return new Vector3(MathF.Sin(heading), 0f, MathF.Cos(heading));
            }
        }
        #endregion

        #region Private Fields
        // The root holds the plane's position and rotation; the body parts and propeller are its children
        private readonly ModelPart _root;
        private readonly ModelPart? _propeller;

        // GPU resources this class owns, deleted in Delete()
        private readonly List<Mesh> _meshes = new();
        private readonly List<Texture> _textures = new();

        // Current tilt (degrees) and propeller angle (radians)
        private float _bank;
        private float _pitch;
        private float _propellerAngle;

        // Steering built up from mouse movement, -1..1 on each axis (0 = flying straight)
        private Vector2 _stick;
        #endregion

        #region Constructors
        /// <summary>
        /// Constructor that loads the plane's parts and textures.
        /// </summary>
        /// <param name="startPosition">Where the plane starts.</param>
        public Plane(Vector3 startPosition)
        {
            // One material on the root; the parts inherit it
            _root = new ModelPart("Plane")
            {
                Position = startPosition,
                Material = new Material
                {
                    AlbedoMap = LoadTexture("Model/BodyTexture.jpeg", Material.ALBEDOMAPUNIT),
                    NormalMap = LoadTexture("Model/MetalNormalGLTexture.jpg", Material.NORMALMAPUNIT),
                    MetallicMap = LoadTexture("Model/MetalMetallicTexture.jpg", Material.METALLICMAPUNIT),
                    RoughnessMap = LoadTexture("Model/MetalRoughnessTexture.jpg", Material.ROUGHNESSMAPUNIT),
                    Metallic = METALLIC,
                    Roughness = ROUGHNESS,
                    DetailScale = DETAILSCALE
                }
            };

            // Every part except the propeller is attached to the root as it is
            Dictionary<string, MeshData> parts = ModelLoader.LoadParts("Model/plane.obj");
            foreach (var (name, data) in parts)
            {
                if (name == PROPELLERPART)
                    continue;

                _root.AddChild(new ModelPart(name, CreateMesh(data, name)));
            }

            // The propeller spins around its hub, but OBJ files don't keep Blender's object origin.
            // So the hub is the middle of the propeller's bounds: the vertices are moved to centre it on (0, 0, 0),
            // and the part is placed back at the hub.
            if (parts.TryGetValue(PROPELLERPART, out MeshData? propellerData))
            {
                var (min, max) = propellerData.GetBounds();
                Vector3 hub = (min + max) / 2f;
                propellerData.Translate(-hub);

                _propeller = _root.AddChild(new ModelPart(PROPELLERPART, CreateMesh(propellerData, PROPELLERPART))
                {
                    Position = hub
                });
            }
            else
            {
                DebugText.Log($"plane.obj has no '{PROPELLERPART}' part, so the propeller won't spin", Color4.Orange, DebugText.WARNINGDURATION);
            }
        }
        #endregion

        #region Public API
        /// <summary>
        /// Steers and moves the plane, and spins the propeller.
        /// </summary>
        /// <param name="mouse">The current mouse state.</param>
        /// <param name="windowWidth">The window's width in screen coordinates (the same units as mouse movement).</param>
        /// <param name="windowHeight">The window's height in screen coordinates.</param>
        /// <param name="deltaTime">Seconds since the last update.</param>
        public void Update(MouseState mouse, float windowWidth, float windowHeight, float deltaTime)
        {
            // Add this frame's mouse movement to the stick. Clamping stops a grabbed cursor
            // (which has no edges) from pushing the stick past full.
            _stick.X = MathHelper.Clamp(_stick.X + mouse.Delta.X / (windowWidth * STICKRANGE), -1f, 1f);
            _stick.Y = MathHelper.Clamp(_stick.Y + mouse.Delta.Y / (windowHeight * STICKRANGE), -1f, 1f);

            float turnInput = ShapeInput(_stick.X);
            float liftInput = ShapeInput(_stick.Y); // mouse down is positive
            if (!PullBackToClimb)
                liftInput = -liftInput;

            // Screen right is world -X (the camera looks along +Z), so turning right lowers the heading
            Heading -= turnInput * TURNRATE * deltaTime;

            // Always fly forward, plus climb or dive
            _root.Position += Forward * FLYSPEED * deltaTime + Vector3.UnitY * liftInput * CLIMBRATE * deltaTime;

            // Ease the tilt towards the stick (the same amount per second at any frame rate)
            float blend = 1f - MathF.Exp(-TILTRESPONSE * deltaTime);
            _bank = MathHelper.Lerp(_bank, turnInput * MAXBANK, blend);
            _pitch = MathHelper.Lerp(_pitch, liftInput * MAXPITCH, blend);

            // Roll around the nose (Z), then pitch (X), then turn to the heading (Y).
            // Positive roll lowers the right wing; negative pitch raises the nose.
            _root.Rotation =
                Quaternion.FromAxisAngle(Vector3.UnitY, MathHelper.DegreesToRadians(Heading)) *
                Quaternion.FromAxisAngle(Vector3.UnitX, MathHelper.DegreesToRadians(-_pitch)) *
                Quaternion.FromAxisAngle(Vector3.UnitZ, MathHelper.DegreesToRadians(_bank));

            // Spin the propeller around the nose, wrapping the angle so it stays small
            if (_propeller != null)
            {
                _propellerAngle = (_propellerAngle + PROPELLERSPEED * deltaTime) % MathHelper.TwoPi;
                _propeller.Rotation = Quaternion.FromAxisAngle(Vector3.UnitZ, _propellerAngle);
            }
        }

        /// <summary>
        /// Moves the plane and levels it out, for a new game.
        /// </summary>
        /// <param name="position">The new position.</param>
        /// <param name="heading">The new heading, in degrees.</param>
        public void Reset(Vector3 position, float heading)
        {
            _root.Position = position;
            Heading = heading;
            _bank = 0f;
            _pitch = 0f;
            _stick = Vector2.Zero;
            _root.Rotation = Quaternion.FromAxisAngle(Vector3.UnitY, MathHelper.DegreesToRadians(Heading));
        }
        #endregion

        #region IRenderable Functions
        /// <summary>
        /// Draws the plane with the model shader.
        /// </summary>
        public void Render(ShaderProgram shader) => _root.Render(shader, Matrix4.Identity);

        /// <summary>
        /// Frees the plane's meshes and textures.
        /// </summary>
        public void Delete()
        {
            foreach (var mesh in _meshes)
                mesh.Delete();
            _meshes.Clear();

            foreach (var texture in _textures)
                texture.Delete();
            _textures.Clear();
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Uploads a part's mesh and keeps it so Delete() can free it.
        /// </summary>
        private Mesh CreateMesh(MeshData data, string name)
        {
            var mesh = new Mesh(data, name);
            _meshes.Add(mesh);
            return mesh;
        }

        /// <summary>
        /// Loads a texture for the material and keeps it so Delete() can free it.
        /// </summary>
        /// <param name="file">The texture file, relative to the Resource folder.</param>
        /// <param name="unit">The texture unit it binds to.</param>
        private Texture LoadTexture(string file, int unit)
        {
            var texture = new Texture(file, unit);
            _textures.Add(texture);
            return texture;
        }

        /// <summary>
        /// Removes the dead zone and squares the stick value (keeping its sign),
        /// so small movements give fine control and full stick gives full turn or climb.
        /// </summary>
        /// <param name="value">The stick value, -1..1.</param>
        /// <returns>The shaped value, -1..1.</returns>
        private static float ShapeInput(float value)
        {
            float magnitude = MathF.Abs(value);
            if (magnitude < DEADZONE)
                return 0f;

            // Start from 0 at the edge of the dead zone instead of jumping straight to DEADZONE
            magnitude = (magnitude - DEADZONE) / (1f - DEADZONE);
            return MathF.Sign(value) * magnitude * magnitude;
        }
        #endregion
    }
}
