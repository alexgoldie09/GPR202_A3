#version 330 core

in vec2 TexCoord; // Texture coordinate from the vertex shader

out vec4 FragColor; // Output colour of the fragment

uniform sampler2D DiffuseMap; // The terrain's colour, stretched across the whole terrain (unit 0)
uniform int DebugView; // one of the DEBUGVIEW_ values, #defined from the C# DebugView enum

void main()
{
    // --- Debug views ---
    if (DebugView == DEBUGVIEW_UVS)
    // UVs: U = red, V = green: black at (0, 0), yellow at (1, 1)
    FragColor = vec4(TexCoord, 0.0, 1.0);
    else if (DebugView == DEBUGVIEW_WIREFRAME)
    // Wireframe: Game switches to line rasterisation, so only edges reach here; draw them in one solid colour
    FragColor = vec4(1.0, 1.0, 0.0, 1.0);
    else
    // Unlit for now: the terrain has no normals until Step 1
    FragColor = texture(DiffuseMap, TexCoord);
}
