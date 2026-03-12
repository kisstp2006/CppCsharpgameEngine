using System.Runtime.CompilerServices;

namespace Engine
{
    public static class Glm
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void Vec2AddInternal(float ax, float ay, float bx, float by, out float rx, out float ry);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void Vec2SubInternal(float ax, float ay, float bx, float by, out float rx, out float ry);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void Vec2ScaleInternal(float x, float y, float scale, out float rx, out float ry);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern float Vec2LengthInternal(float x, float y);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern float Vec2DotInternal(float ax, float ay, float bx, float by);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void Vec2NormalizeInternal(float x, float y, out float rx, out float ry);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void Vec3AddInternal(float ax, float ay, float az,
                                                   float bx, float by, float bz,
                                                   out float rx, out float ry, out float rz);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void Vec3SubInternal(float ax, float ay, float az,
                                                   float bx, float by, float bz,
                                                   out float rx, out float ry, out float rz);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void Vec3ScaleInternal(float x, float y, float z, float scale,
                                                     out float rx, out float ry, out float rz);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern float Vec3LengthInternal(float x, float y, float z);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern float Vec3DotInternal(float ax, float ay, float az,
                                                    float bx, float by, float bz);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void Vec3CrossInternal(float ax, float ay, float az,
                                                     float bx, float by, float bz,
                                                     out float rx, out float ry, out float rz);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void Vec3NormalizeInternal(float x, float y, float z,
                                                         out float rx, out float ry, out float rz);

        public static void Vec2Add(float ax, float ay, float bx, float by, out float rx, out float ry)
        {
            Vec2AddInternal(ax, ay, bx, by, out rx, out ry);
        }

        public static void Vec2Sub(float ax, float ay, float bx, float by, out float rx, out float ry)
        {
            Vec2SubInternal(ax, ay, bx, by, out rx, out ry);
        }

        public static void Vec2Scale(float x, float y, float scale, out float rx, out float ry)
        {
            Vec2ScaleInternal(x, y, scale, out rx, out ry);
        }

        public static float Vec2Length(float x, float y)
        {
            return Vec2LengthInternal(x, y);
        }

        public static float Vec2Dot(float ax, float ay, float bx, float by)
        {
            return Vec2DotInternal(ax, ay, bx, by);
        }

        public static void Vec2Normalize(float x, float y, out float rx, out float ry)
        {
            Vec2NormalizeInternal(x, y, out rx, out ry);
        }

        public static void Vec3Add(float ax, float ay, float az,
                                   float bx, float by, float bz,
                                   out float rx, out float ry, out float rz)
        {
            Vec3AddInternal(ax, ay, az, bx, by, bz, out rx, out ry, out rz);
        }

        public static void Vec3Sub(float ax, float ay, float az,
                                   float bx, float by, float bz,
                                   out float rx, out float ry, out float rz)
        {
            Vec3SubInternal(ax, ay, az, bx, by, bz, out rx, out ry, out rz);
        }

        public static void Vec3Scale(float x, float y, float z, float scale,
                                     out float rx, out float ry, out float rz)
        {
            Vec3ScaleInternal(x, y, z, scale, out rx, out ry, out rz);
        }

        public static float Vec3Length(float x, float y, float z)
        {
            return Vec3LengthInternal(x, y, z);
        }

        public static float Vec3Dot(float ax, float ay, float az,
                                    float bx, float by, float bz)
        {
            return Vec3DotInternal(ax, ay, az, bx, by, bz);
        }

        public static void Vec3Cross(float ax, float ay, float az,
                                     float bx, float by, float bz,
                                     out float rx, out float ry, out float rz)
        {
            Vec3CrossInternal(ax, ay, az, bx, by, bz, out rx, out ry, out rz);
        }

        public static void Vec3Normalize(float x, float y, float z,
                                         out float rx, out float ry, out float rz)
        {
            Vec3NormalizeInternal(x, y, z, out rx, out ry, out rz);
        }
    }
}
