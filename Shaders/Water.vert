#version 330 core

layout(location = 0) in vec3 aPos; // quad corner, already in world space

out vec4 ClipPos; // clip-space position, for finding this pixel in the reflection and refraction textures
out vec3 WorldPos; // world-space position
out vec2 RippleUV; // ripple texture coordinates (tile across the world)

uniform mat4 view; // world to camera
uniform mat4 projection; // camera to clip space
uniform float RippleTileSize; // world units per repeat of the ripples

void main()
{
    WorldPos = aPos;
    RippleUV = aPos.xz / RippleTileSize;

    // Row-vector convention (matrices uploaded transposed). No model matrix: the quad is already in world space.
    ClipPos = vec4(aPos, 1.0) * view * projection;
    gl_Position = ClipPos;
}
