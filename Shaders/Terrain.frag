#version 330 core

in vec3 WorldPos; // world-space position
in vec3 WorldNormal; // world-space normal

out vec4 FragColor;

// Slip faces (steep sides) are a little darker and redder than flat sand
const vec3 SLIPFACETINT = vec3(0.85, 0.75, 0.65);
const float SLIPFACESHARPNESS = 4.0; // how quickly the tint comes in as the slope gets steeper

// Wet sand along the shore
const float WETBANDHEIGHT = 3.0; // how far above the water the wet sand reaches
const float WETBRIGHTNESS = 0.55; // how dark wet sand is (1 = not darker at all)

// Colour of the wireframe debug view
const vec3 WIREFRAMECOLOR = vec3(1.0, 1.0, 0.0);

uniform sampler2D SandColorMap; // tiling sand colour (unit 1)
uniform sampler2D SandNormalMap; // tiling ripples, normal map (unit 2)
uniform float SandTileSize; // world units per repeat of the sand colour
uniform float RippleTileSize; // world units per repeat of the ripples
uniform float WaterLevel; // height of the oasis water

// Debug and lighting
uniform int DebugView; // one of the DEBUGVIEW_ values (#defined from the C# DebugView enum)
uniform int UseLighting; // 1 = lit, 0 = unlit
uniform vec3 lightDir; // direction the sunlight travels
uniform vec3 lightColor; // colour of the sunlight
uniform float ambientStrength; // minimum brightness

void main()
{
    // Textures tile across the world's x and z. The colour and ripples repeat at different sizes,
    // which also hides the tiling.
    vec2 uv = WorldPos.xz / SandTileSize;
    vec2 rippleUV = WorldPos.xz / RippleTileSize;

    // --- Normal ---
    vec3 N = normalize(WorldNormal);

    // The textures run along world X (U) and Z (V), so the tangent is +X made perpendicular to the normal.
    // T, B and N turn the ripple normal map into world space.
    vec3 T = normalize(vec3(1.0, 0.0, 0.0) - N * N.x);
    vec3 B = cross(T, N);
    vec3 ripples = texture(SandNormalMap, rippleUV).rgb * 2.0 - 1.0;
    vec3 detailN = normalize(mat3(T, B, N) * ripples);

    // --- Colour ---
    vec3 albedo = texture(SandColorMap, uv).rgb;

    float steepness = 1.0 - N.y; // 0 = flat
    albedo *= mix(vec3(1.0), SLIPFACETINT, clamp(steepness * SLIPFACESHARPNESS, 0.0, 1.0));

    float wet = 1.0 - smoothstep(WaterLevel, WaterLevel + WETBANDHEIGHT, WorldPos.y);
    albedo *= mix(1.0, WETBRIGHTNESS, wet);

    // --- Output ---
    if (DebugView == DEBUGVIEW_NORMALS)
    FragColor = vec4(detailN * 0.5 + 0.5, 1.0); // -1..1 shown as 0..1
    else if (DebugView == DEBUGVIEW_UVS)
    FragColor = vec4(fract(uv), 0.0, 1.0);
    else if (DebugView == DEBUGVIEW_WIREFRAME)
    FragColor = vec4(WIREFRAMECOLOR, 1.0);
    else if (UseLighting == 0)
    FragColor = vec4(albedo, 1.0);
    else
    {
        // Lambert diffuse only: sand is rough, so there is no highlight
        vec3 L = normalize(-lightDir); // towards the sun
        float diffuse = max(dot(detailN, L), 0.0);
        FragColor = vec4(albedo * (ambientStrength + diffuse) * lightColor, 1.0);
    }
}
