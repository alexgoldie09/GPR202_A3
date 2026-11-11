using GPR202_A3.Debugging;
using GPR202_A3.Utilities;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GPR202_A3.Graphics
{
    /// <summary>
    /// Represents a shader program in OpenGL,
    /// which is used to manage and link vertex and fragment shaders for rendering.
    /// </summary>
    internal class ShaderProgram : IGraphicsResources
    {
        #region Public Fields
        /// <summary>
        /// Gets the ID of the shader program.
        /// </summary>
        public int ID { get; }
        #endregion
        
        #region Private Fields
        // A dictionary to cache uniform locations for efficient access.
        private readonly Dictionary<string, int> _uniformLocations = new();
        #endregion
        
        #region Constructors
        /// <summary>
        /// Constructor that initialises a new instance of the ShaderProgram class.
        /// It loads, compiles, and links the vertex and fragment shaders from the specified file paths.
        /// </summary>
        /// <param name="vertexShaderFilepath">The file path to the vertex shader.</param>
        /// <param name="fragmentShaderFilePath">The file path to the fragment shader.</param>
        public ShaderProgram(string vertexShaderFilepath, string fragmentShaderFilePath)
        {
            // Create the shader program
            ID = GL.CreateProgram();
            
            // Create vertex shader
            var vertexShader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertexShader, InsertDefines(LoadShaderSource(vertexShaderFilepath)));
            GL.CompileShader(vertexShader);
            GL.GetShader(vertexShader, ShaderParameter.CompileStatus, out int vStatus);
            if (vStatus != (int)All.True)
            {
                Console.WriteLine($"Vertex shader compile error: {GL.GetShaderInfoLog(vertexShader)}");
                DebugText.Log($"'{vertexShaderFilepath}' failed to compile (see console)", Color4.Red, 6f);
            }
            
            // Create fragment shader
            var fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShader, InsertDefines(LoadShaderSource(fragmentShaderFilePath)));
            GL.CompileShader(fragmentShader);
            GL.GetShader(fragmentShader, ShaderParameter.CompileStatus, out int fStatus);
            if (fStatus != (int)All.True)
            {
                Console.WriteLine($"Fragment shader compile error: {GL.GetShaderInfoLog(fragmentShader)}");
                DebugText.Log($"'{fragmentShaderFilePath}' failed to compile (see console)", Color4.Red, 6f);
            }
            
            // Attach the shaders to the shader program
            GL.AttachShader(ID, vertexShader);
            GL.AttachShader(ID, fragmentShader);
            
            // Link the shader program to finalize it
            GL.LinkProgram(ID);
            GL.GetProgram(ID, GetProgramParameterName.LinkStatus, out int lStatus);
            if (lStatus != (int)All.True)
            {
                Console.WriteLine($"Shader program link error: {GL.GetProgramInfoLog(ID)}");
                DebugText.Log($"Shader program failed to link (see console)", Color4.Red, 6f);
            }
            
            // Clean up by detaching and deleting the individual shaders after linking
            GL.DetachShader(ID, vertexShader);
            GL.DetachShader(ID, fragmentShader);
            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);
        }
        #endregion
        
        #region Public API
        /// <summary>
        /// Sets a 4x4 matrix uniform variable in the shader program.
        /// Uploaded transposed (row-vector convention), matching the vec4 * matrix order used in the shaders.
        /// </summary>
        /// <param name="name">The name of the uniform variable in the shader.</param>
        /// <param name="matrix">The 4x4 matrix to set.</param>
        public void SetMatrix4(string name, Matrix4 matrix) => GL.UniformMatrix4(GetLocation(name), true, ref matrix);
        
        /// <summary>
        /// Sets a 3x3 matrix uniform variable in the shader program (e.g. the normal matrix).
        /// Uploaded transposed like SetMatrix4, so shaders use the same vec3 * matrix order.
        /// </summary>
        /// <param name="name">The name of the uniform variable in the shader.</param>
        /// <param name="matrix">The 3x3 matrix to set.</param>
        public void SetMatrix3(string name, Matrix3 matrix) => GL.UniformMatrix3(GetLocation(name), true, ref matrix);

        /// <summary>
        /// Sets a 3-component vector uniform variable in the shader program (e.g. a camera position or light direction).
        /// </summary>
        /// <param name="name">The name of the uniform variable in the shader.</param>
        /// <param name="value">The vector to set.</param>
        public void SetVector3(string name, Vector3 value) => GL.Uniform3(GetLocation(name), value);

        /// <summary>
        /// Sets a float uniform variable in the shader program (e.g. a reflectivity amount).
        /// </summary>
        /// <param name="name">The name of the uniform variable in the shader.</param>
        /// <param name="value">The float value to set.</param>
        public void SetFloat(string name, float value) => GL.Uniform1(GetLocation(name), value);

        /// <summary>
        /// Sets an integer uniform variable in the shader program.
        /// Also used for bools and for sampler uniforms (the texture unit number).
        /// </summary>
        /// <param name="name">The name of the uniform variable in the shader.</param>
        /// <param name="value">The integer value to set.</param>
        public void SetInt(string name, int value) => GL.Uniform1(GetLocation(name), value);
        #endregion
        
        #region IGraphicsResources Functions
        /// <summary>
        /// Binds the shader program for use in rendering.
        /// </summary>
        public void Bind() => GL.UseProgram(ID);
        /// <summary>
        /// Unbinds the shader program, reverting to the default OpenGL state.
        /// </summary>
        public void Unbind() => GL.UseProgram(0);
        /// <summary>
        /// Deletes the shader program from the GPU, freeing up resources.
        /// </summary>
        public void Delete() => GL.DeleteProgram(ID);
        #endregion
        
        #region Helper Methods
        /// <summary>
        /// Retrieves the location of a uniform variable in the shader program.
        /// If the location has been previously queried, it is cached for faster access.
        /// </summary>
        /// <param name="name">The name of the uniform variable in the shader.</param>
        /// <returns>The uniform's location, or -1 if not found.</returns>
        private int GetLocation(string name)
        {
            if (_uniformLocations.TryGetValue(name, out int location))
                return location;

            location = GL.GetUniformLocation(ID, name);
            _uniformLocations[name] = location;

            if (location == -1)
                DebugText.Log($"Uniform '{name}' not found in shader {ID}", Color4.Orange, 6f);

            return location;
        }

        /// <summary>
        /// Loads the shader source code from a file in the Shaders folder.
        /// </summary>
        /// <param name="fileName">The shader file</param>
        /// <returns>The shader source code, or an empty string if the file could not be read.</returns>
        private static string LoadShaderSource(string fileName)
        {
            string path = AssetPaths.Shader(fileName);

            try
            {
                return File.ReadAllText(path);
            }
            // An empty source will then fail to compile, which is logged by the constructor
            catch (Exception e)
            {
                Console.WriteLine($"Error loading shader source from '{path}': {e.Message}");
                DebugText.Log($"Shader '{fileName}' could not be read (see console)", Color4.Red, 6f);
                return string.Empty;
            }
        }

        /// <summary>
        /// Builds a string containing #define lines for each DebugView enum value.
        /// </summary>
        /// <returns>The #define lines, one per value.</returns>
        private static string BuildDefines()
        {
            string defines = "";
            foreach (DebugView view in Enum.GetValues<DebugView>())
                defines += $"#define DEBUGVIEW_{view.ToString().ToUpperInvariant()} {(int)view}\n";
            return defines;
        }

        /// <summary>
        /// Inserts the #defines straight after the #version line in the shader source.
        /// </summary>
        /// <param name="source">The shader source, starting with its #version line.</param>
        /// <returns>The source with the defines added.</returns>
        private static string InsertDefines(string source)
        {
            int endOfVersionLine = source.IndexOf('\n');

            // No line break means an empty (unreadable) or one-line file; leave it for the compiler to report
            if (endOfVersionLine == -1)
                return source;

            return source.Insert(endOfVersionLine + 1, BuildDefines());
        }
        #endregion
    }
}