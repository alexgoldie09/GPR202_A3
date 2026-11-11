#version 330 core

layout(location = 0) in vec3 aPos; // Corner of the cube, centred on the origin

out vec3 TexCoords; // Direction from the cube's centre

uniform mat4 view; // View matrix (world to camera)
uniform mat4 projection; // Projection matrix (camera to clip space)

void main()
{
    // The cube is centred on the origin, so each corner's position is also the direction to it from the centre
    TexCoords = aPos;

    // Row-vector convention (matrices are uploaded with transpose = true), so vectors go on the left.
    // w = 0: the translation part of the view matrix is ignored, so the skybox doesn't move when the camera moves.
    vec4 pos = vec4(aPos, 0.0) * view * projection;

    // The skybox is drawn behind everything else, so it should always be at the far plane. 
    gl_Position = pos.xyww;
}
