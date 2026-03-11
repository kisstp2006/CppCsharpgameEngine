using System.Runtime.CompilerServices;

namespace Engine
{
    public static class DebugDraw
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void LineInternal(float x0, float y0, float x1, float y1,
            float r, float g, float b, float a, float thickness, float durationSeconds);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void CircleInternal(float centerX, float centerY, float radius,
            float r, float g, float b, float a, float thickness, int segments, float durationSeconds);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void FilledCircleInternal(float centerX, float centerY, float radius,
            float r, float g, float b, float a, int segments, float durationSeconds);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void RectInternal(float x, float y, float width, float height,
            float r, float g, float b, float a, float thickness, float durationSeconds);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void FilledRectInternal(float x, float y, float width, float height,
            float r, float g, float b, float a, float durationSeconds);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void ClearInternal();

        public static void Line(float x0, float y0, float x1, float y1,
            float r = 1.0f, float g = 1.0f, float b = 1.0f, float a = 1.0f,
            float thickness = 1.0f, float durationSeconds = 0.0f)
        {
            LineInternal(x0, y0, x1, y1, r, g, b, a, thickness, durationSeconds);
        }

        public static void Circle(float centerX, float centerY, float radius,
            float r = 1.0f, float g = 1.0f, float b = 1.0f, float a = 1.0f,
            float thickness = 1.0f, int segments = 32, float durationSeconds = 0.0f)
        {
            CircleInternal(centerX, centerY, radius, r, g, b, a, thickness, segments, durationSeconds);
        }

        public static void FilledCircle(float centerX, float centerY, float radius,
            float r = 1.0f, float g = 1.0f, float b = 1.0f, float a = 1.0f,
            int segments = 32, float durationSeconds = 0.0f)
        {
            FilledCircleInternal(centerX, centerY, radius, r, g, b, a, segments, durationSeconds);
        }

        public static void Rect(float x, float y, float width, float height,
            float r = 1.0f, float g = 1.0f, float b = 1.0f, float a = 1.0f,
            float thickness = 1.0f, float durationSeconds = 0.0f)
        {
            RectInternal(x, y, width, height, r, g, b, a, thickness, durationSeconds);
        }

        public static void FilledRect(float x, float y, float width, float height,
            float r = 1.0f, float g = 1.0f, float b = 1.0f, float a = 1.0f,
            float durationSeconds = 0.0f)
        {
            FilledRectInternal(x, y, width, height, r, g, b, a, durationSeconds);
        }

        public static void Box(float x, float y, float width, float height,
            float r = 1.0f, float g = 1.0f, float b = 1.0f, float a = 1.0f,
            float thickness = 1.0f, float durationSeconds = 0.0f)
        {
            Rect(x, y, width, height, r, g, b, a, thickness, durationSeconds);
        }

        public static void Clear()
        {
            ClearInternal();
        }
    }
}
