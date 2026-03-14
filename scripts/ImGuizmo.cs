using System.Runtime.CompilerServices;

namespace Engine
{
    public static class ImGuizmo
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool IsUsing();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool Manipulate2DTranslate(float viewportX,
                                                        float viewportY,
                                                        float viewportWidth,
                                                        float viewportHeight,
                                                        float cameraX,
                                                        float cameraY,
                                                        float cameraZoom,
                                                        ref float x,
                                                        ref float y,
                                                        float objectWidth,
                                                        float objectHeight);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool Manipulate2DRotate(float viewportX,
                                                     float viewportY,
                                                     float viewportWidth,
                                                     float viewportHeight,
                                                     float cameraX,
                                                     float cameraY,
                                                     float cameraZoom,
                                                     ref float x,
                                                     ref float y,
                                                     ref float rotationDegrees,
                                                     float objectWidth,
                                                     float objectHeight);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool Manipulate2DScale(float viewportX,
                                                    float viewportY,
                                                    float viewportWidth,
                                                    float viewportHeight,
                                                    float cameraX,
                                                    float cameraY,
                                                    float cameraZoom,
                                                    ref float x,
                                                    ref float y,
                                                    ref float width,
                                                    ref float height,
                                                    float rotationDegrees);
    }
}
