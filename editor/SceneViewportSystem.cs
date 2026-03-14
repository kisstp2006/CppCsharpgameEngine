using System;
using System.Globalization;
using System.IO;

using Engine;

namespace EngineEditor
{
    internal static class SceneViewportSystem
    {
        private static float _tickAccumulator = 0.0f;
        private static bool _worldInitialized;
        private static readonly Camera2D _editorCamera = new Camera2D();

        private const float MinZoom = 0.15f;
        private const float MaxZoom = 8.0f;
        private const float GridStepWorld = 64.0f;

        private static bool _isDraggingEntity;
        private static int _dragEntityId = -1;
        private static float _dragOffsetWorldX;
        private static float _dragOffsetWorldY;
        private static float _gameViewPosX;
        private static float _gameViewPosY;
        private static float _gameViewWidth = 1.0f;
        private static float _gameViewHeight = 1.0f;

        private static bool _optionsRegistered;
        private static bool _defaultGizmoSnapEnabled;
        private static float _defaultGizmoSnapStep = 32.0f;
        private static string _loadedProjectSettingsPath = string.Empty;

        private const string ProjectSettingDefaultSnapEnabled = "editor.scene.defaultSnapEnabled";
        private const string ProjectSettingDefaultSnapStep = "editor.scene.defaultSnapStep";

        private static int SelectedEntityId
        {
            get => EditorContext.SelectedEntityId;
            set => EditorContext.SelectedEntityId = value;
        }

        private static bool GizmoSnapEnabled
        {
            get => EditorContext.GizmoSnapEnabled;
            set => EditorContext.GizmoSnapEnabled = value;
        }

        private static float GizmoSnapStep
        {
            get => EditorContext.GizmoSnapStep;
            set => EditorContext.GizmoSnapStep = value;
        }

        private static bool GameViewFocused
        {
            get => EditorContext.SceneViewportFocused;
            set => EditorContext.SceneViewportFocused = value;
        }

        private static float PreviewCameraX
        {
            get => EditorContext.PreviewCameraX;
            set => EditorContext.PreviewCameraX = value;
        }

        private static float PreviewCameraY
        {
            get => EditorContext.PreviewCameraY;
            set => EditorContext.PreviewCameraY = value;
        }

        private static float PreviewCameraZoom
        {
            get => EditorContext.PreviewCameraZoom;
            set => EditorContext.PreviewCameraZoom = value;
        }

        private static bool PreviewCameraEnabled
        {
            get => EditorContext.PreviewCameraEnabled;
            set => EditorContext.PreviewCameraEnabled = value;
        }

        public static float MinZoomValue => MinZoom;
        public static float MaxZoomValue => MaxZoom;

        public static void ResetState()
        {
            EnsureProjectSettingsLoaded();

            _tickAccumulator = 0.0f;
            _worldInitialized = false;
            _editorCamera.MinZoom = MinZoom;
            _editorCamera.MaxZoom = MaxZoom;
            _editorCamera.Reset();

            _isDraggingEntity = false;
            _dragEntityId = -1;
            _dragOffsetWorldX = 0.0f;
            _dragOffsetWorldY = 0.0f;
            GameViewFocused = false;
            _gameViewPosX = 0.0f;
            _gameViewPosY = 0.0f;
            _gameViewWidth = 1.0f;
            _gameViewHeight = 1.0f;
            GizmoSnapEnabled = _defaultGizmoSnapEnabled;
            GizmoSnapStep = _defaultGizmoSnapStep;
            SyncPreviewCameraState(false);
        }

        public static void RegisterEditorOptions()
        {
            EnsureProjectSettingsLoaded();

            if (_optionsRegistered)
                return;

            _optionsRegistered = true;
            EditorOptionsRegistry.Register("sceneeditor.viewport",
                                           "Scene Editor",
                                           "Viewport",
                                           DrawSceneEditorOptions,
                                           10);
        }

        private static void DrawSceneEditorOptions()
        {
            EnsureProjectSettingsLoaded();

            bool defaultSnapEnabled = _defaultGizmoSnapEnabled;
            if (ImGui.Checkbox("Default snap enabled", ref defaultSnapEnabled))
            {
                _defaultGizmoSnapEnabled = defaultSnapEnabled;
                SaveProjectSettings();
            }

            float defaultSnapStep = _defaultGizmoSnapStep;
            if (ImGui.InputFloat("Default snap step", ref defaultSnapStep, 1.0f))
            {
                _defaultGizmoSnapStep = Clamp(defaultSnapStep, 1.0f, 1024.0f);
                SaveProjectSettings();
            }

            ImGui.Text("These defaults are applied when the scene editor state resets.");
        }

        public static void UpdateTick(float deltaTime)
        {
            _tickAccumulator += deltaTime;
            if (_tickAccumulator < 1.0f)
                return;

            _tickAccumulator = 0.0f;
        }

        public static void DrawWorldViewportPanel(float deltaTime)
        {
            EnsureWorldInitialized();
            int simulationState = EditorBridge.GetSimulationState();
            bool simulationRunning = simulationState == EditorBridge.SimulationPlay
                                     || simulationState == EditorBridge.SimulationPause;

            if (!ImGui.Begin("Game View"))
            {
                ImGui.End();
                return;
            }

            if (ImGui.Button("Reset Camera"))
            {
                _editorCamera.Reset();
                SyncPreviewCameraState(!simulationRunning);
            }

            ImGui.SameLine();
            DrawGameViewTopBar();

            ImGui.Separator();

            float availWidth = ImGui.GetContentRegionAvailX();
            float availHeight = ImGui.GetContentRegionAvailY();
            if (availWidth < 1.0f)
                availWidth = 1.0f;
            if (availHeight < 1.0f)
                availHeight = 1.0f;

            _gameViewPosX = ImGui.GetCursorScreenPosX();
            _gameViewPosY = ImGui.GetCursorScreenPosY();
            _gameViewWidth = availWidth;
            _gameViewHeight = availHeight;

            if (!simulationRunning)
            {
                EditorBridge.SetGameViewSize(_gameViewWidth, _gameViewHeight);
                SyncPreviewCameraState(true);
                EditorBridge.SetEditorPreviewCamera(PreviewCameraX, PreviewCameraY, PreviewCameraZoom, PreviewCameraEnabled);
            }
            else
            {
                SyncPreviewCameraState(false);
                EditorBridge.SetEditorPreviewCamera(PreviewCameraX, PreviewCameraY, PreviewCameraZoom, PreviewCameraEnabled);
            }

            if (simulationRunning)
            {
                ImGui.Text("Play mode active: rendering is shown in Game Runtime window.");
                ImGui.InvisibleButton("##GameViewPlayModePlaceholder", _gameViewWidth, _gameViewHeight);
                ImGui.End();
                return;
            }

            ulong gameViewTextureHandle = EditorBridge.GetGameViewTextureHandle();
            if (gameViewTextureHandle != 0)
                ImGui.Image(gameViewTextureHandle, _gameViewWidth, _gameViewHeight);
            else
                ImGui.InvisibleButton("##GameViewImagePlaceholder", _gameViewWidth, _gameViewHeight);

            bool itemHovered = ImGui.IsItemHovered();
            bool leftClicked = ImGui.IsMouseClicked(MouseButton.Left);

            if (itemHovered && leftClicked)
                GameViewFocused = true;
            else if (!itemHovered && leftClicked)
                GameViewFocused = false;

            bool viewportCanHandleMouse = GameViewFocused && itemHovered;
            if (viewportCanHandleMouse)
            {
                float wheel = Input.GetMouseWheel();
                _editorCamera.ApplyWheel(wheel, 0.12f);

                if (Input.GetMouseButton(MouseButton.Middle) || Input.GetMouseButton(MouseButton.Right))
                {
                    float deltaX = Input.GetMouseDeltaX();
                    float deltaY = Input.GetMouseDeltaY();
                    _editorCamera.PanPixels(deltaX, deltaY);
                }

                SyncPreviewCameraState(true);
            }

            HandleViewportSelectionAndDrag(viewportCanHandleMouse);
            DrawGridAndGizmos(deltaTime);

            ImGui.End();
        }

        public static void DrawRuntimeGamePanel()
        {
            int simulationState = EditorBridge.GetSimulationState();
            bool playing = simulationState == EditorBridge.SimulationPlay;
            bool paused = simulationState == EditorBridge.SimulationPause;
            if (!playing && !paused)
                return;

            ImGui.SetNextWindowFocus();

            if (!ImGui.Begin("Game Runtime"))
            {
                ImGui.End();
                return;
            }

            ImGui.Text(paused ? "Runtime view (paused)." : "Runtime view (playing).");
            ImGui.Separator();

            float availWidth = ImGui.GetContentRegionAvailX();
            float availHeight = ImGui.GetContentRegionAvailY();
            if (availWidth < 1.0f)
                availWidth = 1.0f;
            if (availHeight < 1.0f)
                availHeight = 1.0f;

            EditorBridge.SetGameViewSize(availWidth, availHeight);
            ulong gameViewTextureHandle = EditorBridge.GetGameViewTextureHandle();
            if (gameViewTextureHandle != 0)
                ImGui.Image(gameViewTextureHandle, availWidth, availHeight);
            else
                ImGui.InvisibleButton("##RuntimeGameImagePlaceholder", availWidth, availHeight);

            ImGui.End();
        }

        private static void EnsureProjectSettingsLoaded()
        {
            string activeProjectPath = NormalizeProjectPath(ProjectOperations.ActiveProjectPath);
            if (string.Equals(_loadedProjectSettingsPath, activeProjectPath, StringComparison.OrdinalIgnoreCase))
                return;

            _loadedProjectSettingsPath = activeProjectPath;

            _defaultGizmoSnapEnabled = ReadProjectBoolSetting(ProjectSettingDefaultSnapEnabled, false);
            _defaultGizmoSnapStep = ReadProjectFloatSetting(ProjectSettingDefaultSnapStep, 32.0f, 1.0f, 1024.0f);

            GizmoSnapEnabled = _defaultGizmoSnapEnabled;
            GizmoSnapStep = _defaultGizmoSnapStep;
        }

        private static void SaveProjectSettings()
        {
            try
            {
                EditorBridge.SetProjectSetting(ProjectSettingDefaultSnapEnabled,
                                               _defaultGizmoSnapEnabled ? "true" : "false");
                EditorBridge.SetProjectSetting(ProjectSettingDefaultSnapStep,
                                               _defaultGizmoSnapStep.ToString("0.###", CultureInfo.InvariantCulture));
            }
            catch
            {
                // Keep editor option interactions responsive even if project persistence fails.
            }
        }

        private static bool ReadProjectBoolSetting(string key, bool fallbackValue)
        {
            try
            {
                string rawValue = EditorBridge.GetProjectSetting(key, fallbackValue ? "true" : "false");
                if (bool.TryParse(rawValue, out bool parsedBool))
                    return parsedBool;

                if (string.Equals(rawValue, "1", StringComparison.Ordinal))
                    return true;

                if (string.Equals(rawValue, "0", StringComparison.Ordinal))
                    return false;
            }
            catch
            {
            }

            return fallbackValue;
        }

        private static float ReadProjectFloatSetting(string key, float fallbackValue, float minValue, float maxValue)
        {
            try
            {
                string rawValue = EditorBridge.GetProjectSetting(key,
                                                                 fallbackValue.ToString("0.###", CultureInfo.InvariantCulture));
                if (float.TryParse(rawValue,
                                   NumberStyles.Float,
                                   CultureInfo.InvariantCulture,
                                   out float parsedValue))
                {
                    return Clamp(parsedValue, minValue, maxValue);
                }
            }
            catch
            {
            }

            return Clamp(fallbackValue, minValue, maxValue);
        }

        private static string NormalizeProjectPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return path.Trim();
            }
        }

        private static void EnsureWorldInitialized()
        {
            if (_worldInitialized)
                return;

            _worldInitialized = true;
            _editorCamera.MinZoom = MinZoom;
            _editorCamera.MaxZoom = MaxZoom;
            _editorCamera.Reset();
            SyncPreviewCameraState(false);
            if (SelectedEntityId >= 0 && !EntityManager.IsEntityValid((uint)SelectedEntityId))
                SelectedEntityId = -1;
        }

        private static void SyncPreviewCameraState(bool enabled)
        {
            PreviewCameraX = _editorCamera.X;
            PreviewCameraY = _editorCamera.Y;
            PreviewCameraZoom = _editorCamera.Zoom;
            PreviewCameraEnabled = enabled;
        }

        private static float Clamp(float value, float minValue, float maxValue)
        {
            if (value < minValue)
                return minValue;
            if (value > maxValue)
                return maxValue;
            return value;
        }

        private static float SnapValue(float value, float step)
        {
            if (step <= 0.0001f)
                return value;

            float scaled = value / step;
            if (scaled >= 0.0f)
                return ((float)Math.Floor(scaled + 0.5f)) * step;

            return ((float)Math.Ceiling(scaled - 0.5f)) * step;
        }

        private static void DrawGameViewTopBar()
        {
            ImGui.Text("Tool:");
            ImGui.SameLine();
            ImGui.Button("Move (W)");
            EditorContext.ActiveTool = EditorTool.Move;

            ImGui.SameLine();
            bool snap = GizmoSnapEnabled;
            if (ImGui.Checkbox("Snap", ref snap))
                GizmoSnapEnabled = snap;

            ImGui.SameLine();
            ImGui.SetNextItemWidth(90.0f);
            float step = GizmoSnapStep;
            if (ImGui.InputFloat("Step", ref step, 1.0f))
                GizmoSnapStep = Clamp(step, 1.0f, 1024.0f);

            ImGui.SameLine();
            if (ImGui.Button("Focus"))
                GameViewFocused = true;
        }

        private static bool IsPointInsideRect(float px, float py, float x, float y, float width, float height)
        {
            return px >= x && py >= y && px <= (x + width) && py <= (y + height);
        }

        private static void ResolveEntityVisualRect(uint entityId,
                                                    float transformX,
                                                    float transformY,
                                                    float transformWidth,
                                                    float transformHeight,
                                                    out float rectX,
                                                    out float rectY,
                                                    out float rectWidth,
                                                    out float rectHeight)
        {
            rectWidth = Math.Abs(transformWidth);
            rectHeight = Math.Abs(transformHeight);
            if (rectWidth < 0.0001f)
                rectWidth = 0.0001f;
            if (rectHeight < 0.0001f)
                rectHeight = 0.0001f;

            rectX = transformX;
            rectY = transformY;

            if (!EntityManager.HasSprite(entityId))
                return;

            bool centered;
            float offsetX;
            float offsetY;
            bool flipH;
            bool flipV;
            uint hframes;
            uint vframes;
            uint frame;
            bool regionEnabled;
            float regionX;
            float regionY;
            float regionWidth;
            float regionHeight;

            if (!EditorBridge.GetSpriteSettings(entityId,
                                                out centered,
                                                out offsetX,
                                                out offsetY,
                                                out flipH,
                                                out flipV,
                                                out hframes,
                                                out vframes,
                                                out frame,
                                                out regionEnabled,
                                                out regionX,
                                                out regionY,
                                                out regionWidth,
                                                out regionHeight))
            {
                return;
            }

            rectX = transformX + offsetX;
            rectY = transformY + offsetY;
            if (centered)
            {
                rectX -= rectWidth * 0.5f;
                rectY -= rectHeight * 0.5f;
            }
        }

        private static void DrawCameraDebugBounds(float centerX, float centerY)
        {
            int entityCount = EntityManager.GetEntityCount();
            for (int i = 0; i < entityCount; ++i)
            {
                uint entityId = EntityManager.GetEntityIdAtIndex(i);
                if (!EntityManager.HasCamera(entityId))
                    continue;

                float camX;
                float camY;
                float camZoom;
                bool enabled;
                bool primary;
                bool clearColor;
                uint backgroundColor;
                uint cullingMask;
                float viewportX;
                float viewportY;
                float viewportWidth;
                float viewportHeight;
                float orthographicSize;
                if (!EntityManager.GetCameraSettingsV2(entityId,
                                                       out camX,
                                                       out camY,
                                                       out camZoom,
                                                       out enabled,
                                                       out primary,
                                                       out clearColor,
                                                       out backgroundColor,
                                                       out cullingMask,
                                                       out viewportX,
                                                       out viewportY,
                                                       out viewportWidth,
                                                       out viewportHeight,
                                                       out orthographicSize))
                {
                    continue;
                }

                float clampedViewportWidth = Clamp(viewportWidth, 0.01f, 1.0f);
                float clampedViewportHeight = Clamp(viewportHeight, 0.01f, 1.0f);

                float viewportPixelWidth = Math.Max(1.0f, _gameViewWidth * clampedViewportWidth);
                float viewportPixelHeight = Math.Max(1.0f, _gameViewHeight * clampedViewportHeight);

                float halfWorldWidth;
                float halfWorldHeight;
                if (orthographicSize > 0.0001f)
                {
                    halfWorldHeight = orthographicSize;
                    float aspect = viewportPixelWidth / viewportPixelHeight;
                    halfWorldWidth = halfWorldHeight * aspect;
                }
                else
                {
                    float effectiveZoom = Clamp(camZoom, 0.01f, 100.0f);
                    halfWorldWidth = (viewportPixelWidth * 0.5f) / effectiveZoom;
                    halfWorldHeight = (viewportPixelHeight * 0.5f) / effectiveZoom;
                }

                float worldX = camX - halfWorldWidth;
                float worldY = camY - halfWorldHeight;
                float worldWidth = halfWorldWidth * 2.0f;
                float worldHeight = halfWorldHeight * 2.0f;

                float sx = _editorCamera.WorldToScreenX(worldX, centerX);
                float syBottom = _editorCamera.WorldToScreenY(worldY, centerY);
                float sw = Math.Max(1.0f, worldWidth * _editorCamera.Zoom);
                float sh = Math.Max(1.0f, worldHeight * _editorCamera.Zoom);

                float drawX = _gameViewPosX + sx;
                float drawY = _gameViewPosY + (_gameViewHeight - (syBottom + sh));

                float r = primary ? 1.0f : 0.3f;
                float g = primary ? 0.85f : 0.75f;
                float b = primary ? 0.2f : 1.0f;
                float a = enabled ? 0.95f : 0.45f;

                ImGui.DrawRect(drawX, drawY, sw, sh, r, g, b, a, primary ? 2.0f : 1.5f);

                float cx = _gameViewPosX + _editorCamera.WorldToScreenX(camX, centerX);
                float cyBottom = _editorCamera.WorldToScreenY(camY, centerY);
                float cy = _gameViewPosY + (_gameViewHeight - cyBottom);
                ImGui.DrawLine(cx - 8.0f, cy, cx + 8.0f, cy, r, g, b, a, 1.0f);
                ImGui.DrawLine(cx, cy - 8.0f, cx, cy + 8.0f, r, g, b, a, 1.0f);
            }
        }

        private static void HandleViewportSelectionAndDrag(bool hovered)
        {
            if (ImGuizmo.IsUsing())
            {
                _isDraggingEntity = false;
                _dragEntityId = -1;
                return;
            }

            bool leftDown = Input.GetMouseButton(MouseButton.Left);
            bool pressedThisFrame = Input.GetMouseButtonDown(MouseButton.Left);
            bool releasedThisFrame = Input.GetMouseButtonUp(MouseButton.Left);

            if (!hovered)
            {
                _isDraggingEntity = false;
                _dragEntityId = -1;
                return;
            }

            float centerX = _gameViewWidth * 0.5f;
            float centerY = _gameViewHeight * 0.5f;

            float mouseX = Input.GetMousePosX();
            float mouseYTopLeft = Input.GetMousePosY();
            float mouseLocalX = mouseX - _gameViewPosX;
            float mouseLocalYTopLeft = mouseYTopLeft - _gameViewPosY;
            float mouseLocalYBottomLeft = _gameViewHeight - mouseLocalYTopLeft;
            float mouseWorldX = _editorCamera.ScreenToWorldX(mouseLocalX, centerX);
            float mouseWorldY = _editorCamera.ScreenToWorldY(mouseLocalYBottomLeft, centerY);

            if (pressedThisFrame && hovered)
            {
                int hitEntityId = -1;
                float bestArea = float.MaxValue;

                int entityCount = EntityManager.GetEntityCount();
                for (int i = 0; i < entityCount; ++i)
                {
                    uint entityId = EntityManager.GetEntityIdAtIndex(i);
                    if (!EntityManager.HasTransform(entityId))
                        continue;

                    float x;
                    float y;
                    float width;
                    float height;
                    if (!EntityManager.GetTransform(entityId, out x, out y, out width, out height))
                        continue;

                    float rectX;
                    float rectY;
                    float rectWidth;
                    float rectHeight;
                    ResolveEntityVisualRect(entityId, x, y, width, height, out rectX, out rectY, out rectWidth, out rectHeight);

                    if (!IsPointInsideRect(mouseWorldX, mouseWorldY, rectX, rectY, rectWidth, rectHeight))
                        continue;

                    float area = Math.Abs(rectWidth * rectHeight);
                    if (area < bestArea)
                    {
                        bestArea = area;
                        hitEntityId = (int)entityId;
                    }
                }

                SelectedEntityId = hitEntityId;
                if (SelectedEntityId >= 0)
                {
                    uint selectedEntity = (uint)SelectedEntityId;
                    float tx;
                    float ty;
                    float tw;
                    float th;
                    if (EntityManager.GetTransform(selectedEntity, out tx, out ty, out tw, out th))
                    {
                        _isDraggingEntity = true;
                        _dragEntityId = SelectedEntityId;
                        _dragOffsetWorldX = mouseWorldX - tx;
                        _dragOffsetWorldY = mouseWorldY - ty;
                    }
                }
            }

            if (_isDraggingEntity && leftDown)
            {
                if (_dragEntityId >= 0)
                {
                    uint dragEntity = (uint)_dragEntityId;
                    if (EntityManager.IsEntityValid(dragEntity) && EntityManager.HasTransform(dragEntity))
                    {
                        float x;
                        float y;
                        float width;
                        float height;
                        if (EntityManager.GetTransform(dragEntity, out x, out y, out width, out height))
                        {
                            float targetX = mouseWorldX - _dragOffsetWorldX;
                            float targetY = mouseWorldY - _dragOffsetWorldY;
                            EntityManager.SetTransform(dragEntity, targetX, targetY, width, height);
                        }
                    }
                }
            }

            if (releasedThisFrame)
            {
                _isDraggingEntity = false;
                _dragEntityId = -1;
            }
        }

        private static void DrawGridAndGizmos(float deltaTime)
        {
            _ = deltaTime;

            if (_gameViewWidth <= 1.0f || _gameViewHeight <= 1.0f)
                return;

            float centerX = _gameViewWidth * 0.5f;
            float centerY = _gameViewHeight * 0.5f;
            float halfWorldWidth = (_gameViewWidth * 0.5f) / _editorCamera.Zoom;
            float halfWorldHeight = (_gameViewHeight * 0.5f) / _editorCamera.Zoom;

            float minWorldX = _editorCamera.X - halfWorldWidth;
            float maxWorldX = _editorCamera.X + halfWorldWidth;
            float minWorldY = _editorCamera.Y - halfWorldHeight;
            float maxWorldY = _editorCamera.Y + halfWorldHeight;

            int majorEvery = 4;

            int startGridX = (int)Math.Floor(minWorldX / GridStepWorld);
            int endGridX = (int)Math.Ceiling(maxWorldX / GridStepWorld);
            for (int gx = startGridX; gx <= endGridX; ++gx)
            {
                float worldX = gx * GridStepWorld;
                float sx = _gameViewPosX + _editorCamera.WorldToScreenX(worldX, centerX);
                bool major = (gx % majorEvery) == 0;

                float c = major ? 0.30f : 0.18f;
                float a = major ? 0.70f : 0.45f;
                ImGui.DrawLine(sx, _gameViewPosY, sx, _gameViewPosY + _gameViewHeight, c, c, c, a, 1.0f);
            }

            int startGridY = (int)Math.Floor(minWorldY / GridStepWorld);
            int endGridY = (int)Math.Ceiling(maxWorldY / GridStepWorld);
            for (int gy = startGridY; gy <= endGridY; ++gy)
            {
                float worldY = gy * GridStepWorld;
                float syBottom = _editorCamera.WorldToScreenY(worldY, centerY);
                float syTop = _gameViewPosY + (_gameViewHeight - syBottom);
                bool major = (gy % majorEvery) == 0;

                float c = major ? 0.30f : 0.18f;
                float a = major ? 0.70f : 0.45f;
                ImGui.DrawLine(_gameViewPosX, syTop, _gameViewPosX + _gameViewWidth, syTop, c, c, c, a, 1.0f);
            }

            float axisX = _gameViewPosX + _editorCamera.WorldToScreenX(0.0f, centerX);
            float axisYBottom = _editorCamera.WorldToScreenY(0.0f, centerY);
            float axisYTop = _gameViewPosY + (_gameViewHeight - axisYBottom);
            ImGui.DrawLine(axisX, _gameViewPosY, axisX, _gameViewPosY + _gameViewHeight, 0.95f, 0.2f, 0.2f, 0.95f, 2.0f);
            ImGui.DrawLine(_gameViewPosX, axisYTop, _gameViewPosX + _gameViewWidth, axisYTop, 0.2f, 0.95f, 0.2f, 0.95f, 2.0f);

            DrawCameraDebugBounds(centerX, centerY);

            int entityCount = EntityManager.GetEntityCount();
            for (int i = 0; i < entityCount; ++i)
            {
                uint entityId = EntityManager.GetEntityIdAtIndex(i);
                if (!EntityManager.HasTransform(entityId))
                    continue;

                float x;
                float y;
                float width;
                float height;
                if (!EntityManager.GetTransform(entityId, out x, out y, out width, out height))
                    continue;

                float rotation = EntityManager.GetTransformRotation(entityId);

                float rectX;
                float rectY;
                float rectWidth;
                float rectHeight;
                ResolveEntityVisualRect(entityId, x, y, width, height, out rectX, out rectY, out rectWidth, out rectHeight);

                float sx = _editorCamera.WorldToScreenX(rectX, centerX);
                float syBottom = _editorCamera.WorldToScreenY(rectY, centerY);
                float sw = Math.Max(1.0f, rectWidth * _editorCamera.Zoom);
                float sh = Math.Max(1.0f, rectHeight * _editorCamera.Zoom);

                float drawX = _gameViewPosX + sx;
                float drawY = _gameViewPosY + (_gameViewHeight - (syBottom + sh));

                bool selected = (SelectedEntityId >= 0) && ((uint)SelectedEntityId == entityId);
                float r = selected ? 1.0f : 0.75f;
                float g = selected ? 0.85f : 0.75f;
                float b = selected ? 0.2f : 1.0f;
                float a = selected ? 1.0f : 0.8f;

                ImGui.DrawRect(drawX, drawY, sw, sh, r, g, b, a, selected ? 2.0f : 1.0f);

                if (selected)
                {
                    float gizmoX = x;
                    float gizmoY = y;
                    float gizmoWidth = width;
                    float gizmoHeight = height;
                    float gizmoRotation = rotation;

                    bool timelineGizmoActive = AnimationSystem.TryManipulateSelectedKeyframeWithGizmo(entityId,
                                                                                                        _gameViewPosX,
                                                                                                        _gameViewPosY,
                                                                                                        _gameViewWidth,
                                                                                                        _gameViewHeight,
                                                                                                        _editorCamera.X,
                                                                                                        _editorCamera.Y,
                                                                                                        _editorCamera.Zoom,
                                                                                                        GizmoSnapEnabled,
                                                                                                        GizmoSnapStep,
                                                                                                        ref gizmoX,
                                                                                                        ref gizmoY,
                                                                                                        ref gizmoWidth,
                                                                                                        ref gizmoHeight,
                                                                                                        ref gizmoRotation);

                    bool gizmoChanged;
                    if (timelineGizmoActive)
                    {
                        gizmoChanged = Math.Abs(gizmoX - x) > 0.0001f
                                       || Math.Abs(gizmoY - y) > 0.0001f
                                       || Math.Abs(gizmoWidth - width) > 0.0001f
                                       || Math.Abs(gizmoHeight - height) > 0.0001f
                                       || Math.Abs(gizmoRotation - rotation) > 0.0001f;
                    }
                    else
                    {
                        gizmoChanged = ImGuizmo.Manipulate2DTranslate(_gameViewPosX,
                                                                      _gameViewPosY,
                                                                      _gameViewWidth,
                                                                      _gameViewHeight,
                                                                      _editorCamera.X,
                                                                      _editorCamera.Y,
                                                                      _editorCamera.Zoom,
                                                                      ref gizmoX,
                                                                      ref gizmoY,
                                                                      width,
                                                                      height);
                    }

                    if (gizmoChanged)
                    {
                        if (!timelineGizmoActive && GizmoSnapEnabled)
                        {
                            gizmoX = SnapValue(gizmoX, GizmoSnapStep);
                            gizmoY = SnapValue(gizmoY, GizmoSnapStep);
                        }

                        EntityManager.SetTransform(entityId, gizmoX, gizmoY, gizmoWidth, gizmoHeight);
                        EntityManager.SetTransformRotation(entityId, gizmoRotation);

                        x = gizmoX;
                        y = gizmoY;
                        width = gizmoWidth;
                        height = gizmoHeight;
                        rotation = gizmoRotation;

                        ResolveEntityVisualRect(entityId, x, y, width, height, out rectX, out rectY, out rectWidth, out rectHeight);
                        sx = _editorCamera.WorldToScreenX(rectX, centerX);
                        syBottom = _editorCamera.WorldToScreenY(rectY, centerY);
                        sw = Math.Max(1.0f, rectWidth * _editorCamera.Zoom);
                        sh = Math.Max(1.0f, rectHeight * _editorCamera.Zoom);
                        drawX = _gameViewPosX + sx;
                        drawY = _gameViewPosY + (_gameViewHeight - (syBottom + sh));
                    }
                }

                float cx = drawX + (sw * 0.5f);
                float cy = drawY + (sh * 0.5f);
                ImGui.DrawLine(cx - 6.0f, cy, cx + 6.0f, cy, r, g, b, 0.9f, 1.0f);
                ImGui.DrawLine(cx, cy - 6.0f, cx, cy + 6.0f, r, g, b, 0.9f, 1.0f);
            }
        }
    }
}
