#version 330 core

layout(location = 0) in vec2 aPos; // Screen position in pixels
layout(location = 1) in vec2 aTexCoord; // Position in the font atlas (negative for background boxes)
layout(location = 2) in vec4 aColor; // Text or background colour

out vec2 TexCoord; // Position in the font atlas
out vec4 TextColor; // Text or background colour

uniform mat4 projection; // Orthographic: pixels to clip space

void main()
{
    // Row-vector convention (matrices are uploaded with transpose = true), so vectors go on the left.
    // z = 0 and w = 1: a flat point in screen pixels.
    gl_Position = vec4(aPos, 0.0, 1.0) * projection;

    TexCoord = aTexCoord;
    TextColor = aColor;
}
