using OpenTK.Graphics.OpenGL4;

namespace GPR202_A3.Graphics
{
    /// <summary>
    /// Represents a Vertex Array Object (VAO) in OpenGL,
    /// which encapsulates the state needed to specify vertex data for rendering.
    /// </summary>
    internal class VAO : IGraphicsResources
    {
        #region Constants
        // A 4x4 matrix is passed as 4 attributes (one per row), each 4 floats
        private const int MATRIXROWS = 4;
        private const int ROWSIZE = 4;
        private const int MATRIXBYTES = MATRIXROWS * ROWSIZE * sizeof(float);

        // Attribute divisor: move to the next value once per instance (0 would be once per vertex)
        private const int ONEPERINSTANCE = 1;
        #endregion

        #region Public Fields
        /// <summary>
        /// Gets the ID of the Vertex Array Object (VAO).
        /// </summary>
        public int ID { get; }
        #endregion

        #region Constructors
        /// <summary>
        /// Initializes a new instance of the VAO class and generates a Vertex Array Object (VAO) in OpenGL.
        /// </summary>
        public VAO()
        {
            // Generate a new Vertex Array Object (VAO) ID and bind it to the OpenGL context
            ID = GL.GenVertexArray();
            GL.BindVertexArray(ID);
        }
        #endregion

        #region Public API
        /// <summary>
        /// Links a Vertex Buffer Object (VBO) to the Vertex Array Object (VAO).
        /// </summary>
        /// <param name="location">The location of the vertex attribute.</param>
        /// <param name="size">The number of components per vertex attribute.</param>
        /// <param name="vbo">The Vertex Buffer Object (VBO) to link.</param>
        public void LinkToVao(int location, int size, VBO vbo)
        {
            // Bind the VAO and VBO, set up the vertex attribute pointer, and enable the vertex attribute array
            Bind();
            vbo.Bind();
            GL.VertexAttribPointer(location, size, VertexAttribPointerType.Float, false, 0, 0);
            GL.EnableVertexAttribArray(location);
            Unbind();
        }

        /// <summary>
        /// Links a VBO of per-instance matrices (one per copy of the mesh) as 4 attributes, one per row,
        /// starting at the given location. The divisor of 1 means each instance moves on to the next matrix
        /// (instead of each vertex).
        /// </summary>
        /// <param name="startLocation">The first of the 4 attribute locations.</param>
        /// <param name="vbo">The VBO of Matrix4s.</param>
        public void LinkInstanceMatrices(int startLocation, VBO vbo)
        {
            Bind();
            vbo.Bind();

            int stride = MATRIXBYTES;
            int rowBytes = ROWSIZE * sizeof(float);
            for (int row = 0; row < MATRIXROWS; row++)
            {
                int location = startLocation + row;
                GL.VertexAttribPointer(location, ROWSIZE, VertexAttribPointerType.Float, false, stride, row * rowBytes);
                GL.EnableVertexAttribArray(location);
                GL.VertexAttribDivisor(location, ONEPERINSTANCE);
            }

            Unbind();
        }
        #endregion

        #region IGraphicsResources Functions
        /// <summary>
        /// Binds the Vertex Array Object (VAO) to the current OpenGL context.
        /// </summary>
        public void Bind() => GL.BindVertexArray(ID);
        /// <summary>
        /// Unbinds the Vertex Array Object (VAO) from the current OpenGL context.
        /// </summary>
        public void Unbind() => GL.BindVertexArray(0);
        /// <summary>
        /// Deletes the Vertex Array Object (VAO) from the GPU.
        /// </summary>
        public void Delete() => GL.DeleteVertexArray(ID);
        #endregion
    }
}