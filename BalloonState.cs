using GPR202_Assessment3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenTK.Mathematics;
using System.Diagnostics;

namespace GPR_Command
{
    public class BalloonState
    {
        private const float DEFAULT_SCALE = 2.0f;
        private const float ROTATE_SPEED = 1.0f;

        public static Random random = new Random();

        public Vector3 position;
        public Vector3 rotation;
        public bool collected;
        public float animate;
        public float scale;

        

        public BalloonState()
        {
            position = Vector3.Zero;
            collected = false;
            animate = 0f;
            scale = DEFAULT_SCALE;
        }

        // Reset Balloon State and assign a random position in the game world.
        public void Spawn()
        {
            int position_x = random.Next(0, (int)Terrain.width);
            int position_z = random.Next(0, (int)Terrain.length);
            float balloon_h = random.Next(15, 65);

            position = Terrain.WorldPositionFromHeightMap(position_x, position_z) + new Vector3(0f, balloon_h, 0f);
            rotation = new Vector3(0f, random.Next(0, 359), 0f);
            collected = false;
            animate = 0f;
            scale = DEFAULT_SCALE;
        }

        public bool Update(Vector3 playerpos, float deltaT)
        {
            if (collected == false)
            {
                rotation += new Vector3(0f, deltaT * ROTATE_SPEED, 0f);

                // Test distance between this balloon and plane. If lower than collect threshold, trigger collection.
                if (Vector3.Distance(position + new Vector3(0f, 5.0f, 0f), playerpos) < 15.0f)
                {
                    collected = true;
                    animate = 0.0001f;
                }
            }
            else
            {
                if (animate > 0f && animate < 1f)
                {
                    animate += (deltaT * 4);
                    animate = MathHelper.Clamp(animate, 0f, 1f);
                    scale = MathHelper.Clamp(2f - (animate * 2), 0f, 2f);
                    position += new Vector3(0f, (deltaT * 64), 0f);
                    rotation += new Vector3((deltaT * 64), 0, (deltaT * 64));
                    if (animate == 1f)
                    {
                        // Return true to destroy balloon.
                        return true;
                    }
                    
                }
            }
            return false;
        }
    }
}
