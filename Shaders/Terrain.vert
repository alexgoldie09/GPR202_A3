#version 330 core

// The terrain has no vertex buffer. Each vertex works out its grid position from its number (gl_VertexID),
// and reads its height from the height map texture. TerrainLod draws each patch with a base vertex, which
// gl_VertexID includes, so every patch's vertex numbers come out as z * GridSize + x.
// Normals aren't worked out here: the fragment shader reads them from the normal map, per pixel.

out vec3 WorldPos; // world-space position

uniform sampler2D HeightMap; // one height per vertex (unit 0)
uniform int GridSize; // vertices along each side
uniform float GridSpacing; // world units between vertices

uniform mat4 view; // world to camera
uniform mat4 projection; // camera to clip space
uniform vec4 clipPlane; // water passes: anything on the negative side is cut away (ignored when clipping is off)

void main()
{
    // The vertex number is z * GridSize + x, so this gets x and z back
    int x = gl_VertexID % GridSize;
    int z = gl_VertexID / GridSize;

    // The grid is centred on the origin
    float halfSize = float(GridSize - 1) * GridSpacing / 2.0;
    float height = texelFetch(HeightMap, ivec2(x, z), 0).r; // reads one exact texel (no filtering)
    WorldPos = vec3(float(x) * GridSpacing - halfSize, height, float(z) * GridSpacing - halfSize);

    // Row-vector convention (matrices uploaded transposed). No model matrix: the terrain is already in world space.
    gl_ClipDistance[0] = dot(vec4(WorldPos, 1.0), clipPlane);
    gl_Position = vec4(WorldPos, 1.0) * view * projection;
}
