#version 330 core

in vec2 TexCoord; // Texture coordinate from the vertex shader
in vec3 WorldNormal; // World-space normal from the vertex shader
in vec3 WorldPos; // World-space position (for the view direction)
in vec3 WorldTangent; // World-space tangent (normal mapping)
in vec3 WorldBitangent; // World-space bitangent (normal mapping)

out vec4 FragColor; // Output colour of the fragment

uniform sampler2D AlbedoMap; // Surface colour, laid out to the model's UVs (unit 0)
uniform sampler2D NormalMap; // Tangent-space normal map, OpenGL convention (unit 1)
uniform sampler2D MetallicMap; // Greyscale, white = metal (unit 2)
uniform sampler2D RoughnessMap; // Greyscale, white = rough (unit 3)

// Environment: the sky cube map, bound once by Skybox (unit 4)
uniform samplerCube SkyboxMap;

// Material values (set per mesh): each multiplies its map.
// A material without a map gets a neutral default instead (white, or a flat normal), so the value is used alone.
uniform vec3 albedoFactor; // colour tint (white = map unchanged)
uniform float metallicFactor; // 0 = non-metal, 1 = metal
uniform float roughnessFactor; // 0 = mirror smooth, 1 = fully matte
uniform float normalStrength; // 0 = flat, 1 = normal map as authored
uniform float detailScale; // how many times the detail maps repeat across the UVs

// Debug and lighting
uniform int DebugView; // one of the DEBUGVIEW_ values, #defined from the C# DebugView enum
uniform int UseLighting; // 1 = lit, 0 = raw texture colour
uniform vec3 lightDir; // direction the light travels, in world space (sunlight pointing down)
uniform vec3 lightColor; // colour/intensity of the light
uniform float ambientStrength; // minimum brightness, so faces facing away aren't pitch black
uniform vec3 cameraPos; // for specular highlights and reflections

void main()
{
    // --- Material values: factor x map ---
    // The albedo uses the model's own UVs; the tiling detail maps repeat detailScale times across them
    vec3 albedo = albedoFactor * texture(AlbedoMap, TexCoord).rgb;
    vec2 detailUV = TexCoord * detailScale;
    float metallic = metallicFactor * texture(MetallicMap, detailUV).r;
    float roughness = roughnessFactor * texture(RoughnessMap, detailUV).r;

    // --- Surface normal ---
    // Without a normal map, the flat default unpacks to about (0, 0, 1), which leaves the mesh normal unchanged
    vec3 N = normalize(WorldNormal); // re-normalise: interpolation shortens it
    
    // Tangent, bitangent and normal as the axes of the surface. 
    // The tangent is straightened to be exactly perpendicular to the normal, and the bitangent's sign keeps mirrored UVs the right way round.
    vec3 T = normalize(WorldTangent - dot(WorldTangent, N) * N);
    float handedness = dot(cross(N, T), WorldBitangent) < 0.0 ? -1.0 : 1.0;
    vec3 B = cross(N, T) * handedness;

    // Normal mapping: the normal map is in tangent space, so we transform it to world space using the TBN matrix.
    vec3 mapped = texture(NormalMap, detailUV).rgb * 2.0 - 1.0;
    mapped.xy *= normalStrength;
    N = normalize(mat3(T, B, N) * mapped);

    // --- Debug views ---
    if (DebugView == DEBUGVIEW_NORMALS)
    // Normals: Map each normal component from -1..1 to 0..1: +X = red, +Y (up) = green, +Z = blue
    FragColor = vec4(normalize(WorldNormal) * 0.5 + 0.5, 1.0);
    else if (DebugView == DEBUGVIEW_UVS)
    // UVs: U = red, V = green: black at (0, 0), yellow at (1, 1)
    FragColor = vec4(TexCoord, 0.0, 1.0);
    else if (DebugView == DEBUGVIEW_WIREFRAME)
    // Wireframe: Game switches to line rasterisation, so only edges reach here; draw them in one solid colour
    FragColor = vec4(1.0, 1.0, 0.0, 1.0);
    else if (DebugView == DEBUGVIEW_METALLIC)
    // Metallic: white = metal, black = non-metal
    FragColor = vec4(vec3(metallic), 1.0);
    else if (DebugView == DEBUGVIEW_ROUGHNESS)
    // Roughness: white = rough, black = smooth
    FragColor = vec4(vec3(roughness), 1.0);
    // --- Lighting ---
    else if (UseLighting == 0)
    // Unlit: the colour exactly as textured
    FragColor = vec4(albedo, 1.0);
    else
    {
        vec3 L = normalize(-lightDir); // direction from the surface towards the light
        vec3 V = normalize(cameraPos - WorldPos); // direction from the surface towards the camera

        // Lambert diffuse: full brightness facing the light, falling to 0 side-on
        float diffuse = max(dot(N, L), 0.0);

        // Roughness drives the highlight: smooth = small and bright, rough = broad and faint, fully rough = none.
        // A simple remap that keeps shininess in the 8..256 range that suits these flat-shaded models.
        float smoothness = 1.0 - clamp(roughness, 0.0, 1.0);
        float shininess = mix(8.0, 256.0, smoothness * smoothness);
        float specularStrength = smoothness;

        // Blinn-Phong specular: a highlight where the light reflects towards the camera
        vec3 H = normalize(L + V);
        float specular = pow(max(dot(N, H), 0.0), shininess) * specularStrength;

        // Metals reflect light in their own colour; non-metals keep a white highlight
        vec3 specularColour = mix(vec3(1.0), albedo, metallic);

        // Environment reflection: the skybox reflected in the surface, blurred by roughness. 
        // The reflection is tinted by the specular colour and scaled by the metallic factor.
        vec3 R = reflect(-V, N);
        vec3 reflection = texture(SkyboxMap, normalize(mix(R, N, roughness))).rgb;
        vec3 environment = reflection * specularColour * metallic * mix(0.3, 1.0, smoothness);

        // Diffuse amount: metals don't have diffuse reflection, so we scale it down by the metallic factor.
        float diffuseAmount = 1.0 - 0.8 * metallic;

        // Combine the three lighting components, plus a minimum ambient brightness so faces facing away from the light aren't pitch black.
        vec3 colour = albedo * diffuseAmount * (ambientStrength + diffuse) * lightColor
        + specular * specularColour * lightColor
        + environment;
        FragColor = vec4(colour, 1.0);
    }
}
