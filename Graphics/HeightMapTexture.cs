using OpenTK.Graphics.OpenGL4;

namespace GPR202_A3.Graphics
{
    /// <summary>
    /// A square, single-channel float texture holding the terrain's heights, one texel per grid vertex.
    ///
    /// The terrain's vertex shader reads its height from here, so the heights live on the GPU instead of in a vertex
    /// buffer. Changing a region of this texture (e.g. a crater) changes the terrain's shape and lighting
    /// straight away, without rebuilding any mesh.
    /// </summary>
    internal class HeightMapTexture : IGraphicsResources
    {
        #region Public Fields
        /// <summary>
        /// Gets the ID of the texture object in OpenGL.
        /// </summary>
        public int ID { get; }

        /// <summary>
        /// The texture unit this texture always binds to.
        /// </summary>
        public int Unit { get; }

        /// <summary>
        /// Texels along each side.
        /// </summary>
        public int Size { get; }
        #endregion

        #region Constructors
        /// <summary>
        /// Constructor that uploads the heights as an R32F texture (one 32-bit float per texel).
        /// </summary>
        /// <param name="heights">Size * Size heights, row by row (index = z * size + x).</param>
        /// <param name="size">Texels along each side.</param>
        /// <param name="unit">The texture unit this texture always binds to.</param>
        public HeightMapTexture(float[] heights, int size, int unit)
        {
            Size = size;
            Unit = unit;

            // Uploads happen on unit 0, like the other texture classes
            GL.ActiveTexture(TextureUnit.Texture0);
            ID = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, ID);

            // The shader reads exact texels with texelFetch, so there is no filtering or mipmapping.
            // Nearest also keeps the texture "complete" without mipmaps, which OpenGL requires before it can be sampled.
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            // R32F: one red channel stored as a full float, so heights keep their precision (no 0..255 stepping)
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R32f, size, size, 0,
                PixelFormat.Red, PixelType.Float, heights);

            GL.BindTexture(TextureTarget.Texture2D, 0);
        }
        #endregion

        #region Public API
        /// <summary>
        /// Replaces a rectangle of heights on the GPU, e.g. after a crater changes part of the terrain.
        /// Only the changed rectangle is uploaded, not the whole texture.
        /// </summary>
        /// <param name="x">The rectangle's first column.</param>
        /// <param name="z">The rectangle's first row.</param>
        /// <param name="width">Columns in the rectangle.</param>
        /// <param name="height">Rows in the rectangle.</param>
        /// <param name="heights">width * height heights, row by row.</param>
        public void UpdateRegion(int x, int z, int width, int height, float[] heights)
        {
            Bind();
            GL.TexSubImage2D(TextureTarget.Texture2D, 0, x, z, width, height, PixelFormat.Red, PixelType.Float, heights);
        }
        #endregion

        #region IGraphicsResources Functions
        /// <summary>
        /// Binds the texture to its unit.
        /// </summary>
        public void Bind()
        {
            GL.ActiveTexture(TextureUnit.Texture0 + Unit);
            GL.BindTexture(TextureTarget.Texture2D, ID);
        }

        /// <summary>
        /// Unbinds the texture from its unit.
        /// </summary>
        public void Unbind()
        {
            GL.ActiveTexture(TextureUnit.Texture0 + Unit);
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        /// <summary>
        /// Deletes the texture from the GPU.
        /// </summary>
        public void Delete() => GL.DeleteTexture(ID);
        #endregion
    }
}
