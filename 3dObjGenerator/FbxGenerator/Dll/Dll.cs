using System;
using System.Runtime.InteropServices;
using ImGuiNET;

namespace FbxGenerator.Dll
{

    public static class ImGui_ImplGlfw
    {
        // 加载本地DLL
        const string DLL_NAME = "imgui_impl_glfw";

        // 初始化函数
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool Init(IntPtr window, bool install_callbacks);

        // 关闭函数
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Shutdown();

        // 处理输入事件
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void NewFrame();

        // 可选：GLFW回调函数
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void KeyCallback(IntPtr window, int key, int scancode, int action, int mods);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetKeyCallback(KeyCallback callback);

        // 其他必要的函数...
    }

    public static class ImGui_ImplOpenGL3
    {
        const string DLL_NAME = "imgui_impl_opengl3";

        // 初始化函数，传递GLSL版本
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool Init(string glsl_version);

        // 关闭函数
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Shutdown();

        // 渲染函数
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void RenderDrawData(ImDrawDataPtr draw_data);

        // 可选：处理DX11特定的函数
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CreateFontsTexture();

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void DestroyFontsTexture();

        // 处理输入事件
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void NewFrame();

        // 其他必要的函数...
    }
}
