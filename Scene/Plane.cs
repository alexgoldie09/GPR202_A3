using GPR202_A3.Debugging;
using GPR202_A3.Graphics;
using GPR202_A3.Loading;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace GPR202_A3.Scene
{
    /// <summary>
    /// The player's plane: a body with a propeller that spins, flown with the mouse.
    ///
    /// The cursor's horizontal position from the centre of the window sets how fast the plane turns,
    /// and its vertical position sets how fast it climbs or dives. The plane always flies forward.
    /// The body banks into turns and pitches with the climb, which is only visual (the flight path doesn't change).
    /// </summary>
    internal class Plane : IRenderable
    {
        #region Constants
        // The name of the propeller object in plane.obj ("o Propeller"), which spins on its own
        private const string PROPELLERPART = "Propeller";

        // Flight
        private const float FLYSPEED = 35f; // forward speed, units per second
        private const float TURNRATE = 45f; // heading change at full turn input, degrees per second
        private const float CLIMBRATE = 25.6f; // height change at full lift input, units per second (matches the original game)
        private const float DEADZONE = 0.05f; // inputs this close to the window's centre are ignored

        // Invert the lift input so moving the cursor up makes the plane climb (like a real plane) instead of dive (like a mouse pointer).
        private static readonly bool InvertLift = true;

        // Visual banking and pitching
        private const float MAXBANK = 30f; // roll at full turn input, degrees
        private const float MAXPITCH = 15f; // nose up/down at full lift input, degrees
        private const float TILTRESPONSE = 4f; // how quickly the bank and pitch catch up to the input (higher = snappier)

        // How fast the propeller spins, radians per second
        private const float PROPELLERSPEED = 30f;
        #endregion

        #region Public Fields
        /// <summary>
        /// Where the plane is in the world.
        /// </summary>
        public Vector3 Position => _root.Position;

        /// <summary>
        /// Which way the plane is facing around the vertical axis, in degrees. 0 = towards +Z.
        /// </summary>
        public float Heading { get; private set; }

        /// <summary>
        /// The direction the plane is flying (level, along its heading).
        /// The model's nose points along +Z, so a heading of 0 flies towards +Z.
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
        // Hierarchy: the root holds the plane's position and rotation; the body and propeller hang off it
        private readonly ModelPart _root;
        private readonly ModelPart? _propeller;
        
        // GPU resources this class owns, deleted exactly once in Delete()
        private readonly List<Mesh> _meshes = new();
        private readonly List<Texture> _textures = new();

        // Current visual tilt (degrees) and propeller angle (radians)
        private float _bank;
        private float _pitch;
        private float _propellerAngle;
        
        // Steering input built up from mouse movement, -1..1 on each axis (0 = flying straight)
        private Vector2 _stick;
        #endregion

        #region Constructors
        /// <summary>
        /// Constructor that loads the plane's parts and builds its hierarchy.
        /// </summary>
        /// <param name="startPosition">Where the plane starts in the world.</param>
        public Plane(Vector3 startPosition)
        {
            // One material for the whole plane (the children inherit it from the root).
            // The metal maps tile across the body's UVs, DetailScale times.
            // Metallic and Roughness multiply their maps, so they are 1 to use the maps as they are.
            _root = new ModelPart("Plane")
            {
                Position = startPosition,
                Material = new Material
                {
                    AlbedoMap = LoadTexture("Model/BodyTexture.jpeg", Material.ALBEDOMAPUNIT),
                    NormalMap = LoadTexture("Model/MetalNormalGLTexture.jpg", Material.NORMALMAPUNIT),
                    MetallicMap = LoadTexture("Model/MetalMetallicTexture.jpg", Material.METALLICMAPUNIT),
                    RoughnessMap = LoadTexture("Model/MetalRoughnessTexture.jpg", Material.ROUGHNESSMAPUNIT),
                    Metallic = 1f,
                    Roughness = 1f,
                    DetailScale = 2f
                }
            };

            // Every part except the propeller is static, so they are simply attached to the root
            Dictionary<string, MeshData> parts = ModelLoader.LoadParts("Model/plane.obj");
            foreach (var (name, data) in parts)
            {
                if (name == PROPELLERPART)
                    continue;

                _root.AddChild(new ModelPart(name, CreateMesh(data, name)));
            }

            // The propeller has to spin around its own hub, but OBJ files store every vertex in the model's space
            // (the object origin set in Blender is lost). So the hub is found from the middle of the propeller's bounds,
            // the vertices are moved so the hub sits at (0, 0, 0), and the part is placed back at the hub.
            if (parts.TryGetValue(PROPELLERPART, out MeshData? propellerData))
            {
                var (min, max) = propellerData.GetBounds();
                Vector3 hub = (min + max) * 0.5f;
                propellerData.Translate(-hub);

                _propeller = _root.AddChild(new ModelPart(PROPELLERPART, CreateMesh(propellerData, PROPELLERPART))
                {
                    Position = hub
                });
            }
            else
            {
                DebugText.Log($"plane.obj has no '{PROPELLERPART}' part, so the propeller won't spin", Color4.Orange, 10f);
            }
        }
        #endregion

        #region Public API
        /// <summary>
        /// Steers and moves the plane from the cursor's position, and spins the propeller.
        /// </summary>
        /// <param name="mouse">The current mouse state.</param>
        /// <param name="windowWidth">The window's width in screen coordinates (the same space as the mouse position).</param>
        /// <param name="windowHeight">The window's height in screen coordinates.</param>
        /// <param name="deltaTime">Seconds since the last update.</param>
        public void Update(MouseState mouse, float windowWidth, float windowHeight, float deltaTime)
        {
            // Add this frame's mouse movement to the stick (half the window = full turn or lift).
            // Clamping stops a grabbed cursor's endless movement from building up past full deflection.
            _stick.X = MathHelper.Clamp(_stick.X + mouse.Delta.X / (windowWidth * 0.5f), -1f, 1f);
            _stick.Y = MathHelper.Clamp(_stick.Y + mouse.Delta.Y / (windowHeight * 0.5f), -1f, 1f);

            // Apply the dead zone and response curve
            float turnInput = ShapeInput(_stick.X);
            float liftInput = ShapeInput(_stick.Y);
            if (!InvertLift)
                liftInput = -liftInput;

            // Turning: the cursor to the right turns right. The camera looks along +Z with +Y up,
            // so the screen's right is -X, and turning towards -X lowers the heading.
            Heading -= turnInput * TURNRATE * deltaTime;

            // Moving: always forward along the heading, plus climbing or diving
            _root.Position += Forward * FLYSPEED * deltaTime + Vector3.UnitY * liftInput * CLIMBRATE * deltaTime;

            // Visual tilt eases towards the input (frame-rate independent smoothing)
            float blend = 1f - MathF.Exp(-TILTRESPONSE * deltaTime);
            _bank = MathHelper.Lerp(_bank, turnInput * MAXBANK, blend);
            _pitch = MathHelper.Lerp(_pitch, liftInput * MAXPITCH, blend);

            // Rotations are applied right to left: roll around the nose (Z), then pitch (X), then the heading (Y).
            // A positive roll lowers the right wing (-X), and a negative pitch raises the nose.
            _root.Rotation =
                Quaternion.FromAxisAngle(Vector3.UnitY, MathHelper.DegreesToRadians(Heading)) *
                Quaternion.FromAxisAngle(Vector3.UnitX, MathHelper.DegreesToRadians(-_pitch)) *
                Quaternion.FromAxisAngle(Vector3.UnitZ, MathHelper.DegreesToRadians(_bank));

            // Spin the propeller around its hub (the nose axis), wrapping the angle so it never grows large
            if (_propeller != null)
            {
                _propellerAngle = (_propellerAngle + PROPELLERSPEED * deltaTime) % MathHelper.TwoPi;
                _propeller.Rotation = Quaternion.FromAxisAngle(Vector3.UnitZ, _propellerAngle);
            }
        }

        /// <summary>
        /// Moves the plane to a new position and heading, and levels it out (used when starting a new game).
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
        /// Draws the plane's parts with the model shader.
        /// </summary>
        /// <param name="shader">The bound model shader.</param>
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
        /// Uploads a part's mesh and remembers it so Delete() can free it.
        /// </summary>
        private Mesh CreateMesh(MeshData data, string name)
        {
            var mesh = new Mesh(data, name);
            _meshes.Add(mesh);
            return mesh;
        }

        /// <summary>
        /// Clamps a -1..1 cursor input, removes the dead zone in the middle, and squares it (keeping the sign)
        /// so small movements give fine control and the edges of the window give full turn or lift.
        /// </summary>
        /// <param name="value">The raw input, -1..1 from the centre of the window.</param>
        /// <returns>The shaped input, -1..1.</returns>
        private static float ShapeInput(float value)
        {
            value = MathHelper.Clamp(value, -1f, 1f);

            float magnitude = MathF.Abs(value);
            if (magnitude < DEADZONE)
                return 0f;

            // Rescale so the input starts from 0 at the edge of the dead zone instead of jumping to DEADZONE
            magnitude = (magnitude - DEADZONE) / (1f - DEADZONE);
            return MathF.Sign(value) * magnitude * magnitude;
        }
        
        /// <summary>
        /// Loads a texture for one of the material's units and remembers it so Delete() can free it.
        /// </summary>
        /// <param name="file">The texture file, relative to the Resource folder.</param>
        /// <param name="unit">The texture unit the map binds to (e.g. Material.NORMALMAPUNIT).</param>
        private Texture LoadTexture(string file, int unit)
        {
            var texture = new Texture(file, unit);
            _textures.Add(texture);
            return texture;
        }
        #endregion
    }
}
