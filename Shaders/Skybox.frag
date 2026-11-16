#version 330 core

in vec3 TexCoords; // Direction from the cube's centre, from the vertex shader

out vec4 FragColor; // Output colour of the fragment

uniform samplerCube SkyboxMap; // The sky cube map (unit 4)

// Fog, shared with the other world shaders
#include "Fog.glsl"

void main()
{
    // A cube map picks the face the direction points at, then the pixel on that face
    vec3 sky = texture(SkyboxMap, TexCoords).rgb;

    // Haze over the bottom of the sky, so the far terrain melts into it
    FragColor = vec4(ApplySkyHaze(sky, normalize(TexCoords)), 1.0);
}
