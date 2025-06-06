using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenTK.Graphics.OpenGL4;

namespace StlGenerator.Rendering
{
    /// <summary>
    /// OpenGL着色器管理类
    /// </summary>
    public class Shader : IDisposable
    {
        public int Handle { get; private set; }
        private bool _disposedValue = false;

        public Shader(string vertexShaderSource, string fragmentShaderSource)
        {
            // 创建顶点着色器
            int vertexShader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertexShader, vertexShaderSource);
            GL.CompileShader(vertexShader);

            // 检查顶点着色器编译错误
            string infoLogVert = GL.GetShaderInfoLog(vertexShader);
            if (!string.IsNullOrWhiteSpace(infoLogVert))
                Console.WriteLine($"顶点着色器编译错误: {infoLogVert}");

            // 创建片段着色器
            int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShader, fragmentShaderSource);
            GL.CompileShader(fragmentShader);

            // 检查片段着色器编译错误
            string infoLogFrag = GL.GetShaderInfoLog(fragmentShader);
            if (!string.IsNullOrWhiteSpace(infoLogFrag))
                Console.WriteLine($"片段着色器编译错误: {infoLogFrag}");

            // 创建着色器程序
            Handle = GL.CreateProgram();
            GL.AttachShader(Handle, vertexShader);
            GL.AttachShader(Handle, fragmentShader);
            GL.LinkProgram(Handle);

            // 检查链接错误
            string infoLogLink = GL.GetProgramInfoLog(Handle);
            if (!string.IsNullOrWhiteSpace(infoLogLink))
                Console.WriteLine($"着色器程序链接错误: {infoLogLink}");

            // 链接后可以删除着色器
            GL.DetachShader(Handle, vertexShader);
            GL.DetachShader(Handle, fragmentShader);
            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);
        }

        public void Use()
        {
            GL.UseProgram(Handle);
        }

        public int GetAttribLocation(string attribName)
        {
            return GL.GetAttribLocation(Handle, attribName);
        }

        public int GetUniformLocation(string uniformName)
        {
            return GL.GetUniformLocation(Handle, uniformName);
        }

        public void SetMatrix4(string name, Matrix4 matrix)
        {
            int location = GetUniformLocation(name);
            GL.UniformMatrix4(location, false, ref matrix);
        }

        public void SetVector3(string name, Vector3 vector)
        {
            int location = GetUniformLocation(name);
            GL.Uniform3(location, vector.X, vector.Y, vector.Z);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                GL.DeleteProgram(Handle);
                _disposedValue = true;
            }
        }

        ~Shader()
        {
            if (_disposedValue == false)
                Console.WriteLine("GPU资源泄漏: 着色器未正确释放!");
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
