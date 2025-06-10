using FbxGenerator.Engine;
using FbxGenerator.Rendering;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System.Collections.Generic;

namespace FbxGenerator.Particles
{
    public class ParticleSystem : Component
    {
        public int MaxParticles { get; set; } = 1000;
        public float EmissionRate { get; set; } = 100.0f;
        public float LifeTime { get; set; } = 2.0f;
        public Vector3 InitialVelocity { get; set; } = Vector3.UnitY;
        public Vector3 VelocityVariation { get; set; } = Vector3.One;
        public Color4 StartColor { get; set; } = Color4.White;
        public Color4 EndColor { get; set; } = Color4.Transparent;
        public float StartSize { get; set; } = 0.1f;
        public float EndSize { get; set; } = 0.0f;

        private List<Particle> _particles = new();
        private float _emissionTimer = 0.0f;
        private ShaderProgram? _shaderProgram;
        private int _vao, _vbo;
        private Texture? _texture;
        private Transform _transform;

        public override void Initialize()
        {
            base.Initialize();

            // 创建着色器程序
            _shaderProgram = new ShaderProgram(
                @"Assets/Shaders/particle_vertex_shader.glsl",
                @"Assets/Shaders/particle_fragment_shader.glsl");

            // 创建VAO和VBO
            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();

            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);

            // 设置粒子属性
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 11 * sizeof(float), 0);

            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, 11 * sizeof(float), 3 * sizeof(float));

            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, 11 * sizeof(float), 7 * sizeof(float));

            GL.EnableVertexAttribArray(3);
            GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, 11 * sizeof(float), 10 * sizeof(float));

            GL.BindVertexArray(0);

            // 加载粒子纹理
            _texture = new Texture(@"Assets/Textures/particle.png");
            _transform = GameObject.Transform;
        }

        public override void Update(float deltaTime)
        {
            _emissionTimer += deltaTime;

            // 发射新粒子
            var particlesToSpawn = (int)(_emissionTimer * EmissionRate);
            _emissionTimer -= particlesToSpawn / EmissionRate;

            for (int i = 0; i < particlesToSpawn && _particles.Count < MaxParticles; i++)
            {
                SpawnParticle();
            }

            // 更新所有粒子
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                var particle = _particles[i];
                particle.LifeRemaining -= deltaTime;

                if (particle.LifeRemaining <= 0)
                {
                    _particles.RemoveAt(i);
                    continue;
                }

                // 更新粒子位置
                particle.Position += particle.Velocity * deltaTime;

                // 应用重力或其他力
                particle.Velocity += new Vector3(0, -9.81f, 0) * deltaTime;

                _particles[i] = particle;
            }
        }

        private void SpawnParticle()
        {
            var random = new System.Random();

            var particle = new Particle
            {
                Position = _transform.Position,
                Velocity = InitialVelocity + new Vector3(
                    (float)random.NextDouble() * 2 - 1,
                    (float)random.NextDouble() * 2 - 1,
                    (float)random.NextDouble() * 2 - 1) * VelocityVariation,
                Color = StartColor,
                Size = StartSize,
                LifeRemaining = LifeTime,
                LifeTime = LifeTime
            };

            _particles.Add(particle);
        }

        public void Render(ShaderProgram cameraShader, Transform cameraTransform)
        {
            if (_shaderProgram == null || _texture == null)
                return;

            _shaderProgram.Use();

            // 设置相机相关的uniforms
            _shaderProgram.SetMatrix4("view", cameraShader.GetMatrix4("view"));
            _shaderProgram.SetMatrix4("projection", cameraShader.GetMatrix4("projection"));

            // 设置粒子系统的uniforms
            _shaderProgram.SetVector3("cameraPosition", cameraTransform.Position);
            _shaderProgram.SetColor4("startColor", StartColor);
            _shaderProgram.SetColor4("endColor", EndColor);
            _shaderProgram.SetFloat("startSize", StartSize);
            _shaderProgram.SetFloat("endSize", EndSize);

            // 绑定纹理
            _texture.Bind(0);
            _shaderProgram.SetInt("particleTexture", 0);

            // 准备粒子数据
            var particleData = new List<float>();

            foreach (var particle in _particles)
            {
                var lifeRatio = particle.LifeRemaining / particle.LifeTime;

                particleData.Add(particle.Position.X);
                particleData.Add(particle.Position.Y);
                particleData.Add(particle.Position.Z);

                particleData.Add(particle.Color.R);
                particleData.Add(particle.Color.G);
                particleData.Add(particle.Color.B);
                particleData.Add(particle.Color.A * lifeRatio);

                particleData.Add(particle.Velocity.X);
                particleData.Add(particle.Velocity.Y);
                particleData.Add(particle.Velocity.Z);

                particleData.Add(lifeRatio);
            }

            // 更新VBO数据
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, particleData.Count * sizeof(float), particleData.ToArray(), BufferUsageHint.StreamDraw);

            // 渲染粒子
            GL.BindVertexArray(_vao);
            GL.DrawArrays(PrimitiveType.Points, 0, _particles.Count);
            GL.BindVertexArray(0);
        }
    }

    public struct Particle
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public Color4 Color;
        public float Size;
        public float LifeRemaining;
        public float LifeTime;
    }
}