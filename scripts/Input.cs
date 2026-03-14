using System.Runtime.CompilerServices;

namespace Engine
{
    public static class MouseButton
    {
        public const int Left = 0;
        public const int Right = 1;
        public const int Middle = 2;
    }

    public static class KeyCode
    {
        public const int Return = 40;
        public const int Enter = 40;
        public const int W = 26;
        public const int A = 4;
        public const int S = 22;
        public const int D = 7;
        public const int Space = 44;
        public const int Backspace = 42;
        public const int Tab = 43;
        public const int LeftArrow = 80;
        public const int RightArrow = 79;
        public const int Delete = 76;
        public const int Home = 74;
        public const int End = 77;
        public const int LeftShift = 225;
        public const int RightShift = 229;
        public const int Escape = 41;
    }

    public static class Input
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool GetMouseButton(int button);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool GetMouseButtonDown(int button);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool GetMouseButtonUp(int button);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern float GetMouseDeltaX();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern float GetMouseDeltaY();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern float GetMouseWheel();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern float GetMousePosX();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern float GetMousePosY();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool GetKey(int scancode);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool GetKeyDown(int scancode);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool GetKeyUp(int scancode);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern string GetTextInput();
    }
}
