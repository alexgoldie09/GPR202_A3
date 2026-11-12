using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GPR202_A3.Graphics
{
    /// <summary>
    /// Represents a Vertex Buffer Object (VBO) in OpenGL,
    /// which is used to store vertex data on the GPU for rendering.
    /// </summary>
    internal class VBO : IGraphicsResources
    {
        #region Constants
        // Bytes in a 4x4 float matrix
        private const int MATRIXBYTES = 16 * sizeof(float);
        #endregion

        #region Public Fields
        /// <summary>
        /// Gets the ID of the Vertex Buffer Object (VBO).
        /// </summary>
        public int ID { get; }
        #endregion

        #region Constructors
        /// <summary>
        /// Constructor that initialises a new instance of the VBO class with Vector3 data.
        /// Used for storing 3D vertex positions in the buffer to create a 3D model.
        /// </summary>
        /// <param name="data">The vertex data to be stored in the buffer.</param>
        public VBO(List<Vector3> data)
        {
            // Generate a new buffer ID and bind it to the OpenGL context
            ID = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, ID);
            GL.BufferData(BufferTarget.ArrayBuffer, data.Count * Vector3.SizeInBytes, data.ToArray(), BufferUsageHint.StaticDraw);
        }

        /// <summary>
        /// Constructor that initialises a new instance of the VBO class with Vector2 data.
        /// Used for storing 2D vertex positions in the buffer to create a 2D model or texture coordinates.
        /// </summary>
        /// <param name="data">The vertex data to be stored in the buffer.</param>
        public VBO(List<Vector2> data)
        {
            // Generate a new buffer ID and bind it to the OpenGL context
            ID = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, ID);
            GL.BufferData(BufferTarget.ArrayBuffer, data.Count * Vector2.SizeInBytes, data.ToArray(), BufferUsageHint.StaticDraw);
        }

        /// <summary>
        /// Constructor that uploads a list of matrices (e.g. one per instance of a mesh).
        /// </summary>
        /// <param name="data">The matrices.</param>
        public VBO(List<Matrix4> data)
        {
            ID = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, ID);
            GL.BufferData(BufferTarget.ArrayBuffer, data.Count * MATRIXBYTES, data.ToArray(), BufferUsageHint.StaticDraw);
        }
        #endregion
        
        #region IGraphicsResources Functions
        /// <summary>
        /// Binds the Vertex Buffer Object (VBO) to the current OpenGL context.
        /// </summary>
        public void Bind() => GL.BindBuffer(BufferTarget.ArrayBuffer, ID);
        /// <summary>
        /// Unbinds the Vertex Buffer Object (VBO) from the current OpenGL context.
        /// </summary>
        public void Unbind() => GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        /// <summary>
        /// Deletes the Vertex Buffer Object (VBO) from the GPU.
        /// </summary>
        public void Delete() => GL.DeleteBuffer(ID);
        #endregion
    }
}