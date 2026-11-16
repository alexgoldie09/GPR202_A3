using OpenTK.Mathematics;

namespace GPR202_A3.Graphics
{
    /// <summary>
    /// The fog settings, uploaded to every shader that includes Fog.glsl (terrain, models, water, particles, sky).
    /// Every mode is tuned to be fully fogged by FULLFOGDISTANCE, the nearest the terrain's edge can be to the player.
    /// </summary>
    internal class Fog
    {
        #region Constants
        // Nothing past this distance shows through the fog (the play area's edge is this far from the terrain's edge)
        private const float FULLFOGDISTANCE = 1000f;

        // Linear: clear up to here, then a straight ramp to fully fogged at FULLFOGDISTANCE
        private const float LINEARSTART = 150f;

        // Exponential: halfway fogged at about 230 units, 95% by FULLFOGDISTANCE
        private const float EXPDENSITY = 0.003f;

        // Exponential squared: halfway at about 400 units, 99% by FULLFOGDISTANCE
        private const float EXP2DENSITY = 0.0021f;

        // Height layer: how thick the low fog is at LAYERBASE (the dune valleys), and how many units up it halves
        private const float LAYERDENSITY = 0.004f;
        private const float LAYERHALFHEIGHT = 20f;
        private const float LAYERBASE = 0f;

        // Fog colour: the skybox's mip level to sample (its 512 faces become 16 pixels, a soft average)
        private const float SKYBLURLEVEL = 5f;

        // The sky's haze: up to this far above the horizon (the up part of a direction, 0 = level, 1 = straight up)
        private const float SKYHAZEHEIGHT = 0.25f;

        // Sun glow: how much the fog takes the sun's colour looking towards it, and how tight the glow is
        private const float SUNGLOW = 0.5f;
        private const float SUNGLOWPOWER = 8f;

        // Number of fog modes, for cycling through them
        private static readonly int ModeCount = Enum.GetValues<FogMode>().Length;
        #endregion

        #region Public Fields
        /// <summary>
        /// Which fog equation is used. Upload it to the shaders again after changing it.
        /// </summary>
        public FogMode Mode { get; private set; } = FogMode.HeightLayer;
        #endregion

        #region Private Fields
        private readonly Vector3 _sunDirection;
        private readonly Vector3 _sunColor;
        private readonly float _waterLevel;
        private readonly int _skyUnit;
        #endregion

        #region Constructors
        /// <summary>
        /// Constructor that takes the scene values the fog needs.
        /// </summary>
        /// <param name="sunDirection">The direction sunlight travels (for the sun glow).</param>
        /// <param name="sunColor">The sun's colour.</param>
        /// <param name="waterLevel">The water's height (for the reflection's mirrored camera).</param>
        /// <param name="skyUnit">The texture unit the skybox's cube map stays bound to.</param>
        public Fog(Vector3 sunDirection, Vector3 sunColor, float waterLevel, int skyUnit)
        {
            _sunDirection = sunDirection;
            _sunColor = sunColor;
            _waterLevel = waterLevel;
            _skyUnit = skyUnit;
        }
        #endregion

        #region Public API
        /// <summary>
        /// Moves on to the next fog mode (F11).
        /// </summary>
        public void NextMode() => Mode = (FogMode)(((int)Mode + 1) % ModeCount);

        /// <summary>
        /// Uploads the fog settings to a shader. Each shader only uses some of them (the sky only needs the colour),
        /// so ones it doesn't have are skipped.
        /// </summary>
        public void Upload(ShaderProgram shader)
        {
            shader.Bind();
            SetIfUsed(shader, "FogMode", (int)Mode);
            SetIfUsed(shader, "FogStart", LINEARSTART);
            SetIfUsed(shader, "FogEnd", FULLFOGDISTANCE);
            SetIfUsed(shader, "FogDensity", EXPDENSITY);
            SetIfUsed(shader, "FogDensitySquared", EXP2DENSITY);
            SetIfUsed(shader, "FogLayerDensity", LAYERDENSITY);
            SetIfUsed(shader, "FogLayerFalloff", MathF.Log(2f) / LAYERHALFHEIGHT); // halves every LAYERHALFHEIGHT units
            SetIfUsed(shader, "FogLayerBase", LAYERBASE);
            SetIfUsed(shader, "FogWaterLevel", _waterLevel);
            SetIfUsed(shader, "FogSky", _skyUnit);
            SetIfUsed(shader, "FogSkyBlur", SKYBLURLEVEL);
            SetIfUsed(shader, "FogSkyHaze", SKYHAZEHEIGHT);
            SetIfUsed(shader, "FogSunGlow", SUNGLOW);
            SetIfUsed(shader, "FogSunGlowPower", SUNGLOWPOWER);
            if (shader.HasUniform("FogSunDirection")) shader.SetVector3("FogSunDirection", _sunDirection);
            if (shader.HasUniform("FogSunColor")) shader.SetVector3("FogSunColor", _sunColor);
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Sets a float uniform, if the shader uses it.
        /// </summary>
        private static void SetIfUsed(ShaderProgram shader, string name, float value)
        {
            if (shader.HasUniform(name))
                shader.SetFloat(name, value);
        }

        /// <summary>
        /// Sets an int uniform, if the shader uses it.
        /// </summary>
        private static void SetIfUsed(ShaderProgram shader, string name, int value)
        {
            if (shader.HasUniform(name))
                shader.SetInt(name, value);
        }
        #endregion
    }
}
