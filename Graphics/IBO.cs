using OpenTK.Graphics.OpenGL4;

namespace GPR202_A3.Graphics
{
    /// <summary>
    /// Represents an Index Buffer Object (IBO) in OpenGL, which is used to store index data for rendering.
    /// </summary>
    internal class IBO : IGraphicsResources
    {
        #region Public Fields
        /// <summary>
        /// Gets the ID of the Index Buffer Object (IBO).
        /// </summary>
        public int ID { get; }
        #endregion

        #region Constructors
        /// <summary>
        /// Constructor that intialises a new instance of the IBO class with the specified index data.
        /// </summary>
        /// <param name="indices">The index data to be stored in the IBO.</param>
        public IBO(List<uint> indices)
        {
            // Generate a new buffer ID and bind it to the OpenGL context
            ID = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ID);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Count * sizeof(uint), indices.ToArray(),
                BufferUsageHint.StaticDraw);
        }
        #endregion

        #region IGraphicsResources Functions
        /// <summary>
        /// Binds the Index Buffer Object (IBO) to the current OpenGL context.
        /// </summary>
        public void Bind() => GL.BindBuffer(BufferTarget.ElementArrayBuffer, ID);
        /// <summary>
        /// Unbinds the Index Buffer Object (IBO) from the current OpenGL context.
        /// </summary>
        public void Unbind() => GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
        /// <summary>
        /// Deletes the Index Buffer Object (IBO) from the GPU.
        /// </summary>
        public void Delete() => GL.DeleteBuffer(ID);
        #endregion
    }
}