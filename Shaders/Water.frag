#version 330 core

in vec4 ClipPos; // clip-space position
in vec3 WorldPos; // world-space position
in vec2 RippleUV; // ripple texture coordinates

out vec4 FragColor;

// Ripples
const float DISTORTION = 0.025; // how far the ripples push the reflection and refraction (fraction of the screen)
const float NORMALFLATNESS = 1.5; // flattens the ripple normals for lighting and Fresnel (larger = calmer water)

// Colour by depth
const vec3 SHALLOWTINT = vec3(0.75, 0.95, 0.9); // tints the floor seen through shallow water
const vec3 DEEPCOLOR = vec3(0.02, 0.16, 0.2); // what the water fades to where it's deep
const float DEEPDEPTH = 14.0; // water this thick (seen along the view) is fully DEEPCOLOR
const float EDGESOFTNESS = 1.5; // water thinner than this fades out at the shore

// Reflection
const float FRESNELPOWER = 2.0; // larger = the reflection only shows at shallower viewing angles
const float MINREFLECTION = 0.05; // a little reflection even looking straight down

// Sun highlight
const float SHININESS = 120.0;
const float SPECULARSTRENGTH = 0.8;

// Colour of the wireframe debug view
const vec3 WIREFRAMECOLOR = vec3(0.0, 1.0, 1.0);

uniform sampler2D ReflectionMap; // the scene mirrored above the water (unit 0)
uniform sampler2D RefractionMap; // the scene below the water (unit 1)
uniform sampler2D RefractionDepthMap; // depth of the scene below the water (unit 2)
uniform sampler2D NormalMap; // ripple normals, generated from noise (unit 3)
uniform float SecondLayerScale; // the second ripple layer repeats this many times more often
uniform vec2 FirstRippleOffset; // how far each ripple layer has drifted
uniform vec2 SecondRippleOffset;

uniform float nearPlane; // the camera's near and far planes, to turn depth values into distances
uniform float farPlane;
uniform vec3 cameraPos;

// Debug and lighting
uniform int DebugView; // one of the DEBUGVIEW_ values (#defined from the C# DebugView enum)
uniform int UseLighting; // 1 = sun highlight on
uniform vec3 lightDir; // direction the sunlight travels
uniform vec3 lightColor; // colour of the sunlight

// Distance from the camera for a depth buffer value (the depth buffer stores it non-linearly)
float LinearDepth(float depth)
{
    float z = depth * 2.0 - 1.0; // 0..1 to -1..1
    return 2.0 * nearPlane * farPlane / (farPlane + nearPlane - z * (farPlane - nearPlane));
}

void main()
{
    // Where this pixel is on screen (0..1), which is where it is in the reflection and refraction textures
    vec2 screenUV = ClipPos.xy / ClipPos.w * 0.5 + 0.5;
    vec2 refractUV = screenUV;
    vec2 reflectUV = vec2(screenUV.x, 1.0 - screenUV.y); // the reflection was drawn from a mirrored camera, so it's upside down

    // --- Water thickness: distance from the surface to the floor behind it ---
    float floorDistance = LinearDepth(texture(RefractionDepthMap, refractUV).r);
    float surfaceDistance = LinearDepth(gl_FragCoord.z);
    float thickness = max(floorDistance - surfaceDistance, 0.0);
    float edge = clamp(thickness / EDGESOFTNESS, 0.0, 1.0); // 0 at the shore, 1 once it's deep enough

    // --- Ripples: two layers drifting in different directions ---
    vec3 first = texture(NormalMap, RippleUV + FirstRippleOffset).rgb * 2.0 - 1.0;
    vec3 second = texture(NormalMap, RippleUV * SecondLayerScale + SecondRippleOffset).rgb * 2.0 - 1.0;
    vec3 ripple = first + second;

    // The map's x and y lie along world X and Z, and its z points up
    vec3 N = normalize(vec3(ripple.x, ripple.z * NORMALFLATNESS, ripple.y));

    // --- Distortion (the ripples' sideways tilt, like a DuDv map), weaker at the shore ---
    vec2 distortion = ripple.xy * DISTORTION * edge;
    reflectUV = clamp(reflectUV + distortion, 0.001, 0.999);
    refractUV = clamp(refractUV + distortion, 0.001, 0.999);

    vec3 reflection = texture(ReflectionMap, reflectUV).rgb;
    vec3 refraction = texture(RefractionMap, refractUV).rgb;

    // --- Colour by depth: the floor shows through shallow water, deep water fades to DEEPCOLOR ---
    float deepness = clamp(thickness / DEEPDEPTH, 0.0, 1.0);
    refraction = mix(refraction * SHALLOWTINT, DEEPCOLOR, deepness);

    // --- Fresnel: more reflection at shallow viewing angles, more see-through looking down ---
    vec3 V = normalize(cameraPos - WorldPos); // towards the camera
    float fresnel = mix(MINREFLECTION, 1.0, pow(1.0 - max(dot(V, N), 0.0), FRESNELPOWER));
    vec3 colour = mix(refraction, reflection, fresnel);

    // --- Sun highlight (Blinn-Phong), faded out at the shore ---
    if (UseLighting == 1)
    {
        vec3 L = normalize(-lightDir); // towards the sun
        vec3 H = normalize(L + V);
        colour += pow(max(dot(N, H), 0.0), SHININESS) * SPECULARSTRENGTH * edge * lightColor;
    }

    // --- Output ---
    if (DebugView == DEBUGVIEW_NORMALS)
    FragColor = vec4(N * 0.5 + 0.5, 1.0); // -1..1 shown as 0..1
    else if (DebugView == DEBUGVIEW_WIREFRAME)
    FragColor = vec4(WIREFRAMECOLOR, 1.0);
    else
    FragColor = vec4(colour, edge); // the shore fades into the sand underneath
}
