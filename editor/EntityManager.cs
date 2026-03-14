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

        public static uint DuplicateEntity(uint entityId)
        {
            return EditorBridge.DuplicateEntity(entityId);
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

        public static uint GetParentEntity(uint entityId)
        {
            return EditorBridge.GetParentEntity(entityId);
        }

        public static bool SetParentEntity(uint childEntityId, uint parentEntityId)
        {
            return EditorBridge.SetParentEntity(childEntityId, parentEntityId);
        }

        public static int GetRootEntityCount()
        {
            return EditorBridge.GetRootEntityCount();
        }

        public static uint GetRootEntityAt(int index)
        {
            return EditorBridge.GetRootEntityAt(index);
        }

        public static int GetChildEntityCount(uint entityId)
        {
            return EditorBridge.GetChildEntityCount(entityId);
        }

        public static uint GetChildEntityAt(uint entityId, int index)
        {
            return EditorBridge.GetChildEntityAt(entityId, index);
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

        public static float GetTransformRotation(uint entityId)
        {
            return EditorBridge.GetTransformRotation(entityId);
        }

        public static void SetTransformRotation(uint entityId, float rotation)
        {
            EditorBridge.SetTransformRotation(entityId, rotation);
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
            return EditorBridge.GetCameraSettings(entityId,
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
            EditorBridge.SetCameraSettings(entityId,
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

        public static bool GetCameraSettingsV2(uint entityId,
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
                                               out float viewportHeight,
                                               out float orthographicSize)
        {
            return EditorBridge.GetCameraSettingsV2(entityId,
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
                                                    out viewportHeight,
                                                    out orthographicSize);
        }

        public static void SetCameraSettingsV2(uint entityId,
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
                                               float viewportHeight,
                                               float orthographicSize)
        {
            EditorBridge.SetCameraSettingsV2(entityId,
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
                                             viewportHeight,
                                             orthographicSize);
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

        public static bool HasAnimator(uint entityId)
        {
            return EditorBridge.HasAnimator(entityId);
        }

        public static void AddAnimator(uint entityId)
        {
            EditorBridge.AddAnimator(entityId);
        }

        public static void RemoveAnimator(uint entityId)
        {
            EditorBridge.RemoveAnimator(entityId);
        }

        public static string GetAnimatorClipPath(uint entityId)
        {
            return EditorBridge.GetAnimatorClipPath(entityId);
        }

        public static void SetAnimatorClipPath(uint entityId, string clipPath)
        {
            EditorBridge.SetAnimatorClipPath(entityId, clipPath);
        }

        public static float GetAnimatorTime(uint entityId)
        {
            return EditorBridge.GetAnimatorTime(entityId);
        }

        public static void SetAnimatorTime(uint entityId, float time)
        {
            EditorBridge.SetAnimatorTime(entityId, time);
        }

        public static bool GetAnimatorPlaying(uint entityId)
        {
            return EditorBridge.GetAnimatorPlaying(entityId);
        }

        public static void SetAnimatorPlaying(uint entityId, bool playing)
        {
            EditorBridge.SetAnimatorPlaying(entityId, playing);
        }

        public static bool GetAnimatorLoop(uint entityId)
        {
            return EditorBridge.GetAnimatorLoop(entityId);
        }

        public static void SetAnimatorLoop(uint entityId, bool loop)
        {
            EditorBridge.SetAnimatorLoop(entityId, loop);
        }

        public static float GetAnimatorSpeed(uint entityId)
        {
            return EditorBridge.GetAnimatorSpeed(entityId);
        }

        public static void SetAnimatorSpeed(uint entityId, float speed)
        {
            EditorBridge.SetAnimatorSpeed(entityId, speed);
        }

        public static bool GetAnimatorApplyPoseWhenStopped(uint entityId)
        {
            return EditorBridge.GetAnimatorApplyPoseWhenStopped(entityId);
        }

        public static void SetAnimatorApplyPoseWhenStopped(uint entityId, bool value)
        {
            EditorBridge.SetAnimatorApplyPoseWhenStopped(entityId, value);
        }
    }
}
