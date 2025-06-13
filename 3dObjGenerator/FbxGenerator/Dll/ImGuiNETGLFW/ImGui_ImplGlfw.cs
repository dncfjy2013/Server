using System;
using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;

namespace FbxGenerator.Dll.ImGuiNETGLFW
{
    public unsafe class ImGui_ImplGlfw
    {
        // GLFW窗口句柄
        private static GLFW.WindowPtr _window;
        // 是否安装了输入回调
        private static bool _installedCallbacks;
        // 鼠标按下状态
        private static bool[] _mousePressed = new bool[5];
        // 鼠标滚轮值
        private static float _mouseWheel;
        private static float _mouseWheelH;
        // 键盘映射表
        private static ImGuiKey[] _keyMap = new ImGuiKey[512];
        // 上一帧时间
        private static double _time;
        // 上次鼠标位置
        private static Vector2 _mousePosPrev;
        // 窗口大小变化标志
        private static bool _framebufferResized;

        // GLFW导入函数
        public static class GLFW
        {
            public delegate void ErrorCallback(int error, IntPtr description);
            public delegate void KeyCallback(WindowPtr window, int key, int scancode, int action, int mods);
            public delegate void CharCallback(WindowPtr window, uint codepoint);
            public delegate void MouseButtonCallback(WindowPtr window, int button, int action, int mods);
            public delegate void ScrollCallback(WindowPtr window, double xoffset, double yoffset);
            public delegate void FramebufferSizeCallback(WindowPtr window, int width, int height);

            [StructLayout(LayoutKind.Sequential)]
            public struct WindowPtr
            {
                public IntPtr Handle;
            }

            [DllImport("glfw3.dll")]
            public static extern void glfwSetErrorCallback(ErrorCallback cbfun);

            [DllImport("glfw3.dll")]
            public static extern bool glfwInit();

            [DllImport("glfw3.dll")]
            public static extern void glfwTerminate();

            [DllImport("glfw3.dll")]
            public static extern WindowPtr glfwCreateWindow(int width, int height, string title, IntPtr monitor, IntPtr share);

            [DllImport("glfw3.dll")]
            public static extern void glfwMakeContextCurrent(WindowPtr window);

            [DllImport("glfw3.dll")]
            public static extern void glfwSwapBuffers(WindowPtr window);

            [DllImport("glfw3.dll")]
            public static extern void glfwPollEvents();

            [DllImport("glfw3.dll")]
            public static extern bool glfwWindowShouldClose(WindowPtr window);

            [DllImport("glfw3.dll")]
            public static extern void glfwGetCursorPos(WindowPtr window, out double xpos, out double ypos);

            [DllImport("glfw3.dll")]
            public static extern void glfwSetCursorPos(WindowPtr window, double xpos, double ypos);

            [DllImport("glfw3.dll")]
            public static extern void glfwSetKeyCallback(WindowPtr window, KeyCallback callback);

            [DllImport("glfw3.dll")]
            public static extern void glfwSetCharCallback(WindowPtr window, CharCallback callback);

            [DllImport("glfw3.dll")]
            public static extern void glfwSetMouseButtonCallback(WindowPtr window, MouseButtonCallback callback);

            [DllImport("glfw3.dll")]
            public static extern void glfwSetScrollCallback(WindowPtr window, ScrollCallback callback);

            [DllImport("glfw3.dll")]
            public static extern void glfwSetFramebufferSizeCallback(WindowPtr window, FramebufferSizeCallback callback);

            [DllImport("glfw3.dll")]
            public static extern void glfwSetInputMode(WindowPtr window, int mode, int value);

            [DllImport("glfw3.dll")]
            public static extern int glfwGetInputMode(WindowPtr window, int mode);

            [DllImport("glfw3.dll")]
            public static extern double glfwGetTime();

            [DllImport("glfw3.dll")]
            public static extern int glfwGetKey(WindowPtr window, int key);

            [DllImport("glfw3.dll")]
            public static extern int glfwGetMouseButton(WindowPtr window, int button);

            [DllImport("glfw3.dll")]
            public static extern void glfwGetWindowSize(WindowPtr window, out int width, out int height);

            [DllImport("glfw3.dll")]
            public static extern void glfwGetFramebufferSize(WindowPtr window, out int width, out int height);
        }

        // GLFW错误常量
        private const int GLFW_CURSOR = 0x00033001;
        private const int GLFW_CURSOR_NORMAL = 0x00034001;
        private const int GLFW_CURSOR_HIDDEN = 0x00034002;
        private const int GLFW_CURSOR_DISABLED = 0x00034003;

        // GLFW按键常量
        private const int GLFW_KEY_SPACE = 32;
        private const int GLFW_KEY_APOSTROPHE = 39;
        private const int GLFW_KEY_COMMA = 44;
        private const int GLFW_KEY_MINUS = 45;
        private const int GLFW_KEY_PERIOD = 46;
        private const int GLFW_KEY_SLASH = 47;
        private const int GLFW_KEY_0 = 48;
        private const int GLFW_KEY_1 = 49;
        private const int GLFW_KEY_2 = 50;
        private const int GLFW_KEY_3 = 51;
        private const int GLFW_KEY_4 = 52;
        private const int GLFW_KEY_5 = 53;
        private const int GLFW_KEY_6 = 54;
        private const int GLFW_KEY_7 = 55;
        private const int GLFW_KEY_8 = 56;
        private const int GLFW_KEY_9 = 57;
        private const int GLFW_KEY_SEMICOLON = 59;
        private const int GLFW_KEY_EQUAL = 61;
        private const int GLFW_KEY_A = 65;
        private const int GLFW_KEY_B = 66;
        private const int GLFW_KEY_C = 67;
        private const int GLFW_KEY_D = 68;
        private const int GLFW_KEY_E = 69;
        private const int GLFW_KEY_F = 70;
        private const int GLFW_KEY_G = 71;
        private const int GLFW_KEY_H = 72;
        private const int GLFW_KEY_I = 73;
        private const int GLFW_KEY_J = 74;
        private const int GLFW_KEY_K = 75;
        private const int GLFW_KEY_L = 76;
        private const int GLFW_KEY_M = 77;
        private const int GLFW_KEY_N = 78;
        private const int GLFW_KEY_O = 79;
        private const int GLFW_KEY_P = 80;
        private const int GLFW_KEY_Q = 81;
        private const int GLFW_KEY_R = 82;
        private const int GLFW_KEY_S = 83;
        private const int GLFW_KEY_T = 84;
        private const int GLFW_KEY_U = 85;
        private const int GLFW_KEY_V = 86;
        private const int GLFW_KEY_W = 87;
        private const int GLFW_KEY_X = 88;
        private const int GLFW_KEY_Y = 89;
        private const int GLFW_KEY_Z = 90;
        private const int GLFW_KEY_LEFT_BRACKET = 91;
        private const int GLFW_KEY_BACKSLASH = 92;
        private const int GLFW_KEY_RIGHT_BRACKET = 93;
        private const int GLFW_KEY_GRAVE_ACCENT = 96;
        private const int GLFW_KEY_WORLD_1 = 161;
        private const int GLFW_KEY_WORLD_2 = 162;
        private const int GLFW_KEY_ESCAPE = 256;
        private const int GLFW_KEY_ENTER = 257;
        private const int GLFW_KEY_TAB = 258;
        private const int GLFW_KEY_BACKSPACE = 259;
        private const int GLFW_KEY_INSERT = 260;
        private const int GLFW_KEY_DELETE = 261;
        private const int GLFW_KEY_RIGHT = 262;
        private const int GLFW_KEY_LEFT = 263;
        private const int GLFW_KEY_DOWN = 264;
        private const int GLFW_KEY_UP = 265;
        private const int GLFW_KEY_PAGE_UP = 266;
        private const int GLFW_KEY_PAGE_DOWN = 267;
        private const int GLFW_KEY_HOME = 268;
        private const int GLFW_KEY_END = 269;
        private const int GLFW_KEY_CAPS_LOCK = 280;
        private const int GLFW_KEY_SCROLL_LOCK = 281;
        private const int GLFW_KEY_NUM_LOCK = 282;
        private const int GLFW_KEY_PRINT_SCREEN = 283;
        private const int GLFW_KEY_PAUSE = 284;
        private const int GLFW_KEY_F1 = 290;
        private const int GLFW_KEY_F2 = 291;
        private const int GLFW_KEY_F3 = 292;
        private const int GLFW_KEY_F4 = 293;
        private const int GLFW_KEY_F5 = 294;
        private const int GLFW_KEY_F6 = 295;
        private const int GLFW_KEY_F7 = 296;
        private const int GLFW_KEY_F8 = 297;
        private const int GLFW_KEY_F9 = 298;
        private const int GLFW_KEY_F10 = 299;
        private const int GLFW_KEY_F11 = 300;
        private const int GLFW_KEY_F12 = 301;
        private const int GLFW_KEY_F13 = 302;
        private const int GLFW_KEY_F14 = 303;
        private const int GLFW_KEY_F15 = 304;
        private const int GLFW_KEY_F16 = 305;
        private const int GLFW_KEY_F17 = 306;
        private const int GLFW_KEY_F18 = 307;
        private const int GLFW_KEY_F19 = 308;
        private const int GLFW_KEY_F20 = 309;
        private const int GLFW_KEY_F21 = 310;
        private const int GLFW_KEY_F22 = 311;
        private const int GLFW_KEY_F23 = 312;
        private const int GLFW_KEY_F24 = 313;
        private const int GLFW_KEY_F25 = 314;
        private const int GLFW_KEY_KP_0 = 320;
        private const int GLFW_KEY_KP_1 = 321;
        private const int GLFW_KEY_KP_2 = 322;
        private const int GLFW_KEY_KP_3 = 323;
        private const int GLFW_KEY_KP_4 = 324;
        private const int GLFW_KEY_KP_5 = 325;
        private const int GLFW_KEY_KP_6 = 326;
        private const int GLFW_KEY_KP_7 = 327;
        private const int GLFW_KEY_KP_8 = 328;
        private const int GLFW_KEY_KP_9 = 329;
        private const int GLFW_KEY_KP_DECIMAL = 330;
        private const int GLFW_KEY_KP_DIVIDE = 331;
        private const int GLFW_KEY_KP_MULTIPLY = 332;
        private const int GLFW_KEY_KP_SUBTRACT = 333;
        private const int GLFW_KEY_KP_ADD = 334;
        private const int GLFW_KEY_KP_ENTER = 335;
        private const int GLFW_KEY_KP_EQUAL = 336;
        private const int GLFW_KEY_LEFT_SHIFT = 340;
        private const int GLFW_KEY_LEFT_CONTROL = 341;
        private const int GLFW_KEY_LEFT_ALT = 342;
        private const int GLFW_KEY_LEFT_SUPER = 343;
        private const int GLFW_KEY_RIGHT_SHIFT = 344;
        private const int GLFW_KEY_RIGHT_CONTROL = 345;
        private const int GLFW_KEY_RIGHT_ALT = 346;
        private const int GLFW_KEY_RIGHT_SUPER = 347;
        private const int GLFW_KEY_MENU = 348;

        // GLFW动作常量
        private const int GLFW_PRESS = 1;
        private const int GLFW_RELEASE = 0;
        private const int GLFW_REPEAT = 2;

        // 回调函数实例
        private static GLFW.KeyCallback _keyCallback;
        private static GLFW.CharCallback _charCallback;
        private static GLFW.MouseButtonCallback _mouseButtonCallback;
        private static GLFW.ScrollCallback _scrollCallback;
        private static GLFW.FramebufferSizeCallback _framebufferSizeCallback;

        // 初始化函数
        public static bool Init(GLFW.WindowPtr window, bool installCallbacks)
        {
            _window = window;
            _time = 0.0;
            _framebufferResized = false;
            _mousePosPrev = new Vector2(-1, -1);

            // 设置键盘映射
            _keyMap[GLFW_KEY_TAB] = ImGuiKey.Tab;
            _keyMap[GLFW_KEY_LEFT] = ImGuiKey.LeftArrow;
            _keyMap[GLFW_KEY_RIGHT] = ImGuiKey.RightArrow;
            _keyMap[GLFW_KEY_UP] = ImGuiKey.UpArrow;
            _keyMap[GLFW_KEY_DOWN] = ImGuiKey.DownArrow;
            _keyMap[GLFW_KEY_PAGE_UP] = ImGuiKey.PageUp;
            _keyMap[GLFW_KEY_PAGE_DOWN] = ImGuiKey.PageDown;
            _keyMap[GLFW_KEY_HOME] = ImGuiKey.Home;
            _keyMap[GLFW_KEY_END] = ImGuiKey.End;
            _keyMap[GLFW_KEY_INSERT] = ImGuiKey.Insert;
            _keyMap[GLFW_KEY_DELETE] = ImGuiKey.Delete;
            _keyMap[GLFW_KEY_BACKSPACE] = ImGuiKey.Backspace;
            _keyMap[GLFW_KEY_SPACE] = ImGuiKey.Space;
            _keyMap[GLFW_KEY_ENTER] = ImGuiKey.Enter;
            _keyMap[GLFW_KEY_ESCAPE] = ImGuiKey.Escape;
            _keyMap[GLFW_KEY_APOSTROPHE] = ImGuiKey.Apostrophe;
            _keyMap[GLFW_KEY_COMMA] = ImGuiKey.Comma;
            _keyMap[GLFW_KEY_MINUS] = ImGuiKey.Minus;
            _keyMap[GLFW_KEY_PERIOD] = ImGuiKey.Period;
            _keyMap[GLFW_KEY_SLASH] = ImGuiKey.Slash;
            _keyMap[GLFW_KEY_SEMICOLON] = ImGuiKey.Semicolon;
            _keyMap[GLFW_KEY_EQUAL] = ImGuiKey.Equal;
            _keyMap[GLFW_KEY_LEFT_BRACKET] = ImGuiKey.LeftBracket;
            _keyMap[GLFW_KEY_BACKSLASH] = ImGuiKey.Backslash;
            _keyMap[GLFW_KEY_RIGHT_BRACKET] = ImGuiKey.RightBracket;
            _keyMap[GLFW_KEY_GRAVE_ACCENT] = ImGuiKey.GraveAccent;
            _keyMap[GLFW_KEY_CAPS_LOCK] = ImGuiKey.CapsLock;
            _keyMap[GLFW_KEY_SCROLL_LOCK] = ImGuiKey.ScrollLock;
            _keyMap[GLFW_KEY_NUM_LOCK] = ImGuiKey.NumLock;
            _keyMap[GLFW_KEY_PRINT_SCREEN] = ImGuiKey.PrintScreen;
            _keyMap[GLFW_KEY_PAUSE] = ImGuiKey.Pause;
            _keyMap[GLFW_KEY_KP_0] = ImGuiKey.Keypad0;
            _keyMap[GLFW_KEY_KP_1] = ImGuiKey.Keypad1;
            _keyMap[GLFW_KEY_KP_2] = ImGuiKey.Keypad2;
            _keyMap[GLFW_KEY_KP_3] = ImGuiKey.Keypad3;
            _keyMap[GLFW_KEY_KP_4] = ImGuiKey.Keypad4;
            _keyMap[GLFW_KEY_KP_5] = ImGuiKey.Keypad5;
            _keyMap[GLFW_KEY_KP_6] = ImGuiKey.Keypad6;
            _keyMap[GLFW_KEY_KP_7] = ImGuiKey.Keypad7;
            _keyMap[GLFW_KEY_KP_8] = ImGuiKey.Keypad8;
            _keyMap[GLFW_KEY_KP_9] = ImGuiKey.Keypad9;
            _keyMap[GLFW_KEY_KP_DECIMAL] = ImGuiKey.KeypadDecimal;
            _keyMap[GLFW_KEY_KP_DIVIDE] = ImGuiKey.KeypadDivide;
            _keyMap[GLFW_KEY_KP_MULTIPLY] = ImGuiKey.KeypadMultiply;
            _keyMap[GLFW_KEY_KP_SUBTRACT] = ImGuiKey.KeypadSubtract;
            _keyMap[GLFW_KEY_KP_ADD] = ImGuiKey.KeypadAdd;
            _keyMap[GLFW_KEY_KP_ENTER] = ImGuiKey.KeypadEnter;
            _keyMap[GLFW_KEY_KP_EQUAL] = ImGuiKey.KeypadEqual;
            _keyMap[GLFW_KEY_LEFT_SHIFT] = ImGuiKey.LeftShift;
            _keyMap[GLFW_KEY_LEFT_CONTROL] = ImGuiKey.LeftCtrl;
            _keyMap[GLFW_KEY_LEFT_ALT] = ImGuiKey.LeftAlt;
            _keyMap[GLFW_KEY_LEFT_SUPER] = ImGuiKey.LeftSuper;
            _keyMap[GLFW_KEY_RIGHT_SHIFT] = ImGuiKey.RightShift;
            _keyMap[GLFW_KEY_RIGHT_CONTROL] = ImGuiKey.RightCtrl;
            _keyMap[GLFW_KEY_RIGHT_ALT] = ImGuiKey.RightAlt;
            _keyMap[GLFW_KEY_RIGHT_SUPER] = ImGuiKey.RightSuper;
            _keyMap[GLFW_KEY_MENU] = ImGuiKey.Menu;
            _keyMap[GLFW_KEY_F1] = ImGuiKey.F1;
            _keyMap[GLFW_KEY_F2] = ImGuiKey.F2;
            _keyMap[GLFW_KEY_F3] = ImGuiKey.F3;
            _keyMap[GLFW_KEY_F4] = ImGuiKey.F4;
            _keyMap[GLFW_KEY_F5] = ImGuiKey.F5;
            _keyMap[GLFW_KEY_F6] = ImGuiKey.F6;
            _keyMap[GLFW_KEY_F7] = ImGuiKey.F7;
            _keyMap[GLFW_KEY_F8] = ImGuiKey.F8;
            _keyMap[GLFW_KEY_F9] = ImGuiKey.F9;
            _keyMap[GLFW_KEY_F10] = ImGuiKey.F10;
            _keyMap[GLFW_KEY_F11] = ImGuiKey.F11;
            _keyMap[GLFW_KEY_F12] = ImGuiKey.F12;

            // 安装回调函数
            if (installCallbacks)
            {
                _keyCallback = GlfwKeyCallback;
                _charCallback = GlfwCharCallback;
                _mouseButtonCallback = GlfwMouseButtonCallback;
                _scrollCallback = GlfwScrollCallback;
                _framebufferSizeCallback = GlfwFramebufferSizeCallback;

                GLFW.glfwSetKeyCallback(window, _keyCallback);
                GLFW.glfwSetCharCallback(window, _charCallback);
                GLFW.glfwSetMouseButtonCallback(window, _mouseButtonCallback);
                GLFW.glfwSetScrollCallback(window, _scrollCallback);
                GLFW.glfwSetFramebufferSizeCallback(window, _framebufferSizeCallback);
            }

            _installedCallbacks = installCallbacks;
            return true;
        }

        // 关闭函数
        public static void Shutdown()
        {
            if (_installedCallbacks)
            {
                GLFW.glfwSetKeyCallback(_window, null);
                GLFW.glfwSetCharCallback(_window, null);
                GLFW.glfwSetMouseButtonCallback(_window, null);
                GLFW.glfwSetScrollCallback(_window, null);
                GLFW.glfwSetFramebufferSizeCallback(_window, null);
            }

            _window = new GLFW.WindowPtr();
            _installedCallbacks = false;
        }

        // 处理输入事件
        public static void ProcessEvents()
        {
            GLFW.glfwPollEvents();
        }

        // 新帧开始
        public static void NewFrame()
        {
            // 获取当前时间
            double currentTime = GLFW.glfwGetTime();
            ImGuiIOPtr io = ImGui.GetIO();

            // 设置DeltaTime
            if (_time > 0.0)
                io.DeltaTime = (float)(currentTime - _time);
            else
                io.DeltaTime = 1.0f / 60.0f;

            _time = currentTime;

            // 获取窗口大小
            GLFW.glfwGetWindowSize(_window, out int width, out int height);
            io.DisplaySize = new Vector2(width, height);

            // 获取鼠标位置
            GLFW.glfwGetCursorPos(_window, out double mouseX, out double mouseY);
            io.MousePos = new Vector2((float)mouseX, (float)mouseY);

            // 设置鼠标按钮状态
            for (int i = 0; i < 5; i++)
            {
                io.MouseDown[i] = _mousePressed[i] ||
                    (GLFW.glfwGetMouseButton(_window, i) != 0 && io.WantCaptureMouse);
            }

            // 设置鼠标滚轮
            io.MouseWheel = _mouseWheel;
            io.MouseWheelH = _mouseWheelH;
            _mouseWheel = _mouseWheelH = 0.0f;

            // 更新光标
            UpdateMouseCursor();
        }

        // 更新鼠标光标
        private static void UpdateMouseCursor()
        {
            if (ImGui.GetIO().ConfigFlags.HasFlag(ImGuiConfigFlags.NoMouseCursorChange))
                return;

            ImGuiMouseCursor imguiCursor = ImGui.GetMouseCursor();
            if (ImGui.IsAnyItemActive())
                imguiCursor = ImGuiMouseCursor.Hand;

            int glfwCursor = GLFW_CURSOR_NORMAL;
            switch (imguiCursor)
            {
                case ImGuiMouseCursor.Arrow: glfwCursor = GLFW_CURSOR_NORMAL; break;
                case ImGuiMouseCursor.TextInput: glfwCursor = GLFW_CURSOR_NORMAL; break;
                case ImGuiMouseCursor.ResizeAll: glfwCursor = GLFW_CURSOR_NORMAL; break;
                case ImGuiMouseCursor.ResizeEW: glfwCursor = GLFW_CURSOR_NORMAL; break;
                case ImGuiMouseCursor.ResizeNS: glfwCursor = GLFW_CURSOR_NORMAL; break;
                case ImGuiMouseCursor.ResizeNESW: glfwCursor = GLFW_CURSOR_NORMAL; break;
                case ImGuiMouseCursor.ResizeNWSE: glfwCursor = GLFW_CURSOR_NORMAL; break;
                case ImGuiMouseCursor.Hand: glfwCursor = GLFW_CURSOR_NORMAL; break;
                case ImGuiMouseCursor.NotAllowed: glfwCursor = GLFW_CURSOR_NORMAL; break;
                default: glfwCursor = GLFW_CURSOR_NORMAL; break;
            }

            GLFW.glfwSetInputMode(_window, GLFW_CURSOR, glfwCursor);
        }

        // GLFW回调函数
        private static void GlfwKeyCallback(GLFW.WindowPtr window, int key, int scancode, int action, int mods)
        {
            if (key < 0 || key >= 512)
                return;

            ImGuiIOPtr io = ImGui.GetIO();

            // 使用 ImGuiNative 直接调用底层函数
            if (_keyMap[key] != 0)
            {
                bool isDown = (action == GLFW_PRESS || action == GLFW_REPEAT);
                io.AddKeyEvent(_keyMap[key], isDown);

                // 更新修饰键状态
                UpdateModifierKeys(io);
            }
        }

        // 更新修饰键状态
        private static void UpdateModifierKeys(ImGuiIOPtr io)
        {
            io.AddKeyEvent(ImGuiKey.ModCtrl,
                GLFW.glfwGetKey(_window, GLFW_KEY_LEFT_CONTROL) != 0 ||
                GLFW.glfwGetKey(_window, GLFW_KEY_RIGHT_CONTROL) != 0);

            io.AddKeyEvent(ImGuiKey.ModShift,
                GLFW.glfwGetKey(_window, GLFW_KEY_LEFT_SHIFT) != 0 ||
                GLFW.glfwGetKey(_window, GLFW_KEY_RIGHT_SHIFT) != 0);

            io.AddKeyEvent(ImGuiKey.ModAlt,
                GLFW.glfwGetKey(_window, GLFW_KEY_LEFT_ALT) != 0 ||
                GLFW.glfwGetKey(_window, GLFW_KEY_RIGHT_ALT) != 0);

            io.AddKeyEvent(ImGuiKey.ModSuper,
                GLFW.glfwGetKey(_window, GLFW_KEY_LEFT_SUPER) != 0 ||
                GLFW.glfwGetKey(_window, GLFW_KEY_RIGHT_SUPER) != 0);
        }

        private static void GlfwCharCallback(GLFW.WindowPtr window, uint codepoint)
        {
            if (codepoint > 0xFFFF)
                return;

            ImGuiIOPtr io = ImGui.GetIO();
            io.AddInputCharacter((char)codepoint);
        }

        private static void GlfwMouseButtonCallback(GLFW.WindowPtr window, int button, int action, int mods)
        {
            if (button >= 0 && button < 5)
            {
                _mousePressed[button] = action == GLFW_PRESS;
            }
        }

        private static void GlfwScrollCallback(GLFW.WindowPtr window, double xoffset, double yoffset)
        {
            _mouseWheelH += (float)xoffset;
            _mouseWheel += (float)yoffset;
        }

        private static void GlfwFramebufferSizeCallback(GLFW.WindowPtr window, int width, int height)
        {
            _framebufferResized = true;
        }
    }
    

}
