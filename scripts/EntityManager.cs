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

    public static class EntityManager
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern uint CreateEntityInternal();

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void DestroyEntityInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool IsEntityValidInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern int GetEntityCountInternal();

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern uint GetEntityIdAtIndexInternal(int index);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern string GetEntityNameInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void SetEntityNameInternal(uint entityId, string name);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool GetEntityActiveInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void SetEntityActiveInternal(uint entityId, bool active);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool HasComponentInternal(uint entityId, int componentType);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void AddComponentInternal(uint entityId, int componentType);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void RemoveComponentInternal(uint entityId, int componentType);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool HasTransformInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void AddTransformInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool GetTransformInternal(uint entityId, out float x, out float y, out float width, out float height);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void SetTransformInternal(uint entityId, float x, float y, float width, float height);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool HasCameraInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void AddCameraInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool GetCameraInternal(uint entityId, out float x, out float y, out float zoom);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void SetCameraInternal(uint entityId, float x, float y, float zoom);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool GetCameraSettingsInternal(uint entityId,
                                     out float x,
                                     out float y,
                                     out float zoom,
                                     out bool enabled,
                                     out bool primary,
                                     out bool clearColor,
                                     out uint backgroundColor,
                                     out uint cullingMask,
                                     out float viewportX,
                                     out float viewportY,
                                     out float viewportWidth,
                                     out float viewportHeight);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void SetCameraSettingsInternal(uint entityId,
                                     float x,
                                     float y,
                                     float zoom,
                                     bool enabled,
                                     bool primary,
                                     bool clearColor,
                                     uint backgroundColor,
                                     uint cullingMask,
                                     float viewportX,
                                     float viewportY,
                                     float viewportWidth,
                                     float viewportHeight);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void RemoveCameraInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool HasSpriteInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void AddSpriteInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void RemoveSpriteInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool HasScriptInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void AddScriptInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void RemoveScriptInternal(uint entityId);

        public static uint CreateEntity()
        {
            return CreateEntityInternal();
        }

        public static void DestroyEntity(uint entityId)
        {
            DestroyEntityInternal(entityId);
        }

        public static bool Exists(uint entityId)
        {
            return IsEntityValidInternal(entityId);
        }

        public static bool IsEntityValid(uint entityId)
        {
            return IsEntityValidInternal(entityId);
        }

        public static int GetEntityCount()
        {
            return GetEntityCountInternal();
        }

        public static uint GetEntityIdAtIndex(int index)
        {
            return GetEntityIdAtIndexInternal(index);
        }

        public static string GetEntityName(uint entityId)
        {
            return GetEntityNameInternal(entityId);
        }

        public static void SetEntityName(uint entityId, string name)
        {
            SetEntityNameInternal(entityId, name);
        }

        public static bool GetEntityActive(uint entityId)
        {
            return GetEntityActiveInternal(entityId);
        }

        public static void SetEntityActive(uint entityId, bool active)
        {
            SetEntityActiveInternal(entityId, active);
        }

        public static bool HasComponent(uint entityId, int componentType)
        {
            return HasComponentInternal(entityId, componentType);
        }

        public static void AddComponent(uint entityId, int componentType)
        {
            AddComponentInternal(entityId, componentType);
        }

        public static void RemoveComponent(uint entityId, int componentType)
        {
            RemoveComponentInternal(entityId, componentType);
        }

        public static bool HasTransform(uint entityId)
        {
            return HasTransformInternal(entityId);
        }

        public static void AddTransform(uint entityId)
        {
            AddTransformInternal(entityId);
        }

        public static bool GetTransform(uint entityId, out float x, out float y, out float width, out float height)
        {
            return GetTransformInternal(entityId, out x, out y, out width, out height);
        }

        public static void SetTransform(uint entityId, float x, float y, float width, float height)
        {
            SetTransformInternal(entityId, x, y, width, height);
        }

        public static bool HasCamera(uint entityId)
        {
            return HasCameraInternal(entityId);
        }

        public static void AddCamera(uint entityId)
        {
            AddCameraInternal(entityId);
        }

        public static bool GetCamera(uint entityId, out float x, out float y, out float zoom)
        {
            return GetCameraInternal(entityId, out x, out y, out zoom);
        }

        public static void SetCamera(uint entityId, float x, float y, float zoom)
        {
            SetCameraInternal(entityId, x, y, zoom);
        }

        public static bool GetCameraSettings(uint entityId,
                                             out float x,
                                             out float y,
                                             out float zoom,
                                             out bool enabled,
                                             out bool primary,
                                             out bool clearColor,
                                             out uint backgroundColor,
                                             out uint cullingMask,
                                             out float viewportX,
                                             out float viewportY,
                                             out float viewportWidth,
                                             out float viewportHeight)
        {
            return GetCameraSettingsInternal(entityId,
                                             out x,
                                             out y,
                                             out zoom,
                                             out enabled,
                                             out primary,
                                             out clearColor,
                                             out backgroundColor,
                                             out cullingMask,
                                             out viewportX,
                                             out viewportY,
                                             out viewportWidth,
                                             out viewportHeight);
        }

        public static void SetCameraSettings(uint entityId,
                                             float x,
                                             float y,
                                             float zoom,
                                             bool enabled,
                                             bool primary,
                                             bool clearColor,
                                             uint backgroundColor,
                                             uint cullingMask,
                                             float viewportX,
                                             float viewportY,
                                             float viewportWidth,
                                             float viewportHeight)
        {
            SetCameraSettingsInternal(entityId,
                                      x,
                                      y,
                                      zoom,
                                      enabled,
                                      primary,
                                      clearColor,
                                      backgroundColor,
                                      cullingMask,
                                      viewportX,
                                      viewportY,
                                      viewportWidth,
                                      viewportHeight);
        }

        public static void RemoveCamera(uint entityId)
        {
            RemoveCameraInternal(entityId);
        }

        public static bool HasSprite(uint entityId)
        {
            return HasSpriteInternal(entityId);
        }

        public static void AddSprite(uint entityId)
        {
            AddSpriteInternal(entityId);
        }

        public static void RemoveSprite(uint entityId)
        {
            RemoveSpriteInternal(entityId);
        }

        public static bool HasScript(uint entityId)
        {
            return HasScriptInternal(entityId);
        }

        public static void AddScript(uint entityId)
        {
            AddScriptInternal(entityId);
        }

        public static void RemoveScript(uint entityId)
        {
            RemoveScriptInternal(entityId);
        }
    }
}
