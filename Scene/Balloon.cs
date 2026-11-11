using GPR202_A3.Graphics;
using GPR202_A3.Loading;
using OpenTK.Mathematics;

namespace GPR202_A3.Scene
{
    /// <summary>
    /// The balloon model, loaded once and drawn for every balloon.
    /// Each balloon's position and state are kept in its own BalloonState, so all of them share one mesh and texture.
    /// </summary>
    internal class Balloon
    {
        #region Constants
        // Balloon fabric is matte, so only a faint highlight
        private const float ROUGHNESS = 0.9f;
        #endregion

        #region Private Fields
        // The shared part (mesh and material) every balloon is drawn with
        private readonly ModelPart _part;

        // GPU resources this class owns, deleted in Delete()
        private readonly Mesh _mesh;
        private readonly Texture _texture;
        #endregion

        #region Constructors
        /// <summary>
        /// Constructor that loads the balloon model and its texture.
        /// </summary>
        public Balloon()
        {
            _texture = new Texture("Model/balloon.png", Material.ALBEDOMAPUNIT);

            // The balloon's parts never move separately, so it is loaded as one mesh
            _mesh = new Mesh(ModelLoader.Load("Model/balloon.obj"), "Balloon");

            _part = new ModelPart("Balloon", _mesh)
            {
                Material = new Material { AlbedoMap = _texture, Roughness = ROUGHNESS }
            };
        }
        #endregion

        #region Public API
        /// <summary>
        /// Draws one balloon with the model shader, at that balloon's position, rotation and scale.
        /// </summary>
        /// <param name="shader">The bound model shader.</param>
        /// <param name="state">The balloon to draw.</param>
        public void Render(ShaderProgram shader, BalloonState state)
        {
            // A fully shrunk balloon is invisible, so there is nothing to draw
            if (state.Scale <= 0f)
                return;

            _part.Position = state.Position;
            _part.Rotation = Quaternion.FromEulerAngles(state.Rotation);
            _part.Scale = new Vector3(state.Scale);
            _part.Render(shader, Matrix4.Identity);
        }

        /// <summary>
        /// Frees the balloon's mesh and texture.
        /// </summary>
        public void Delete()
        {
            _mesh.Delete();
            _texture.Delete();
        }
        #endregion
    }
}
