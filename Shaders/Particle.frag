#version 330 core

in vec2 TexCoord; // position in the atlas
in vec4 ParticleColor;

out vec4 FragColor;

uniform sampler2D ParticleAtlas; // white shapes; only alpha holds the shape

// Nearly invisible pixels are skipped (saves blending work on the empty corners of every quad)
const float MINALPHA = 0.01;

void main()
{
    float shape = texture(ParticleAtlas, TexCoord).a;
    float alpha = ParticleColor.a * shape;
    if (alpha < MINALPHA)
        discard;

    FragColor = vec4(ParticleColor.rgb, alpha);
}
