#version 330 core

layout(location = 0) in vec3 aPos; // Vertex position 
layout(location = 1) in vec2 aTexCoord; // Texture coordinate
layout(location = 2) in vec3 aNormal; // Surface normal
layout(location = 3) in vec3 aTangent; // Direction the texture's U axis runs, for normal mapping
layout(location = 4) in vec3 aBitangent; // Direction the texture's V axis runs, for normal mapping

out vec2 TexCoord; // Texture coordinate for the fragment shader
out vec3 WorldNormal; // Normal in world space (for lighting / reflections)
out vec3 WorldPos; // Position in world space (for lighting / reflections)
out vec3 WorldTangent; // Tangent in world space (normal mapping)
out vec3 WorldBitangent; // Bitangent in world space (normal mapping)

uniform mat4 model; // Model matrix (object to world)
uniform mat4 view; // View matrix (world to camera)
uniform mat4 projection; // Projection matrix (camera to clip space)
uniform mat3 normalMatrix; // Inverse-transpose of the model matrix, so normals stay perpendicular under non-uniform scale

void main()
{
    // Row-vector convention (matrices are uploaded with transpose = true), so vectors go on the left
    vec4 worldPos = vec4(aPos, 1.0) * model;

    WorldPos = worldPos.xyz;
    WorldNormal = aNormal * normalMatrix;
    TexCoord = aTexCoord;

    // Tangent and bitangent lie along the surface, so they move with the model matrix itself
    // (w = 0: they are directions, so the translation is ignored)
    WorldTangent = (vec4(aTangent, 0.0) * model).xyz;
    WorldBitangent = (vec4(aBitangent, 0.0) * model).xyz;

    gl_Position = worldPos * view * projection;
}
