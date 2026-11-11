using Assimp;
using Assimp.Unmanaged;
using GPR202_A3.Debugging;
using GPR202_A3.Graphics;
using GPR202_A3.Utilities;
using OpenTK.Mathematics;
// Assimp has its own Scene and Mesh types, so these aliases make it clear which one is meant
using AssimpScene = Assimp.Scene;
using AssimpMesh = Assimp.Mesh;

namespace GPR202_A3.Loading
{
    /// <summary>
    /// Loads model files with Assimp into MeshData, ready to upload with Mesh.
    /// Every model gets the same post-processing, and each part of a file can be kept separate.
    /// </summary>
    internal static class ModelLoader
    {
        #region Constants
        // Homebrew's Assimp on Apple Silicon. Only used on macOS if the file exists; Windows uses the NuGet package's copy.
        private const string MACASSIMPPATH = "/opt/homebrew/opt/assimp/lib/libassimp.dylib";

        // Post-processing applied to every model:
        //  - Triangulate: turns quads and n-gons into triangles
        //  - JoinIdenticalVertices: merges duplicate vertices so the index buffer can reuse them
        //  - GenerateSmoothNormals: makes normals if the file has none
        //  - CalculateTangentSpace: tangents and bitangents for normal mapping
        //  - ImproveCacheLocality: reorders triangles so the GPU reuses vertices it has just processed
        //  - ValidateDataStructure: catches broken files at load instead of at draw time
        // OptimizeMeshes and PreTransformVertices are left out because they merge parts (e.g. the propeller into the body).
        private const PostProcessSteps IMPORTSTEPS =
            PostProcessSteps.Triangulate |
            PostProcessSteps.JoinIdenticalVertices |
            PostProcessSteps.GenerateSmoothNormals |
            PostProcessSteps.CalculateTangentSpace |
            PostProcessSteps.ImproveCacheLocality |
            PostProcessSteps.ValidateDataStructure;

        // Indices in one triangle
        private const int TRIANGLEINDICES = 3;
        #endregion

        #region Private Fields
        // Whether the native library has been set up yet (only needed once)
        private static bool _nativeLibraryChecked;
        #endregion

        #region Public API
        /// <summary>
        /// Loads each part of a model file as its own MeshData, keyed by the part's name
        /// (in an .obj file, the object names from Blender, e.g. "Propeller").
        /// </summary>
        /// <param name="relativePath">The model file, relative to the Resource folder.</param>
        /// <returns>One MeshData per part, or none if the file failed to load.</returns>
        public static Dictionary<string, MeshData> LoadParts(string relativePath)
        {
            var parts = new Dictionary<string, MeshData>();
            AssimpScene? scene = Import(relativePath);
            if (scene == null)
                return parts;

            CollectNode(scene, scene.RootNode, parts);

            DebugText.Log($"{relativePath}: parts [{string.Join(", ", parts.Keys)}]", Color4.LightGreen, DebugText.INFODURATION);
            return parts;
        }

        /// <summary>
        /// Loads a whole model file as one MeshData, for models whose parts never move separately.
        /// </summary>
        /// <param name="relativePath">The model file, relative to the Resource folder.</param>
        /// <returns>The model, or an empty MeshData if the file failed to load.</returns>
        public static MeshData Load(string relativePath)
        {
            var merged = new MeshData();
            AssimpScene? scene = Import(relativePath);
            if (scene == null)
                return merged;

            foreach (AssimpMesh mesh in scene.Meshes)
                AppendMesh(merged, mesh);

            return merged;
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Imports a file with Assimp.
        /// </summary>
        /// <returns>The imported scene, or null if it failed (the error is logged).</returns>
        private static AssimpScene? Import(string relativePath)
        {
            EnsureNativeLibrary();

            string path = AssetPaths.Resource(relativePath);
            if (!File.Exists(path))
            {
                DebugText.Log($"Model '{relativePath}' not found", Color4.Red, DebugText.WARNINGDURATION);
                return null;
            }

            try
            {
                // 'using' frees Assimp's unmanaged memory as soon as the import is done
                using var importer = new AssimpContext();
                return importer.ImportFile(path, IMPORTSTEPS);
            }
            catch (AssimpException e)
            {
                Console.WriteLine($"Assimp failed to import '{path}': {e.Message}");
                DebugText.Log($"Model '{relativePath}' failed to import (see console)", Color4.Red, DebugText.WARNINGDURATION);
                return null;
            }
        }

        /// <summary>
        /// On macOS, points Assimp at Homebrew's library. Does nothing on Windows.
        /// </summary>
        private static void EnsureNativeLibrary()
        {
            if (_nativeLibraryChecked)
                return;

            _nativeLibraryChecked = true;

            AssimpLibrary library = AssimpLibrary.Instance;
            if (OperatingSystem.IsMacOS() && !library.IsLibraryLoaded && File.Exists(MACASSIMPPATH))
                library.LoadLibrary(MACASSIMPPATH);
        }

        /// <summary>
        /// Adds each node's meshes to the part with the node's name, then does the same for its children.
        /// </summary>
        private static void CollectNode(AssimpScene scene, Node node, Dictionary<string, MeshData> parts)
        {
            if (node.HasMeshes)
            {
                if (!parts.TryGetValue(node.Name, out MeshData? part))
                {
                    part = new MeshData();
                    parts[node.Name] = part;
                }

                foreach (int meshIndex in node.MeshIndices)
                    AppendMesh(part, scene.Meshes[meshIndex]);
            }

            foreach (Node child in node.Children)
                CollectNode(scene, child, parts);
        }

        /// <summary>
        /// Copies an Assimp mesh onto the end of a MeshData.
        /// </summary>
        private static void AppendMesh(MeshData data, AssimpMesh mesh)
        {
            // This mesh's indices start at 0, so they are moved past the vertices already in data
            int vertexOffset = data.VertexCount;

            bool hasUVs = mesh.HasTextureCoords(0);
            bool hasTangents = mesh.HasTangentBasis;

            for (int i = 0; i < mesh.VertexCount; i++)
            {
                data.Positions.Add(ToVector3(mesh.Vertices[i]));
                data.Normals.Add(ToVector3(mesh.Normals[i]));

                // Every vertex needs a UV so the attribute counts match
                if (hasUVs)
                    data.UVs.Add(ToVector2(mesh.TextureCoordinateChannels[0][i]));
                else
                    data.UVs.Add(Vector2.Zero);

                // If Assimp didn't make tangents, Mesh works them out on upload
                if (hasTangents)
                {
                    data.Tangents.Add(ToVector3(mesh.Tangents[i]));
                    data.Bitangents.Add(ToVector3(mesh.BiTangents[i]));
                }
            }

            foreach (Face face in mesh.Faces)
            {
                // Skip anything that isn't a triangle (points or lines)
                if (face.IndexCount != TRIANGLEINDICES)
                    continue;

                foreach (int index in face.Indices)
                    data.Indices.Add((uint)(index + vertexOffset));
            }
        }

        /// <summary>
        /// Converts an Assimp vector to an OpenTK one.
        /// </summary>
        private static Vector3 ToVector3(Vector3D v) => new(v.X, v.Y, v.Z);

        /// <summary>
        /// Converts an Assimp texture coordinate (stored as 3D) to a 2D UV.
        /// </summary>
        private static Vector2 ToVector2(Vector3D v) => new(v.X, v.Y);
        #endregion
    }
}
