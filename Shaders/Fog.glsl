// Fog, shared by every shader that draws the world. ShaderProgram puts this file's text wherever a shader
// has #include "Fog.glsl". The FOGMODE_ values are #defined from the C# FogMode enum.
// The settings are uploaded by Fog.cs.

uniform int FogMode; // one of the FOGMODE_ values
uniform float FogStart; // linear: where the fog starts
uniform float FogEnd; // linear: where it's complete
uniform float FogDensity; // exponential: how quickly it builds up
uniform float FogDensitySquared; // exponential squared (and the haze in height layer mode)
uniform float FogLayerDensity; // height layer: how thick it is at FogLayerBase
uniform float FogLayerFalloff; // height layer: how quickly it thins going up
uniform float FogLayerBase; // height layer: the height FogLayerDensity is measured at
uniform float FogWaterLevel; // the water's height (for the reflection's mirrored camera)
uniform samplerCube FogSky; // the skybox, which the fog takes its colour from
uniform float FogSkyBlur; // which mip level of the sky to use (higher = blurrier)
uniform float FogSkyHaze; // how far up the sky the haze reaches (the sky shader)
uniform vec3 FogSunDirection; // the direction sunlight travels
uniform vec3 FogSunColor; // the sun's colour
uniform float FogSunGlow; // how much the fog takes the sun's colour, looking towards it
uniform float FogSunGlowPower; // larger = the glow stays closer around the sun

// Below this, the view counts as level (stops a divide by zero in the height layer)
const float FOGMINRISE = 0.0001;

// The fog's colour looking in a direction: the sky at the horizon that way, blurred, warmed towards the sun's colour
vec3 FogColour(vec3 viewDir)
{
    // Looking down still uses the horizon (not the bottom of the skybox). The small lift keeps it valid looking straight down.
    vec3 horizon = normalize(vec3(viewDir.x, max(viewDir.y, 0.0) + FOGMINRISE, viewDir.z));
    vec3 colour = textureLod(FogSky, horizon, FogSkyBlur).rgb; // a blurry mip level, so clouds don't get painted onto the dunes

    float towardsSun = max(dot(viewDir, -FogSunDirection), 0.0);
    return mix(colour, FogSunColor, pow(towardsSun, FogSunGlowPower) * FogSunGlow);
}

// How fogged a point is, 0 (clear) to 1 (completely fog)
float FogAmount(vec3 worldPos, vec3 cameraPos)
{
    // The water reflection is drawn from a camera mirrored under the water. Its fog only starts where the
    // view reaches the water: the fog between the real camera and the water is added when the water is drawn.
    if (cameraPos.y < FogWaterLevel && worldPos.y > FogWaterLevel)
        cameraPos = mix(cameraPos, worldPos, (FogWaterLevel - cameraPos.y) / (worldPos.y - cameraPos.y));

    float distance = length(worldPos - cameraPos);

    if (FogMode == FOGMODE_LINEAR)
        return clamp((distance - FogStart) / (FogEnd - FogStart), 0.0, 1.0);

    if (FogMode == FOGMODE_EXPONENTIAL)
        return 1.0 - exp(-FogDensity * distance);

    // Exponential squared, and the haze in height layer mode
    float haze = FogDensitySquared * distance;
    float thickness = haze * haze;

    if (FogMode == FOGMODE_HEIGHTLAYER)
    {
        // The layer's thickness halves every so many units up. Adding it all up along the line from the camera
        // to this point has an exact answer, so there's no stepping through it (Quilez, "Better Fog").
        float atCamera = FogLayerDensity * exp(-(cameraPos.y - FogLayerBase) * FogLayerFalloff);
        float rise = (worldPos.y - cameraPos.y) * FogLayerFalloff;
        float averageAlongView = abs(rise) > FOGMINRISE ? (1.0 - exp(-rise)) / rise : 1.0;
        thickness += atCamera * distance * averageAlongView;
    }
    else if (FogMode != FOGMODE_EXPONENTIALSQUARED)
        return 0.0; // off

    return 1.0 - exp(-thickness);
}

// A solid surface's colour with the fog over it
vec3 ApplyFog(vec3 colour, vec3 worldPos, vec3 cameraPos)
{
    if (FogMode == FOGMODE_OFF)
        return colour;

    return mix(colour, FogColour(normalize(worldPos - cameraPos)), FogAmount(worldPos, cameraPos));
}

// How much of a see-through thing (particles) still shows. They fade out instead of turning fog-coloured,
// since whatever is behind them is fogged already.
float FogVisibility(vec3 worldPos, vec3 cameraPos)
{
    if (FogMode == FOGMODE_OFF)
        return 1.0;

    return 1.0 - FogAmount(worldPos, cameraPos);
}

// The sky with haze over its bottom, fading out higher up, so the far terrain melts into it
vec3 ApplySkyHaze(vec3 colour, vec3 viewDir)
{
    if (FogMode == FOGMODE_OFF)
        return colour;

    float haze = 1.0 - smoothstep(0.0, FogSkyHaze, viewDir.y);
    return mix(colour, FogColour(viewDir), haze);
}
