using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GPR202_Assessment3
{
    public class GameObject
    {
        public Vector3 position = new Vector3(0f, 0f, 0f);
        protected Vector3 rotation = new Vector3(0f, 0f, 0f);
        protected float scale = 1f;

        public Matrix4 model;

        protected int vbo, vao, ebo;
        protected int textureHandle;

        public Vector3 GetPosition()
        {
            return position;
        }
        public void SetRotation(Vector3 rot)
        {
            rotation = rot;
        }
        public Vector3 GetRotation()
        {
            return rotation;
        }
    }

    public struct VertexData
    {
        public float x, y, z, u, v;
        public VertexData(float X, float Y, float Z, float U, float V)
        {
            x = X;
            y = Y;
            z = Z;
            u = U;
            v = V;
        }
    }
}
