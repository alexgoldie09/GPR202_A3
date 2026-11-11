#version 330 core

// The terrain has no vertex buffer. Each vertex works out its grid position from its number (gl_VertexID),
// and reads its height from the height map texture.

out vec3 WorldPos; // world-space position
out vec3 WorldNormal; // world-space normal

uniform sampler2D HeightMap; // one height per vertex (unit 0)
uniform int GridSize; // vertices along each side
uniform float GridSpacing; // world units between vertices

uniform mat4 view; // world to camera
uniform mat4 projection; // camera to clip space

// Height of vertex (x, z). Positions past the edge are clamped to it.
float HeightAt(int x, int z)
{
    ivec2 texel = clamp(ivec2(x, z), ivec2(0), ivec2(GridSize - 1));
    return texelFetch(HeightMap, texel, 0).r; // reads one exact texel (no filtering)
}

void main()
{
    // The index buffer numbers each vertex z * GridSize + x, so this gets x and z back
    int x = gl_VertexID % GridSize;
    int z = gl_VertexID / GridSize;

    // The grid is centred on the origin
    float halfSize = float(GridSize - 1) * GridSpacing / 2.0;
    WorldPos = vec3(float(x) * GridSpacing - halfSize, HeightAt(x, z), float(z) * GridSpacing - halfSize);

    // Normal from the neighbours' heights: if the ground rises to the right, the normal leans left
    float left = HeightAt(x - 1, z);
    float right = HeightAt(x + 1, z);
    float down = HeightAt(x, z - 1);
    float up = HeightAt(x, z + 1);
    WorldNormal = normalize(vec3(left - right, 2.0 * GridSpacing, down - up));

    // Row-vector convention (matrices uploaded transposed). No model matrix: the terrain is already in world space.
    gl_Position = vec4(WorldPos, 1.0) * view * projection;
}
