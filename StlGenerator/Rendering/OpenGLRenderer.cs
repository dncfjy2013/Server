using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using StlGenerator.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StlGenerator.Rendering
{
    /// <summary>
    /// OpenGL渲染器，负责将3D模型渲染到屏幕上
    /// </summary>
    public class OpenGLRenderer : IDisposable
    {
        private Model _model;
        private int _vertexArrayObject;
        private int _vertexBufferObject;
        private int _elementBufferObject;
        private Shader _shader;
        private bool _isInitialized = false;
        private int _triangleCount = 0;

        public OpenGLRenderer()
        {
        }

        public void Initialize(Model model)
        {
            _model = model;
            CreateBuffers();
            CreateShaders();
            _isInitialized = true;
            _triangleCount = model.Triangles.Count;
        }

        private void CreateBuffers()
        {
            if (_model.Triangles.Count == 0)
                return;

            // 准备顶点数据
            List<float> vertexData = new List<float>();
            List<uint> indices = new List<uint>();
            uint index = 0;

            foreach (var triangle in _model.Triangles)
            {
                // 顶点1
                vertexData.Add(triangle.Vertex1.X);
                vertexData.Add(triangle.Vertex1.Y);
                vertexData.Add(triangle.Vertex1.Z);
                vertexData.Add(triangle.Normal.X);
                vertexData.Add(triangle.Normal.Y);
                vertexData.Add(triangle.Normal.Z);
                vertexData.Add(triangle.Color.R);
                vertexData.Add(triangle.Color.G);
                vertexData.Add(triangle.Color.B);
                vertexData.Add(triangle.Color.A);
                indices.Add(index++);

                // 顶点2
                vertexData.Add(triangle.Vertex2.X);
                vertexData.Add(triangle.Vertex2.Y);
                vertexData.Add(triangle.Vertex2.Z);
                vertexData.Add(triangle.Normal.X);
                vertexData.Add(triangle.Normal.Y);
                vertexData.Add(triangle.Normal.Z);
                vertexData.Add(triangle.Color.R);
                vertexData.Add(triangle.Color.G);
                vertexData.Add(triangle.Color.B);
                vertexData.Add(triangle.Color.A);
                indices.Add(index++);

                // 顶点3
                vertexData.Add(triangle.Vertex3.X);
                vertexData.Add(triangle.Vertex3.Y);
                vertexData.Add(triangle.Vertex3.Z);
                vertexData.Add(triangle.Normal.X);
                vertexData.Add(triangle.Normal.Y);
                vertexData.Add(triangle.Normal.Z);
                vertexData.Add(triangle.Color.R);
                vertexData.Add(triangle.Color.G);
                vertexData.Add(triangle.Color.B);
                vertexData.Add(triangle.Color.A);
                indices.Add(index++);
            }

            // 创建VAO、VBO和EBO
            _vertexArrayObject = GL.GenVertexArray();
            _vertexBufferObject = GL.GenBuffer();
            _elementBufferObject = GL.GenBuffer();

            // 绑定VAO
            GL.BindVertexArray(_vertexArrayObject);

            // 绑定VBO并上传数据
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBufferObject);
            GL.BufferData(BufferTarget.ArrayBuffer, vertexData.Count * sizeof(float), vertexData.ToArray(), BufferUsageHint.StaticDraw);

            // 绑定EBO并上传数据
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _elementBufferObject);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Count * sizeof(uint), indices.ToArray(), BufferUsageHint.StaticDraw);

            // 设置顶点属性指针
            int stride = 10 * sizeof(float); // 每个顶点包含10个float值

            // 位置属性
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
            GL.EnableVertexAttribArray(0);

            // 法线属性
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            // 颜色属性
            GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));
            GL.EnableVertexAttribArray(2);

            // 解绑VAO
            GL.BindVertexArray(0);
        }

        private void CreateShaders()
        {
            string vertexShaderSource = @"
                #version 330 core

                layout(location = 0) in vec3 aPosition;
                layout(location = 1) in vec3 aNormal;
                layout(location = 2) in vec4 aColor;

                uniform mat4 model;
                uniform mat4 view;
                uniform mat4 projection;

                out vec3 FragPos;
                out vec3 Normal;
                out vec4 Color;

                void main()
                {
                    gl_Position = projection * view * model * vec4(aPosition, 1.0);
                    FragPos = vec3(model * vec4(aPosition, 1.0));
                    Normal = mat3(transpose(inverse(model))) * aNormal;
                    Color = aColor;
                }
                ";

            string fragmentShaderSource = @"
                #version 330 core

                in vec3 FragPos;
                in vec3 Normal;
                in vec4 Color;

                out vec4 FragColor;

                uniform vec3 lightPos;
                uniform vec3 viewPos;
                uniform vec3 lightColor;

                void main()
                {
                    float ambientStrength = 0.2;
                    vec3 ambient = ambientStrength * lightColor;

                    vec3 norm = normalize(Normal);
                    vec3 lightDir = normalize(lightPos - FragPos);
                    float diff = max(dot(norm, lightDir), 0.0);
                    vec3 diffuse = diff * lightColor;

                    float specularStrength = 0.5;
                    vec3 viewDir = normalize(viewPos - FragPos);
                    vec3 reflectDir = reflect(-lightDir, norm);
                    float spec = pow(max(dot(viewDir, reflectDir), 0.0), 32);
                    vec3 specular = specularStrength * spec * lightColor;

                    vec3 result = (ambient + diffuse + specular) * Color.rgb;
                    FragColor = vec4(result, Color.a);
                }
                ";

            _shader = new Shader(vertexShaderSource, fragmentShaderSource);
        }

        public void Render(Matrix4 modelMatrix, Matrix4 viewMatrix, Matrix4 projectionMatrix, Vector3 lightPos, Vector3 cameraPosition)
        {
            if (!_isInitialized || _triangleCount == 0)
                return;

            _shader.Use();

            // 设置矩阵
            _shader.SetMatrix4("model", modelMatrix);
            _shader.SetMatrix4("view", viewMatrix);
            _shader.SetMatrix4("projection", projectionMatrix);

            // 设置光照参数
            _shader.SetVector3("lightPos", lightPos);
            _shader.SetVector3("viewPos", cameraPosition);
            _shader.SetVector3("lightColor", new Vector3(1.0f, 1.0f, 1.0f));

            // 绘制模型
            GL.BindVertexArray(_vertexArrayObject);
            GL.DrawElements(PrimitiveType.Triangles, _triangleCount * 3, DrawElementsType.UnsignedInt, 0);
            GL.BindVertexArray(0);
        }

        public void Dispose()
        {
            // 清理OpenGL资源
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
            GL.BindVertexArray(0);

            if (_vertexBufferObject != 0)
                GL.DeleteBuffer(_vertexBufferObject);

            if (_elementBufferObject != 0)
                GL.DeleteBuffer(_elementBufferObject);

            if (_vertexArrayObject != 0)
                GL.DeleteVertexArray(_vertexArrayObject);

            if (_shader != null)
                _shader.Dispose();

            _isInitialized = false;
        }
    }
}
