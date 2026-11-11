using GPR202_A3.Debugging;
using GPR202_A3.Utilities;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using StbImageSharp;

namespace GPR202_A3.Graphics
{
    /// <summary>
    /// Represents a 2D texture in OpenGL, allowing for loading, binding, and managing texture resources.
    /// </summary>
    internal class Texture : IGraphicsResources
    {
        #region Constants
        // Alpha for single-colour textures (fully opaque)
        private const byte OPAQUE = 255;
        #endregion

        #region Public Fields
        /// <summary>
        /// Gets the ID of the texture object in OpenGL.
        /// </summary>
        public int ID { get; }
        
        /// <summary>
        /// The texture unit this texture always binds to (e.g. Material.NORMALMAPUNIT).
        /// </summary>
        public int Unit { get; private set; }
        #endregion
        
        #region Constructors
        /// <summary>
        /// Constructor that initialises a new instance of the Texture class and loads a texture from the Resource folder.
        /// </summary>
        /// <param name="filename">The texture file, relative to the Resource folder (e.g. "Model/balloon.png").</param>
        /// <param name="unit">The texture unit this texture always binds to (e.g. Material.NORMALMAPUNIT).</param>
        /// <param name="pixelPerfect">Whether to use pixel-perfect filtering (nearest) or trilinear filtering (LinearMipmapLinear).</param>
        public Texture(string filename, int unit = 0, bool pixelPerfect = false)
        {
            Unit = unit;
            
            if (string.IsNullOrEmpty(filename))
            {
                DebugText.Log("Texture filename is null or empty.", Color4.Red, DebugText.WARNINGDURATION);
                return;
            }
            
            GL.ActiveTexture(TextureUnit.Texture0);
            
            // Generate a new texture ID and bind it to the OpenGL context
            ID = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, ID);
            
            // Wrapping: repeat, so UVs outside 0..1 tile the texture
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

            // Filtering: nearest for pixel art, trilinear (blends between mipmap levels) for everything else
            var minFilter = pixelPerfect ? TextureMinFilter.Nearest : TextureMinFilter.LinearMipmapLinear;
            var magFilter = pixelPerfect ? TextureMagFilter.Nearest : TextureMagFilter.Linear;
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)minFilter);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)magFilter);
            
            // Image files store the top row first, but OpenGL expects the bottom row first, so flip on load.
            StbImage.stbi_set_flip_vertically_on_load(1);

            // Use a try-catch block to handle potential exceptions during file reading and image loading
            try
            {
                using var stream = File.OpenRead(AssetPaths.Resource(filename));
                ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

                if (image == null)
                {
                    DebugText.Log($"Failed to load texture '{filename}': ImageResult is null.", Color4.Red, DebugText.WARNINGDURATION);
                }
                else
                {
                    // Upload the image data to the GPU
                    GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, image.Width, image.Height, 0,
                        PixelFormat.Rgba, PixelType.UnsignedByte, image.Data);

                    // Build the smaller mipmap levels from the uploaded image (required by LinearMipmapLinear,
                    // otherwise the texture is "incomplete" and samples as black)
                    if (!pixelPerfect)
                        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);

                    DebugText.Log($"Loaded '{filename}' ({image.Width}x{image.Height})", Color4.LightGreen, DebugText.INFODURATION);
                }
            }
            catch (Exception e)
            {
                DebugText.Log($"Texture '{filename}' failed: {e.Message}", Color4.Red, DebugText.WARNINGDURATION);
            }

            // Unbind the texture to prevent accidental modification
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }
        
        /// <summary>
        /// Constructor that creates a 1x1 texture of a single colour, without loading a file.
        /// Used for the neutral "no map" textures in Material, so every sampler always has a texture bound.
        /// </summary>
        /// <param name="red">Red, 0 to 255.</param>
        /// <param name="green">Green, 0 to 255.</param>
        /// <param name="blue">Blue, 0 to 255.</param>
        /// <param name="unit">The texture unit this texture always binds to (e.g. Material.NORMALMAPUNIT).</param>
        public Texture(byte red, byte green, byte blue, int unit = 0)
        {
            Unit = unit;
            
            // Uploads always happen on unit 0 (see the file constructor)
            GL.ActiveTexture(TextureUnit.Texture0);

            ID = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, ID);

            // A single pixel: repeat so every UV reads it, nearest filtering and no mipmaps needed
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

            byte[] pixel = { red, green, blue, OPAQUE };
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, 1, 1, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, pixel);

            GL.BindTexture(TextureTarget.Texture2D, 0);
        }
        #endregion

        #region IGraphicsResources Functions
        /// <summary>
        /// Binds the texture to a texture unit it is assigned to.
        /// </summary>
        public void Bind()
        {
            GL.ActiveTexture(TextureUnit.Texture0 + Unit);
            GL.BindTexture(TextureTarget.Texture2D, ID);
        }

        /// <summary>
        /// Unbinds any 2D texture from the texture unit this texture is assigned to.
        /// </summary>
        public void Unbind()
        {
            GL.ActiveTexture(TextureUnit.Texture0 + Unit);
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        /// <summary>
        /// Deletes the texture from the GPU, freeing up resources.
        /// </summary>
        public void Delete() => GL.DeleteTexture(ID);
        #endregion
    }
}