#version 330 core

in vec2 TexCoord; // position in the atlas
in vec4 ParticleColor;
in vec3 WorldPos; // world-space position

out vec4 FragColor;

uniform vec3 cameraPos; // for the fog
uniform sampler2D ParticleAtlas; // white shapes; only alpha holds the shape

// Nearly invisible pixels are skipped (saves blending work on the empty corners of every quad)
const float MINALPHA = 0.01;

// Fog, shared with the other world shaders
#include "Fog.glsl"

void main()
{
    float shape = texture(ParticleAtlas, TexCoord).a;
    // Particles fade into the fog (what's behind them is already fogged)
    float alpha = ParticleColor.a * shape * FogVisibility(WorldPos, cameraPos);
    if (alpha < MINALPHA)
        discard;

    FragColor = vec4(ParticleColor.rgb, alpha);
}
