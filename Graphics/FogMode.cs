namespace GPR202_A3.Graphics
{
    /// <summary>
    /// The fog equations F11 cycles through. The values are #defined into every shader as FOGMODE_ constants
    /// (the same way as DebugView), so the C# and GLSL always agree.
    /// </summary>
    internal enum FogMode
    {
        Off,
        Linear, // clear until a start distance, fully fogged at an end distance
        Exponential, // builds up from the camera
        ExponentialSquared, // clear close up, then thickens quickly (the most natural haze)
        HeightLayer // exponential squared haze, plus thick fog lying low in the dunes that thins with height
    }
}
