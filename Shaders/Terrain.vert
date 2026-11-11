#version 330 core

// No vertex attributes: the terrain has no vertex buffer. Each vertex is built from its number (gl_VertexID),
// which the index buffer sets to z * GridSize + x, and its height is read from the height map texture.

out vec3 WorldPos; // Position in world space (for lighting, texturing and the water line)
out vec3 WorldNormal; // Surface normal in world space (for lighting)

uniform sampler2D HeightMap; // R32F, one texel per grid vertex, in world units (unit 0)
uniform int GridSize; // vertices along each side
uniform float GridSpacing; // world units between neighbouring vertices

uniform mat4 view; // View matrix (world to camera)
uniform mat4 projection; // Projection matrix (camera to clip space)

// Height of grid vertex (x, z). Positions past the edge are clamped, so edge vertices reuse their own height.
float HeightAt(int x, int z)
{
    ivec2 texel = clamp(ivec2(x, z), ivec2(0), ivec2(GridSize - 1));
    return texelFetch(HeightMap, texel, 0).r; // texelFetch reads one exact texel: no filtering, no mipmaps
}

void main()
{
    // Grid coordinates back from the vertex number
    int x = gl_VertexID % GridSize;
    int z = gl_VertexID / GridSize;

    // World position: the grid is centred on the origin, and the height comes from the texture
    float halfSize = float(GridSize - 1) * GridSpacing * 0.5;
    WorldPos = vec3(float(x) * GridSpacing - halfSize, HeightAt(x, z), float(z) * GridSpacing - halfSize);

    // Normal from the slope between the neighbours either side (central differences).
    // The surface rises by (right - left) over 2 grid spacings along x, and (up - down) along z,
    // so the normal leans the opposite way: (left - right, 2 * spacing, down - up), then normalised.
    float left = HeightAt(x - 1, z);
    float right = HeightAt(x + 1, z);
    float down = HeightAt(x, z - 1);
    float up = HeightAt(x, z + 1);
    WorldNormal = normalize(vec3(left - right, 2.0 * GridSpacing, down - up));

    // Row-vector convention (matrices are uploaded with transpose = true), so vectors go on the left.
    // The terrain is generated in world space, so there is no model matrix.
    gl_Position = vec4(WorldPos, 1.0) * view * projection;
}
