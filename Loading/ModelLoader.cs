using Assimp;
using Assimp.Unmanaged;
using GPR202_A3.Debugging;
using GPR202_A3.Graphics;
using GPR202_A3.Utilities;
using OpenTK.Mathematics;
// Assimp has its own Scene, Mesh and Material types, which clash with this project's names,
// so these aliases make it clear which one is meant.
using AssimpScene = Assimp.Scene;
using AssimpMesh = Assimp.Mesh;

namespace GPR202_A3.Loading
{
    /// <summary>
    /// Loads model files (.obj and anything else Assimp supports) into MeshData, ready to upload with Mesh.
    ///
    /// Replaces the import code that was copied into Plane and Balloon. Every model goes through the same
    /// post-processing steps, every part of a file is kept (not just the first mesh), and the native Assimp
    /// library is only pointed at a custom path on macOS, so the same code runs on Windows.
    /// </summary>
    internal static class ModelLoader
    {
        #region Constants
        // Homebrew's Assimp on Apple Silicon. Only used on macOS, and only if the file exists;
        // otherwise (and on Windows) the native library that ships with the NuGet package is used.
        private const string MACASSIMPPATH = "/opt/homebrew/opt/assimp/lib/libassimp.dylib";

        // Post-processing applied to every model as it is imported:
        //  - Triangulate: splits quads and n-gons into triangles (the only primitive Mesh draws)
        //  - JoinIdenticalVertices: OBJ stores a separate vertex per face corner; this merges identical ones
        //    so the index buffer can reuse them (far fewer vertices to upload and transform)
        //  - GenerateSmoothNormals: creates normals for lighting if the file has none
        //  - CalculateTangentSpace: tangents and bitangents for normal mapping
        //  - ImproveCacheLocality: reorders triangles so the GPU's vertex cache is reused more often
        //  - ValidateDataStructure: catches broken files at import instead of at draw time
        // OptimizeMeshes and PreTransformVertices are left out on purpose: they would merge the parts
        // of a model (e.g. the plane's propeller into its body), and the parts must stay separate to animate them.
        private const PostProcessSteps IMPORTSTEPS =
            PostProcessSteps.Triangulate |
            PostProcessSteps.JoinIdenticalVertices |
            PostProcessSteps.GenerateSmoothNormals |
            PostProcessSteps.CalculateTangentSpace |
            PostProcessSteps.ImproveCacheLocality |
            PostProcessSteps.ValidateDataStructure;
        #endregion

        #region Private Fields
        // Whether the native library check has been done (it only needs doing once)
        private static bool _nativeLibraryChecked;
        #endregion

        #region Public API
        /// <summary>
        /// Loads every part of a model file as its own MeshData, keyed by the part's name.
        /// In an .obj file each part is an object ("o Propeller"), so the names match the objects in Blender.
        /// </summary>
        /// <param name="relativePath">The model file, relative to the Resource folder (e.g. "Model/plane.obj").</param>
        /// <returns>One MeshData per part. Empty if the file could not be loaded (the error is logged).</returns>
        public static Dictionary<string, MeshData> LoadParts(string relativePath)
        {
            var parts = new Dictionary<string, MeshData>();
            AssimpScene? scene = Import(relativePath);
            if (scene == null)
                return parts;

            CollectNode(scene, scene.RootNode, parts);

            DebugText.Log($"{relativePath}: parts [{string.Join(", ", parts.Keys)}]", Color4.LightGreen, 6f);
            return parts;
        }

        /// <summary>
        /// Loads every part of a model file merged into one MeshData, for models that never move their parts separately.
        /// </summary>
        /// <param name="relativePath">The model file, relative to the Resource folder (e.g. "Model/balloon.obj").</param>
        /// <returns>The whole model as one MeshData. Empty if the file could not be loaded (the error is logged).</returns>
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
        /// Imports a file with Assimp, applying the post-processing steps above.
        /// </summary>
        /// <param name="relativePath">The model file, relative to the Resource folder.</param>
        /// <returns>The imported scene, or null if it failed (the error is logged).</returns>
        private static AssimpScene? Import(string relativePath)
        {
            EnsureNativeLibrary();

            string path = AssetPaths.Resource(relativePath);
            if (!File.Exists(path))
            {
                DebugText.Log($"Model '{relativePath}' not found", Color4.Red, 10f);
                return null;
            }

            try
            {
                // The context only lives for this import; 'using' releases its unmanaged memory straight away
                using var importer = new AssimpContext();
                return importer.ImportFile(path, IMPORTSTEPS);
            }
            catch (AssimpException e)
            {
                Console.WriteLine($"Assimp failed to import '{path}': {e.Message}");
                DebugText.Log($"Model '{relativePath}' failed to import (see console)", Color4.Red, 10f);
                return null;
            }
        }

        /// <summary>
        /// Points Assimp at Homebrew's native library on macOS (Apple Silicon), once.
        /// On Windows this does nothing, and the win-x64 library from the NuGet package is loaded automatically.
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
        /// Walks the node tree, adding each node's meshes to the part with the node's name.
        /// A part made of several meshes (one per material) is merged into a single MeshData.
        /// </summary>
        /// <param name="scene">The imported scene, which owns the meshes.</param>
        /// <param name="node">The node to collect, then its children.</param>
        /// <param name="parts">The parts found so far, keyed by name.</param>
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
        /// Copies one Assimp mesh onto the end of a MeshData, offsetting its indices past the vertices already there.
        /// </summary>
        /// <param name="data">The MeshData to add to.</param>
        /// <param name="mesh">The Assimp mesh to copy.</param>
        private static void AppendMesh(MeshData data, AssimpMesh mesh)
        {
            // Indices in this mesh start at 0, so they are moved past any vertices already in data
            int vertexOffset = data.VertexCount;

            bool hasUVs = mesh.HasTextureCoords(0);
            bool hasTangents = mesh.HasTangentBasis;

            for (int i = 0; i < mesh.VertexCount; i++)
            {
                data.Positions.Add(ToVector3(mesh.Vertices[i]));
                data.Normals.Add(ToVector3(mesh.Normals[i]));

                // A mesh without UVs still needs one per vertex so the attribute counts match
                if (hasUVs)
                    data.UVs.Add(ToVector2(mesh.TextureCoordinateChannels[0][i]));
                else
                    data.UVs.Add(Vector2.Zero);

                // Assimp's tangents are used if it made them; otherwise Mesh works them out on upload
                if (hasTangents)
                {
                    data.Tangents.Add(ToVector3(mesh.Tangents[i]));
                    data.Bitangents.Add(ToVector3(mesh.BiTangents[i]));
                }
            }

            foreach (Face face in mesh.Faces)
            {
                // Triangulate guarantees 3 indices per face; anything else (points, lines) is skipped
                if (face.IndexCount != 3)
                    continue;

                for (int j = 0; j < 3; j++)
                    data.Indices.Add((uint)(face.Indices[j] + vertexOffset));
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
