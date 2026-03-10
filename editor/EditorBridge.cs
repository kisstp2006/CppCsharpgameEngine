using System.Runtime.CompilerServices;

namespace Engine
{
    public static class EditorBridge
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern int GetEntityCount();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern uint GetEntityIdAtIndex(int index);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool IsEntityValid(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern uint CreateEntity();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void DestroyEntity(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern int GetScriptedEntityCount();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool HasTransform(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void AddTransform(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool GetTransform(uint entityId, out float x, out float y, out float width, out float height);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void SetTransform(uint entityId, float x, float y, float width, float height);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool HasScript(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void AddScript(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void RemoveScript(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void SetScriptEnabled(uint entityId, bool enabled);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool GetScriptEnabled(uint entityId);
    }
}
