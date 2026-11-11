using GPR202_A3.Debugging;
using OpenTK.Mathematics;

namespace GPR202_A3.Graphics
{
    /// <summary>
    /// Represents the raw data of a 3D mesh, including vertex positions, texture coordinates, normals, tangents, bitangents, and indices.
    ///
    /// This class is used to store mesh data on the CPU before it is uploaded to the GPU for rendering.
    /// </summary>
    internal class MeshData
    {
        #region Constants
        // Below this, a triangle's UVs have no area, so it can't give a tangent direction
        private const float MINUVAREA = 1e-8f;

        // Below this (squared length), a summed tangent is treated as zero
        private const float MINTANGENTLENGTHSQUARED = 1e-12f;

        // If the normal is closer than this to straight up, X is used to build the fallback tangent instead of Y
        // (crossing two almost-parallel vectors gives a tiny, unreliable result)
        private const float NEARLYVERTICAL = 0.99f;
        #endregion

        #region Public Fields
        /// <summary>
        /// Vertex positions in the model's local space.
        /// </summary>
        public List<Vector3> Positions { get; } = new();
        
        /// <summary>
        /// Texture coordinates for each vertex, used for mapping textures onto the mesh (one per vertex).
        /// </summary>
        public List<Vector2> UVs { get; } = new();
        
        /// <summary>
        /// Normals for each vertex, used for lighting calculations (one per vertex).
        /// </summary>
        public List<Vector3> Normals { get; } = new();
        
        /// <summary>
        /// Tangents for each vertex: the direction the texture's U axis runs across the surface.
        /// </summary>
        public List<Vector3> Tangents { get; } = new();

        /// <summary>
        /// Bitangents for each vertex: the direction the texture's V axis runs across the surface.
        /// </summary>
        public List<Vector3> Bitangents { get; } = new();
        
        /// <summary>
        /// Indices defining the order in which vertices are connected to form triangles (three indices per triangle).
        /// </summary>
        public List<uint> Indices { get; } = new();
        
        /// <summary>
        /// The number of unique vertices in the mesh.
        /// </summary>
        public int VertexCount => Positions.Count;
        
        /// <summary>
        /// The number of indices in the mesh, which is typically three per triangle.
        /// </summary>
        public int IndexCount => Indices.Count;
        #endregion

        #region Public API
        /// <summary>
        /// Works out the axis-aligned box that contains every vertex.
        /// Used to find a part's centre (e.g. the propeller hub), and later for collision boxes.
        /// </summary>
        /// <returns>The smallest and largest corner of the box. Both are zero for an empty mesh.</returns>
        public (Vector3 Min, Vector3 Max) GetBounds()
        {
            if (VertexCount == 0)
                return (Vector3.Zero, Vector3.Zero);

            Vector3 min = Positions[0];
            Vector3 max = Positions[0];
            foreach (Vector3 position in Positions)
            {
                min = Vector3.ComponentMin(min, position);
                max = Vector3.ComponentMax(max, position);
            }

            return (min, max);
        }

        /// <summary>
        /// Moves every vertex by the same offset, effectively translating the entire mesh in 3D space.
        /// </summary>
        /// <param name="offset">The amount to move every vertex by.</param>
        public void Translate(Vector3 offset)
        {
            for(int i = 0; i < Positions.Count; i++)
                Positions[i] += offset;
        }
        
        /// <summary>
        /// Works out a tangent and bitangent for every vertex from the triangles' positions and UVs.
        /// For each triangle, the two edges are expressed in terms of how far U and V change along them.
        /// Each vertex then averages the results of the triangles that share it.
        /// </summary>
        public void ComputeTangents()
        {
            var tangentSums = new Vector3[VertexCount];
            var bitangentSums = new Vector3[VertexCount];

            // For every triangle, work out the tangent and bitangent directions and add them to the sums for each vertex
            for (int i = 0; i + 2 < Indices.Count; i += 3)
            {
                int i0 = (int)Indices[i], i1 = (int)Indices[i + 1], i2 = (int)Indices[i + 2];

                Vector3 edge1 = Positions[i1] - Positions[i0];
                Vector3 edge2 = Positions[i2] - Positions[i0];
                Vector2 deltaUV1 = UVs[i1] - UVs[i0];
                Vector2 deltaUV2 = UVs[i2] - UVs[i0];

                // A triangle whose UVs have no area can't define a direction, so it is skipped
                float determinant = deltaUV1.X * deltaUV2.Y - deltaUV2.X * deltaUV1.Y;
                if (MathF.Abs(determinant) < MINUVAREA)
                    continue;

                // The tangent and bitangent are scaled by the inverse of the determinant to account for the UV area
                float f = 1f / determinant;
                Vector3 tangent = f * (deltaUV2.Y * edge1 - deltaUV1.Y * edge2);
                Vector3 bitangent = f * (-deltaUV2.X * edge1 + deltaUV1.X * edge2);

                foreach (int index in new[] { i0, i1, i2 })
                {
                    tangentSums[index] += tangent;
                    bitangentSums[index] += bitangent;
                }
            }

            // Clear the existing tangents and bitangents and normalize the summed vectors for each vertex
            Tangents.Clear();
            Bitangents.Clear();
            
            for (int v = 0; v < VertexCount; v++)
            {
                Vector3 tangent = tangentSums[v];
                Vector3 bitangent = bitangentSums[v];

                // Fallback for vertices only touched by skipped triangles: any pair of directions
                // perpendicular to the normal (the normal map then has no meaningful orientation there)
                if (tangent.LengthSquared < MINTANGENTLENGTHSQUARED || bitangent.LengthSquared < MINTANGENTLENGTHSQUARED)
                {
                    Vector3 normal = Normals[v];
                    Vector3 helper = MathF.Abs(normal.Y) < NEARLYVERTICAL ? Vector3.UnitY : Vector3.UnitX;
                    tangent = Vector3.Cross(normal, helper);
                    bitangent = Vector3.Cross(normal, tangent);
                }

                Tangents.Add(Vector3.Normalize(tangent));
                Bitangents.Add(Vector3.Normalize(bitangent));
            }
        }

        /// <summary>
        /// Validates the mesh data to ensure it is well-formed and ready for rendering.
        /// </summary>
        /// <param name="name">A name for the mesh</param>
        /// <returns>True if the mesh is valid; otherwise, false.</returns>
        public bool Validate(string name)
        {
            // Check for empty mesh
            if (VertexCount == 0 || IndexCount == 0)
            {
                DebugText.Log($"{name}: mesh is empty", Color4.Red, DebugText.WARNINGDURATION);
                return false;
            }
            
            // Check for attribute count mismatch
            if (UVs.Count != VertexCount || Normals.Count != VertexCount)
            {
                DebugText.Log($"{name}: attribute count mismatch " +
                              $"(pos {VertexCount}, uv {UVs.Count}, normal {Normals.Count})", Color4.Red, DebugText.WARNINGDURATION);
                return false;
            }
            
            // Check for index count being a multiple of 3
            if (IndexCount % 3 != 0)
            {
                DebugText.Log($"{name}: index count {IndexCount} is not a multiple of 3", Color4.Red, DebugText.WARNINGDURATION);
                return false;
            }

            // Check for indices being within the valid range
            foreach (uint index in Indices)
            {
                if (index >= VertexCount)
                {
                    DebugText.Log($"{name}: index {index} is out of range (vertex count {VertexCount})", Color4.Red, DebugText.WARNINGDURATION);
                    return false;
                }
            }
            
            return true;
        }
        #endregion
    }
}