using System;
using System.Collections.Generic;

using Engine;

namespace EngineEditor
{
    internal static class SceneEditor
    {
        private static int _selectedEntityId = -1;
        private static float _tickAccumulator = 0.0f;
        private static bool _worldInitialized = false;
        private static readonly Camera2D _editorCamera = new Camera2D();

        private const float MinZoom = 0.15f;
        private const float MaxZoom = 8.0f;
        private const float GridStepWorld = 64.0f;

        private static bool _isDraggingEntity = false;
        private static int _dragEntityId = -1;
        private static float _dragOffsetWorldX = 0.0f;
        private static float _dragOffsetWorldY = 0.0f;
        private static bool _gameViewFocusedByClick = false;
        private static float _gameViewPosX = 0.0f;
        private static float _gameViewPosY = 0.0f;
        private static float _gameViewWidth = 1.0f;
        private static float _gameViewHeight = 1.0f;
        private static bool _gizmoSnapEnabled = false;
        private static float _gizmoSnapStep = 32.0f;
        private static bool _componentRegistryInitialized = false;
        private static readonly List<ComponentInspectorEntry> _componentEntries = new List<ComponentInspectorEntry>();
        private static readonly Dictionary<uint, string> _scriptAssignmentErrors = new Dictionary<uint, string>();

        private sealed class ComponentInspectorEntry
        {
            public string Name;
            public string Category;
            public int ComponentType;
            public bool CanRemove;
            public Action<uint> Draw;
        }

        public static int SelectedEntityId => _selectedEntityId;

        public static void ResetEditorState()
        {
            _selectedEntityId = -1;
            _tickAccumulator = 0.0f;
            _worldInitialized = false;
            _editorCamera.MinZoom = MinZoom;
            _editorCamera.MaxZoom = MaxZoom;
            _editorCamera.Reset();
            _isDraggingEntity = false;
            _dragEntityId = -1;
            _dragOffsetWorldX = 0.0f;
            _dragOffsetWorldY = 0.0f;
            _gameViewFocusedByClick = false;
            _gameViewPosX = 0.0f;
            _gameViewPosY = 0.0f;
            _gameViewWidth = 1.0f;
            _gameViewHeight = 1.0f;
            _gizmoSnapEnabled = false;
            _gizmoSnapStep = 32.0f;
            _scriptAssignmentErrors.Clear();
            DebugDraw.Clear();
        }

        public static void ResetSelection()
        {
            _selectedEntityId = -1;
        }

        public static void CreateEntityAndSelect()
        {
            uint created = EditorBridge.CreateEntity();
            if (EditorBridge.IsEntityValid(created))
                _selectedEntityId = (int)created;
        }

        public static void UpdateTick(float deltaTime)
        {
            _tickAccumulator += deltaTime;
            if (_tickAccumulator < 1.0f)
                return;

            _tickAccumulator = 0.0f;

            int totalEntitiesLog = EditorBridge.GetEntityCount();
            int scriptedEntitiesLog = EditorBridge.GetScriptedEntityCount();

            Console.WriteLine("[Editor] Entities=" + totalEntitiesLog + ", Scripted=" + scriptedEntitiesLog);
        }

        public static void DrawWorldViewportPanel(float deltaTime)
        {
            EnsureWorldInitialized();

            if (!ImGui.Begin("Game View"))
            {
                ImGui.End();
                return;
            }

            if (ImGui.Button("Reset Camera"))
                _editorCamera.Reset();

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

            EditorBridge.SetGameViewSize(_gameViewWidth, _gameViewHeight);
            ulong gameViewTextureHandle = EditorBridge.GetGameViewTextureHandle();
            if (gameViewTextureHandle != 0)
                ImGui.Image(gameViewTextureHandle, _gameViewWidth, _gameViewHeight);
            else
                ImGui.InvisibleButton("##GameViewImagePlaceholder", _gameViewWidth, _gameViewHeight);

            bool itemHovered = ImGui.IsItemHovered();
            bool leftClicked = ImGui.IsMouseClicked(MouseButton.Left);

            if (itemHovered && leftClicked)
                _gameViewFocusedByClick = true;
            else if (!itemHovered && leftClicked)
                _gameViewFocusedByClick = false;

            bool viewportCanHandleMouse = _gameViewFocusedByClick && itemHovered;
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
            }

            HandleViewportSelectionAndDrag(viewportCanHandleMouse);
            DrawGridAndGizmos(deltaTime);

            ImGui.End();
        }

        public static void DrawSceneTreePanel()
        {
            if (ImGui.Begin("Scene Tree"))
            {
                if (ImGui.Button("Project Manager"))
                {
                    EditorHost.SetShowProjectManagerView(true);
                    ImGui.End();
                    return;
                }

                ImGui.SameLine();
                if (ImGui.Button("Create Entity"))
                {
                    uint created = EditorBridge.CreateEntity();
                    _selectedEntityId = (int)created;
                }

                ImGui.Separator();

                int entityCount = EditorBridge.GetEntityCount();
                for (int i = 0; i < entityCount; ++i)
                {
                    uint entityId = EditorBridge.GetEntityIdAtIndex(i);
                    string label = "Entity " + entityId;
                    bool selected = ((uint)_selectedEntityId == entityId);
                    if (ImGui.Selectable(label, selected))
                        _selectedEntityId = (int)entityId;
                }
            }
            ImGui.End();
        }

        public static void DrawInspectorPanel()
        {
            if (ImGui.Begin("Inspector"))
            {
                if (_selectedEntityId < 0)
                {
                    ImGui.Text("No entity selected.");
                }
                else
                {
                    uint entityId = (uint)_selectedEntityId;
                    bool valid = EditorBridge.IsEntityValid(entityId);
                    if (!valid)
                    {
                        _selectedEntityId = -1;
                        ImGui.Text("Selected entity is no longer valid.");
                    }
                    else
                    {
                        ImGui.Text("Entity: " + entityId);

                        if (ImGui.Button("Delete Entity"))
                        {
                            EditorBridge.DestroyEntity(entityId);
                            _selectedEntityId = -1;
                            ImGui.End();
                            return;
                        }

                        ImGui.Separator();
                        DrawAddComponentMenu(entityId);

                        for (int i = 0; i < _componentEntries.Count; ++i)
                        {
                            ComponentInspectorEntry entry = _componentEntries[i];
                            if (!EditorBridge.HasComponent(entityId, entry.ComponentType))
                                continue;

                            ImGui.Separator();
                            ImGui.Text(entry.Name);

                            if (entry.CanRemove)
                            {
                                ImGui.SameLine();
                                if (ImGui.Button("Remove##" + entry.Name))
                                {
                                    EditorBridge.RemoveComponent(entityId, entry.ComponentType);
                                    continue;
                                }
                            }

                            entry.Draw(entityId);
                        }
                    }
                }
            }
            ImGui.End();
        }

        private static void EnsureWorldInitialized()
        {
            if (_worldInitialized)
                return;

            _worldInitialized = true;
            _editorCamera.MinZoom = MinZoom;
            _editorCamera.MaxZoom = MaxZoom;
            _editorCamera.Reset();
            InitializeComponentRegistry();
            if (_selectedEntityId >= 0 && !EditorBridge.IsEntityValid((uint)_selectedEntityId))
                _selectedEntityId = -1;
        }

        private static void InitializeComponentRegistry()
        {
            if (_componentRegistryInitialized)
                return;

            _componentRegistryInitialized = true;

            RegisterInspectorComponent("Transform",
                                       "Core",
                                       ComponentType.Transform,
                                       DrawTransformInspector,
                                       false);

            RegisterInspectorComponent("Sprite",
                                       "Rendering",
                                       ComponentType.Sprite,
                                       DrawSpriteInspector,
                                       true);

            RegisterInspectorComponent("Camera",
                                       "Rendering",
                                       ComponentType.Camera,
                                       DrawCameraInspector,
                                       true);

            RegisterInspectorComponent("Script",
                                       "Logic",
                                       ComponentType.Script,
                                       DrawScriptInspector,
                                       true);
        }

        private static void RegisterInspectorComponent(string name,
                                                       string category,
                                                       int componentType,
                                                       Action<uint> draw,
                                                       bool canRemove)
        {
            string resolvedCategory = category;
            if (string.IsNullOrEmpty(resolvedCategory))
                resolvedCategory = "Others";

            var entry = new ComponentInspectorEntry();
            entry.Name = name;
            entry.Category = resolvedCategory;
            entry.ComponentType = componentType;
            entry.CanRemove = canRemove;
            entry.Draw = draw;
            _componentEntries.Add(entry);
        }

        private static void DrawAddComponentMenu(uint entityId)
        {
            if (ImGui.Button("Add Component"))
                ImGui.OpenPopup("Add Component Popup");

            if (!ImGui.BeginPopupModal("Add Component Popup"))
                return;

            bool hasAnyAddable = false;
            var categories = new List<string>();
            for (int i = 0; i < _componentEntries.Count; ++i)
            {
                ComponentInspectorEntry entry = _componentEntries[i];
                if (EditorBridge.HasComponent(entityId, entry.ComponentType))
                    continue;

                hasAnyAddable = true;
                bool knownCategory = false;
                for (int c = 0; c < categories.Count; ++c)
                {
                    if (categories[c] == entry.Category)
                    {
                        knownCategory = true;
                        break;
                    }
                }

                if (!knownCategory)
                    categories.Add(entry.Category);
            }

            if (!hasAnyAddable)
            {
                ImGui.Text("All registered components are already attached.");
            }
            else
            {
                for (int c = 0; c < categories.Count; ++c)
                {
                    string category = categories[c];
                    ImGui.Text(category);

                    for (int i = 0; i < _componentEntries.Count; ++i)
                    {
                        ComponentInspectorEntry entry = _componentEntries[i];
                        if (entry.Category != category || EditorBridge.HasComponent(entityId, entry.ComponentType))
                            continue;

                        if (ImGui.Selectable("  + " + entry.Name + "##AddComponent" + entry.Name, false))
                        {
                            EditorBridge.AddComponent(entityId, entry.ComponentType);
                            ImGui.CloseCurrentPopup();
                            ImGui.EndPopup();
                            return;
                        }
                    }

                    if (c + 1 < categories.Count)
                        ImGui.Separator();
                }
            }

            ImGui.Separator();
            if (ImGui.Button("Close"))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }

        private static void DrawTransformInspector(uint entityId)
        {
            float x;
            float y;
            float width;
            float height;
            if (!EditorBridge.GetTransform(entityId, out x, out y, out width, out height))
                return;

            bool changed = false;
            changed |= InspectorInputs.Vector2("Position", ref x, ref y, 1.0f);
            changed |= InspectorInputs.Vector2("Size", ref width, ref height, 1.0f);

            if (changed)
                EditorBridge.SetTransform(entityId, x, y, width, height);
        }

        private static void DrawSpriteInspector(uint entityId)
        {
            _ = entityId;
            ImGui.Text("Sprite settings are not exposed yet.");
        }

        private static void DrawCameraInspector(uint entityId)
        {
            float camX;
            float camY;
            float camZoom;
            if (EditorBridge.GetCamera(entityId, out camX, out camY, out camZoom))
            {
                bool cameraChanged = false;
                cameraChanged |= InspectorInputs.Vector2("Cam Position", ref camX, ref camY, 1.0f);
                cameraChanged |= ImGui.InputFloat("Cam Zoom", ref camZoom, 0.1f);

                if (cameraChanged)
                    EditorBridge.SetCamera(entityId, camX, camY, Clamp(camZoom, MinZoom, MaxZoom));
            }

        }

        private static void DrawScriptInspector(uint entityId)
        {
            ScriptValidationSnapshot validationSnapshot = ScriptComponentValidation.GetSnapshot();

            string currentScriptTypeName = EditorBridge.GetScriptTypeName(entityId);
            if (currentScriptTypeName == null)
                currentScriptTypeName = string.Empty;

            string requestedScriptTypeName = currentScriptTypeName;
            if (InspectorInputs.ScriptType("Script Type", ref requestedScriptTypeName, validationSnapshot.RegisteredScriptTypes))
            {
                ScriptTypeValidationResult requestedTypeValidation = ScriptComponentValidation.ValidateTypeName(requestedScriptTypeName, validationSnapshot);

                if (validationSnapshot.HasCompileErrors)
                {
                    _scriptAssignmentErrors[entityId] = "Assignment blocked: fix script compile errors first.";
                }
                else if (!requestedTypeValidation.IsValid)
                {
                    _scriptAssignmentErrors[entityId] = "Assignment blocked: " + requestedTypeValidation.Message;
                }
                else
                {
                    EditorBridge.SetScriptTypeName(entityId, requestedTypeValidation.NormalizedTypeName);
                    currentScriptTypeName = requestedTypeValidation.NormalizedTypeName;
                    _scriptAssignmentErrors.Remove(entityId);
                }
            }

            ScriptTypeValidationResult currentTypeValidation = ScriptComponentValidation.ValidateTypeName(currentScriptTypeName, validationSnapshot);
            if (currentTypeValidation.IsValid)
            {
                ImGui.Text("Script status: OK");
            }
            else
            {
                ImGui.Text("Script error: " + currentTypeValidation.Message);
            }

            if (_scriptAssignmentErrors.TryGetValue(entityId, out string assignmentError) && !string.IsNullOrEmpty(assignmentError))
                ImGui.Text(assignmentError);

            if (validationSnapshot.IsCompiling)
            {
                ImGui.Text("Compile: checking script project...");
            }
            else
            {
                ImGui.Text("Compile: " + validationSnapshot.CompileSummary);
            }

            if (validationSnapshot.HasCompileErrors)
            {
                for (int i = 0; i < validationSnapshot.CompileErrorLines.Length; ++i)
                    ImGui.Text(validationSnapshot.CompileErrorLines[i]);
            }
            else if (validationSnapshot.RegisteredScriptTypes.Length == 0)
            {
                ImGui.Text("No registered C# script classes found in the loaded script assembly.");
            }

            bool scriptEnabled = EditorBridge.GetScriptEnabled(entityId);
            ImGui.Text("Enabled: " + (scriptEnabled ? "yes" : "no"));

            bool enabledValue = scriptEnabled;
            if (InspectorInputs.Bool("Script Enabled", ref enabledValue) && enabledValue != scriptEnabled)
                EditorBridge.SetScriptEnabled(entityId, enabledValue);

            if (currentTypeValidation.IsValid)
                ScriptFieldInspector.DrawScriptFields(entityId, currentTypeValidation.NormalizedTypeName, validationSnapshot);

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

            ImGui.SameLine();
            bool snap = _gizmoSnapEnabled;
            if (ImGui.Checkbox("Snap", ref snap))
                _gizmoSnapEnabled = snap;

            ImGui.SameLine();
            ImGui.SetNextItemWidth(90.0f);
            float step = _gizmoSnapStep;
            if (ImGui.InputFloat("Step", ref step, 1.0f))
                _gizmoSnapStep = Clamp(step, 1.0f, 1024.0f);

            ImGui.SameLine();
            if (ImGui.Button("Focus"))
                _gameViewFocusedByClick = true;
        }

        private static bool IsPointInsideRect(float px, float py, float x, float y, float width, float height)
        {
            return px >= x && py >= y && px <= (x + width) && py <= (y + height);
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

                int entityCount = EditorBridge.GetEntityCount();
                for (int i = 0; i < entityCount; ++i)
                {
                    uint entityId = EditorBridge.GetEntityIdAtIndex(i);
                    if (!EditorBridge.HasTransform(entityId))
                        continue;

                    float x;
                    float y;
                    float width;
                    float height;
                    if (!EditorBridge.GetTransform(entityId, out x, out y, out width, out height))
                        continue;

                    if (!IsPointInsideRect(mouseWorldX, mouseWorldY, x, y, width, height))
                        continue;

                    float area = Math.Abs(width * height);
                    if (area < bestArea)
                    {
                        bestArea = area;
                        hitEntityId = (int)entityId;
                    }
                }

                _selectedEntityId = hitEntityId;
                if (_selectedEntityId >= 0)
                {
                    uint selectedEntity = (uint)_selectedEntityId;
                    float tx;
                    float ty;
                    float tw;
                    float th;
                    if (EditorBridge.GetTransform(selectedEntity, out tx, out ty, out tw, out th))
                    {
                        _isDraggingEntity = true;
                        _dragEntityId = _selectedEntityId;
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
                    if (EditorBridge.IsEntityValid(dragEntity) && EditorBridge.HasTransform(dragEntity))
                    {
                        float x;
                        float y;
                        float width;
                        float height;
                        if (EditorBridge.GetTransform(dragEntity, out x, out y, out width, out height))
                        {
                            float targetX = mouseWorldX - _dragOffsetWorldX;
                            float targetY = mouseWorldY - _dragOffsetWorldY;
                            EditorBridge.SetTransform(dragEntity, targetX, targetY, width, height);
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

            int entityCount = EditorBridge.GetEntityCount();
            for (int i = 0; i < entityCount; ++i)
            {
                uint entityId = EditorBridge.GetEntityIdAtIndex(i);
                if (!EditorBridge.HasTransform(entityId))
                    continue;

                float x;
                float y;
                float width;
                float height;
                if (!EditorBridge.GetTransform(entityId, out x, out y, out width, out height))
                    continue;

                float sx = _editorCamera.WorldToScreenX(x, centerX);
                float syBottom = _editorCamera.WorldToScreenY(y, centerY);
                float sw = Math.Max(1.0f, width * _editorCamera.Zoom);
                float sh = Math.Max(1.0f, height * _editorCamera.Zoom);

                float drawX = _gameViewPosX + sx;
                float drawY = _gameViewPosY + (_gameViewHeight - (syBottom + sh));

                bool selected = (_selectedEntityId >= 0) && ((uint)_selectedEntityId == entityId);
                float r = selected ? 1.0f : 0.75f;
                float g = selected ? 0.85f : 0.75f;
                float b = selected ? 0.2f : 1.0f;
                float a = selected ? 1.0f : 0.8f;

                ImGui.DrawRect(drawX, drawY, sw, sh, r, g, b, a, selected ? 2.0f : 1.0f);

                if (selected && _gameViewFocusedByClick)
                {
                    float gizmoX = x;
                    float gizmoY = y;
                    bool gizmoChanged = ImGuizmo.Manipulate2DTranslate(_gameViewPosX,
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
                    if (gizmoChanged)
                    {
                        if (_gizmoSnapEnabled)
                        {
                            gizmoX = SnapValue(gizmoX, _gizmoSnapStep);
                            gizmoY = SnapValue(gizmoY, _gizmoSnapStep);
                        }

                        EditorBridge.SetTransform(entityId, gizmoX, gizmoY, width, height);
                        x = gizmoX;
                        y = gizmoY;
                        sx = _editorCamera.WorldToScreenX(x, centerX);
                        syBottom = _editorCamera.WorldToScreenY(y, centerY);
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
