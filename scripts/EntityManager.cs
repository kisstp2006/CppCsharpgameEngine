using System.Runtime.CompilerServices;

namespace Engine
{
    public static class ComponentType
    {
        public const int Transform = 0;
        public const int Camera = 1;
        public const int Sprite = 2;
        public const int Script = 3;
        public const int Animator = 4;
        public const int UiCanvas = 5;
        public const int UiRectTransform = 6;
        public const int UiImage = 7;
        public const int UiText = 8;
        public const int UiButton = 9;
        public const int UiInputField = 10;
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
        private static extern float GetTransformRotationInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void SetTransformRotationInternal(uint entityId, float rotation);

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
        private static extern bool GetCameraSettingsV2Internal(uint entityId,
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
                         out float orthographicSize);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void SetCameraSettingsV2Internal(uint entityId,
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
                         float orthographicSize);

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

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool HasAnimatorInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void AddAnimatorInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void RemoveAnimatorInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern string GetAnimatorClipPathInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void SetAnimatorClipPathInternal(uint entityId, string clipPath);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern float GetAnimatorTimeInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void SetAnimatorTimeInternal(uint entityId, float time);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool GetAnimatorPlayingInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void SetAnimatorPlayingInternal(uint entityId, bool playing);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool GetAnimatorLoopInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void SetAnimatorLoopInternal(uint entityId, bool loop);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern float GetAnimatorSpeedInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void SetAnimatorSpeedInternal(uint entityId, float speed);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool GetAnimatorApplyPoseWhenStoppedInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void SetAnimatorApplyPoseWhenStoppedInternal(uint entityId, bool value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool HasUiCanvasInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void AddUiCanvasInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void RemoveUiCanvasInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool GetUiCanvasSettingsInternal(uint entityId,
                                       out bool enabled,
                                       out int sortingOrder,
                                       out bool pixelPerfect,
                                       out int renderMode,
                                       out int targetDisplay,
                                       out uint additionalShaderChannels,
                                       out bool vertexColorAlwaysGammaSpace);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void SetUiCanvasSettingsInternal(uint entityId,
                                       bool enabled,
                                       int sortingOrder,
                                       bool pixelPerfect,
                                       int renderMode,
                                       int targetDisplay,
                                       uint additionalShaderChannels,
                                       bool vertexColorAlwaysGammaSpace);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool HasUiRectTransformInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void AddUiRectTransformInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void RemoveUiRectTransformInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool GetUiRectTransformInternal(uint entityId,
                                      out float anchorMinX,
                                      out float anchorMinY,
                                      out float anchorMaxX,
                                      out float anchorMaxY,
                                      out float pivotX,
                                      out float pivotY,
                                      out float anchoredX,
                                      out float anchoredY,
                                      out float sizeDeltaX,
                                      out float sizeDeltaY);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void SetUiRectTransformInternal(uint entityId,
                                      float anchorMinX,
                                      float anchorMinY,
                                      float anchorMaxX,
                                      float anchorMaxY,
                                      float pivotX,
                                      float pivotY,
                                      float anchoredX,
                                      float anchoredY,
                                      float sizeDeltaX,
                                      float sizeDeltaY);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool HasUiImageInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void AddUiImageInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void RemoveUiImageInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool GetUiImageSettingsInternal(uint entityId,
                                      out bool enabled,
                                      out ulong textureAssetHandle,
                                      out uint color,
                                      out bool preserveAspect,
                                      out float cornerRadius);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void SetUiImageSettingsInternal(uint entityId,
                                      bool enabled,
                                      ulong textureAssetHandle,
                                      uint color,
                                      bool preserveAspect,
                                      float cornerRadius);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern string GetUiImageTexturePathInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void SetUiImageTexturePathInternal(uint entityId, string texturePath);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool HasUiTextInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void AddUiTextInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void RemoveUiTextInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool GetUiTextSettingsInternal(uint entityId,
                                     out bool enabled,
                                     out float fontSize,
                                     out uint color,
                                     out int horizontalAlign,
                                     out bool wrap);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void SetUiTextSettingsInternal(uint entityId,
                                     bool enabled,
                                     float fontSize,
                                     uint color,
                                     int horizontalAlign,
                                     bool wrap);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern string GetUiTextValueInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void SetUiTextValueInternal(uint entityId, string text);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool HasUiButtonInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void AddUiButtonInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void RemoveUiButtonInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool GetUiButtonSettingsInternal(uint entityId,
                                       out bool enabled,
                                       out bool interactable,
                                       out uint normalColor,
                                       out uint highlightedColor,
                                       out uint pressedColor,
                                       out uint disabledColor);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void SetUiButtonSettingsInternal(uint entityId,
                                       bool enabled,
                                       bool interactable,
                                       uint normalColor,
                                       uint highlightedColor,
                                       uint pressedColor,
                                       uint disabledColor);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool HasUiInputFieldInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void AddUiInputFieldInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void RemoveUiInputFieldInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool GetUiInputFieldSettingsInternal(uint entityId,
                                       out bool enabled,
                                       out bool interactable,
                                       out uint textColor,
                                       out uint placeholderColor,
                                       out uint maxLength);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void SetUiInputFieldSettingsInternal(uint entityId,
                                       bool enabled,
                                       bool interactable,
                                       uint textColor,
                                       uint placeholderColor,
                                       uint maxLength);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern string GetUiInputFieldTextInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void SetUiInputFieldTextInternal(uint entityId, string text);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern string GetUiInputFieldPlaceholderInternal(uint entityId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void SetUiInputFieldPlaceholderInternal(uint entityId, string placeholder);

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

        public static float GetTransformRotation(uint entityId)
        {
            return GetTransformRotationInternal(entityId);
        }

        public static void SetTransformRotation(uint entityId, float rotation)
        {
            SetTransformRotationInternal(entityId, rotation);
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
            return GetCameraSettingsV2Internal(entityId,
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
            SetCameraSettingsV2Internal(entityId,
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

        public static bool HasAnimator(uint entityId)
        {
            return HasAnimatorInternal(entityId);
        }

        public static void AddAnimator(uint entityId)
        {
            AddAnimatorInternal(entityId);
        }

        public static void RemoveAnimator(uint entityId)
        {
            RemoveAnimatorInternal(entityId);
        }

        public static string GetAnimatorClipPath(uint entityId)
        {
            return GetAnimatorClipPathInternal(entityId);
        }

        public static void SetAnimatorClipPath(uint entityId, string clipPath)
        {
            SetAnimatorClipPathInternal(entityId, clipPath);
        }

        public static float GetAnimatorTime(uint entityId)
        {
            return GetAnimatorTimeInternal(entityId);
        }

        public static void SetAnimatorTime(uint entityId, float time)
        {
            SetAnimatorTimeInternal(entityId, time);
        }

        public static bool GetAnimatorPlaying(uint entityId)
        {
            return GetAnimatorPlayingInternal(entityId);
        }

        public static void SetAnimatorPlaying(uint entityId, bool playing)
        {
            SetAnimatorPlayingInternal(entityId, playing);
        }

        public static bool GetAnimatorLoop(uint entityId)
        {
            return GetAnimatorLoopInternal(entityId);
        }

        public static void SetAnimatorLoop(uint entityId, bool loop)
        {
            SetAnimatorLoopInternal(entityId, loop);
        }

        public static float GetAnimatorSpeed(uint entityId)
        {
            return GetAnimatorSpeedInternal(entityId);
        }

        public static void SetAnimatorSpeed(uint entityId, float speed)
        {
            SetAnimatorSpeedInternal(entityId, speed);
        }

        public static bool GetAnimatorApplyPoseWhenStopped(uint entityId)
        {
            return GetAnimatorApplyPoseWhenStoppedInternal(entityId);
        }

        public static void SetAnimatorApplyPoseWhenStopped(uint entityId, bool value)
        {
            SetAnimatorApplyPoseWhenStoppedInternal(entityId, value);
        }

        public static bool HasUiCanvas(uint entityId)
        {
            return HasUiCanvasInternal(entityId);
        }

        public static void AddUiCanvas(uint entityId)
        {
            AddUiCanvasInternal(entityId);
        }

        public static void RemoveUiCanvas(uint entityId)
        {
            RemoveUiCanvasInternal(entityId);
        }

        public static bool GetUiCanvasSettings(uint entityId,
                                               out bool enabled,
                                               out int sortingOrder,
                                               out bool pixelPerfect)
        {
            int renderMode;
            int targetDisplay;
            uint additionalShaderChannels;
            bool vertexColorAlwaysGammaSpace;
            return GetUiCanvasSettingsInternal(entityId,
                                               out enabled,
                                               out sortingOrder,
                                               out pixelPerfect,
                                               out renderMode,
                                               out targetDisplay,
                                               out additionalShaderChannels,
                                               out vertexColorAlwaysGammaSpace);
        }

        public static void SetUiCanvasSettings(uint entityId,
                                               bool enabled,
                                               int sortingOrder,
                                               bool pixelPerfect)
        {
            bool currentEnabled;
            int currentSortingOrder;
            bool currentPixelPerfect;
            int currentRenderMode;
            int currentTargetDisplay;
            uint currentAdditionalShaderChannels;
            bool currentVertexColorAlwaysGammaSpace;

            if (!GetUiCanvasSettingsInternal(entityId,
                                            out currentEnabled,
                                            out currentSortingOrder,
                                            out currentPixelPerfect,
                                            out currentRenderMode,
                                            out currentTargetDisplay,
                                            out currentAdditionalShaderChannels,
                                            out currentVertexColorAlwaysGammaSpace))
            {
                return;
            }

            SetUiCanvasSettingsInternal(entityId,
                                        enabled,
                                        sortingOrder,
                                        pixelPerfect,
                                        currentRenderMode,
                                        currentTargetDisplay,
                                        currentAdditionalShaderChannels,
                                        currentVertexColorAlwaysGammaSpace);
        }

        public static bool GetUiCanvasSettingsV2(uint entityId,
                                                 out bool enabled,
                                                 out int sortingOrder,
                                                 out bool pixelPerfect,
                                                 out int renderMode,
                                                 out int targetDisplay,
                                                 out uint additionalShaderChannels,
                                                 out bool vertexColorAlwaysGammaSpace)
        {
            return GetUiCanvasSettingsInternal(entityId,
                                               out enabled,
                                               out sortingOrder,
                                               out pixelPerfect,
                                               out renderMode,
                                               out targetDisplay,
                                               out additionalShaderChannels,
                                               out vertexColorAlwaysGammaSpace);
        }

        public static void SetUiCanvasSettingsV2(uint entityId,
                                                 bool enabled,
                                                 int sortingOrder,
                                                 bool pixelPerfect,
                                                 int renderMode,
                                                 int targetDisplay,
                                                 uint additionalShaderChannels,
                                                 bool vertexColorAlwaysGammaSpace)
        {
            SetUiCanvasSettingsInternal(entityId,
                                        enabled,
                                        sortingOrder,
                                        pixelPerfect,
                                        renderMode,
                                        targetDisplay,
                                        additionalShaderChannels,
                                        vertexColorAlwaysGammaSpace);
        }

        public static bool HasUiRectTransform(uint entityId)
        {
            return HasUiRectTransformInternal(entityId);
        }

        public static void AddUiRectTransform(uint entityId)
        {
            AddUiRectTransformInternal(entityId);
        }

        public static void RemoveUiRectTransform(uint entityId)
        {
            RemoveUiRectTransformInternal(entityId);
        }

        public static bool GetUiRectTransform(uint entityId,
                                              out float anchorMinX,
                                              out float anchorMinY,
                                              out float anchorMaxX,
                                              out float anchorMaxY,
                                              out float pivotX,
                                              out float pivotY,
                                              out float anchoredX,
                                              out float anchoredY,
                                              out float sizeDeltaX,
                                              out float sizeDeltaY)
        {
            return GetUiRectTransformInternal(entityId,
                                              out anchorMinX,
                                              out anchorMinY,
                                              out anchorMaxX,
                                              out anchorMaxY,
                                              out pivotX,
                                              out pivotY,
                                              out anchoredX,
                                              out anchoredY,
                                              out sizeDeltaX,
                                              out sizeDeltaY);
        }

        public static void SetUiRectTransform(uint entityId,
                                              float anchorMinX,
                                              float anchorMinY,
                                              float anchorMaxX,
                                              float anchorMaxY,
                                              float pivotX,
                                              float pivotY,
                                              float anchoredX,
                                              float anchoredY,
                                              float sizeDeltaX,
                                              float sizeDeltaY)
        {
            SetUiRectTransformInternal(entityId,
                                       anchorMinX,
                                       anchorMinY,
                                       anchorMaxX,
                                       anchorMaxY,
                                       pivotX,
                                       pivotY,
                                       anchoredX,
                                       anchoredY,
                                       sizeDeltaX,
                                       sizeDeltaY);
        }

        public static bool HasUiImage(uint entityId)
        {
            return HasUiImageInternal(entityId);
        }

        public static void AddUiImage(uint entityId)
        {
            AddUiImageInternal(entityId);
        }

        public static void RemoveUiImage(uint entityId)
        {
            RemoveUiImageInternal(entityId);
        }

        public static bool GetUiImageSettings(uint entityId,
                                              out bool enabled,
                                              out ulong textureAssetHandle,
                                              out uint color,
                                              out bool preserveAspect,
                                              out float cornerRadius)
        {
            return GetUiImageSettingsInternal(entityId,
                                              out enabled,
                                              out textureAssetHandle,
                                              out color,
                                              out preserveAspect,
                                              out cornerRadius);
        }

        public static void SetUiImageSettings(uint entityId,
                                              bool enabled,
                                              ulong textureAssetHandle,
                                              uint color,
                                              bool preserveAspect,
                                              float cornerRadius)
        {
            SetUiImageSettingsInternal(entityId,
                                       enabled,
                                       textureAssetHandle,
                                       color,
                                       preserveAspect,
                                       cornerRadius);
        }

        public static string GetUiImageTexturePath(uint entityId)
        {
            return GetUiImageTexturePathInternal(entityId);
        }

        public static void SetUiImageTexturePath(uint entityId, string texturePath)
        {
            SetUiImageTexturePathInternal(entityId, texturePath);
        }

        public static bool HasUiText(uint entityId)
        {
            return HasUiTextInternal(entityId);
        }

        public static void AddUiText(uint entityId)
        {
            AddUiTextInternal(entityId);
        }

        public static void RemoveUiText(uint entityId)
        {
            RemoveUiTextInternal(entityId);
        }

        public static bool GetUiTextSettings(uint entityId,
                                             out bool enabled,
                                             out float fontSize,
                                             out uint color,
                                             out int horizontalAlign,
                                             out bool wrap)
        {
            return GetUiTextSettingsInternal(entityId,
                                             out enabled,
                                             out fontSize,
                                             out color,
                                             out horizontalAlign,
                                             out wrap);
        }

        public static void SetUiTextSettings(uint entityId,
                                             bool enabled,
                                             float fontSize,
                                             uint color,
                                             int horizontalAlign,
                                             bool wrap)
        {
            SetUiTextSettingsInternal(entityId,
                                      enabled,
                                      fontSize,
                                      color,
                                      horizontalAlign,
                                      wrap);
        }

        public static string GetUiTextValue(uint entityId)
        {
            return GetUiTextValueInternal(entityId);
        }

        public static void SetUiTextValue(uint entityId, string text)
        {
            SetUiTextValueInternal(entityId, text);
        }

        public static bool HasUiButton(uint entityId)
        {
            return HasUiButtonInternal(entityId);
        }

        public static void AddUiButton(uint entityId)
        {
            AddUiButtonInternal(entityId);
        }

        public static void RemoveUiButton(uint entityId)
        {
            RemoveUiButtonInternal(entityId);
        }

        public static bool GetUiButtonSettings(uint entityId,
                                               out bool enabled,
                                               out bool interactable,
                                               out uint normalColor,
                                               out uint highlightedColor,
                                               out uint pressedColor,
                                               out uint disabledColor)
        {
            return GetUiButtonSettingsInternal(entityId,
                                               out enabled,
                                               out interactable,
                                               out normalColor,
                                               out highlightedColor,
                                               out pressedColor,
                                               out disabledColor);
        }

        public static void SetUiButtonSettings(uint entityId,
                                               bool enabled,
                                               bool interactable,
                                               uint normalColor,
                                               uint highlightedColor,
                                               uint pressedColor,
                                               uint disabledColor)
        {
            SetUiButtonSettingsInternal(entityId,
                                        enabled,
                                        interactable,
                                        normalColor,
                                        highlightedColor,
                                        pressedColor,
                                        disabledColor);
        }

        public static bool HasUiInputField(uint entityId)
        {
            return HasUiInputFieldInternal(entityId);
        }

        public static void AddUiInputField(uint entityId)
        {
            AddUiInputFieldInternal(entityId);
        }

        public static void RemoveUiInputField(uint entityId)
        {
            RemoveUiInputFieldInternal(entityId);
        }

        public static bool GetUiInputFieldSettings(uint entityId,
                                                   out bool enabled,
                                                   out bool interactable,
                                                   out uint textColor,
                                                   out uint placeholderColor,
                                                   out uint maxLength)
        {
            return GetUiInputFieldSettingsInternal(entityId,
                                                   out enabled,
                                                   out interactable,
                                                   out textColor,
                                                   out placeholderColor,
                                                   out maxLength);
        }

        public static void SetUiInputFieldSettings(uint entityId,
                                                   bool enabled,
                                                   bool interactable,
                                                   uint textColor,
                                                   uint placeholderColor,
                                                   uint maxLength)
        {
            SetUiInputFieldSettingsInternal(entityId,
                                            enabled,
                                            interactable,
                                            textColor,
                                            placeholderColor,
                                            maxLength);
        }

        public static string GetUiInputFieldText(uint entityId)
        {
            return GetUiInputFieldTextInternal(entityId);
        }

        public static void SetUiInputFieldText(uint entityId, string text)
        {
            SetUiInputFieldTextInternal(entityId, text);
        }

        public static string GetUiInputFieldPlaceholder(uint entityId)
        {
            return GetUiInputFieldPlaceholderInternal(entityId);
        }

        public static void SetUiInputFieldPlaceholder(uint entityId, string placeholder)
        {
            SetUiInputFieldPlaceholderInternal(entityId, placeholder);
        }
    }
}
