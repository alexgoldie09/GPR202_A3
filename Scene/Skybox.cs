using GPR202_A3.Debugging;
using GPR202_A3.Graphics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GPR202_A3.Scene
{
    /// <summary>
    /// A skybox is a cube with a cube map texture that surrounds the camera, giving the illusion of a distant environment.
    /// 
    /// Not an IRenderable: the scene is everything drawn with the model shader, while the skybox needs its own
    /// shader and depth test (the same reason DebugText is kept separate).
    /// </summary>
    internal class Skybox
    {
        #region Constants
        // The texture unit the cube map is bound to (units 0 to 3 are the material maps).
        public const int CUBEMAPUNIT = 4;

        // Shader location of the cube's corner positions
        private const int POSITIONLOCATION = 0;
        private const int POSITIONSIZE = 3; // floats per corner (x, y, z)

        // The cube's 8 corners, centred on the origin.
        // The shader only uses the direction to each corner, and places the cube at the far plane.
        private static readonly List<Vector3> Corners =
        [
            new(-1f, -1f, -1f), // 0
            new(1f, -1f, -1f), // 1
            new(1f, 1f, -1f), // 2
            new(-1f, 1f, -1f), // 3
            new(-1f, -1f, 1f), // 4
            new(1f, -1f, 1f), // 5
            new(1f, 1f, 1f),  // 6
            new(-1f, 1f, 1f)  // 7
        ];

        // Two triangles per face, wound counter-clockwise when seen from inside the cube (the camera being inside),
        // so the inside faces are the front faces and backface culling doesn't affect them
        private static readonly List<uint> Indices =
        [
            3, 0, 1, 1, 2, 3, // back (-Z)
            4, 0, 3, 3, 7, 4, // left (-X)
            1, 5, 6, 6, 2, 1, // right (+X)
            4, 7, 6, 6, 5, 4, // front (+Z)
            3, 2, 6, 6, 7, 3, // top (+Y)
            0, 4, 1, 1, 4, 5  // bottom (-Y)
        ];
        #endregion

        #region Private Fields
        // GPU resources this class owns, deleted in Delete()
        private readonly ShaderProgram _shader;
        private readonly CubeMap _cubeMap;
        private readonly VAO _vao;
        private readonly VBO _positionVbo;
        private readonly IBO _ibo;
        #endregion

        /// <summary>
        /// Constructor that loads the cube map, uploads the cube and creates the skybox shader.
        /// </summary>
        /// <param name="faceFiles">Six files the folder, in OpenGL's face order: +X, -X, +Y, -Y, +Z, -Z.</param>
        public Skybox(string[] faceFiles)
        {
            // --- Cube map ---
            _cubeMap = new CubeMap(faceFiles, CUBEMAPUNIT);
            
            // The cube map is bound once here, and the shader's sampler uniform is set to the same unit, so it never needs to be changed
            _cubeMap.Bind();

            // --- Cube ---
            // Positions only (no UVs or normals: the cube map is looked up by direction
            _vao = new VAO();
            _positionVbo = new VBO(Corners);
            _vao.LinkToVao(POSITIONLOCATION, POSITIONSIZE, _positionVbo);

            // Create the IBO while the VAO is bound so the VAO remembers it
            _vao.Bind();
            _ibo = new IBO(Indices);
            _vao.Unbind();

            // --- Shader ---
            // Its own shader program since the sampler unit never changes, so it is set once here
            _shader = new ShaderProgram("Skybox.vert", "Skybox.frag");
            _shader.Bind();
            _shader.SetInt("SkyboxMap", CUBEMAPUNIT);

            DebugText.CheckGLError("Skybox setup");
        }

        #region Public API
        /// <summary>
        /// Uploads the camera's projection matrix (on load and whenever the window resizes).
        /// </summary>
        /// <param name="projection">The camera's projection matrix.</param>
        public void SetProjection(Matrix4 projection)
        {
            _shader.Bind();
            _shader.SetMatrix4("projection", projection);
        }

        /// <summary>
        /// Draws the sky. Call after the scene, so the pixels already covered by models are skipped by the depth test.
        /// </summary>
        /// <param name="view">The camera's view matrix (the shader ignores its translation).</param>
        public void Render(Matrix4 view)
        {
            // The shader puts the sky at a depth of exactly 1.0, which is also what the depth buffer is cleared to.
            // The default test (Less) would reject 1.0 < 1.0, so Less-or-equal is used while the sky is drawn.
            GL.DepthFunc(DepthFunction.Lequal);

            // Bind the shader and upload the view matrix (the projection is already set)
            _shader.Bind();
            _shader.SetMatrix4("view", view);

            // Draw the cube with the cube map bound to the shader's sampler uniform
            _vao.Bind();
            GL.DrawElements(PrimitiveType.Triangles, Indices.Count, DrawElementsType.UnsignedInt, 0);
            _vao.Unbind();

            // Back to the default for everything drawn next frame
            GL.DepthFunc(DepthFunction.Less);
        }

        /// <summary>
        /// Frees the skybox's GPU resources (shader, cube buffers and cube map).
        /// </summary>
        public void Delete()
        {
            _shader.Delete();
            _vao.Delete();
            _positionVbo.Delete();
            _ibo.Delete();
            _cubeMap.Delete();
        }
        #endregion
    }
}
