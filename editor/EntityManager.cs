namespace Engine
{
    public static class EntityManager
    {
        public static uint CreateEntity()
        {
            return EditorBridge.CreateEntity();
        }

        public static void DestroyEntity(uint entityId)
        {
            EditorBridge.DestroyEntity(entityId);
        }

        public static bool Exists(uint entityId)
        {
            return EditorBridge.IsEntityValid(entityId);
        }

        public static bool IsEntityValid(uint entityId)
        {
            return EditorBridge.IsEntityValid(entityId);
        }

        public static int GetEntityCount()
        {
            return EditorBridge.GetEntityCount();
        }

        public static uint GetEntityIdAtIndex(int index)
        {
            return EditorBridge.GetEntityIdAtIndex(index);
        }

        public static string GetEntityName(uint entityId)
        {
            return EditorBridge.GetEntityName(entityId);
        }

        public static void SetEntityName(uint entityId, string name)
        {
            EditorBridge.SetEntityName(entityId, name);
        }

        public static bool GetEntityActive(uint entityId)
        {
            return EditorBridge.GetEntityActive(entityId);
        }

        public static void SetEntityActive(uint entityId, bool active)
        {
            EditorBridge.SetEntityActive(entityId, active);
        }

        public static bool HasComponent(uint entityId, int componentType)
        {
            return EditorBridge.HasComponent(entityId, componentType);
        }

        public static void AddComponent(uint entityId, int componentType)
        {
            EditorBridge.AddComponent(entityId, componentType);
        }

        public static void RemoveComponent(uint entityId, int componentType)
        {
            EditorBridge.RemoveComponent(entityId, componentType);
        }

        public static bool HasTransform(uint entityId)
        {
            return EditorBridge.HasTransform(entityId);
        }

        public static void AddTransform(uint entityId)
        {
            EditorBridge.AddTransform(entityId);
        }

        public static bool GetTransform(uint entityId, out float x, out float y, out float width, out float height)
        {
            return EditorBridge.GetTransform(entityId, out x, out y, out width, out height);
        }

        public static void SetTransform(uint entityId, float x, float y, float width, float height)
        {
            EditorBridge.SetTransform(entityId, x, y, width, height);
        }

        public static bool HasCamera(uint entityId)
        {
            return EditorBridge.HasCamera(entityId);
        }

        public static void AddCamera(uint entityId)
        {
            EditorBridge.AddCamera(entityId);
        }

        public static bool GetCamera(uint entityId, out float x, out float y, out float zoom)
        {
            return EditorBridge.GetCamera(entityId, out x, out y, out zoom);
        }

        public static void SetCamera(uint entityId, float x, float y, float zoom)
        {
            EditorBridge.SetCamera(entityId, x, y, zoom);
        }

        public static void RemoveCamera(uint entityId)
        {
            EditorBridge.RemoveCamera(entityId);
        }

        public static bool HasSprite(uint entityId)
        {
            return EditorBridge.HasSprite(entityId);
        }

        public static void AddSprite(uint entityId)
        {
            EditorBridge.AddSprite(entityId);
        }

        public static void RemoveSprite(uint entityId)
        {
            EditorBridge.RemoveSprite(entityId);
        }

        public static bool HasScript(uint entityId)
        {
            return EditorBridge.HasScript(entityId);
        }

        public static void AddScript(uint entityId)
        {
            EditorBridge.AddScript(entityId);
        }

        public static void RemoveScript(uint entityId)
        {
            EditorBridge.RemoveScript(entityId);
        }
    }
}
