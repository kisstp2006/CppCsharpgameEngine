using System.Runtime.CompilerServices;

namespace Engine
{
    public static class ComponentType
    {
        public const int Transform = 0;
        public const int Camera = 1;
        public const int Sprite = 2;
        public const int Script = 3;
    }

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
        public static extern bool HasComponent(uint entityId, int componentType);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void AddComponent(uint entityId, int componentType);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void RemoveComponent(uint entityId, int componentType);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool HasTransform(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void AddTransform(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool GetTransform(uint entityId, out float x, out float y, out float width, out float height);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void SetTransform(uint entityId, float x, float y, float width, float height);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool HasCamera(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void AddCamera(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool GetCamera(uint entityId, out float x, out float y, out float zoom);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void SetCamera(uint entityId, float x, float y, float zoom);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void RemoveCamera(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool HasSprite(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void AddSprite(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void RemoveSprite(uint entityId);

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

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern string GetScriptTypeName(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void SetScriptTypeName(uint entityId, string scriptTypeName);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void SetGameViewSize(float width, float height);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern ulong GetGameViewTextureHandle();
    }
}
