using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenTK.Graphics.OpenGL;
using OpenTK;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using System.Diagnostics;
using System.Reflection;
using StbImageSharp;
using System.Reflection.Metadata;
using System.IO;
using OpenTK.Windowing.GraphicsLibraryFramework;
using OpenTK.Input;
using static System.Net.Mime.MediaTypeNames;
using OpenTK.Windowing.Common.Input;
using System.Text.RegularExpressions;
using GPR_Command;

namespace GPR202_Assessment3
{
    public class Game : GameWindow
    {
        private Stopwatch timer;
        private int[] viewport = new int[4];
        
        Camera cam;
        Skybox skybox;
        Plane plane;
        Terrain terrain;
        Balloon balloon;

        public const int balloons_total = 8;
        public int balloons_left = balloons_total;
        public List<BalloonState> m_balloons;

        public Game() : base(GameWindowSettings.Default, NativeWindowSettings.Default)
        {
            this.CenterWindow(new Vector2i(1280, 720));
        }

        protected override void OnLoad()
        {
            timer = new Stopwatch();
            timer.Start();

            // OpenGL Setup
            GL.Enable(EnableCap.DepthTest);
            GL.ClearColor(new Color4(0.3f, 0.4f, 0.5f, 1f));

            GL.GetInteger(GetPName.Viewport, viewport);
            Console.WriteLine(viewport[0] + ", " + viewport[1] + ", " + viewport[2] + ", " + viewport[3]);

            // Load Game Resources
            cam = new Camera(Vector3.UnitZ * 3, (float)viewport[2], (float)viewport[3]);
            skybox = new Skybox();
            plane = new Plane();
            terrain = new Terrain();
            balloon = new Balloon();

            // Start a New Game
            SetupNewGame();


            base.OnLoad();
        }

        private void SetupNewGame()
        {
            balloons_left = balloons_total;
            m_balloons = new List<BalloonState>();
            for (int i = 0; i < balloons_left; i++)
            {
                BalloonState newballoon = new BalloonState();
                newballoon.Spawn();
                m_balloons.Add(newballoon);
                //Console.WriteLine("Balloon spawned at: " + newballoon.position.ToString());
            }
        }

        protected override void OnUnload()
        {
            timer.Stop();

            base.OnUnload();
        }

        protected override void OnResize(ResizeEventArgs e)
        {
            GL.Viewport(0, 0, e.Width, e.Height);
            viewport[2] = e.Width;
            viewport[3] = e.Height;
            base.OnResize(e);
        }


        protected override void OnUpdateFrame(FrameEventArgs args)
        {
            Title = $"GPR2 Balloon Hunt!         Remaining: {balloons_left}         FPS: {1f / args.Time:0}";

            base.OnUpdateFrame(args);

            if (!IsFocused) // check to see if the window is focused
            {
                return;
            }

            float deltatime = (float)args.Time;

            HandleInput(deltatime);

            plane.OnFrameUpdate(this.MouseState, viewport[2], viewport[3], deltatime);

            // Plane can collect Balloons!
            for (int i = 0; i < m_balloons.Count; i++)
            {
                bool collected = m_balloons[i].collected;
                if (m_balloons[i].Update(plane.position, deltatime))
                {
                    // Destroy balloon.
                } 
                else
                {
                    if (m_balloons[i].collected != collected)
                    {
                        balloons_left--;
                        Console.WriteLine("Balloon Collected! " + balloons_left + " remaining.");
                    }
                }
            }
            
        }

        private bool firstMove = true;
        private Vector2 lastPos;
        private void HandleInput(float deltaT)
        {
            // Keyboard
            KeyboardState keyboard = KeyboardState;
            if (keyboard.IsKeyDown(Keys.Escape))
                Close();

            // Mouse
            MouseState mouse = this.MouseState;
            if (firstMove) // this bool variable is initially set to true
            {
                lastPos = new Vector2(mouse.X, mouse.Y);
                firstMove = false;
            }
            else
            {
                float TurnFactor = ((1.0f / viewport[2]) * mouse.X);
                float TurnFactorN;
                float midPoint = 0.5f;
                if (TurnFactor > 0.5f)
                {
                    TurnFactor = 1 - TurnFactor;
                    TurnFactorN = (1 - ((TurnFactor - ((1 - TurnFactor) * TurnFactor)) * (1 / (1 - midPoint))));
                }
                else
                {
                    TurnFactorN = (TurnFactor - ((1 - TurnFactor) * TurnFactor)) * (1 / midPoint);
                }

                float deltaX = ((TurnFactorN - 0.5f) * 2f) * 0.1f;

                //Apply yaw rotation to camera and plane together
                cam.Yaw -= deltaX * 1f * plane.TurnSpeed * deltaT;
                plane.Yaw -= deltaX * -1f * plane.TurnSpeed * deltaT;
            }
        }

        protected override void OnMouseMove(MouseMoveEventArgs e)
        {
            base.OnMouseMove(e);
        }

        protected override void OnRenderFrame(FrameEventArgs args)
        {
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // update the uniform color
            double timeValue = timer.Elapsed.TotalSeconds;

            // Setup camera position and rotation
            Vector3 cam_pos = new Vector3(0f, 0f, 0f);
            Vector3 cam_dir = new Vector3(cam.Yaw, 20.0f, 0.0f);
            float distance = 11.0f;
            cam_pos.X = (float)(distance * -Math.Sin(cam_dir.X * (Math.PI / 180)) * Math.Cos((cam_dir.Y) * (Math.PI / 180)));
            cam_pos.Y = (float)(distance * -Math.Sin((cam_dir.Y) * (Math.PI / 180)));
            cam_pos.Z = (float)(-distance * Math.Cos((cam_dir.X) * (Math.PI / 180)) * Math.Cos((cam_dir.Y) * (Math.PI / 180)));
            Matrix4 view = Matrix4.LookAt(plane.GetPosition() - cam_pos, plane.GetPosition(), new Vector3(0f, 1f, 0f));

            // Rotate & Move Plane
            //float rotY = MathHelper.DegreesToRadians(90.0f);
            float rotY = (float)MathHelper.DegreesToRadians(180.0f + cam.Yaw);
            plane.SetRotation(new Vector3(0f, rotY, 0f));
            plane.UpdateVertices();
            plane.position -= Vector3.Normalize(plane.Right) * plane.FlySpeed * (float)args.Time;


            // Skybox
            Matrix4 proj = cam.GetProjectionMatrix();
            GL.DepthFunc(DepthFunction.Lequal);
            skybox.Draw(view, proj);
            GL.DepthFunc(DepthFunction.Less);


            // Draw Plane
            plane.Draw(view, proj, cam);

            // Draw Balloons
            for (int i = 0; i < m_balloons.Count; i++)
                balloon.Draw(m_balloons[i].position, m_balloons[i].rotation, m_balloons[i].scale, view, proj);

            // Draw Landscape
            terrain.Draw(view, proj);



            // Finalise Frame
            this.Context.SwapBuffers();
            base.OnRenderFrame(args);
        }
    }
}