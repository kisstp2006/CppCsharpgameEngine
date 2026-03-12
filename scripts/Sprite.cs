using System.Runtime.CompilerServices;

namespace Engine
{
    public static class Sprite
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool Has(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void Add(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void Remove(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern string GetTexturePath(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void SetTexturePath(uint entityId, string texturePath);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern uint GetFallbackColor(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void SetFallbackColor(uint entityId, uint color);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool GetSettings(uint entityId,
                                              out bool centered,
                                              out float offsetX,
                                              out float offsetY,
                                              out bool flipH,
                                              out bool flipV,
                                              out uint hframes,
                                              out uint vframes,
                                              out uint frame,
                                              out bool regionEnabled,
                                              out float regionX,
                                              out float regionY,
                                              out float regionWidth,
                                              out float regionHeight);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void SetSettings(uint entityId,
                                              bool centered,
                                              float offsetX,
                                              float offsetY,
                                              bool flipH,
                                              bool flipV,
                                              uint hframes,
                                              uint vframes,
                                              uint frame,
                                              bool regionEnabled,
                                              float regionX,
                                              float regionY,
                                              float regionWidth,
                                              float regionHeight);
    }
}
