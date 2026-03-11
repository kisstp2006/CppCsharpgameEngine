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
        public const int SimulationEdit = 0;
        public const int SimulationPlay = 1;
        public const int SimulationPause = 2;

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern int GetEntityCount();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern uint GetEntityIdAtIndex(int index);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool IsEntityValid(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern string GetEntityName(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void SetEntityName(uint entityId, string value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern string GetEntityTag(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void SetEntityTag(uint entityId, string value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern uint GetEntityLayer(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void SetEntityLayer(uint entityId, uint value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool GetEntityStatic(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void SetEntityStatic(uint entityId, bool value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool GetEntityActive(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void SetEntityActive(uint entityId, bool value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern uint CreateEntity();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void DestroyEntity(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void NewScene();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool SaveScene(string scenePath, int storageFormat);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool LoadScene(string scenePath, int storageFormat);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern string GetLastSceneIoStatus();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern int GetSimulationState();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool StartPlayMode();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void StopPlayMode();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void SetSimulationPaused(bool paused);

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
        public static extern string GetSpriteTexturePath(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void SetSpriteTexturePath(uint entityId, string texturePath);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool GetSpriteSettings(uint entityId,
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
        public static extern void SetSpriteSettings(uint entityId,
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
        public static extern string GetScriptFieldValue(uint entityId, string fieldName);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool SetScriptFieldValue(uint entityId, string fieldName, string fieldValue);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void SetGameViewSize(float width, float height);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern ulong GetGameViewTextureHandle();
    }
}
