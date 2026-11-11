using OpenTK.Graphics.OpenGL4;

namespace GPR202_A3.Graphics
{
    /// <summary>
    /// A square float texture holding the terrain's heights, one texel per grid vertex.
    /// The terrain's vertex shader reads its heights from here, and UpdateRegion changes part of it (e.g. a crater).
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

            // The shader reads exact texels with texelFetch, so no filtering or mipmaps are needed.
            // (Nearest also means OpenGL doesn't expect mipmaps before it will sample the texture.)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            // R32F: one 32-bit float per texel, so heights keep their full precision
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R32f, size, size, 0,
                PixelFormat.Red, PixelType.Float, heights);

            GL.BindTexture(TextureTarget.Texture2D, 0);
        }
        #endregion

        #region Public API
        /// <summary>
        /// Uploads a changed rectangle of heights (only that rectangle, not the whole texture).
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
