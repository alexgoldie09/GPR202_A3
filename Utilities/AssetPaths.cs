namespace GPR202_A3.Utilities
{
    /// <summary>
    /// Builds full paths to asset files, from the folder the executable runs from.
    /// The .csproj copies Shaders and Resource next to the executable, so this works from the IDE or the .exe.
    /// </summary>
    internal static class AssetPaths
    {
        #region Private Fields
        // The folder the executable runs from
        private static readonly string Root = AppContext.BaseDirectory;
        #endregion

        #region Public API
        /// <summary>
        /// Full path to a shader file in the Shaders folder.
        /// </summary>
        /// <param name="fileName">The shader file, e.g. "Model.vert".</param>
        /// <returns>The full path to the file.</returns>
        public static string Shader(string fileName) => Path.Combine(Root, "Shaders", fileName);

        /// <summary>
        /// Full path to a file in the Resource folder (textures, models, the skybox and the terrain maps).
        /// </summary>
        /// <param name="relativePath">The path inside Resource, e.g. "Model/plane.obj" or "Skybox/top.jpg".</param>
        /// <returns>The full path to the file.</returns>
        public static string Resource(string relativePath) => Path.Combine(Root, "Resource", relativePath);
        #endregion
    }
}
