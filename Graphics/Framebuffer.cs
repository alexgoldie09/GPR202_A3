using GPR202_A3.Debugging;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GPR202_A3.Graphics
{
    /// <summary>
    /// An off-screen render target: draw into it instead of the window, then use its colour (and optionally its
    /// depth) as textures. Used for the water's reflection and refraction, and later for post-processing.
    /// </summary>
    internal class Framebuffer : IGraphicsResources
    {
        #region Public Fields
        /// <summary>
        /// Gets the ID of the framebuffer object in OpenGL.
        /// </summary>
        public int ID { get; private set; }

        /// <summary>
        /// Width in pixels.
        /// </summary>
        public int Width { get; private set; }

        /// <summary>
        /// Height in pixels.
        /// </summary>
        public int Height { get; private set; }

        /// <summary>
        /// The colour texture everything is drawn into.
        /// </summary>
        public int ColorTexture { get; private set; }

        /// <summary>
        /// The depth texture, or 0 if this framebuffer keeps its depth in a renderbuffer instead.
        /// </summary>
        public int DepthTexture { get; private set; }
        #endregion

        #region Private Fields
        // Whether depth is kept as a texture (readable by shaders) or a renderbuffer (only used for depth testing)
        private readonly bool _depthAsTexture;

        // The depth renderbuffer, if depth isn't a texture
        private int _depthRenderbuffer;
        #endregion

        #region Constructors
        /// <summary>
        /// Constructor that creates the framebuffer and its attachments.
        /// </summary>
        /// <param name="width">Width in pixels.</param>
        /// <param name="height">Height in pixels.</param>
        /// <param name="depthAsTexture">true to keep depth in a texture that shaders can read (e.g. for water depth).</param>
        public Framebuffer(int width, int height, bool depthAsTexture)
        {
            _depthAsTexture = depthAsTexture;
            Create(width, height);
        }
        #endregion

        #region Public API
        /// <summary>
        /// Recreates the attachments at a new size (e.g. when the window is resized).
        /// </summary>
        public void Resize(int width, int height)
        {
            if (width == Width && height == Height)
                return;

            Delete();
            Create(width, height);
        }

        /// <summary>
        /// Binds a texture of this framebuffer to a texture unit, for a shader to read.
        /// </summary>
        /// <param name="texture">ColorTexture or DepthTexture.</param>
        /// <param name="unit">The texture unit.</param>
        public static void BindTexture(int texture, int unit)
        {
            GL.ActiveTexture(TextureUnit.Texture0 + unit);
            GL.BindTexture(TextureTarget.Texture2D, texture);
        }
        #endregion

        #region IGraphicsResources Functions
        /// <summary>
        /// Draws into this framebuffer from now on, and sets the viewport to its size.
        /// </summary>
        public void Bind()
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, ID);
            GL.Viewport(0, 0, Width, Height);
        }

        /// <summary>
        /// Draws into the window again. The caller sets the viewport back to the window's size.
        /// </summary>
        public void Unbind() => GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        /// <summary>
        /// Deletes the framebuffer and its attachments from the GPU.
        /// </summary>
        public void Delete()
        {
            GL.DeleteFramebuffer(ID);
            GL.DeleteTexture(ColorTexture);
            if (DepthTexture != 0)
                GL.DeleteTexture(DepthTexture);
            if (_depthRenderbuffer != 0)
                GL.DeleteRenderbuffer(_depthRenderbuffer);

            ID = 0;
            ColorTexture = 0;
            DepthTexture = 0;
            _depthRenderbuffer = 0;
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Creates the framebuffer, a colour texture, and a depth texture or renderbuffer.
        /// </summary>
        private void Create(int width, int height)
        {
            Width = Math.Max(width, 1);
            Height = Math.Max(height, 1);

            ID = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, ID);

            // --- Colour ---
            ColorTexture = CreateTexture(PixelInternalFormat.Rgba8, PixelFormat.Rgba, PixelType.UnsignedByte, TextureMinFilter.Linear);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D, ColorTexture, 0);

            // --- Depth ---
            if (_depthAsTexture)
            {
                // 24-bit depth. Linear filtering smooths edges when it's read at a different resolution (e.g. the water's shoreline).
                DepthTexture = CreateTexture(PixelInternalFormat.DepthComponent24, PixelFormat.DepthComponent, PixelType.Float, TextureMinFilter.Linear);
                GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                    TextureTarget.Texture2D, DepthTexture, 0);
            }
            else
            {
                // A renderbuffer is enough when the depth only needs to be tested, not read
                _depthRenderbuffer = GL.GenRenderbuffer();
                GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthRenderbuffer);
                GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent24, Width, Height);
                GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                    RenderbufferTarget.Renderbuffer, _depthRenderbuffer);
            }

            FramebufferErrorCode status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != FramebufferErrorCode.FramebufferComplete)
                DebugText.Log($"Framebuffer {Width}x{Height} is incomplete: {status}", Color4.Red, DebugText.WARNINGDURATION);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        /// <summary>
        /// Creates an empty texture the size of the framebuffer, clamped at the edges.
        /// </summary>
        private int CreateTexture(PixelInternalFormat internalFormat, PixelFormat format, PixelType type, TextureMinFilter filter)
        {
            GL.ActiveTexture(TextureUnit.Texture0); // uploads happen on unit 0, like the other texture classes
            int texture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, internalFormat, Width, Height, 0, format, type, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)filter);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)filter);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            return texture;
        }
        #endregion
    }
}
