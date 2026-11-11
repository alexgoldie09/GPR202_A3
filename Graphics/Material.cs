using OpenTK.Mathematics;

namespace GPR202_A3.Graphics
{
    /// <summary>
    /// A material is a set of textures and values that describe how a surface looks. The material is applied to a mesh
    /// before drawing, binding its textures to the correct units and uploading its values to the shader.
    /// </summary>
    internal class Material
    {
        #region Texture Unit Constants
        // One texture unit per map.
        public const int ALBEDOMAPUNIT = 0;
        public const int NORMALMAPUNIT = 1;
        public const int METALLICMAPUNIT = 2;
        public const int ROUGHNESSMAPUNIT = 3;
        #endregion

        #region Default Textures
        // Bound in place of a missing map, so every sampler always has a texture.
        private static Texture? _defaultAlbedo;
        private static Texture? _defaultNormal;
        private static Texture? _defaultMetallic;
        private static Texture? _defaultRoughness;
        #endregion

        #region Redundant State Tracking
        // The material applied most recently.
        private static Material? _lastApplied;

        /// <summary>
        /// How many materials were actually applied (bound and uploaded) since the last reset.
        /// </summary>
        public static int AppliedCount { get; private set; }

        /// <summary>
        /// How many applies were skipped since the last reset, because that material was already current.
        /// </summary>
        public static int SkippedCount { get; private set; }
        #endregion

        #region Public Fields
        /// <summary>
        /// The surface colour (albedo) map, e.g. a model's baked atlas, or null to use Colour alone.
        /// Must be created for unit ALBEDOMAPUNIT.
        /// </summary>
        public Texture? AlbedoMap { get; set; }

        /// <summary>
        /// Tangent-space normal map (OpenGL convention, green = +Y), or null to use the mesh normals.
        /// Must be created for unit NORMALMAPUNIT.
        /// </summary>
        public Texture? NormalMap { get; set; }

        /// <summary>
        /// Greyscale metallic map (white = metal), or null to use Metallic alone.
        /// Must be created for unit METALLICMAPUNIT.
        /// </summary>
        public Texture? MetallicMap { get; set; }

        /// <summary>
        /// Greyscale roughness map (black = smooth, white = rough), or null to use Roughness alone.
        /// Must be created for unit ROUGHNESSMAPUNIT.
        /// </summary>
        public Texture? RoughnessMap { get; set; }

        /// <summary>
        /// Colour tint, multiplying the albedo map if there is one (white = the map's colours unchanged).
        /// </summary>
        public Vector3 Colour { get; set; } = Vector3.One;

        /// <summary>
        /// How metallic the surface is, 0 to 1 (multiplies the metallic map if there is one).
        /// </summary>
        public float Metallic { get; set; } = 0f;

        /// <summary>
        /// How rough the surface is, 0 to 1 (multiplies the roughness map if there is one).
        /// 1 = fully matte, no highlight.
        /// </summary>
        public float Roughness { get; set; } = 1f;

        /// <summary>
        /// How strongly the normal map bends the surface: 0 = flat, 1 = as authored.
        /// </summary>
        public float NormalStrength { get; set; } = 1f;

        /// <summary>
        /// How many times the detail maps repeat across the surface. 1 = as authored, 2 = twice as many repeats, etc.
        /// </summary>
        public float DetailScale { get; set; } = 1f;
        #endregion

        #region Public API
        /// <summary>
        /// Applies this material to the currently bound shader, binding its textures and uploading its values.
        /// </summary>
        /// <param name="shader">The bound model shader.</param>
        public void Apply(ShaderProgram shader)
        {
            // Skip redundant applies: if the last applied material is this one, nothing needs to change.
            if (_lastApplied == this)
            {
                SkippedCount++;
                return;
            }

            _lastApplied = this;
            AppliedCount++;

            // Create the shared default textures
            if (_defaultAlbedo == null)
                _defaultAlbedo = new Texture(255, 255, 255, ALBEDOMAPUNIT);
            if (_defaultNormal == null)
                _defaultNormal = new Texture(128, 128, 255, NORMALMAPUNIT);
            if (_defaultMetallic == null)
                _defaultMetallic = new Texture(255, 255, 255, METALLICMAPUNIT);
            if (_defaultRoughness == null)
                _defaultRoughness = new Texture(255, 255, 255, ROUGHNESSMAPUNIT);

            // Each texture binds to the unit it was created for
            BindOrDefault(AlbedoMap, _defaultAlbedo);
            BindOrDefault(NormalMap, _defaultNormal);
            BindOrDefault(MetallicMap, _defaultMetallic);
            BindOrDefault(RoughnessMap, _defaultRoughness);

            // Upload the material values to the shader
            shader.SetVector3("albedoFactor", Colour);
            shader.SetFloat("metallicFactor", Metallic);
            shader.SetFloat("roughnessFactor", Roughness);
            shader.SetFloat("normalStrength", NormalStrength);
            shader.SetFloat("detailScale", DetailScale);
        }

        /// <summary>
        /// Forgets the current material and resets the counts. Call at the start of each frame's scene drawing.
        /// </summary>
        public static void ResetLastApplied()
        {
            _lastApplied = null;
            AppliedCount = 0;
            SkippedCount = 0;
        }

        /// <summary>
        /// Frees the shared default textures. Call once when closing, after the scene is deleted.
        /// </summary>
        public static void DeleteDefaults()
        {
            _lastApplied = null;

            if (_defaultAlbedo != null)
            {
                _defaultAlbedo.Delete();
                _defaultAlbedo = null;
            }

            if (_defaultNormal != null)
            {
                _defaultNormal.Delete();
                _defaultNormal = null;
            }

            if (_defaultMetallic != null)
            {
                _defaultMetallic.Delete();
                _defaultMetallic = null;
            }

            if (_defaultRoughness != null)
            {
                _defaultRoughness.Delete();
                _defaultRoughness = null;
            }
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Binds a map to its texture unit, or the default texture when the material has no such map.
        /// </summary>
        /// <param name="map">The material's map, or null.</param>
        /// <param name="fallback">The neutral default to bind instead (created for the same unit).</param>
        private static void BindOrDefault(Texture? map, Texture fallback)
        {
            if (map != null)
                map.Bind();
            else
                fallback.Bind();
        }
        #endregion
    }
}
