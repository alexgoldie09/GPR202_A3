#version 330 core

in vec2 TexCoord; // Texture coordinate from the vertex shader
in vec4 TextColor; // Colour of the text, set per draw call

out vec4 FragColor; // Output colour of the fragment

uniform sampler2D FontAtlas; // The font atlas is a texture containing all the glyphs, laid out to the text's UVs (unit 0)

void main()
{
    // The atlas is white glyphs on transparent, so only its alpha matters
    float glyph = texture(FontAtlas, TexCoord).a;

    // Pixel font: a pixel is either part of the glyph or not
    if (glyph < 0.5)
        discard;

    FragColor = TextColor;
}
