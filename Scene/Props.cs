using GPR202_A3.Debugging;
using GPR202_A3.Generation;
using GPR202_A3.Graphics;
using OpenTK.Mathematics;

namespace GPR202_A3.Scene
{
    /// <summary>
    /// The desert's props: palm trees around the oasis, and rocks around its rim and across the dunes.
    /// They're placed with a seeded random number generator and the rules below, and drawn with instancing.
    /// </summary>
    internal class Props
    {
        #region Constants
        // Seed for the placement (change it for a different, but repeatable, layout)
        private const int SEED = 2026;

        // Palms: a ring around the oasis, between the shore and the rim
        private static readonly ScatterRules PalmRules = new()
        {
            Count = 20, MinDistance = 112f, MaxDistance = 175f, Spacing = 14f, MaxSlope = 30f,
            MinHeightAboveWater = 0.5f, MinScale = 1.8f, MaxScale = 2.6f, MaxTilt = 10f, SinkDepth = 0.3f
        };

        // Small rocks dotted around the oasis rim
        private static readonly ScatterRules RimRockRules = new()
        {
            Count = 12, MinDistance = 150f, MaxDistance = 240f, Spacing = 18f, MaxSlope = 35f,
            MinHeightAboveWater = 1f, MinScale = 0.5f, MaxScale = 1.2f, MaxTilt = 25f, SinkDepth = 0.8f
        };

        // Rocks across the desert (RockOne is about 5.5 units across, RockTwo about 14)
        private static readonly ScatterRules DesertRockOneRules = new()
        {
            Count = 70, MinDistance = 240f, MaxDistance = 1500f, Spacing = 22f, MaxSlope = 35f,
            MinHeightAboveWater = 1f, MinScale = 0.8f, MaxScale = 2.5f, MaxTilt = 25f, SinkDepth = 0.8f
        };
        private static readonly ScatterRules DesertRockTwoRules = new()
        {
            Count = 40, MinDistance = 240f, MaxDistance = 1500f, Spacing = 32f, MaxSlope = 30f,
            MinHeightAboveWater = 1f, MinScale = 0.5f, MaxScale = 1.4f, MaxTilt = 20f, SinkDepth = 1.5f
        };

        // Leaves: pixels less opaque than this are cut out
        private const float LEAFALPHACUTOFF = 0.5f;

        // Surface roughness (1 = fully matte)
        private const float BARKROUGHNESS = 0.9f;
        private const float LEAFROUGHNESS = 0.8f;
        private const float ROCKROUGHNESS = 1f; // multiplies the roughness map, so 1 uses the map as it is
        #endregion

        #region Public Fields
        /// <summary>
        /// Total props placed.
        /// </summary>
        public int InstanceCount { get; }

        /// <summary>
        /// Draw calls per frame for every prop.
        /// </summary>
        public int DrawCalls { get; }
        #endregion

        #region Private Fields
        // GPU resources this class owns, deleted in Delete()
        private readonly List<InstancedModel> _models = new();
        private readonly List<Texture> _textures = new();
        #endregion

        #region Constructors
        /// <summary>
        /// Constructor that sets up the materials, places every prop, and uploads them.
        /// Call after the terrain exists (placement reads its heights).
        /// </summary>
        public Props()
        {
            // --- Materials (keyed by the material names from Blender) ---
            var rock = new Material
            {
                AlbedoMap = LoadTexture("Props/RockColour.jpg", Material.ALBEDOMAPUNIT),
                NormalMap = LoadTexture("Props/RockNormalGL.jpg", Material.NORMALMAPUNIT),
                RoughnessMap = LoadTexture("Props/RockRoughness.jpg", Material.ROUGHNESSMAPUNIT),
                Roughness = ROCKROUGHNESS
            };
            var rockMaterials = new Dictionary<string, Material> { ["Rock"] = rock };

            var palmMaterials = new Dictionary<string, Material>
            {
                ["PalmTree"] = new Material
                {
                    AlbedoMap = LoadTexture("Props/PalmBarkColour.png", Material.ALBEDOMAPUNIT),
                    Roughness = BARKROUGHNESS
                },
                ["PalmTop"] = new Material
                {
                    AlbedoMap = LoadTexture("Props/PalmTopColour.png", Material.ALBEDOMAPUNIT),
                    Roughness = BARKROUGHNESS
                },
                ["Leaf"] = new Material
                {
                    AlbedoMap = LoadTexture("Props/PalmLeaf.png", Material.ALBEDOMAPUNIT),
                    Roughness = LEAFROUGHNESS,
                    AlphaCutoff = LEAFALPHACUTOFF,
                    DoubleSided = true
                }
            };

            // --- Placement: palms first, so the rocks fit around them ---
            var random = new Random(SEED);
            var placed = new List<(Vector2 Position, float Spacing)>();
            List<Matrix4> palms = PropScatter.Scatter(PalmRules, random, placed);
            List<Matrix4> rimRocks = PropScatter.Scatter(RimRockRules, random, placed);
            List<Matrix4> rockOnes = PropScatter.Scatter(DesertRockOneRules, random, placed);
            List<Matrix4> rockTwos = PropScatter.Scatter(DesertRockTwoRules, random, placed);

            // The rim rocks are small RockOnes, so they're drawn with the desert RockOnes
            rockOnes.AddRange(rimRocks);

            // --- Upload ---
            _models.Add(new InstancedModel("Props/PalmTree.obj", palmMaterials, palms));
            _models.Add(new InstancedModel("Props/RockOne.obj", rockMaterials, rockOnes));
            _models.Add(new InstancedModel("Props/RockTwo.obj", rockMaterials, rockTwos));

            foreach (InstancedModel model in _models)
            {
                InstanceCount += model.InstanceCount;
                DrawCalls += model.DrawCalls;
            }

            DebugText.Log($"Props: {palms.Count} palms, {rockOnes.Count + rockTwos.Count} rocks, {DrawCalls} draw calls",
                Color4.LightGreen, DebugText.INFODURATION);
        }
        #endregion

        #region Public API
        /// <summary>
        /// Draws every prop.
        /// </summary>
        /// <param name="shader">The bound instanced model shader.</param>
        public void Render(ShaderProgram shader)
        {
            foreach (InstancedModel model in _models)
                model.Render(shader);

            // The leaves turn culling off; put it back for whatever draws next
            Material.RestoreCulling();
        }

        /// <summary>
        /// Frees the props' meshes and textures.
        /// </summary>
        public void Delete()
        {
            foreach (InstancedModel model in _models)
                model.Delete();
            _models.Clear();

            foreach (Texture texture in _textures)
                texture.Delete();
            _textures.Clear();
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Loads a texture and keeps it so Delete() can free it.
        /// </summary>
        private Texture LoadTexture(string file, int unit)
        {
            var texture = new Texture(file, unit);
            _textures.Add(texture);
            return texture;
        }
        #endregion
    }
}
