#version 330 core

in vec2 TexCoord; // Position in the font atlas (negative for background boxes)
in vec4 TextColor; // Text or background colour

out vec4 FragColor;

uniform sampler2D FontAtlas; // White characters on transparent (unit 0)

// Pixels of the atlas at least this opaque count as part of a character
const float GLYPHCUTOFF = 0.5;

void main()
{
    // Background boxes have negative texture coordinates: fill them with their colour (its alpha lets the game show through)
    if (TexCoord.x < 0.0)
    {
        FragColor = TextColor;
        return;
    }

    // The atlas is white characters on transparent, so only its alpha matters
    float glyph = texture(FontAtlas, TexCoord).a;

    // Pixel font: a pixel is either part of the character or not
    if (glyph < GLYPHCUTOFF)
        discard;

    FragColor = TextColor;
}
