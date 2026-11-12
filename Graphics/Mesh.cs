using GPR202_A3.Debugging;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GPR202_A3.Graphics
{
   /// <summary>
   /// Represents a 3D mesh, which is a collection of vertices, normals, UVs, tangents, bitangents and indices that can be drawn with OpenGL.
   /// </summary>
   internal class Mesh
   {
      #region Attribute Location
      // Define the attribute locations for the vertex shader inputs.
      public const int POSITIONLOCATION = 0;
      public const int UVLOCATION = 1;
      public const int NORMALLOCATION = 2;
      public const int TANGENTLOCATION = 3;
      public const int BITANGENTLOCATION = 4;
      public const int INSTANCEMATRIXLOCATION = 5; // uses locations 5 to 8 (one per matrix row)

      // Floats per value of each attribute
      private const int VECTOR3SIZE = 3;
      private const int VECTOR2SIZE = 2;
      #endregion

      #region Public Fields
      /// <summary>
      /// The name of the mesh, used for debugging and logging purposes.
      /// </summary>
      public string Name { get; private set; }

      /// <summary>
      /// The number of indices drawn per call (three per triangle). 0 if the mesh is empty.
      /// </summary>
      public int IndexCount { get; private set; }
      #endregion

      #region Private Fields
      // The various OpenGL objects used to store the mesh data on the GPU.
      private readonly VAO? _vao;
      private readonly VBO? _positionVbo;
      private readonly VBO? _uvVbo;
      private readonly VBO? _normalVbo;
      private readonly VBO? _tangentVbo;
      private readonly VBO? _bitangentVbo;
      private readonly IBO? _ibo;
      #endregion
      
      #region Constructors
      /// <summary>
      /// Constructor that validates the mesh data and uploads it to the GPU.
      /// Invalid data is logged and not uploaded, and the mesh then draws nothing instead of crashing.
      /// </summary>
      /// <param name="data">The CPU-side mesh data to upload.</param>
      /// <param name="name">A name for the mesh, used in log messages.</param>
      public Mesh(MeshData data, string name)
      {
         Name = name;

         if (!data.Validate(name))
            return;

         // Tangents are used by normal mapping. ModelLoader asks Assimp for them; any mesh without a full set gets them worked out here
         if (data.Tangents.Count != data.VertexCount)
            data.ComputeTangents();

         _vao = new VAO();

         // One VBO per attribute, each linked to its shader location
         _positionVbo = new VBO(data.Positions);
         _vao.LinkToVao(POSITIONLOCATION, VECTOR3SIZE, _positionVbo);

         _uvVbo = new VBO(data.UVs);
         _vao.LinkToVao(UVLOCATION, VECTOR2SIZE, _uvVbo);

         _normalVbo = new VBO(data.Normals);
         _vao.LinkToVao(NORMALLOCATION, VECTOR3SIZE, _normalVbo);

         _tangentVbo = new VBO(data.Tangents);
         _vao.LinkToVao(TANGENTLOCATION, VECTOR3SIZE, _tangentVbo);

         _bitangentVbo = new VBO(data.Bitangents);
         _vao.LinkToVao(BITANGENTLOCATION, VECTOR3SIZE, _bitangentVbo);

         // Create the IBO while the VAO is bound so the VAO remembers it
         _vao.Bind();
         _ibo = new IBO(data.Indices);
         _vao.Unbind();

         IndexCount = data.IndexCount;

         DebugText.Log(
            $"{name} uploaded: {data.VertexCount} vertices, {IndexCount} indices ({IndexCount / 3} triangles)",
            Color4.LightGreen, DebugText.INFODURATION);
         DebugText.CheckGLError($"{name} upload");
      }
      #endregion

      #region Public API
      /// <summary>
      /// Draws the mesh with whatever shader, textures and model matrix are currently bound.
      /// </summary>
      public void Draw()
      {
         if (_vao == null || IndexCount == 0)
            return;

         _vao.Bind();
         GL.DrawElements(PrimitiveType.Triangles, IndexCount, DrawElementsType.UnsignedInt, 0);
         _vao.Unbind();
      }

      /// <summary>
      /// Links a VBO of per-instance matrices, so DrawInstanced can draw many copies in one call.
      /// </summary>
      /// <param name="instanceVbo">One Matrix4 per copy.</param>
      public void LinkInstances(VBO instanceVbo) => _vao?.LinkInstanceMatrices(INSTANCEMATRIXLOCATION, instanceVbo);

      /// <summary>
      /// Draws many copies of the mesh in one call, each with its own matrix from the linked instance VBO.
      /// </summary>
      /// <param name="instanceCount">How many copies to draw.</param>
      public void DrawInstanced(int instanceCount)
      {
         if (_vao == null || IndexCount == 0 || instanceCount == 0)
            return;

         _vao.Bind();
         GL.DrawElementsInstanced(PrimitiveType.Triangles, IndexCount, DrawElementsType.UnsignedInt, IntPtr.Zero, instanceCount);
         _vao.Unbind();
      }

      /// <summary>
      /// Frees the mesh's GPU buffers.
      /// </summary>
      public void Delete()
      {
         _vao?.Delete();
         _positionVbo?.Delete();
         _uvVbo?.Delete();
         _normalVbo?.Delete();
         _tangentVbo?.Delete();
         _bitangentVbo?.Delete();
         _ibo?.Delete();
      }
      #endregion
   }
}