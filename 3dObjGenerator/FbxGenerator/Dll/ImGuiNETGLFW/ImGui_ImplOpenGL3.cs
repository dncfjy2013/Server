using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace FbxGenerator.Dll.ImGuiNETGLFW
{
    public unsafe class ImGui_ImplOpenGL3
    {
        private static bool _initialized = false;
        private static int _shaderHandle;
        private static int _vertexArrayObject;
        private static int _vertexBufferHandle;
        private static int _indexBufferHandle;
        private static int _vertexPositionLocation;
        private static int _vertexUVLocation;
        private static int _vertexColorLocation;
        private static int _shaderTextureLocation;
        private static int _shaderProjMtxLocation;
        private static int _fontTexture = 0;
        private static int _vertexBufferSize = 5000;
        private static int _indexBufferSize = 10000;
        private static readonly Dictionary<nint, int> _userTextureToGlTexture = new Dictionary<nint, int>();

        // 着色器源码
        private static readonly string VertexShaderSource = @"
            #version 330 core
            uniform mat4 ProjMtx;
            in vec2 Position;
            in vec2 UV;
            in vec4 Color;
            out vec2 Frag_UV;
            out vec4 Frag_Color;
            void main()
            {
                Frag_UV = UV;
                Frag_Color = Color;
                gl_Position = ProjMtx * vec4(Position.xy, 0, 1);
            }
        ";

        private static readonly string FragmentShaderSource = @"
            #version 330 core
            uniform sampler2D Texture;
            in vec2 Frag_UV;
            in vec4 Frag_Color;
            out vec4 Out_Color;
            void main()
            {
                Out_Color = Frag_Color * texture(Texture, Frag_UV.st);
            }
        ";

        // 初始化 ImGui OpenGL3 后端
        public static bool Init(string glslVersion = "#version 130")
        {
            if (_initialized)
                return true;

            _initialized = true;

            // 创建着色器
            if (!CreateDeviceObjects())
            {
                Shutdown();
                return false;
            }

            return true;
        }

        // 关闭 ImGui OpenGL3 后端
        public static void Shutdown()
        {
            if (!_initialized)
                return;

            // 清理着色器和缓冲区
            GL.DeleteProgram(_shaderHandle);
            GL.DeleteBuffers(1, ref _vertexBufferHandle);
            GL.DeleteBuffers(1, ref _indexBufferHandle);
            GL.DeleteVertexArrays(1, ref _vertexArrayObject);

            if (_fontTexture != 0)
            {
                GL.DeleteTextures(1, ref _fontTexture);
                _fontTexture = 0;
            }

            _initialized = false;
        }

        // 新帧开始
        public static void NewFrame()
        {
            if (!_initialized)
                return;

            if (_fontTexture == 0)
                CreateFontsTexture();
        }

        // 渲染 ImGui 数据
        public static void RenderDrawData(ImDrawDataPtr drawData)
        {
            if (!_initialized)
                return;

            // 备份当前 OpenGL 状态
            GLStateBackup glStateBackup = new GLStateBackup();

            // 计算投影矩阵
            float L = drawData.DisplayPos.X;
            float R = drawData.DisplayPos.X + drawData.DisplaySize.X;
            float T = drawData.DisplayPos.Y;
            float B = drawData.DisplayPos.Y + drawData.DisplaySize.Y;
            Matrix4 mvp = new Matrix4(
                2.0f / (R - L), 0.0f, 0.0f, 0.0f,
                0.0f, 2.0f / (T - B), 0.0f, 0.0f,
                0.0f, 0.0f, 0.5f, 0.0f,
                (R + L) / (L - R), (T + B) / (B - T), 0.5f, 1.0f
            );

            // 设置视口
            GL.Viewport((int)drawData.DisplayPos.X, (int)drawData.DisplayPos.Y,
                        (int)drawData.DisplaySize.X, (int)drawData.DisplaySize.Y);

            // 启用必要的 OpenGL 状态
            GL.Enable(EnableCap.Blend);
            GL.BlendEquation(BlendEquationMode.FuncAdd);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.StencilTest);
            GL.Enable(EnableCap.ScissorTest);

            // 绑定着色器和顶点数组
            GL.UseProgram(_shaderHandle);
            // 修正：使用 OpenTK 的矩阵设置方法
            GL.UniformMatrix4(_shaderProjMtxLocation, false, ref mvp);
            GL.BindVertexArray(_vertexArrayObject);

            // 遍历所有绘制列表
            for (int n = 0; n < drawData.CmdListsCount; n++)
            {
                // 修正：使用正确的属性访问方式
                ImDrawListPtr cmdList = drawData.CmdLists[n];

                // 更新顶点和索引缓冲区
                if (cmdList.VtxBuffer.Size > 0)
                {
                    int vertexBufferSize = cmdList.VtxBuffer.Size;
                    if (vertexBufferSize > _vertexBufferSize)
                    {
                        _vertexBufferSize = (int)(vertexBufferSize * 1.5f);
                        GL.DeleteBuffers(1, ref _vertexBufferHandle);
                        GL.GenBuffers(1, (int*)_vertexBufferHandle);
                        GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBufferHandle);
                        GL.BufferData(BufferTarget.ArrayBuffer, _vertexBufferSize * Marshal.SizeOf<ImDrawVert>(), nint.Zero, BufferUsageHint.DynamicDraw);
                    }

                    GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBufferHandle);
                    GL.BufferSubData(BufferTarget.ArrayBuffer, nint.Zero, vertexBufferSize * Marshal.SizeOf<ImDrawVert>(), cmdList.VtxBuffer.Data);
                }

                if (cmdList.IdxBuffer.Size > 0)
                {
                    int indexBufferSize = cmdList.IdxBuffer.Size;
                    if (indexBufferSize > _indexBufferSize)
                    {
                        _indexBufferSize = (int)(indexBufferSize * 1.5f);
                        GL.DeleteBuffers(1, ref _indexBufferHandle);
                        GL.GenBuffers(1, (int*)_indexBufferHandle);
                        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBufferHandle);
                        GL.BufferData(BufferTarget.ElementArrayBuffer, _indexBufferSize * sizeof(ushort), nint.Zero, BufferUsageHint.DynamicDraw);
                    }

                    GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBufferHandle);
                    GL.BufferSubData(BufferTarget.ElementArrayBuffer, nint.Zero, indexBufferSize * sizeof(ushort), cmdList.IdxBuffer.Data);
                }

                // 处理每个绘制命令
                int idxBufferOffset = 0;
                for (int cmd_i = 0; cmd_i < cmdList.CmdBuffer.Size; cmd_i++)
                {
                    ImDrawCmdPtr pcmd = cmdList.CmdBuffer[cmd_i];
                    if (pcmd.UserCallback != nint.Zero)
                    {
                        // 用户回调
                        throw new NotImplementedException("User callbacks not implemented");
                    }
                    else
                    {
                        // 应用裁剪矩形
                        GL.Scissor(
                            (int)(pcmd.ClipRect.X - drawData.DisplayPos.X),
                            (int)(drawData.DisplaySize.Y - (pcmd.ClipRect.W - drawData.DisplayPos.Y)),
                            (int)(pcmd.ClipRect.Z - pcmd.ClipRect.X),
                            (int)(pcmd.ClipRect.W - pcmd.ClipRect.Y)
                        );

                        // 绑定纹理
                        GL.ActiveTexture(TextureUnit.Texture0);
                        GL.BindTexture(TextureTarget.Texture2D, GetGlTextureFromImGuiTexture(pcmd.TextureId));
                        GL.Uniform1(_shaderTextureLocation, 0);

                        // 绘制
                        GL.DrawElementsBaseVertex(
                            PrimitiveType.Triangles,
                            (int)pcmd.ElemCount,
                            DrawElementsType.UnsignedShort,
                            idxBufferOffset * sizeof(ushort),
                            (int)pcmd.VtxOffset
                        );
                    }
                    idxBufferOffset += (int)pcmd.ElemCount;
                }
            }

            // 恢复 OpenGL 状态
            glStateBackup.Restore();
        }

        // 处理窗口大小变化
        public static void InvalidateDeviceObjects()
        {
            if (!_initialized)
                return;

            if (_fontTexture != 0)
            {
                GL.DeleteTextures(1, ref _fontTexture);
                _fontTexture = 0;
            }

            if (_shaderHandle != 0)
            {
                GL.DeleteProgram(_shaderHandle);
                _shaderHandle = 0;
            }

            if (_vertexArrayObject != 0)
            {
                GL.DeleteVertexArrays(1, ref _vertexArrayObject);
                _vertexArrayObject = 0;
            }

            if (_vertexBufferHandle != 0)
            {
                GL.DeleteBuffers(1, ref _vertexBufferHandle);
                _vertexBufferHandle = 0;
            }

            if (_indexBufferHandle != 0)
            {
                GL.DeleteBuffers(1, ref _indexBufferHandle);
                _indexBufferHandle = 0;
            }
        }

        // 创建字体纹理
        private static bool CreateFontsTexture()
        {
            ImGuiIOPtr io = ImGui.GetIO();

            // 使用指针版本 - 适用于大多数 ImGui.NET 版本
            io.Fonts.GetTexDataAsRGBA32(out nint pixelsPtr, out int width, out int height, out int bytesPerPixel);

            // 分配托管内存并复制数据
            int dataSize = width * height * bytesPerPixel;
            byte[] pixels = new byte[dataSize];
            Marshal.Copy(pixelsPtr, pixels, 0, dataSize);

            // 释放字体纹理数据
            io.Fonts.ClearTexData();

            // 生成 OpenGL 纹理
            GL.GenTextures(1, (int*)_fontTexture);
            GL.BindTexture(TextureTarget.Texture2D, _fontTexture);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            // 上传纹理数据 - 修正版本
            GL.TexImage2D(
                TextureTarget.Texture2D,
                0,
                PixelInternalFormat.Rgba,
                width,
                height,
                0,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                pixels
            );

            // 存储纹理ID供 ImGui 使用
            io.Fonts.SetTexID(new nint(_fontTexture));

            return true;
        }

        // 创建着色器和缓冲区
        private static bool CreateDeviceObjects()
        {
            // 创建着色器
            int vertexShader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertexShader, VertexShaderSource);
            GL.CompileShader(vertexShader);
            CheckShaderCompileErrors(vertexShader);

            int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShader, FragmentShaderSource);
            GL.CompileShader(fragmentShader);
            CheckShaderCompileErrors(fragmentShader);

            _shaderHandle = GL.CreateProgram();
            GL.AttachShader(_shaderHandle, vertexShader);
            GL.AttachShader(_shaderHandle, fragmentShader);
            GL.LinkProgram(_shaderHandle);
            CheckProgramLinkErrors(_shaderHandle);

            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);

            // 获取着色器变量位置
            _shaderTextureLocation = GL.GetUniformLocation(_shaderHandle, "Texture");
            _shaderProjMtxLocation = GL.GetUniformLocation(_shaderHandle, "ProjMtx");
            _vertexPositionLocation = GL.GetAttribLocation(_shaderHandle, "Position");
            _vertexUVLocation = GL.GetAttribLocation(_shaderHandle, "UV");
            _vertexColorLocation = GL.GetAttribLocation(_shaderHandle, "Color");

            // 创建顶点数组和缓冲区
            GL.GenVertexArrays(1, (int*)_vertexArrayObject);
            GL.GenBuffers(1, (int*)_vertexBufferHandle);
            GL.GenBuffers(1, (int*)_indexBufferHandle);

            GL.BindVertexArray(_vertexArrayObject);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBufferHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, _vertexBufferSize * Marshal.SizeOf<ImDrawVert>(), nint.Zero, BufferUsageHint.DynamicDraw);

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBufferHandle);
            GL.BufferData(BufferTarget.ElementArrayBuffer, _indexBufferSize * sizeof(ushort), nint.Zero, BufferUsageHint.DynamicDraw);

            // 设置顶点属性
            int stride = Marshal.SizeOf<ImDrawVert>();
            GL.EnableVertexAttribArray(_vertexPositionLocation);
            GL.VertexAttribPointer(_vertexPositionLocation, 2, VertexAttribPointerType.Float, false, stride, Marshal.OffsetOf<ImDrawVert>("pos"));
            GL.EnableVertexAttribArray(_vertexUVLocation);
            GL.VertexAttribPointer(_vertexUVLocation, 2, VertexAttribPointerType.Float, false, stride, Marshal.OffsetOf<ImDrawVert>("uv"));
            GL.EnableVertexAttribArray(_vertexColorLocation);
            GL.VertexAttribPointer(_vertexColorLocation, 4, VertexAttribPointerType.UnsignedByte, true, stride, Marshal.OffsetOf<ImDrawVert>("col"));

            return true;
        }

        // 检查着色器编译错误
        private static void CheckShaderCompileErrors(int shader)
        {
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int success);
            if (success == 0)
            {
                string infoLog = GL.GetShaderInfoLog(shader);
                throw new Exception($"Shader compilation error: {infoLog}");
            }
        }

        // 检查着色器程序链接错误
        private static void CheckProgramLinkErrors(int program)
        {
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int success);
            if (success == 0)
            {
                string infoLog = GL.GetProgramInfoLog(program);
                throw new Exception($"Program linking error: {infoLog}");
            }
        }

        // 将 ImGui 纹理 ID 转换为 OpenGL 纹理 ID
        private static int GetGlTextureFromImGuiTexture(nint imGuiTextureId)
        {
            // 修正：使用正确的命名空间引用
            ImGuiIOPtr io = ImGui.GetIO();

            // 简单情况下，ImGui 纹理 ID 可能直接就是 OpenGL 纹理 ID
            if (imGuiTextureId == io.Fonts.TexID)
                return _fontTexture;

            // 处理用户自定义纹理
            if (_userTextureToGlTexture.TryGetValue(imGuiTextureId, out int glTextureId))
                return glTextureId;

            // 默认返回字体纹理
            return _fontTexture;
        }

        // 注册用户自定义纹理
        public static void RegisterTexture(nint imGuiTextureId, int glTextureId)
        {
            _userTextureToGlTexture[imGuiTextureId] = glTextureId;
        }

        // OpenGL 状态备份类
        private class GLStateBackup
        {
            // 修正：使用正确的数组声明语法
            private int[] _viewport = new int[4];
            private int[] _scissorBox = new int[4];
            private bool _blendEnabled;
            private bool _cullFaceEnabled;
            private bool _depthTestEnabled;
            private bool _scissorTestEnabled;
            private BlendingFactor _blendSrc;
            private BlendingFactor _blendDst;
            private BlendEquationMode _blendEquation;
            private CullFaceMode _cullFace;
            private int _currentProgram;
            private int _activeTexture;
            private int _boundTexture;
            private int _boundArrayBuffer;
            private int _boundElementArrayBuffer;
            private int _boundVertexArray;

            public GLStateBackup()
            {
                GL.GetInteger(GetPName.Viewport, _viewport);
                GL.GetInteger(GetPName.ScissorBox, _scissorBox);

                _blendEnabled = GL.IsEnabled(EnableCap.Blend);
                _cullFaceEnabled = GL.IsEnabled(EnableCap.CullFace);
                _depthTestEnabled = GL.IsEnabled(EnableCap.DepthTest);
                _scissorTestEnabled = GL.IsEnabled(EnableCap.ScissorTest);

                GL.GetInteger(GetPName.CurrentProgram, out _currentProgram);
                GL.GetInteger(GetPName.ActiveTexture, out _activeTexture);
                GL.GetInteger(GetPName.TextureBinding2D, out _boundTexture);
                GL.GetInteger(GetPName.ArrayBufferBinding, out _boundArrayBuffer);
                GL.GetInteger(GetPName.ElementArrayBufferBinding, out _boundElementArrayBuffer);
                GL.GetInteger(GetPName.VertexArrayBinding, out _boundVertexArray);

                if (_blendEnabled)
                {
                    GL.GetInteger(GetPName.BlendSrcRgb, out int blendSrcRgb);
                    GL.GetInteger(GetPName.BlendDstRgb, out int blendDstRgb);
                    GL.GetInteger(GetPName.BlendEquationRgb, out int blendEquationRgb);

                    _blendSrc = (BlendingFactor)blendSrcRgb;
                    _blendDst = (BlendingFactor)blendDstRgb;
                    _blendEquation = (BlendEquationMode)blendEquationRgb;
                }

                if (_cullFaceEnabled)
                {
                    GL.GetInteger(GetPName.CullFaceMode, out int cullFaceMode);
                    _cullFace = (CullFaceMode)cullFaceMode;
                }
            }

            public void Restore()
            {
                GL.Viewport(_viewport[0], _viewport[1], _viewport[2], _viewport[3]);
                GL.Scissor(_scissorBox[0], _scissorBox[1], _scissorBox[2], _scissorBox[3]);

                if (_blendEnabled) GL.Enable(EnableCap.Blend); else GL.Disable(EnableCap.Blend);
                if (_cullFaceEnabled) GL.Enable(EnableCap.CullFace); else GL.Disable(EnableCap.CullFace);
                if (_depthTestEnabled) GL.Enable(EnableCap.DepthTest); else GL.Disable(EnableCap.DepthTest);
                if (_scissorTestEnabled) GL.Enable(EnableCap.ScissorTest);

                GL.UseProgram(_currentProgram);
                GL.ActiveTexture((TextureUnit)_activeTexture);
                GL.BindTexture(TextureTarget.Texture2D, _boundTexture);
                GL.BindBuffer(BufferTarget.ArrayBuffer, _boundArrayBuffer);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, _boundElementArrayBuffer);
                GL.BindVertexArray(_boundVertexArray);

                if (_blendEnabled)
                {
                    GL.BlendEquation(_blendEquation);
                    GL.BlendFunc(_blendSrc, _blendDst);
                }

                if (_cullFaceEnabled)
                {
                    GL.CullFace(_cullFace);
                }
            }
        }
    }
}
