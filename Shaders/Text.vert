#version 330 core

layout(location = 0) in vec2 aPos; // Screen position in pixels
layout(location = 1) in vec2 aTexCoord; // Position in the font atlas
layout(location = 2) in vec4 aColor; // Text colour

out vec2 TexCoord; // Position in the font atlas
out vec4 TextColor; // Text colour

uniform mat4 projection; // Orthographic: pixels to clip space

void main()
{
    // Row-vector convention (matrices are uploaded with transpose = true), so vectors go on the left
    // w = 0: the translation part of the projection matrix is ignored, so the text doesn't move when the camera moves.
    gl_Position = vec4(aPos, 0.0, 1.0) * projection;
    
    TexCoord = aTexCoord;
    TextColor = aColor;
}
