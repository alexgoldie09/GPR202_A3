using GPR202_A3.Graphics;

namespace GPR202_A3.Scene
{
    /// <summary>
    /// The common interface every object in the scene shares with the rest of the program.
    /// </summary>
    internal interface IRenderable
    {
        #region Public API
        /// <summary>
        /// Draws the object. The shader is already bound, with the view, projection and lighting set.
        /// </summary>
        /// <param name="shader">The model shader to upload this object's matrices to.</param>
        public void Render(ShaderProgram shader);

        /// <summary>
        /// Frees the object's GPU resources (meshes and textures).
        /// </summary>
        public void Delete();
        #endregion
    }
}