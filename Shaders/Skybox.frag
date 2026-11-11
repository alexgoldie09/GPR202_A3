#version 330 core

in vec3 TexCoords; // Direction from the cube's centre, from the vertex shader

out vec4 FragColor; // Output colour of the fragment

uniform samplerCube SkyboxMap; // The sky cube map (unit 4)

void main()
{
    // A cube map picks the face the direction points at, then the pixel on that face
    FragColor = texture(SkyboxMap, TexCoords);
}
