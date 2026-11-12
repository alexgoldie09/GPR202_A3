#version 330 core

// The same as Model.vert, but for many copies of a mesh drawn in one call (instancing).
// Each copy's model matrix comes from a vertex attribute that steps once per copy, instead of a uniform.

layout(location = 0) in vec3 aPos; // vertex position
layout(location = 1) in vec2 aTexCoord; // texture coordinate
layout(location = 2) in vec3 aNormal; // surface normal
layout(location = 3) in vec3 aTangent; // texture U direction, for normal mapping
layout(location = 4) in vec3 aBitangent; // texture V direction, for normal mapping
layout(location = 5) in vec4 aModelRow0; // this copy's model matrix, one row per attribute (locations 5 to 8)
layout(location = 6) in vec4 aModelRow1;
layout(location = 7) in vec4 aModelRow2;
layout(location = 8) in vec4 aModelRow3;

out vec2 TexCoord;
out vec3 WorldNormal;
out vec3 WorldPos;
out vec3 WorldTangent;
out vec3 WorldBitangent;

uniform mat4 view; // world to camera
uniform mat4 projection; // camera to clip space
uniform vec4 clipPlane; // water passes: anything on the negative side is cut away (only when clipping is on)

void main()
{
    // mat4() takes columns, so the rows are put in and then transposed, giving the same matrix as a
    // transposed uniform upload. Then the row-vector convention (vector on the left) works as in Model.vert.
    mat4 model = transpose(mat4(aModelRow0, aModelRow1, aModelRow2, aModelRow3));

    vec4 worldPos = vec4(aPos, 1.0) * model;
    WorldPos = worldPos.xyz;
    TexCoord = aTexCoord;

    // Props only use rotation and an even (uniform) scale, so the model matrix itself can turn the normals
    // (no inverse-transpose needed); normalising undoes the scale
    WorldNormal = normalize((vec4(aNormal, 0.0) * model).xyz);
    WorldTangent = (vec4(aTangent, 0.0) * model).xyz;
    WorldBitangent = (vec4(aBitangent, 0.0) * model).xyz;

    gl_ClipDistance[0] = dot(worldPos, clipPlane);
    gl_Position = worldPos * view * projection;
}
