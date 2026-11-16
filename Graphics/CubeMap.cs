using GPR202_A3.Debugging;
using GPR202_A3.Utilities;
using OpenTK.Graphics.OpenGL4;
using StbImageSharp;

namespace GPR202_A3.Graphics
{
    /// <summary>
    /// Represents a cube map texture in OpenGL, which is used for environment mapping, skyboxes, and reflections.
    /// </summary>
    internal class CubeMap : IGraphicsResources
    {
        #region Constants
        // A cube map always has six faces
        private const int FACECOUNT = 6;
        #endregion

        #region Public Fields
        /// <summary>
        /// Gets the ID of the cube map texture object in OpenGL.
        /// </summary>
        public int ID { get; }

        /// <summary>
        /// Gets the texture unit this cube map binds to (e.g., Skybox.CUBEMAPUNIT).
        /// </summary>
        public int Unit { get; private set; }
        #endregion

        #region Constructors
        /// <summary>
        /// Constructor that loads six face images from the Resource folder into one cube map.
        /// Every face must be square and the same size as the others, otherwise OpenGL treats the cube map
        /// as incomplete and it samples as black.
        /// </summary>
        /// <param name="faceFiles">The six face files (relative to the Resource folder) in this order: +X, -X, +Y, -Y, +Z, -Z.</param>
        /// <param name="unit">The texture unit this cube map always binds to.</param>
        public CubeMap(string[] faceFiles, int unit)
        {
            Unit = unit;

            if (faceFiles.Length != FACECOUNT)
            {
                Log.Error($"Cube map needs {FACECOUNT} faces but was given {faceFiles.Length}.");
                return;
            }
            
            GL.ActiveTexture(TextureUnit.Texture0);

            // Generate a new texture ID and bind it as a cube map (a different target to a 2D texture)
            ID = GL.GenTexture();
            GL.BindTexture(TextureTarget.TextureCubeMap, ID);

            // Wrapping: clamp to edge on all three axes (S, T and R, the third axis of a direction lookup).
            // This prevents the cube map from sampling the opposite face when the direction is near an edge.
            GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);

            // Filtering: linear, with mipmaps. The sky itself always uses the full-size level, but the fog reads
            // a small, blurry level for its colour (see Fog.glsl).
            GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            // Cube map faces are used the way they are stored in the file (top row first), so no flip.
            StbImage.stbi_set_flip_vertically_on_load(0);
            
            // Keep track of the size of the first face to ensure all faces are square and the same size
            int faceSize = 0;
            
            // Load each face of the cube map from the provided files
            for (int i = 0; i < faceFiles.Length; i++)
            {
                // Use a try-catch block to handle potential exceptions during file reading and image loading
                try
                {
                    using var stream = File.OpenRead(AssetPaths.Resource(faceFiles[i]));
                    ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

                    if (image == null)
                    {
                        Log.Error($"Failed to load cube face '{faceFiles[i]}': ImageResult is null.");
                        continue;
                    }

                    if (i == 0)
                        faceSize = image.Width;

                    if (image.Width != image.Height || image.Width != faceSize)
                        Log.Error($"Cube face '{faceFiles[i]}' is {image.Width}x{image.Height}; every face must be square and {faceSize}x{faceSize}.");

                    // The six face targets come one after another in OpenGL (+X, -X, +Y, -Y, +Z, -Z),
                    // so face i is uploaded to PositiveX + i
                    GL.TexImage2D(TextureTarget.TextureCubeMapPositiveX + i, 0, PixelInternalFormat.Rgba,
                        image.Width, image.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, image.Data);
                }
                catch (Exception e)
                {
                    Log.Error($"Cube face '{faceFiles[i]}' failed: {e.Message}");
                }
            }

            // The smaller levels for the fog colour. Seamless filtering blends across the faces' edges,
            // which matters at small levels, where each face is only a few pixels.
            GL.GenerateMipmap(GenerateMipmapTarget.TextureCubeMap);
            GL.Enable(EnableCap.TextureCubeMapSeamless);

            Log.Info($"Loaded cube map ({faceSize}x{faceSize} faces)");

            // Unbind the cube map to prevent accidental modification
            GL.BindTexture(TextureTarget.TextureCubeMap, 0);
        }
        #endregion

        #region IGraphicsResources Functions
        /// <summary>
        /// Binds the cube map to the texture unit it is assigned to.
        /// </summary>
        public void Bind()
        {
            GL.ActiveTexture(TextureUnit.Texture0 + Unit);
            GL.BindTexture(TextureTarget.TextureCubeMap, ID);
        }

        /// <summary>
        /// Unbinds any cube map from the texture unit this cube map is assigned to.
        /// </summary>
        public void Unbind()
        {
            GL.ActiveTexture(TextureUnit.Texture0 + Unit);
            GL.BindTexture(TextureTarget.TextureCubeMap, 0);
        }

        /// <summary>
        /// Deletes the cube map from the GPU, freeing up resources.
        /// </summary>
        public void Delete() => GL.DeleteTexture(ID);
        #endregion
    }
}
