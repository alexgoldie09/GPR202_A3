using GPR202_A3.Debugging;
using GPR202_A3.Graphics;
using GPR202_A3.Loading;
using OpenTK.Mathematics;

namespace GPR202_A3.Scene
{
    /// <summary>
    /// A model drawn many times in one draw call per material (instancing).
    /// The mesh is uploaded once, and every copy's model matrix goes in one instance buffer,
    /// so 80 rocks cost the same number of draw calls as 1.
    /// </summary>
    internal class InstancedModel
    {
        #region Public Fields
        /// <summary>
        /// How many copies are drawn.
        /// </summary>
        public int InstanceCount { get; }

        /// <summary>
        /// Draw calls per frame (one per material).
        /// </summary>
        public int DrawCalls => _parts.Count;
        #endregion

        #region Private Fields
        // One mesh per material, each with the material it's drawn with
        private readonly List<(Mesh Mesh, Material Material)> _parts = new();

        // Every copy's model matrix, shared by all the parts
        private readonly VBO _instanceVbo;
        #endregion

        #region Constructors
        /// <summary>
        /// Constructor that loads the model and links the instance matrices to each of its meshes.
        /// </summary>
        /// <param name="file">The model file, relative to the Resource folder.</param>
        /// <param name="materials">The material for each material name in the file (as named in Blender).</param>
        /// <param name="instances">One model matrix per copy.</param>
        public InstancedModel(string file, Dictionary<string, Material> materials, List<Matrix4> instances)
        {
            InstanceCount = instances.Count;
            _instanceVbo = new VBO(instances);

            foreach (var (materialName, data) in ModelLoader.LoadByMaterial(file))
            {
                if (!materials.TryGetValue(materialName, out Material? material))
                {
                    DebugText.Log($"{file}: no material set up for '{materialName}', using the default", Color4.Orange, DebugText.WARNINGDURATION);
                    material = new Material();
                }

                var mesh = new Mesh(data, $"{file} ({materialName})");
                mesh.LinkInstances(_instanceVbo);
                _parts.Add((mesh, material));
            }
        }
        #endregion

        #region Public API
        /// <summary>
        /// Draws every copy, one draw call per material.
        /// </summary>
        /// <param name="shader">The bound instanced model shader.</param>
        public void Render(ShaderProgram shader)
        {
            foreach (var (mesh, material) in _parts)
            {
                material.Apply(shader);
                mesh.DrawInstanced(InstanceCount);
            }
        }

        /// <summary>
        /// Frees the meshes and the instance buffer (the materials' textures belong to whoever made them).
        /// </summary>
        public void Delete()
        {
            foreach (var (mesh, _) in _parts)
                mesh.Delete();
            _parts.Clear();
            _instanceVbo.Delete();
        }
        #endregion
    }
}
