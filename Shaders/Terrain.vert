#version 330 core

layout(location = 0) in vec3 aPos; // Vertex position (x, height, z)
layout(location = 1) in vec2 aTexCoord; // Texture coordinate across the whole terrain

out vec2 TexCoord; // Texture coordinate for the fragment shader

uniform mat4 model; // Model matrix (moves the terrain down by OffsetY)
uniform mat4 view; // View matrix (world to camera)
uniform mat4 projection; // Projection matrix (camera to clip space)

void main()
{
    TexCoord = aTexCoord;

    // Row-vector convention (matrices are uploaded with transpose = true), so vectors go on the left
    gl_Position = vec4(aPos, 1.0) * model * view * projection;
}
