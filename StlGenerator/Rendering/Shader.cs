using OpenTK.Mathematics;
using System;
using OpenTK.Graphics.OpenGL4;

namespace StlGenerator.Rendering
{
    /// <summary>
    /// OpenGL着色器管理类，封装着色器程序的创建、编译、链接及资源管理
    /// </summary>
    public class Shader : IDisposable
    {
        /// <summary>着色器程序句柄，用于OpenGL API调用</summary>
        public int Handle { get; private set; }
        private bool _disposedValue = false; // 资源释放标记

        /// <summary>
        /// 构造函数：根据顶点着色器和片段着色器源代码创建着色器程序
        /// </summary>
        /// <param name="vertexShaderSource">顶点着色器GLSL源代码</param>
        /// <param name="fragmentShaderSource">片段着色器GLSL源代码</param>
        public Shader(string vertexShaderSource, string fragmentShaderSource)
        {
            #region 顶点着色器编译
            // 创建顶点着色器对象
            int vertexShader = GL.CreateShader(ShaderType.VertexShader);
            // 绑定顶点着色器源代码
            GL.ShaderSource(vertexShader, vertexShaderSource);
            // 编译顶点着色器
            GL.CompileShader(vertexShader);

            // 检查编译错误并输出日志
            string infoLogVert = GL.GetShaderInfoLog(vertexShader);
            if (!string.IsNullOrWhiteSpace(infoLogVert))
                Console.WriteLine($"顶点着色器编译错误: {infoLogVert}");
            #endregion

            #region 片段着色器编译
            // 创建片段着色器对象
            int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            // 绑定片段着色器源代码
            GL.ShaderSource(fragmentShader, fragmentShaderSource);
            // 编译片段着色器
            GL.CompileShader(fragmentShader);

            // 检查编译错误并输出日志
            string infoLogFrag = GL.GetShaderInfoLog(fragmentShader);
            if (!string.IsNullOrWhiteSpace(infoLogFrag))
                Console.WriteLine($"片段着色器编译错误: {infoLogFrag}");
            #endregion

            #region 着色器程序链接
            // 创建着色器程序对象
            Handle = GL.CreateProgram();
            // 附加顶点着色器和片段着色器到程序
            GL.AttachShader(Handle, vertexShader);
            GL.AttachShader(Handle, fragmentShader);
            // 链接着色器程序
            GL.LinkProgram(Handle);

            // 检查链接错误并输出日志
            string infoLogLink = GL.GetProgramInfoLog(Handle);
            if (!string.IsNullOrWhiteSpace(infoLogLink))
                Console.WriteLine($"着色器程序链接错误: {infoLogLink}");

            // 链接完成后，着色器对象不再需要，可安全删除
            GL.DetachShader(Handle, vertexShader);
            GL.DetachShader(Handle, fragmentShader);
            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);
            #endregion
        }

        /// <summary>激活当前着色器程序，使其在渲染时生效</summary>
        public void Use()
        {
            GL.UseProgram(Handle);
        }

        /// <summary>获取顶点属性的位置索引（用于顶点数据绑定）</summary>
        /// <param name="attribName">顶点属性名称（需与着色器中定义一致）</param>
        /// <returns>属性位置索引，-1表示未找到</returns>
        public int GetAttribLocation(string attribName)
        {
            return GL.GetAttribLocation(Handle, attribName);
        }

        /// <summary>获取Uniform变量的位置索引（用于向着色器传递数据）</summary>
        /// <param name="uniformName">Uniform变量名称（需与着色器中定义一致）</param>
        /// <returns>Uniform位置索引，-1表示未找到</returns>
        public int GetUniformLocation(string uniformName)
        {
            return GL.GetUniformLocation(Handle, uniformName);
        }

        /// <summary>向着色器传递矩阵数据（如模型矩阵、视图矩阵等）</summary>
        /// <param name="name">Uniform变量名称</param>
        /// <param name="matrix">要传递的矩阵数据</param>
        public void SetMatrix4(string name, Matrix4 matrix)
        {
            int location = GetUniformLocation(name);
            GL.UniformMatrix4(location, false, ref matrix);
        }

        /// <summary>向着色器传递向量数据（如光源位置、颜色等）</summary>
        /// <param name="name">Uniform变量名称</param>
        /// <param name="vector">要传递的向量数据</param>
        public void SetVector3(string name, Vector3 vector)
        {
            int location = GetUniformLocation(name);
            GL.Uniform3(location, vector.X, vector.Y, vector.Z);
        }

        #region 资源释放逻辑（实现IDisposable接口）
        /// <summary>释放非托管资源的核心方法</summary>
        /// <param name="disposing">是否为显式释放（true表示通过Dispose调用，false表示垃圾回收）</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                // 删除OpenGL着色器程序对象，释放GPU资源
                GL.DeleteProgram(Handle);
                _disposedValue = true;
            }
        }

        /// <summary>析构函数：确保未显式释放时仍能回收资源（防止内存泄漏）</summary>
        ~Shader()
        {
            if (_disposedValue == false)
                Console.WriteLine("GPU资源泄漏: 着色器未正确释放!");
            Dispose(false);
        }

        /// <summary>显式释放资源的方法（供用户调用）</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this); // 通知垃圾回收器无需调用析构函数
        }
        #endregion
    }
}