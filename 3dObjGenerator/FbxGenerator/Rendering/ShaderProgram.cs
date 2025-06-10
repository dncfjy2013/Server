using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.IO;

namespace FbxGenerator.Rendering
{
    public class ShaderProgram : IDisposable
    {
        private readonly int _programId;
        private bool _disposedValue;

        public ShaderProgram(string vertexPath, string fragmentPath)
        {
            // 加载并编译顶点着色器
            var vertexShader = LoadShader(ShaderType.VertexShader, vertexPath);

            // 加载并编译片段着色器
            var fragmentShader = LoadShader(ShaderType.FragmentShader, fragmentPath);

            // 创建着色器程序
            _programId = GL.CreateProgram();

            // 附加着色器
            GL.AttachShader(_programId, vertexShader);
            GL.AttachShader(_programId, fragmentShader);

            // 链接着色器程序
            GL.LinkProgram(_programId);

            // 检查链接错误
            GL.GetProgram(_programId, GetProgramParameterName.LinkStatus, out var success);
            if (success == 0)
            {
                var infoLog = GL.GetProgramInfoLog(_programId);
                throw new Exception($"着色器程序链接错误: {infoLog}");
            }

            // 着色器已链接到程序，可以删除
            GL.DetachShader(_programId, vertexShader);
            GL.DetachShader(_programId, fragmentShader);
            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);
        }

        public void Use()
        {
            GL.UseProgram(_programId);
        }

        public void SetMatrix4(string name, Matrix4 matrix)
        {
            var location = GL.GetUniformLocation(_programId, name);
            GL.UniformMatrix4(location, false, ref matrix);
        }

        public void SetVector3(string name, Vector3 vector)
        {
            var location = GL.GetUniformLocation(_programId, name);
            GL.Uniform3(location, vector);
        }

        public void SetFloat(string name, float value)
        {
            var location = GL.GetUniformLocation(_programId, name);
            GL.Uniform1(location, value);
        }

        public void SetVector4(string name, Vector4 value)
        {
            int location = GL.GetUniformLocation(_programId, name);
            if (location != -1)
                GL.Uniform4(location, value);
        }

        public Matrix4 GetMatrix4(string name)
        {
            int location = GL.GetUniformLocation(_programId, name);
            if (location == -1)
                return Matrix4.Identity;

            float[] matrixData = new float[16];
            GL.GetUniform(_programId, location, matrixData);
            return new Matrix4(
                matrixData[0], matrixData[1], matrixData[2], matrixData[3],
                matrixData[4], matrixData[5], matrixData[6], matrixData[7],
                matrixData[8], matrixData[9], matrixData[10], matrixData[11],
                matrixData[12], matrixData[13], matrixData[14], matrixData[15]
            );
        }

        public void SetColor4(string name, Color4 color)
        {
            int location = GL.GetUniformLocation(_programId, name);
            if (location != -1)
                GL.Uniform4(location, color);
        }

        public void SetInt(string name, int value)
        {
            var location = GL.GetUniformLocation(_programId, name);
            GL.Uniform1(location, value);
        }

        private static int LoadShader(ShaderType type, string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"着色器文件未找到: {path}");

            var shaderId = GL.CreateShader(type);
            var shaderSource = File.ReadAllText(path);

            GL.ShaderSource(shaderId, shaderSource);
            GL.CompileShader(shaderId);

            GL.GetShader(shaderId, ShaderParameter.CompileStatus, out var success);
            if (success == 0)
            {
                var infoLog = GL.GetShaderInfoLog(shaderId);
                throw new Exception($"着色器编译错误 ({type}): {infoLog}");
            }

            return shaderId;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                GL.DeleteProgram(_programId);
                _disposedValue = true;
            }
        }

        ~ShaderProgram()
        {
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}