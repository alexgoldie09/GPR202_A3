#version 330 core

// One quad drawn once per particle (instancing). The corner comes from the quad; everything else is per particle.
layout(location = 0) in vec2 aCorner; // quad corner, -0.5 to 0.5
layout(location = 1) in vec4 aPositionSize; // world position (xyz) and size (w)
layout(location = 2) in vec4 aColor; // colour at its current age
layout(location = 3) in vec4 aRotation; // tumble axis (xyz, tumbling quads only) and angle in radians (w)
layout(location = 4) in float aSprite; // which atlas cell

out vec2 TexCoord; // position in the atlas
out vec4 ParticleColor;
out vec3 WorldPos; // for the fog

uniform mat4 view; // world to camera
uniform mat4 projection; // camera to clip space
uniform vec3 cameraRight; // the camera's right direction in the world (for billboards)
uniform vec3 cameraUp; // the camera's up direction in the world
uniform int Billboard; // 1 = face the camera, 0 = tumble in 3D
uniform vec2 atlasGrid; // atlas cells across and down

void main()
{
    float size = aPositionSize.w;
    float angle = aRotation.w;
    float s = sin(angle);
    float c = cos(angle);

    vec3 offset;
    if (Billboard == 1)
    {
        // Spin the corner in 2D, then lay it out along the camera's right and up, so the quad faces the camera
        vec2 spun = vec2(aCorner.x * c - aCorner.y * s, aCorner.x * s + aCorner.y * c) * size;
        offset = cameraRight * spun.x + cameraUp * spun.y;
    }
    else
    {
        // Rotate the flat quad around its own axis (Rodrigues' rotation formula), so it tumbles like paper
        vec3 corner = vec3(aCorner * size, 0.0);
        vec3 axis = normalize(aRotation.xyz);
        offset = corner * c + cross(axis, corner) * s + axis * dot(axis, corner) * (1.0 - c);
    }

    // Row-vector convention (matrices are uploaded with transpose = true), so vectors go on the left
    vec4 worldPos = vec4(aPositionSize.xyz + offset, 1.0);
    gl_Position = worldPos * view * projection;
    WorldPos = worldPos.xyz;

    // This sprite's cell in the atlas, and this corner's place in it
    vec2 cell = vec2(mod(aSprite, atlasGrid.x), floor(aSprite / atlasGrid.x));
    TexCoord = (cell + aCorner + 0.5) / atlasGrid;

    ParticleColor = aColor;
}
