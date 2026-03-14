using System;
using System.Collections.Generic;
using System.IO;

using Engine;

namespace EngineEditor
{
    internal static class InspectorSystem
    {
        private static bool _componentRegistryInitialized;
        private static readonly Dictionary<uint, string> _scriptAssignmentErrors = new Dictionary<uint, string>();
        private static readonly Dictionary<string, bool> _componentFoldoutStates = new Dictionary<string, bool>();
        private static readonly Dictionary<uint, List<int>> _componentOrderByEntity = new Dictionary<uint, List<int>>();
        private static int _dragCandidateComponentType = -1;
        private static int _draggingComponentType = -1;
        private static int _hoveredDropComponentType = -1;
        private static uint _dragSourceEntityId;
        private static float _dragStartMouseX;
        private static float _dragStartMouseY;

        private static readonly string[] _defaultTags = new string[]
        {
            "Untagged",
            "Player",
            "Enemy",
            "Environment",
            "Collectible",
            "UI"
        };

        private static readonly string[] _layerOptions = BuildLayerOptions();

        private const int UiCanvasRenderModeScreenSpaceOverlay = 0;
        private const int UiCanvasRenderModeScreenSpaceCamera = 1;
        private const int UiCanvasRenderModeWorldSpace = 2;

        private const uint UiCanvasAdditionalShaderChannelTexCoord1 = 1u << 0;
        private const uint UiCanvasAdditionalShaderChannelTexCoord2 = 1u << 1;
        private const uint UiCanvasAdditionalShaderChannelTexCoord3 = 1u << 2;
        private const uint UiCanvasAdditionalShaderChannelNormal = 1u << 3;
        private const uint UiCanvasAdditionalShaderChannelTangent = 1u << 4;

        private static readonly string[] _uiCanvasRenderModeOptions = new[]
        {
            "Screen Space - Overlay",
            "Screen Space - Camera",
            "World Space",
        };

        private static int SelectedEntityId
        {
            get => EditorContext.SelectedEntityId;
            set => EditorContext.SelectedEntityId = value;
        }



        private enum ComponentCardAction
        {
            None,
            MoveUp,
            MoveDown,
            Removed
        }

        public static void ResetState()
        {
            ResetTransientState();
        }

        public static void ResetTransientState()
        {
            _scriptAssignmentErrors.Clear();
            _componentFoldoutStates.Clear();
            _componentOrderByEntity.Clear();
            ResetComponentCardDragState();
        }

        public static void DrawInspectorPanel()
        {
            EnsureInitialized();

            if (ImGui.Begin("Inspector"))
            {
                if (SelectedEntityId < 0)
                {
                    ImGui.Text("No entity selected.");
                }
                else
                {
                    uint entityId = (uint)SelectedEntityId;
                    bool valid = EntityManager.IsEntityValid(entityId);
                    if (!valid)
                    {
                        SelectedEntityId = -1;
                        ImGui.Text("Selected entity is no longer valid.");
                    }
                    else
                    {
                        if (!DrawEntityHeaderInspector(entityId))
                        {
                            ImGui.End();
                            return;
                        }

                        ImGui.Separator();
                        DrawAddComponentMenu(entityId);

                        float dropZoneX = ImGui.GetCursorScreenPosX();
                        float dropZoneY = ImGui.GetCursorScreenPosY();
                        float dropZoneWidth = ImGui.GetContentRegionAvailX();
                        float dropZoneHeight = ImGui.GetContentRegionAvailY();

                        List<ComponentRegistryEntry> orderedEntries = GetOrderedComponentEntries(entityId);
                        if (_draggingComponentType >= 0 && _dragSourceEntityId == entityId)
                            _hoveredDropComponentType = -1;

                        for (int i = 0; i < orderedEntries.Count; ++i)
                        {
                            ComponentRegistryEntry entry = orderedEntries[i];
                            bool canMoveUp = entry.ComponentType != ComponentType.Transform
                                             && i > 0
                                             && orderedEntries[i - 1].ComponentType != ComponentType.Transform;
                            bool canMoveDown = entry.ComponentType != ComponentType.Transform
                                               && i + 1 < orderedEntries.Count;
                            ComponentCardAction action = DrawComponentCard(entityId,
                                                                           entry,
                                                                           canMoveUp,
                                                                           canMoveDown);
                            if (action == ComponentCardAction.Removed)
                            {
                                RemoveComponentFromOrder(entityId, entry.ComponentType);
                                break;
                            }

                            if (action == ComponentCardAction.MoveUp)
                            {
                                MoveComponentInOrder(entityId, entry.ComponentType, -1);
                                break;
                            }

                            if (action == ComponentCardAction.MoveDown)
                            {
                                MoveComponentInOrder(entityId, entry.ComponentType, 1);
                                break;
                            }
                        }

                        DrawInspectorAssetDropOverlay(entityId, dropZoneX, dropZoneY, dropZoneWidth, dropZoneHeight);
                        HandleComponentReorderDragState(entityId);
                        HandleAssetDropOnInspector(entityId);
                    }
                }
            }

            ImGui.End();
        }

        private static void EnsureInitialized()
        {
            if (_componentRegistryInitialized)
                return;

            _componentRegistryInitialized = true;
            ComponentRegistry.EnsureInitialized();
        }

        private static bool DrawEntityHeaderInspector(uint entityId)
        {
            ImGui.Text("Entity " + entityId);
            ImGui.SameLine();

            if (ImGui.Button(IconsFA.TRASH + " Delete Entity"))
            {
                if (HierarchyWindow.Instance != null)
                    HierarchyWindow.Instance.DeleteEntity(entityId);
                else
                    EntityManager.DestroyEntity(entityId);
                SelectedEntityId = -1;
                return false;
            }

            string entityName = EntityManager.GetEntityName(entityId);
            if (string.IsNullOrWhiteSpace(entityName))
                entityName = "Entity " + entityId;

            if (InspectorInputs.String("Name", ref entityName))
                EntityManager.SetEntityName(entityId, entityName);

            string tag = EditorBridge.GetEntityTag(entityId);
            if (string.IsNullOrWhiteSpace(tag))
                tag = "Untagged";

            if (InspectorInputs.SelectString("Tag", ref tag, BuildTagOptions(entityId)))
                EditorBridge.SetEntityTag(entityId, tag);

            uint layer = EditorBridge.GetEntityLayer(entityId);
            string layerValue = LayerToLabel(layer);
            if (InspectorInputs.SelectString("Layer", ref layerValue, _layerOptions))
            {
                if (TryParseLayerLabel(layerValue, out uint parsedLayer))
                    EditorBridge.SetEntityLayer(entityId, parsedLayer);
            }

            bool isStatic = EditorBridge.GetEntityStatic(entityId);
            if (InspectorInputs.Bool("Static", ref isStatic))
                EditorBridge.SetEntityStatic(entityId, isStatic);

            bool active = EntityManager.GetEntityActive(entityId);
            if (InspectorInputs.Bool("Active", ref active))
                EntityManager.SetEntityActive(entityId, active);

            return true;
        }

        private static ComponentCardAction DrawComponentCard(uint entityId,
                                     ComponentRegistryEntry entry,
                                     bool canMoveUp,
                                     bool canMoveDown)
        {
            string foldoutKey = entityId + ":" + entry.ComponentType;
            bool expanded;
            if (!_componentFoldoutStates.TryGetValue(foldoutKey, out expanded))
            {
                expanded = true;
                _componentFoldoutStates[foldoutKey] = true;
            }

            ImGui.Separator();
            string foldoutLabel = EditorUIHelpers.BuildFoldoutButtonLabel(entry.Name, expanded, "ComponentFoldout" + foldoutKey);
            if (ImGui.Button(foldoutLabel))
            {
                expanded = !expanded;
                _componentFoldoutStates[foldoutKey] = expanded;
            }

            RegisterComponentCardDrag(entityId, entry.ComponentType);
            if (_draggingComponentType >= 0
                && _dragSourceEntityId == entityId
                && ImGui.IsItemHovered())
            {
                _hoveredDropComponentType = entry.ComponentType;
            }

            if (_draggingComponentType == entry.ComponentType && _dragSourceEntityId == entityId)
            {
                ImGui.SameLine();
                ImGui.Text("[dragging]");
            }

            if (canMoveUp)
            {
                ImGui.SameLine();
                if (ImGui.Button("Up##" + foldoutKey))
                    return ComponentCardAction.MoveUp;
            }

            if (canMoveDown)
            {
                ImGui.SameLine();
                if (ImGui.Button("Down##" + foldoutKey))
                    return ComponentCardAction.MoveDown;
            }

            if (TryGetComponentEnabled(entityId, entry.ComponentType, out bool enabled))
            {
                ImGui.SameLine();
                bool enabledValue = enabled;
                if (ImGui.Checkbox("Enabled##" + foldoutKey, ref enabledValue) && enabledValue != enabled)
                    SetComponentEnabled(entityId, entry.ComponentType, enabledValue);
            }

            if (entry.CanRemove)
            {
                ImGui.SameLine();
                if (ImGui.Button("Remove##" + foldoutKey))
                {
                    EntityManager.RemoveComponent(entityId, entry.ComponentType);
                    _componentFoldoutStates.Remove(foldoutKey);
                    return ComponentCardAction.Removed;
                }
            }

            if (expanded)
                entry.DrawInspector(entityId);

            return ComponentCardAction.None;
        }

        private static List<ComponentRegistryEntry> GetOrderedComponentEntries(uint entityId)
        {
            var attachedEntriesByType = new Dictionary<int, ComponentRegistryEntry>();
            var attachedTypes = new List<int>();

            var allEntries = ComponentRegistry.Entries;
            for (int i = 0; i < allEntries.Count; ++i)
            {
                ComponentRegistryEntry entry = allEntries[i];
                if (!EntityManager.HasComponent(entityId, entry.ComponentType))
                    continue;

                attachedEntriesByType[entry.ComponentType] = entry;
                attachedTypes.Add(entry.ComponentType);
            }

            if (!_componentOrderByEntity.TryGetValue(entityId, out List<int> order))
            {
                order = new List<int>();
                _componentOrderByEntity[entityId] = order;
            }

            for (int i = order.Count - 1; i >= 0; --i)
            {
                if (!attachedTypes.Contains(order[i]))
                    order.RemoveAt(i);
            }

            for (int i = 0; i < attachedTypes.Count; ++i)
            {
                int attachedType = attachedTypes[i];
                if (!order.Contains(attachedType))
                    order.Add(attachedType);
            }

            var orderedEntries = new List<ComponentRegistryEntry>();
            for (int i = 0; i < order.Count; ++i)
            {
                int orderedType = order[i];
                if (attachedEntriesByType.TryGetValue(orderedType, out ComponentRegistryEntry entry))
                    orderedEntries.Add(entry);
            }

            return orderedEntries;
        }

        private static void RemoveComponentFromOrder(uint entityId, int componentType)
        {
            if (_componentOrderByEntity.TryGetValue(entityId, out List<int> order))
                order.Remove(componentType);
        }

        private static void MoveComponentInOrder(uint entityId, int componentType, int delta)
        {
            if (delta == 0)
                return;

            if (!_componentOrderByEntity.TryGetValue(entityId, out List<int> order))
                return;

            int index = order.IndexOf(componentType);
            if (index < 0)
                return;

            int targetIndex = index + delta;
            if (targetIndex < 0 || targetIndex >= order.Count)
                return;

            if (order[targetIndex] == ComponentType.Transform)
                return;

            int current = order[index];
            order[index] = order[targetIndex];
            order[targetIndex] = current;
        }

        private static void RegisterComponentCardDrag(uint entityId, int componentType)
        {
            if (componentType == ComponentType.Transform)
                return;

            if (!ImGui.IsItemHovered() || !ImGui.IsMouseClicked(MouseButton.Left))
                return;

            _dragSourceEntityId = entityId;
            _dragCandidateComponentType = componentType;
            _dragStartMouseX = ImGui.GetMousePosX();
            _dragStartMouseY = ImGui.GetMousePosY();
        }

        private static void HandleComponentReorderDragState(uint entityId)
        {
            if (_dragSourceEntityId != 0 && _dragSourceEntityId != entityId)
            {
                ResetComponentCardDragState();
                return;
            }

            if (_dragCandidateComponentType >= 0
                && _draggingComponentType < 0
                && ImGui.IsMouseDown(MouseButton.Left))
            {
                float dx = ImGui.GetMousePosX() - _dragStartMouseX;
                float dy = ImGui.GetMousePosY() - _dragStartMouseY;
                float dragDistanceSq = dx * dx + dy * dy;
                if (dragDistanceSq >= 25.0f)
                    _draggingComponentType = _dragCandidateComponentType;
            }

            if (ImGui.IsMouseDown(MouseButton.Left))
                return;

            if (_draggingComponentType >= 0
                && _hoveredDropComponentType >= 0
                && _hoveredDropComponentType != _draggingComponentType)
            {
                MoveComponentToTarget(entityId, _draggingComponentType, _hoveredDropComponentType);
            }

            ResetComponentCardDragState();
        }

        private static void MoveComponentToTarget(uint entityId, int componentType, int targetComponentType)
        {
            if (!_componentOrderByEntity.TryGetValue(entityId, out List<int> order))
                return;

            int sourceIndex = order.IndexOf(componentType);
            int targetIndex = order.IndexOf(targetComponentType);
            if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
                return;

            if (order[sourceIndex] == ComponentType.Transform)
                return;

            int minIndex = 0;
            if (order.Count > 0 && order[0] == ComponentType.Transform)
                minIndex = 1;

            if (targetIndex < minIndex)
                targetIndex = minIndex;

            order.RemoveAt(sourceIndex);
            if (targetIndex > sourceIndex)
                targetIndex -= 1;

            if (targetIndex < minIndex)
                targetIndex = minIndex;
            if (targetIndex > order.Count)
                targetIndex = order.Count;

            order.Insert(targetIndex, componentType);
        }

        private static void ResetComponentCardDragState()
        {
            _dragSourceEntityId = 0;
            _dragCandidateComponentType = -1;
            _draggingComponentType = -1;
            _hoveredDropComponentType = -1;
            _dragStartMouseX = 0.0f;
            _dragStartMouseY = 0.0f;
        }

        private static void HandleAssetDropOnInspector(uint entityId)
        {
            if (!ImGui.IsWindowHovered() || !EditorContext.AssetDragActive)
                return;

            if (!Input.GetMouseButtonUp(MouseButton.Left))
                return;

            string droppedPath = EditorContext.DraggedAssetPath;
            EditorContext.ClearDraggedAsset();

            if (string.IsNullOrWhiteSpace(droppedPath) || !File.Exists(droppedPath))
                return;

            if (!TryApplyDroppedAssetToEntity(entityId, droppedPath))
                ProjectOperations.SetStatusMessage("Inspector drop not supported for: " + Path.GetFileName(droppedPath));
        }

        private static void DrawInspectorAssetDropOverlay(uint entityId,
                                                          float x,
                                                          float y,
                                                          float width,
                                                          float height)
        {
            if (!EditorContext.AssetDragActive || !ImGui.IsWindowHovered())
                return;

            if (width < 1.0f || height < 1.0f)
                return;

            string droppedPath = EditorContext.DraggedAssetPath;
            bool supported = IsDroppedAssetSupportedForInspector(entityId, droppedPath);

            float r = supported ? 0.20f : 0.95f;
            float g = supported ? 0.85f : 0.35f;
            float b = supported ? 0.35f : 0.25f;
            ImGui.DrawRect(x, y, width, height, r, g, b, 0.95f, 2.0f);

            string fileName = Path.GetFileName(droppedPath);
            if (string.IsNullOrEmpty(fileName))
                fileName = "<asset>";

            if (supported)
                ImGui.SetTooltip("Drop to Inspector: " + fileName);
            else
                ImGui.SetTooltip("Unsupported in Inspector drop: " + fileName);
        }

        private static bool IsDroppedAssetSupportedForInspector(uint entityId, string path)
        {
            if (!EntityManager.IsEntityValid(entityId))
                return false;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            return IsImageAssetPath(path)
                   || IsAnimationAssetPath(path)
                   || IsScriptAssetPath(path);
        }

        private static bool TryApplyDroppedAssetToEntity(uint entityId, string droppedPath)
        {
            string normalizedPath = NormalizeAssetPathForSerialization(droppedPath);

            if (IsImageAssetPath(droppedPath))
            {
                EnsureComponent(entityId, ComponentType.Sprite);
                EditorBridge.SetSpriteTexturePath(entityId, normalizedPath);
                ProjectOperations.SetStatusMessage("Assigned sprite texture from drop: " + Path.GetFileName(droppedPath));
                return true;
            }

            if (IsAnimationAssetPath(droppedPath))
            {
                EnsureComponent(entityId, ComponentType.Animator);
                EntityManager.SetAnimatorClipPath(entityId, normalizedPath);
                ProjectOperations.SetStatusMessage("Assigned animator clip from drop: " + Path.GetFileName(droppedPath));
                return true;
            }

            if (IsScriptAssetPath(droppedPath))
            {
                EnsureComponent(entityId, ComponentType.Script);
                string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(droppedPath);
                string resolvedType = TryResolveScriptTypeFromFileName(fileNameWithoutExtension);
                if (!string.IsNullOrEmpty(resolvedType))
                {
                    EditorBridge.SetScriptTypeName(entityId, resolvedType);
                    ProjectOperations.SetStatusMessage("Assigned script from drop: " + resolvedType);
                }
                else
                {
                    ProjectOperations.SetStatusMessage("Script dropped, but type not found in loaded assembly: " + fileNameWithoutExtension);
                }

                return true;
            }

            return false;
        }

        private static void EnsureComponent(uint entityId, int componentType)
        {
            if (!EntityManager.HasComponent(entityId, componentType))
                EntityManager.AddComponent(entityId, componentType);
        }

        private static bool IsImageAssetPath(string path)
        {
            string lower = (path ?? string.Empty).ToLowerInvariant();
            return lower.EndsWith(".png")
                   || lower.EndsWith(".jpg")
                   || lower.EndsWith(".jpeg")
                   || lower.EndsWith(".bmp")
                   || lower.EndsWith(".tga")
                   || lower.EndsWith(".dds");
        }

        private static bool IsAnimationAssetPath(string path)
        {
            return (path ?? string.Empty).EndsWith(".anim", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsScriptAssetPath(string path)
        {
            return (path ?? string.Empty).EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeAssetPathForSerialization(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
                return string.Empty;

            string normalizedPath = absolutePath;
            string projectRoot = ProjectOperations.ActiveProjectPath;
            if (!string.IsNullOrWhiteSpace(projectRoot) && Directory.Exists(projectRoot))
            {
                string relative = StringUtilities.MakeRelativePath(projectRoot, absolutePath);
                if (!relative.StartsWith("..", StringComparison.Ordinal))
                    normalizedPath = relative;
            }

            return normalizedPath.Replace('\\', '/');
        }

        private static string TryResolveScriptTypeFromFileName(string fileNameWithoutExtension)
        {
            if (string.IsNullOrWhiteSpace(fileNameWithoutExtension))
                return string.Empty;

            ScriptValidationSnapshot snapshot = ScriptComponentValidation.GetSnapshot();
            string[] types = snapshot.RegisteredScriptTypes;
            if (types == null || types.Length == 0)
                return string.Empty;

            string bestSimpleMatch = string.Empty;
            for (int i = 0; i < types.Length; ++i)
            {
                string typeName = types[i] ?? string.Empty;
                if (typeName.Length == 0)
                    continue;

                if (string.Equals(typeName, fileNameWithoutExtension, StringComparison.OrdinalIgnoreCase))
                    return typeName;

                int lastDotIndex = typeName.LastIndexOf('.');
                string simpleName = lastDotIndex >= 0 ? typeName.Substring(lastDotIndex + 1) : typeName;
                if (string.Equals(simpleName, fileNameWithoutExtension, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(bestSimpleMatch))
                        return string.Empty;

                    bestSimpleMatch = typeName;
                }
            }

            return bestSimpleMatch;
        }

        private static bool TryGetComponentEnabled(uint entityId, int componentType, out bool enabled)
        {
            enabled = false;

            if (componentType == ComponentType.Camera)
                return TryGetCameraEnabled(entityId, out enabled);

            if (componentType == ComponentType.Sprite)
            {
                enabled = EditorBridge.GetSpriteEnabled(entityId);
                return true;
            }

            if (componentType == ComponentType.Script)
            {
                enabled = EditorBridge.GetScriptEnabled(entityId);
                return true;
            }

            if (componentType == ComponentType.Animator)
            {
                enabled = EditorBridge.GetAnimatorEnabled(entityId);
                return true;
            }

            if (componentType == ComponentType.UiCanvas)
            {
                int sortingOrder;
                bool pixelPerfect;
                return EntityManager.GetUiCanvasSettings(entityId, out enabled, out sortingOrder, out pixelPerfect);
            }

            if (componentType == ComponentType.UiImage)
            {
                ulong textureAssetHandle;
                uint color;
                bool preserveAspect;
                float cornerRadius;
                return EntityManager.GetUiImageSettings(entityId,
                                                        out enabled,
                                                        out textureAssetHandle,
                                                        out color,
                                                        out preserveAspect,
                                                        out cornerRadius);
            }

            if (componentType == ComponentType.UiText)
            {
                float fontSize;
                uint color;
                int horizontalAlign;
                bool wrap;
                return EntityManager.GetUiTextSettings(entityId,
                                                       out enabled,
                                                       out fontSize,
                                                       out color,
                                                       out horizontalAlign,
                                                       out wrap);
            }

            if (componentType == ComponentType.UiButton)
            {
                bool interactable;
                uint normalColor;
                uint highlightedColor;
                uint pressedColor;
                uint disabledColor;
                return EntityManager.GetUiButtonSettings(entityId,
                                                         out enabled,
                                                         out interactable,
                                                         out normalColor,
                                                         out highlightedColor,
                                                         out pressedColor,
                                                         out disabledColor);
            }

            if (componentType == ComponentType.UiInputField)
            {
                bool interactable;
                uint textColor;
                uint placeholderColor;
                uint maxLength;
                return EntityManager.GetUiInputFieldSettings(entityId,
                                                             out enabled,
                                                             out interactable,
                                                             out textColor,
                                                             out placeholderColor,
                                                             out maxLength);
            }

            return false;
        }

        private static void SetComponentEnabled(uint entityId, int componentType, bool enabled)
        {
            if (componentType == ComponentType.Camera)
            {
                SetCameraEnabled(entityId, enabled);
                return;
            }

            if (componentType == ComponentType.Sprite)
            {
                EditorBridge.SetSpriteEnabled(entityId, enabled);
                return;
            }

            if (componentType == ComponentType.Script)
            {
                EditorBridge.SetScriptEnabled(entityId, enabled);
                return;
            }

            if (componentType == ComponentType.Animator)
            {
                EditorBridge.SetAnimatorEnabled(entityId, enabled);
                return;
            }

            if (componentType == ComponentType.UiCanvas)
            {
                int sortingOrder;
                bool pixelPerfect;
                bool currentEnabled;
                if (EntityManager.GetUiCanvasSettings(entityId, out currentEnabled, out sortingOrder, out pixelPerfect))
                    EntityManager.SetUiCanvasSettings(entityId, enabled, sortingOrder, pixelPerfect);
                return;
            }

            if (componentType == ComponentType.UiImage)
            {
                ulong textureAssetHandle;
                uint color;
                bool preserveAspect;
                float cornerRadius;
                bool currentEnabled;
                if (EntityManager.GetUiImageSettings(entityId,
                                                     out currentEnabled,
                                                     out textureAssetHandle,
                                                     out color,
                                                     out preserveAspect,
                                                     out cornerRadius))
                {
                    EntityManager.SetUiImageSettings(entityId,
                                                     enabled,
                                                     textureAssetHandle,
                                                     color,
                                                     preserveAspect,
                                                     cornerRadius);
                }

                return;
            }

            if (componentType == ComponentType.UiText)
            {
                float fontSize;
                uint color;
                int horizontalAlign;
                bool wrap;
                bool currentEnabled;
                if (EntityManager.GetUiTextSettings(entityId,
                                                    out currentEnabled,
                                                    out fontSize,
                                                    out color,
                                                    out horizontalAlign,
                                                    out wrap))
                {
                    EntityManager.SetUiTextSettings(entityId,
                                                    enabled,
                                                    fontSize,
                                                    color,
                                                    horizontalAlign,
                                                    wrap);
                }

                return;
            }

            if (componentType == ComponentType.UiButton)
            {
                bool interactable;
                uint normalColor;
                uint highlightedColor;
                uint pressedColor;
                uint disabledColor;
                bool currentEnabled;
                if (EntityManager.GetUiButtonSettings(entityId,
                                                      out currentEnabled,
                                                      out interactable,
                                                      out normalColor,
                                                      out highlightedColor,
                                                      out pressedColor,
                                                      out disabledColor))
                {
                    EntityManager.SetUiButtonSettings(entityId,
                                                      enabled,
                                                      interactable,
                                                      normalColor,
                                                      highlightedColor,
                                                      pressedColor,
                                                      disabledColor);
                }

                return;
            }

            if (componentType == ComponentType.UiInputField)
            {
                bool interactable;
                uint textColor;
                uint placeholderColor;
                uint maxLength;
                bool currentEnabled;
                if (EntityManager.GetUiInputFieldSettings(entityId,
                                                          out currentEnabled,
                                                          out interactable,
                                                          out textColor,
                                                          out placeholderColor,
                                                          out maxLength))
                {
                    EntityManager.SetUiInputFieldSettings(entityId,
                                                          enabled,
                                                          interactable,
                                                          textColor,
                                                          placeholderColor,
                                                          maxLength);
                }
            }
        }

        private static bool TryGetCameraEnabled(uint entityId, out bool enabled)
        {
            enabled = false;

            float camX;
            float camY;
            float camZoom;
            bool primary;
            bool clearColor;
            uint backgroundColor;
            uint cullingMask;
            float viewportX;
            float viewportY;
            float viewportWidth;
            float viewportHeight;
            float orthographicSize;

            return EntityManager.GetCameraSettingsV2(entityId,
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
                                                     out orthographicSize);
        }

        private static void SetCameraEnabled(uint entityId, bool enabled)
        {
            float camX;
            float camY;
            float camZoom;
            bool currentEnabled;
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
                                                   out currentEnabled,
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
                return;
            }

            if (currentEnabled == enabled)
                return;

            EntityManager.SetCameraSettingsV2(entityId,
                                              camX,
                                              camY,
                                              camZoom,
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

        private static string[] BuildTagOptions(uint selectedEntityId)
        {
            var tags = new List<string>();

            for (int i = 0; i < _defaultTags.Length; ++i)
            {
                string tag = _defaultTags[i];
                if (!string.IsNullOrWhiteSpace(tag) && !tags.Contains(tag))
                    tags.Add(tag);
            }

            int entityCount = EntityManager.GetEntityCount();
            for (int i = 0; i < entityCount; ++i)
            {
                uint entityId = EntityManager.GetEntityIdAtIndex(i);
                if (entityId == 0 || !EntityManager.IsEntityValid(entityId))
                    continue;

                string tag = EditorBridge.GetEntityTag(entityId);
                if (string.IsNullOrWhiteSpace(tag))
                    continue;

                if (!tags.Contains(tag))
                    tags.Add(tag);
            }

            string selectedTag = EditorBridge.GetEntityTag(selectedEntityId);
            if (!string.IsNullOrWhiteSpace(selectedTag) && !tags.Contains(selectedTag))
                tags.Add(selectedTag);

            if (tags.Count == 0)
                tags.Add("Untagged");

            return tags.ToArray();
        }

        private static string[] BuildLayerOptions()
        {
            var options = new string[32];
            options[0] = "Layer 0 (Default)";
            for (int i = 1; i < options.Length; ++i)
                options[i] = "Layer " + i;

            return options;
        }

        private static string LayerToLabel(uint layer)
        {
            uint clamped = layer > 31 ? 31u : layer;
            return _layerOptions[(int)clamped];
        }

        private static bool TryParseLayerLabel(string label, out uint layer)
        {
            layer = 0;
            if (string.IsNullOrWhiteSpace(label))
                return false;

            string trimmed = label.Trim();
            if (!trimmed.StartsWith("Layer ", StringComparison.OrdinalIgnoreCase))
                return false;

            int startIndex = "Layer ".Length;
            int endIndex = trimmed.IndexOf(' ', startIndex);
            string numberText = endIndex < 0
                ? trimmed.Substring(startIndex)
                : trimmed.Substring(startIndex, endIndex - startIndex);

            if (!uint.TryParse(numberText, out uint parsed))
                return false;

            if (parsed > 31)
                parsed = 31;

            layer = parsed;
            return true;
        }

        private static string UiCanvasRenderModeToLabel(int renderMode)
        {
            if (renderMode == UiCanvasRenderModeScreenSpaceCamera)
                return _uiCanvasRenderModeOptions[1];

            if (renderMode == UiCanvasRenderModeWorldSpace)
                return _uiCanvasRenderModeOptions[2];

            return _uiCanvasRenderModeOptions[0];
        }

        private static int UiCanvasLabelToRenderMode(string label, int fallback)
        {
            if (string.Equals(label, _uiCanvasRenderModeOptions[1], StringComparison.Ordinal))
                return UiCanvasRenderModeScreenSpaceCamera;

            if (string.Equals(label, _uiCanvasRenderModeOptions[2], StringComparison.Ordinal))
                return UiCanvasRenderModeWorldSpace;

            if (string.Equals(label, _uiCanvasRenderModeOptions[0], StringComparison.Ordinal))
                return UiCanvasRenderModeScreenSpaceOverlay;

            return fallback;
        }

        private static string FormatUiCanvasShaderChannels(uint mask)
        {
            if (mask == 0u)
                return "None";

            var labels = new List<string>();
            if ((mask & UiCanvasAdditionalShaderChannelTexCoord1) != 0u)
                labels.Add("TexCoord1");
            if ((mask & UiCanvasAdditionalShaderChannelTexCoord2) != 0u)
                labels.Add("TexCoord2");
            if ((mask & UiCanvasAdditionalShaderChannelTexCoord3) != 0u)
                labels.Add("TexCoord3");
            if ((mask & UiCanvasAdditionalShaderChannelNormal) != 0u)
                labels.Add("Normal");
            if ((mask & UiCanvasAdditionalShaderChannelTangent) != 0u)
                labels.Add("Tangent");

            return labels.Count == 0 ? "None" : string.Join(", ", labels.ToArray());
        }

        private static string FormatLayerMaskSummary(uint mask)
        {
            if (mask == 0u)
                return "Nothing";

            if (mask == 0xFFFFFFFFu)
                return "Everything";

            int selectedCount = 0;
            for (int i = 0; i < 32; ++i)
            {
                if ((mask & (1u << i)) != 0u)
                    ++selectedCount;
            }

            return selectedCount + " layer(s)";
        }

        private static bool DrawLayerMaskPopup(string label, string popupId, ref uint mask)
        {
            bool changed = false;

            ImGui.Text(label + ": " + FormatLayerMaskSummary(mask));
            ImGui.SameLine();
            if (ImGui.Button("Edit##" + popupId))
                ImGui.OpenPopup(popupId);

            if (!ImGui.BeginPopup(popupId))
                return changed;

            if (ImGui.Button("Everything##" + popupId))
            {
                if (mask != 0xFFFFFFFFu)
                {
                    mask = 0xFFFFFFFFu;
                    changed = true;
                }
            }

            ImGui.SameLine();
            if (ImGui.Button("Nothing##" + popupId))
            {
                if (mask != 0u)
                {
                    mask = 0u;
                    changed = true;
                }
            }

            ImGui.Separator();
            for (int i = 0; i < 32; ++i)
            {
                uint bit = 1u << i;
                bool selected = (mask & bit) != 0u;
                if (ImGui.SelectableNoClose(_layerOptions[i] + "##" + popupId + "_" + i, selected))
                {
                    mask = selected ? (mask & ~bit) : (mask | bit);
                    changed = true;
                }
            }

            ImGui.Separator();
            if (ImGui.Button("Close##" + popupId))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
            return changed;
        }

        public static void RegisterBuiltInComponents()
        {
            ComponentRegistry.Register("Transform", "Core", ComponentType.Transform,
                                       DrawTransformInspector, false, false);

            ComponentRegistry.Register("Sprite", "Rendering", ComponentType.Sprite,
                                       DrawSpriteInspector, true);

            ComponentRegistry.Register("Camera", "Rendering", ComponentType.Camera,
                                       DrawCameraInspector, true);

            ComponentRegistry.Register("Script", "Logic", ComponentType.Script,
                                       DrawScriptInspector, true);

            ComponentRegistry.Register("Animator", "Animation", ComponentType.Animator,
                                       DrawAnimatorInspector, true);

            ComponentRegistry.Register("UI Canvas", "UI", ComponentType.UiCanvas,
                                       DrawUiCanvasInspector, true);

            ComponentRegistry.Register("UI RectTransform", "UI", ComponentType.UiRectTransform,
                                       DrawUiRectTransformInspector, true);

            ComponentRegistry.Register("UI Image", "UI", ComponentType.UiImage,
                                       DrawUiImageInspector, true);

            ComponentRegistry.Register("UI Text", "UI", ComponentType.UiText,
                                       DrawUiTextInspector, true);

            ComponentRegistry.Register("UI Button", "UI", ComponentType.UiButton,
                                       DrawUiButtonInspector, true);

            ComponentRegistry.Register("UI InputField", "UI", ComponentType.UiInputField,
                                       DrawUiInputFieldInspector, true);
        }

        private static void DrawAddComponentMenu(uint entityId)
        {
            if (ImGui.Button(IconsFA.CIRCLE_PLUS + " Add Component"))
                ImGui.OpenPopup("Add Component Popup");

            if (!ImGui.BeginPopupModal("Add Component Popup"))
                return;

            var allEntries = ComponentRegistry.Entries;
            bool hasAnyAddable = false;
            var categories = new List<string>();
            for (int i = 0; i < allEntries.Count; ++i)
            {
                ComponentRegistryEntry entry = allEntries[i];
                if (EntityManager.HasComponent(entityId, entry.ComponentType))
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

                    for (int i = 0; i < allEntries.Count; ++i)
                    {
                        ComponentRegistryEntry entry = allEntries[i];
                        if (entry.Category != category || EntityManager.HasComponent(entityId, entry.ComponentType))
                            continue;

                        if (ImGui.Selectable("  + " + entry.Name + "##AddComponent" + entry.Name, false))
                        {
                            EntityManager.AddComponent(entityId, entry.ComponentType);
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
            if (!EntityManager.GetTransform(entityId, out x, out y, out width, out height))
                return;

            float rotation = EntityManager.GetTransformRotation(entityId);

            bool changed = false;
            changed |= InspectorInputs.Vector2("Position", ref x, ref y, 1.0f);
            changed |= ImGui.InputFloat("Rotation", ref rotation, 1.0f);
            changed |= InspectorInputs.Vector2("Size", ref width, ref height, 1.0f);

            if (changed)
            {
                EntityManager.SetTransform(entityId, x, y, width, height);
                EntityManager.SetTransformRotation(entityId, rotation);
            }
        }

        private static void DrawSpriteInspector(uint entityId)
        {
            string texturePath = EditorBridge.GetSpriteTexturePath(entityId) ?? string.Empty;
            if (InspectorInputs.String("Texture", ref texturePath))
                EditorBridge.SetSpriteTexturePath(entityId, texturePath.Trim());

            ImGui.SameLine();
            if (ImGui.Button("Browse##SpriteTexture" + entityId))
            {
                string initialDirectory = ProjectOperations.ActiveProjectPath;
                if (string.IsNullOrWhiteSpace(initialDirectory) || !Directory.Exists(initialDirectory))
                    initialDirectory = Directory.GetCurrentDirectory();

                string pickedPath = Explorer.PickFile("Select sprite texture (png/jpg/bmp/tga)", initialDirectory);
                if (!string.IsNullOrWhiteSpace(pickedPath))
                {
                    string normalizedPath = pickedPath;
                    string projectRoot = ProjectOperations.ActiveProjectPath;
                    if (!string.IsNullOrWhiteSpace(projectRoot) && Directory.Exists(projectRoot))
                    {
                        string relativePath = StringUtilities.MakeRelativePath(projectRoot, pickedPath);
                        if (!relativePath.StartsWith("..", StringComparison.Ordinal))
                            normalizedPath = relativePath;
                    }

                    normalizedPath = normalizedPath.Replace('\\', '/');
                    EditorBridge.SetSpriteTexturePath(entityId, normalizedPath);
                }
            }

            ImGui.SameLine();
            if (ImGui.Button("Clear##SpriteTexture" + entityId))
                EditorBridge.SetSpriteTexturePath(entityId, string.Empty);

            uint fallbackColor = EditorBridge.GetSpriteFallbackColor(entityId);
            bool fallbackColorChanged = InspectorInputs.ColorRgba32("Fallback Color (RGBA 0-255)", ref fallbackColor);
            if (fallbackColorChanged)
                EditorBridge.SetSpriteFallbackColor(entityId, fallbackColor);

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
                ImGui.Text("Sprite data unavailable.");
                return;
            }

            bool settingsChanged = false;

            EditorUIHelpers.DrawSectionHeader("Offset");
            settingsChanged |= InspectorInputs.Bool("Centered", ref centered);
            settingsChanged |= InspectorInputs.Vector2("Offset", ref offsetX, ref offsetY, 0.1f);
            settingsChanged |= InspectorInputs.Bool("Flip H", ref flipH);
            settingsChanged |= InspectorInputs.Bool("Flip V", ref flipV);

            EditorUIHelpers.DrawSectionHeader("Animation");
            settingsChanged |= InspectorInputs.UInt("Hframes", ref hframes);
            settingsChanged |= InspectorInputs.UInt("Vframes", ref vframes);
            settingsChanged |= InspectorInputs.UInt("Frame", ref frame);

            if (hframes < 1)
                hframes = 1;
            if (vframes < 1)
                vframes = 1;

            uint frameX = frame % hframes;
            uint frameY = frame / hframes;
            bool frameCoordsChanged = false;
            frameCoordsChanged |= InspectorInputs.UInt("Frame X", ref frameX);
            frameCoordsChanged |= InspectorInputs.UInt("Frame Y", ref frameY);
            if (frameCoordsChanged)
            {
                ulong frameCount = (ulong)hframes * (ulong)vframes;
                if (frameCount == 0)
                    frameCount = 1;

                ulong resolvedFrame = (ulong)frameY * (ulong)hframes + (ulong)frameX;
                if (resolvedFrame >= frameCount)
                    resolvedFrame = frameCount - 1;

                frame = (uint)resolvedFrame;
                settingsChanged = true;
            }

            ulong maxFrames = (ulong)hframes * (ulong)vframes;
            if (maxFrames == 0)
                maxFrames = 1;
            if (frame >= maxFrames)
            {
                frame = (uint)(maxFrames - 1);
                settingsChanged = true;
            }

            EditorUIHelpers.DrawSectionHeader("Region");
            settingsChanged |= InspectorInputs.Bool("Region Enabled", ref regionEnabled);
            if (regionEnabled)
            {
                settingsChanged |= InspectorInputs.Vector2("Region Pos", ref regionX, ref regionY, 1.0f);
                settingsChanged |= InspectorInputs.Vector2("Region Size", ref regionWidth, ref regionHeight, 1.0f);
                if (regionWidth < 0.0f)
                {
                    regionWidth = 0.0f;
                    settingsChanged = true;
                }

                if (regionHeight < 0.0f)
                {
                    regionHeight = 0.0f;
                    settingsChanged = true;
                }
            }

            if (settingsChanged)
            {
                EditorBridge.SetSpriteSettings(entityId,
                                               centered,
                                               offsetX,
                                               offsetY,
                                               flipH,
                                               flipV,
                                               hframes,
                                               vframes,
                                               frame,
                                               regionEnabled,
                                               regionX,
                                               regionY,
                                               regionWidth,
                                               regionHeight);
            }
        }

        private static void DrawCameraInspector(uint entityId)
        {
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
                ImGui.Text("Camera data unavailable.");
                return;
            }

            bool cameraChanged = false;

            EditorUIHelpers.DrawSectionHeader("General");
            cameraChanged |= InspectorInputs.Bool("Main Camera", ref primary);

            EditorUIHelpers.DrawSectionHeader("Transform");
            cameraChanged |= ImGui.InputFloat("Cam Zoom", ref camZoom, 0.1f);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Camera position comes from Transform. Zoom is used when Orthographic Size is 0.");

            cameraChanged |= ImGui.InputFloat("Orthographic Size", ref orthographicSize, 0.1f);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("If > 0, this overrides zoom (Unity-style). Set to 0 to use legacy zoom.");

            EditorUIHelpers.DrawSectionHeader("Render");
            cameraChanged |= InspectorInputs.Bool("Clear Color", ref clearColor);
            cameraChanged |= InspectorInputs.ColorRgba32("Background Color (RGBA 0-255)", ref backgroundColor);
            cameraChanged |= DrawLayerMaskPopup("Culling Mask", "CameraCullingMask##" + entityId, ref cullingMask);

            EditorUIHelpers.DrawSectionHeader("Viewport");
            cameraChanged |= InspectorInputs.Vector2("Viewport Pos", ref viewportX, ref viewportY, 0.01f);
            cameraChanged |= InspectorInputs.Vector2("Viewport Size", ref viewportWidth, ref viewportHeight, 0.01f);

            float clampedZoom = Clamp(camZoom, SceneViewportSystem.MinZoomValue, SceneViewportSystem.MaxZoomValue);
            if (Math.Abs(clampedZoom - camZoom) > 0.0001f)
            {
                camZoom = clampedZoom;
                cameraChanged = true;
            }

            float clampedOrthographicSize = Clamp(orthographicSize, 0.0f, 100000.0f);
            if (Math.Abs(clampedOrthographicSize - orthographicSize) > 0.0001f)
            {
                orthographicSize = clampedOrthographicSize;
                cameraChanged = true;
            }

            float clampedViewportX = Clamp(viewportX, 0.0f, 1.0f);
            float clampedViewportY = Clamp(viewportY, 0.0f, 1.0f);
            float clampedViewportWidth = Clamp(viewportWidth, 0.01f, 1.0f);
            float clampedViewportHeight = Clamp(viewportHeight, 0.01f, 1.0f);

            if (clampedViewportX + clampedViewportWidth > 1.0f)
                clampedViewportWidth = Clamp(1.0f - clampedViewportX, 0.01f, 1.0f);
            if (clampedViewportY + clampedViewportHeight > 1.0f)
                clampedViewportHeight = Clamp(1.0f - clampedViewportY, 0.01f, 1.0f);

            if (Math.Abs(clampedViewportX - viewportX) > 0.0001f)
            {
                viewportX = clampedViewportX;
                cameraChanged = true;
            }

            if (Math.Abs(clampedViewportY - viewportY) > 0.0001f)
            {
                viewportY = clampedViewportY;
                cameraChanged = true;
            }

            if (Math.Abs(clampedViewportWidth - viewportWidth) > 0.0001f)
            {
                viewportWidth = clampedViewportWidth;
                cameraChanged = true;
            }

            if (Math.Abs(clampedViewportHeight - viewportHeight) > 0.0001f)
            {
                viewportHeight = clampedViewportHeight;
                cameraChanged = true;
            }

            if (cameraChanged)
            {
                EntityManager.SetCameraSettingsV2(entityId,
                                                  camX,
                                                  camY,
                                                  camZoom,
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
                ImGui.Text("[OK] Script type: " + currentTypeValidation.NormalizedTypeName);
            else
                ImGui.Text("[ERROR] Script type: " + currentTypeValidation.Message);

            if (_scriptAssignmentErrors.TryGetValue(entityId, out string assignmentError) && !string.IsNullOrEmpty(assignmentError))
                ImGui.Text("[WARN] " + assignmentError);

            if (validationSnapshot.IsCompiling)
            {
                ImGui.Text("[INFO] Compile: checking script project...");
            }
            else
            {
                if (validationSnapshot.HasCompileErrors)
                    ImGui.Text("[ERROR] Compile: " + validationSnapshot.CompileSummary);
                else
                    ImGui.Text("[OK] Compile: " + validationSnapshot.CompileSummary);
            }

            if (ImGui.Button("Compile Now##ScriptInspectorCompile" + entityId))
                ScriptComponentValidation.RequestImmediateBuildForActiveProject();

            ImGui.SameLine();
            if (ImGui.Button("Reload Runtime##ScriptInspectorReload" + entityId))
                ScriptComponentValidation.RequestImmediateRuntimeReload();

            ImGui.SameLine();
            if (ImGui.Button("Refresh Script Fields##" + entityId))
            {
                ScriptFieldInspector.InvalidateCache();
                ScriptComponentValidation.RequestImmediateRuntimeReload();
                ProjectOperations.SetStatusMessage("Requested script assembly reload and inspector field refresh.");
            }

            if (validationSnapshot.HasCompileErrors)
            {
                for (int i = 0; i < validationSnapshot.CompileErrorLines.Length; ++i)
                    ImGui.Text(validationSnapshot.CompileErrorLines[i]);

                if (validationSnapshot.CompileErrorLines.Length > 0
                    && ImGui.Button("Show First Error In Status##ScriptInspectorError" + entityId))
                {
                    ProjectOperations.SetStatusMessage(validationSnapshot.CompileErrorLines[0]);
                }
            }
            else if (validationSnapshot.RegisteredScriptTypes.Length == 0)
            {
                ImGui.Text("[WARN] No registered C# script classes found in the loaded script assembly.");
                if (ImGui.Button("Create Script Asset##ScriptInspectorCreate" + entityId))
                    AssetBrowserSystem.RequestOpenCreateScriptPopup();
            }

            if (!currentTypeValidation.IsValid)
            {
                if (ImGui.Button("Clear Script Type##ScriptInspectorClear" + entityId))
                {
                    EditorBridge.SetScriptTypeName(entityId, string.Empty);
                    currentScriptTypeName = string.Empty;
                    currentTypeValidation = ScriptComponentValidation.ValidateTypeName(currentScriptTypeName, validationSnapshot);
                }

                string suggestedType = TrySuggestScriptTypeReplacement(currentScriptTypeName, validationSnapshot.RegisteredScriptTypes);
                if (!string.IsNullOrEmpty(suggestedType))
                {
                    ImGui.SameLine();
                    if (ImGui.Button("Use Suggested Type##ScriptInspectorUseSuggestion" + entityId))
                    {
                        EditorBridge.SetScriptTypeName(entityId, suggestedType);
                        currentScriptTypeName = suggestedType;
                        currentTypeValidation = ScriptComponentValidation.ValidateTypeName(currentScriptTypeName, validationSnapshot);
                        _scriptAssignmentErrors.Remove(entityId);
                    }

                    ImGui.Text("Suggested: " + suggestedType);
                }
            }

            ImGui.Text("Lifecycle: toggling component Enabled invokes OnEnable/OnDisable when implemented.");

            if (currentTypeValidation.IsValid)
                ScriptFieldInspector.DrawScriptFields(entityId, currentTypeValidation.NormalizedTypeName, validationSnapshot);

        }

        private static string TrySuggestScriptTypeReplacement(string currentScriptTypeName, string[] registeredTypes)
        {
            if (registeredTypes == null || registeredTypes.Length == 0)
                return string.Empty;

            string normalizedCurrent = (currentScriptTypeName ?? string.Empty).Trim();
            if (normalizedCurrent.Length == 0)
                return string.Empty;

            for (int i = 0; i < registeredTypes.Length; ++i)
            {
                string candidate = registeredTypes[i] ?? string.Empty;
                if (string.Equals(candidate, normalizedCurrent, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }

            int dotIndex = normalizedCurrent.LastIndexOf('.');
            string simpleName = dotIndex >= 0 ? normalizedCurrent.Substring(dotIndex + 1) : normalizedCurrent;

            string bestSimpleMatch = string.Empty;
            for (int i = 0; i < registeredTypes.Length; ++i)
            {
                string candidate = registeredTypes[i] ?? string.Empty;
                if (candidate.Length == 0)
                    continue;

                int candidateDotIndex = candidate.LastIndexOf('.');
                string candidateSimple = candidateDotIndex >= 0 ? candidate.Substring(candidateDotIndex + 1) : candidate;
                if (!string.Equals(candidateSimple, simpleName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrEmpty(bestSimpleMatch))
                    return string.Empty;

                bestSimpleMatch = candidate;
            }

            return bestSimpleMatch;
        }

        private static void DrawAnimatorInspector(uint entityId)
        {
            string clipPath = EntityManager.GetAnimatorClipPath(entityId) ?? string.Empty;
            if (InspectorInputs.String("Clip Path", ref clipPath))
                EntityManager.SetAnimatorClipPath(entityId, clipPath.Trim());

            ImGui.SameLine();
            if (ImGui.Button("Browse##AnimatorClip" + entityId))
            {
                string initialDirectory = ProjectOperations.ActiveProjectPath;
                if (string.IsNullOrWhiteSpace(initialDirectory) || !Directory.Exists(initialDirectory))
                    initialDirectory = Directory.GetCurrentDirectory();

                string pickedPath = Explorer.PickFile("Select animation clip (.anim)", initialDirectory);
                if (!string.IsNullOrWhiteSpace(pickedPath))
                {
                    string normalizedPath = pickedPath;
                    string projectRoot = ProjectOperations.ActiveProjectPath;
                    if (!string.IsNullOrWhiteSpace(projectRoot) && Directory.Exists(projectRoot))
                    {
                        string relativePath = StringUtilities.MakeRelativePath(projectRoot, pickedPath);
                        if (!relativePath.StartsWith("..", StringComparison.Ordinal))
                            normalizedPath = relativePath;
                    }

                    normalizedPath = normalizedPath.Replace('\\', '/');
                    EntityManager.SetAnimatorClipPath(entityId, normalizedPath);
                }
            }

            ImGui.SameLine();
            if (ImGui.Button("Open Timeline##AnimatorClip" + entityId))
            {
                string resolvedPath = clipPath;
                if (!string.IsNullOrWhiteSpace(resolvedPath))
                {
                    string projectRoot = ProjectOperations.ActiveProjectPath;
                    if (!Path.IsPathRooted(resolvedPath)
                        && !string.IsNullOrWhiteSpace(projectRoot)
                        && Directory.Exists(projectRoot))
                    {
                        resolvedPath = Path.Combine(projectRoot, resolvedPath);
                    }

                    AnimationSystem.OpenClipFromPath(resolvedPath);
                }
            }

            bool playing = EntityManager.GetAnimatorPlaying(entityId);
            if (InspectorInputs.Bool("Playing", ref playing))
                EntityManager.SetAnimatorPlaying(entityId, playing);

            bool loop = EntityManager.GetAnimatorLoop(entityId);
            if (InspectorInputs.Bool("Loop", ref loop))
                EntityManager.SetAnimatorLoop(entityId, loop);

            bool applyPose = EntityManager.GetAnimatorApplyPoseWhenStopped(entityId);
            if (InspectorInputs.Bool("Apply Pose When Stopped", ref applyPose))
                EntityManager.SetAnimatorApplyPoseWhenStopped(entityId, applyPose);

            float speed = EntityManager.GetAnimatorSpeed(entityId);
            if (ImGui.InputFloat("Speed", ref speed, 0.05f))
                EntityManager.SetAnimatorSpeed(entityId, speed);

            float time = EntityManager.GetAnimatorTime(entityId);
            if (ImGui.InputFloat("Time", ref time, 0.05f))
            {
                EntityManager.SetAnimatorTime(entityId, time);
                EntityManager.SetAnimatorPlaying(entityId, false);
            }
        }

        private static void DrawUiCanvasInspector(uint entityId)
        {
            bool enabled;
            int sortingOrder;
            bool pixelPerfect;
            int renderMode;
            int targetDisplay;
            uint additionalShaderChannels;
            bool vertexColorAlwaysGammaSpace;
            if (!EntityManager.GetUiCanvasSettingsV2(entityId,
                                                     out enabled,
                                                     out sortingOrder,
                                                     out pixelPerfect,
                                                     out renderMode,
                                                     out targetDisplay,
                                                     out additionalShaderChannels,
                                                     out vertexColorAlwaysGammaSpace))
            {
                ImGui.Text("UI Canvas data unavailable.");
                return;
            }

            bool changed = false;
            string renderModeLabel = UiCanvasRenderModeToLabel(renderMode);
            if (InspectorInputs.SelectString("Render Mode", ref renderModeLabel, _uiCanvasRenderModeOptions))
            {
                int parsedRenderMode = UiCanvasLabelToRenderMode(renderModeLabel, renderMode);
                if (parsedRenderMode != renderMode)
                {
                    renderMode = parsedRenderMode;
                    changed = true;
                }
            }

            float sortingOrderFloat = sortingOrder;
            if (ImGui.InputFloat("Sort Order", ref sortingOrderFloat, 1.0f))
            {
                sortingOrder = (int)Math.Round(sortingOrderFloat);
                changed = true;
            }

            changed |= InspectorInputs.Bool("Pixel Perfect", ref pixelPerfect);

            float targetDisplayIndex = targetDisplay + 1;
            if (ImGui.InputFloat("Target Display", ref targetDisplayIndex, 1.0f))
            {
                int resolvedTargetDisplay = (int)Math.Round(targetDisplayIndex) - 1;
                if (resolvedTargetDisplay < 0)
                    resolvedTargetDisplay = 0;
                if (resolvedTargetDisplay > 7)
                    resolvedTargetDisplay = 7;

                if (resolvedTargetDisplay != targetDisplay)
                {
                    targetDisplay = resolvedTargetDisplay;
                    changed = true;
                }
            }

            ImGui.Text("Additional Shader Channels: " + FormatUiCanvasShaderChannels(additionalShaderChannels));

            bool texCoord1 = (additionalShaderChannels & UiCanvasAdditionalShaderChannelTexCoord1) != 0u;
            if (InspectorInputs.Bool("TexCoord1", ref texCoord1))
            {
                additionalShaderChannels = texCoord1
                    ? (additionalShaderChannels | UiCanvasAdditionalShaderChannelTexCoord1)
                    : (additionalShaderChannels & ~UiCanvasAdditionalShaderChannelTexCoord1);
                changed = true;
            }

            bool texCoord2 = (additionalShaderChannels & UiCanvasAdditionalShaderChannelTexCoord2) != 0u;
            if (InspectorInputs.Bool("TexCoord2", ref texCoord2))
            {
                additionalShaderChannels = texCoord2
                    ? (additionalShaderChannels | UiCanvasAdditionalShaderChannelTexCoord2)
                    : (additionalShaderChannels & ~UiCanvasAdditionalShaderChannelTexCoord2);
                changed = true;
            }

            bool texCoord3 = (additionalShaderChannels & UiCanvasAdditionalShaderChannelTexCoord3) != 0u;
            if (InspectorInputs.Bool("TexCoord3", ref texCoord3))
            {
                additionalShaderChannels = texCoord3
                    ? (additionalShaderChannels | UiCanvasAdditionalShaderChannelTexCoord3)
                    : (additionalShaderChannels & ~UiCanvasAdditionalShaderChannelTexCoord3);
                changed = true;
            }

            bool normal = (additionalShaderChannels & UiCanvasAdditionalShaderChannelNormal) != 0u;
            if (InspectorInputs.Bool("Normal", ref normal))
            {
                additionalShaderChannels = normal
                    ? (additionalShaderChannels | UiCanvasAdditionalShaderChannelNormal)
                    : (additionalShaderChannels & ~UiCanvasAdditionalShaderChannelNormal);
                changed = true;
            }

            bool tangent = (additionalShaderChannels & UiCanvasAdditionalShaderChannelTangent) != 0u;
            if (InspectorInputs.Bool("Tangent", ref tangent))
            {
                additionalShaderChannels = tangent
                    ? (additionalShaderChannels | UiCanvasAdditionalShaderChannelTangent)
                    : (additionalShaderChannels & ~UiCanvasAdditionalShaderChannelTangent);
                changed = true;
            }

            changed |= InspectorInputs.Bool("Vertex Color Always Gamma", ref vertexColorAlwaysGammaSpace);

            if (renderMode != UiCanvasRenderModeScreenSpaceOverlay)
                ImGui.Text("Selected render mode is reserved; runtime currently renders Overlay mode.");

            if ((additionalShaderChannels & (UiCanvasAdditionalShaderChannelNormal | UiCanvasAdditionalShaderChannelTangent)) != 0u)
                ImGui.Text("Normal/Tangent channels are usually unnecessary for Overlay canvas rendering.");

            if (vertexColorAlwaysGammaSpace)
                ImGui.Text("Gamma vertex color keeps UI color precision when converting to linear space.");

            if (changed)
            {
                EntityManager.SetUiCanvasSettingsV2(entityId,
                                                    enabled,
                                                    sortingOrder,
                                                    pixelPerfect,
                                                    renderMode,
                                                    targetDisplay,
                                                    additionalShaderChannels,
                                                    vertexColorAlwaysGammaSpace);
            }
        }

        private static void DrawUiRectTransformInspector(uint entityId)
        {
            float anchorMinX;
            float anchorMinY;
            float anchorMaxX;
            float anchorMaxY;
            float pivotX;
            float pivotY;
            float anchoredX;
            float anchoredY;
            float sizeDeltaX;
            float sizeDeltaY;

            if (!EntityManager.GetUiRectTransform(entityId,
                                                  out anchorMinX,
                                                  out anchorMinY,
                                                  out anchorMaxX,
                                                  out anchorMaxY,
                                                  out pivotX,
                                                  out pivotY,
                                                  out anchoredX,
                                                  out anchoredY,
                                                  out sizeDeltaX,
                                                  out sizeDeltaY))
            {
                ImGui.Text("UI RectTransform data unavailable.");
                return;
            }

            bool changed = false;
            changed |= InspectorInputs.Vector2("Anchor Min", ref anchorMinX, ref anchorMinY, 0.01f);
            changed |= InspectorInputs.Vector2("Anchor Max", ref anchorMaxX, ref anchorMaxY, 0.01f);
            changed |= InspectorInputs.Vector2("Pivot", ref pivotX, ref pivotY, 0.01f);
            changed |= InspectorInputs.Vector2("Anchored Position", ref anchoredX, ref anchoredY, 1.0f);
            changed |= InspectorInputs.Vector2("Size Delta", ref sizeDeltaX, ref sizeDeltaY, 1.0f);

            if (changed)
            {
                EntityManager.SetUiRectTransform(entityId,
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
        }

        private static void DrawUiImageInspector(uint entityId)
        {
            bool enabled;
            ulong textureAssetHandle;
            uint color;
            bool preserveAspect;
            float cornerRadius;
            if (!EntityManager.GetUiImageSettings(entityId,
                                                  out enabled,
                                                  out textureAssetHandle,
                                                  out color,
                                                  out preserveAspect,
                                                  out cornerRadius))
            {
                ImGui.Text("UI Image data unavailable.");
                return;
            }

            string texturePath = EntityManager.GetUiImageTexturePath(entityId) ?? string.Empty;
            bool pathChanged = false;
            if (InspectorInputs.String("Texture", ref texturePath))
            {
                pathChanged = true;
                texturePath = texturePath.Trim();
            }

            ImGui.SameLine();
            if (ImGui.Button("Browse##UiImageTexture" + entityId))
            {
                string initialDirectory = ProjectOperations.ActiveProjectPath;
                if (string.IsNullOrWhiteSpace(initialDirectory) || !Directory.Exists(initialDirectory))
                    initialDirectory = Directory.GetCurrentDirectory();

                string pickedPath = Explorer.PickFile("Select UI image texture (png/jpg/bmp/tga)", initialDirectory);
                if (!string.IsNullOrWhiteSpace(pickedPath))
                {
                    string normalizedPath = pickedPath;
                    string projectRoot = ProjectOperations.ActiveProjectPath;
                    if (!string.IsNullOrWhiteSpace(projectRoot) && Directory.Exists(projectRoot))
                    {
                        string relativePath = StringUtilities.MakeRelativePath(projectRoot, pickedPath);
                        if (!relativePath.StartsWith("..", StringComparison.Ordinal))
                            normalizedPath = relativePath;
                    }

                    texturePath = normalizedPath.Replace('\\', '/');
                    pathChanged = true;
                }
            }

            ImGui.SameLine();
            if (ImGui.Button("Clear##UiImageTexture" + entityId))
            {
                texturePath = string.Empty;
                pathChanged = true;
            }

            bool settingsChanged = false;
            settingsChanged |= InspectorInputs.ColorRgba32("Color (RGBA 0-255)", ref color);
            settingsChanged |= InspectorInputs.Bool("Preserve Aspect", ref preserveAspect);
            settingsChanged |= ImGui.InputFloat("Corner Radius", ref cornerRadius, 0.5f);

            ImGui.Text("Texture Handle: " + textureAssetHandle.ToString());

            if (settingsChanged)
            {
                EntityManager.SetUiImageSettings(entityId,
                                                 enabled,
                                                 textureAssetHandle,
                                                 color,
                                                 preserveAspect,
                                                 cornerRadius);
            }

            if (pathChanged)
                EntityManager.SetUiImageTexturePath(entityId, texturePath);
        }

        private static string UiTextAlignToLabel(int horizontalAlign)
        {
            if (horizontalAlign == 1)
                return "Center";
            if (horizontalAlign == 2)
                return "Right";
            return "Left";
        }

        private static int UiTextAlignFromLabel(string label)
        {
            if (string.Equals(label, "Center", StringComparison.OrdinalIgnoreCase))
                return 1;
            if (string.Equals(label, "Right", StringComparison.OrdinalIgnoreCase))
                return 2;
            return 0;
        }

        private static void DrawUiTextInspector(uint entityId)
        {
            bool enabled;
            float fontSize;
            uint color;
            int horizontalAlign;
            bool wrap;
            if (!EntityManager.GetUiTextSettings(entityId,
                                                 out enabled,
                                                 out fontSize,
                                                 out color,
                                                 out horizontalAlign,
                                                 out wrap))
            {
                ImGui.Text("UI Text data unavailable.");
                return;
            }

            string text = EntityManager.GetUiTextValue(entityId) ?? string.Empty;
            bool textChanged = InspectorInputs.String("Text", ref text);

            bool settingsChanged = false;
            settingsChanged |= ImGui.InputFloat("Font Size", ref fontSize, 0.5f);
            settingsChanged |= InspectorInputs.ColorRgba32("Color (RGBA 0-255)", ref color);
            settingsChanged |= InspectorInputs.Bool("Wrap", ref wrap);

            string align = UiTextAlignToLabel(horizontalAlign);
            if (InspectorInputs.SelectString("Horizontal Align", ref align, new[] { "Left", "Center", "Right" }))
            {
                horizontalAlign = UiTextAlignFromLabel(align);
                settingsChanged = true;
            }

            if (settingsChanged)
            {
                EntityManager.SetUiTextSettings(entityId,
                                                enabled,
                                                fontSize,
                                                color,
                                                horizontalAlign,
                                                wrap);
            }

            if (textChanged)
                EntityManager.SetUiTextValue(entityId, text);
        }

        private static void DrawUiButtonInspector(uint entityId)
        {
            bool enabled;
            bool interactable;
            uint normalColor;
            uint highlightedColor;
            uint pressedColor;
            uint disabledColor;
            if (!EntityManager.GetUiButtonSettings(entityId,
                                                   out enabled,
                                                   out interactable,
                                                   out normalColor,
                                                   out highlightedColor,
                                                   out pressedColor,
                                                   out disabledColor))
            {
                ImGui.Text("UI Button data unavailable.");
                return;
            }

            bool changed = false;
            changed |= InspectorInputs.Bool("Interactable", ref interactable);
            changed |= InspectorInputs.ColorRgba32("Normal Color", ref normalColor);
            changed |= InspectorInputs.ColorRgba32("Highlighted Color", ref highlightedColor);
            changed |= InspectorInputs.ColorRgba32("Pressed Color", ref pressedColor);
            changed |= InspectorInputs.ColorRgba32("Disabled Color", ref disabledColor);

            if (changed)
            {
                EntityManager.SetUiButtonSettings(entityId,
                                                  enabled,
                                                  interactable,
                                                  normalColor,
                                                  highlightedColor,
                                                  pressedColor,
                                                  disabledColor);
            }
        }

        private static void DrawUiInputFieldInspector(uint entityId)
        {
            bool enabled;
            bool interactable;
            uint textColor;
            uint placeholderColor;
            uint maxLength;
            if (!EntityManager.GetUiInputFieldSettings(entityId,
                                                       out enabled,
                                                       out interactable,
                                                       out textColor,
                                                       out placeholderColor,
                                                       out maxLength))
            {
                ImGui.Text("UI InputField data unavailable.");
                return;
            }

            string text = EntityManager.GetUiInputFieldText(entityId) ?? string.Empty;
            string placeholder = EntityManager.GetUiInputFieldPlaceholder(entityId) ?? string.Empty;

            bool textChanged = InspectorInputs.String("Text", ref text);
            bool placeholderChanged = InspectorInputs.String("Placeholder", ref placeholder);

            bool settingsChanged = false;
            settingsChanged |= InspectorInputs.Bool("Interactable", ref interactable);
            settingsChanged |= InspectorInputs.ColorRgba32("Text Color", ref textColor);
            settingsChanged |= InspectorInputs.ColorRgba32("Placeholder Color", ref placeholderColor);
            settingsChanged |= InspectorInputs.UInt("Max Length (0 = unlimited)", ref maxLength);

            if (settingsChanged)
            {
                EntityManager.SetUiInputFieldSettings(entityId,
                                                      enabled,
                                                      interactable,
                                                      textColor,
                                                      placeholderColor,
                                                      maxLength);
            }

            if (textChanged)
                EntityManager.SetUiInputFieldText(entityId, text);

            if (placeholderChanged)
                EntityManager.SetUiInputFieldPlaceholder(entityId, placeholder);
        }

        private static float Clamp(float value, float minValue, float maxValue)
        {
            if (value < minValue)
                return minValue;
            if (value > maxValue)
                return maxValue;
            return value;
        }
    }
}
