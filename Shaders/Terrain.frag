#version 330 core

in vec3 WorldPos; // World-space position from the vertex shader
in vec3 WorldNormal; // World-space normal from the vertex shader

out vec4 FragColor; // Output colour of the fragment

uniform sampler2D SandColorMap; // Tiling sand colour (unit 1)
uniform sampler2D SandNormalMap; // Tiling sand ripples, tangent-space normal map, OpenGL convention (unit 2)
uniform float SandTileSize; // world units covered by one repeat of the sand colour texture
uniform float RippleTileSize; // world units covered by one repeat of the ripple normal map
uniform float WaterLevel; // height of the oasis water, for the wet sand along the shore

// Debug and lighting
uniform int DebugView; // one of the DEBUGVIEW_ values, #defined from the C# DebugView enum
uniform int UseLighting; // 1 = lit, 0 = raw sand colour
uniform vec3 lightDir; // direction the sunlight travels, in world space
uniform vec3 lightColor; // colour/intensity of the sunlight
uniform float ambientStrength; // minimum brightness, so slopes facing away from the sun aren't black

void main()
{
    // The sand textures tile across the world's x and z, so their size on screen doesn't depend on the grid.
    // The colour and the ripples repeat at different sizes, which also hides the tiling of each.
    vec2 uv = WorldPos.xz / SandTileSize;
    vec2 rippleUV = WorldPos.xz / RippleTileSize;

    // --- Surface normal ---
    vec3 N = normalize(WorldNormal); // re-normalise: interpolation shortens it

    // The sand texture's U runs along world +X and V along +Z, so the tangent is +X straightened against the normal,
    // and the bitangent is perpendicular to both. These turn the ripple normal map from texture space into world space.
    vec3 T = normalize(vec3(1.0, 0.0, 0.0) - N * N.x);
    vec3 B = cross(T, N);
    vec3 ripples = texture(SandNormalMap, rippleUV).rgb * 2.0 - 1.0;
    vec3 detailN = normalize(mat3(T, B, N) * ripples);

    // --- Sand colour ---
    vec3 albedo = texture(SandColorMap, uv).rgb;

    // Steep slip faces are slightly darker and redder than the flat, wind-packed sand
    float steepness = 1.0 - N.y; // 0 = flat, larger = steeper
    albedo *= mix(vec3(1.0), vec3(0.85, 0.75, 0.65), clamp(steepness * 4.0, 0.0, 1.0));

    // Wet, darker sand in a band just above the water line, fading out 3 units above it
    float wet = 1.0 - smoothstep(WaterLevel, WaterLevel + 3.0, WorldPos.y);
    albedo *= mix(1.0, 0.55, wet);

    // --- Debug views ---
    if (DebugView == DEBUGVIEW_NORMALS)
    // Normals (including the ripples): +X = red, +Y (up) = green, +Z = blue
    FragColor = vec4(detailN * 0.5 + 0.5, 1.0);
    else if (DebugView == DEBUGVIEW_UVS)
    // UVs: the repeating sand tile, U = red, V = green
    FragColor = vec4(fract(uv), 0.0, 1.0);
    else if (DebugView == DEBUGVIEW_WIREFRAME)
    // Wireframe: Game switches to line rasterisation, so only edges reach here; draw them in one solid colour
    FragColor = vec4(1.0, 1.0, 0.0, 1.0);
    else if (UseLighting == 0)
    // Unlit: the sand colour as it is
    FragColor = vec4(albedo, 1.0);
    else
    {
        // Lambert diffuse with the ripple normal: sand is rough, so there is no specular highlight
        vec3 L = normalize(-lightDir); // direction from the surface towards the sun
        float diffuse = max(dot(detailN, L), 0.0);

        vec3 colour = albedo * (ambientStrength + diffuse) * lightColor;
        FragColor = vec4(colour, 1.0);
    }
}
