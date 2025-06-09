using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using StlGenerator.Models;
using System;
using System.Collections.Generic;

namespace StlGenerator.Rendering
{
    /// <summary>
    /// OpenGL渲染器，负责将3D模型数据渲染到屏幕上
    /// 管理顶点缓冲、着色器程序和渲染流程
    /// </summary>
    public class OpenGLRenderer : IDisposable
    {
        private Model _model;                          // 存储要渲染的3D模型
        private int _vertexArrayObject;                // 顶点数组对象(VAO)句柄
        private int _vertexBufferObject;               // 顶点缓冲对象(VBO)句柄
        private int _elementBufferObject;              // 元素缓冲对象(EBO)句柄
        private Shader _shader;                        // 着色器程序
        private bool _isInitialized = false;           // 初始化状态标记
        private int _triangleCount = 0;                // 模型包含的三角形数量

        /// <summary>构造函数：创建渲染器实例</summary>
        public OpenGLRenderer() { }

        /// <summary>
        /// 初始化渲染器，根据模型数据创建OpenGL缓冲区和着色器
        /// </summary>
        /// <param name="model">要渲染的3D模型</param>
        public void Initialize(Model model)
        {
            _model = model;
            CreateBuffers();       // 创建顶点数据缓冲区
            CreateShaders();       // 初始化着色器程序
            _isInitialized = true; // 标记初始化完成
            _triangleCount = model.Triangles.Count; // 记录三角形数量用于绘制
        }

        /// <summary>
        /// 创建OpenGL缓冲区，将模型顶点数据上传到GPU
        /// 包含顶点位置、法线和颜色信息的组织与存储
        /// </summary>
        private void CreateBuffers()
        {
            if (_model.Triangles.Count == 0) return;

            // 准备顶点属性数据和索引数据
            List<float> vertexData = new List<float>();  // 存储顶点位置/法线/颜色
            List<uint> indices = new List<uint>();        // 存储顶点索引以优化绘制
            uint index = 0;                              // 索引计数器

            // 遍历模型中的每个三角形，提取三个顶点的属性
            foreach (var triangle in _model.Triangles)
            {
                // 顶点1：位置+法线+颜色
                AddVertexData(vertexData, triangle.Vertex1, triangle.Normal, triangle.Color);
                indices.Add(index++);

                // 顶点2：位置+法线+颜色
                AddVertexData(vertexData, triangle.Vertex2, triangle.Normal, triangle.Color);
                indices.Add(index++);

                // 顶点3：位置+法线+颜色
                AddVertexData(vertexData, triangle.Vertex3, triangle.Normal, triangle.Color);
                indices.Add(index++);
            }

            // 生成并绑定OpenGL缓冲区对象
            _vertexArrayObject = GL.GenVertexArray();   // 创建VAO管理顶点属性
            _vertexBufferObject = GL.GenBuffer();       // 创建VBO存储顶点数据
            _elementBufferObject = GL.GenBuffer();      // 创建EBO存储索引数据

            // 绑定VAO（后续操作将关联到该VAO）
            GL.BindVertexArray(_vertexArrayObject);

            // 上传顶点数据到VBO（静态绘制，数据不常更新）
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBufferObject);
            GL.BufferData(BufferTarget.ArrayBuffer, vertexData.Count * sizeof(float),
                         vertexData.ToArray(), BufferUsageHint.StaticDraw);

            // 上传索引数据到EBO（减少重复顶点存储）
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _elementBufferObject);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Count * sizeof(uint),
                         indices.ToArray(), BufferUsageHint.StaticDraw);

            // 设置顶点属性指针（告知OpenGL如何解析数据）
            int stride = 10 * sizeof(float); // 每个顶点包含10个float：3位置+3法线+4颜色

            // 位置属性：对应顶点着色器中的aPosition
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
            GL.EnableVertexAttribArray(0);

            // 法线属性：对应顶点着色器中的aNormal
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            // 颜色属性：对应顶点着色器中的aColor
            GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));
            GL.EnableVertexAttribArray(2);

            // 解绑VAO，完成缓冲区配置
            GL.BindVertexArray(0);
        }

        /// <summary>向顶点数据列表添加单个顶点的属性值</summary>
        private void AddVertexData(List<float> data, Vector3 vertex, Vector3 normal, Color4 color)
        {
            // 按顺序添加位置、法线、颜色分量
            data.Add(vertex.X); data.Add(vertex.Y); data.Add(vertex.Z);   // 3个位置分量
            data.Add(normal.X); data.Add(normal.Y); data.Add(normal.Z);   // 3个法线分量
            data.Add(color.R); data.Add(color.G); data.Add(color.B); data.Add(color.A); // 4个颜色分量
        }

        /// <summary>
        /// 创建着色器程序，包含顶点着色器和片段着色器
        /// 实现模型变换和Phong光照模型
        /// </summary>
        private void CreateShaders()
        {
            // 顶点着色器源代码（GLSL）
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

            // 片段着色器源代码（GLSL）
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

            // 创建着色器程序实例
            _shader = new Shader(vertexShaderSource, fragmentShaderSource);
        }

        /// <summary>
        /// 渲染模型，将变换矩阵和光照参数传入着色器并执行绘制
        /// </summary>
        /// <param name="modelMatrix">模型变换矩阵</param>
        /// <param name="viewMatrix">视图矩阵</param>
        /// <param name="projectionMatrix">投影矩阵</param>
        /// <param name="lightPos">光源位置</param>
        /// <param name="cameraPosition">相机位置</param>
        public void Render(Matrix4 modelMatrix, Matrix4 viewMatrix, Matrix4 projectionMatrix,
                          Vector3 lightPos, Vector3 cameraPosition)
        {
            if (!_isInitialized || _triangleCount == 0) return;

            // 激活着色器程序
            _shader.Use();

            // 传递变换矩阵（控制模型显示位置和角度）
            _shader.SetMatrix4("model", modelMatrix);
            _shader.SetMatrix4("view", viewMatrix);
            _shader.SetMatrix4("projection", projectionMatrix);

            // 传递光照参数（控制模型光照效果）
            _shader.SetVector3("lightPos", lightPos);
            _shader.SetVector3("viewPos", cameraPosition);
            _shader.SetVector3("lightColor", new Vector3(1.0f, 1.0f, 1.0f)); // 白色光源

            // 绑定VAO并执行索引绘制（提高绘制效率）
            GL.BindVertexArray(_vertexArrayObject);
            GL.DrawElements(PrimitiveType.Triangles, _triangleCount * 3, DrawElementsType.UnsignedInt, 0);
            GL.BindVertexArray(0);
        }

        /// <summary>释放OpenGL资源，防止内存泄漏</summary>
        public void Dispose()
        {
            // 解绑所有缓冲区（确保资源可被安全删除）
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
            GL.BindVertexArray(0);

            // 删除缓冲区对象（释放GPU内存）
            if (_vertexBufferObject != 0) GL.DeleteBuffer(_vertexBufferObject);
            if (_elementBufferObject != 0) GL.DeleteBuffer(_elementBufferObject);
            if (_vertexArrayObject != 0) GL.DeleteVertexArray(_vertexArrayObject);

            // 释放着色器资源
            if (_shader != null) _shader.Dispose();

            _isInitialized = false;
        }
    }
}