namespace GPR202_A3.Debugging
{
    /// <summary>
    /// Represents the different debug views available in the application.
    ///
    /// The shader program writes these as #define DEBUGVIEW_xxx, and
    /// the fragment shader uses them to determine what to render.
    /// </summary>
    internal enum DebugView { Texture, Normals, UVs, Wireframe, Metallic, Roughness }
}