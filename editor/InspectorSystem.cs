using System;
using System.Collections.Generic;
using System.IO;

using Engine;

namespace EngineEditor
{
    internal static class InspectorSystem
    {
        private static bool _componentRegistryInitialized;
        private static readonly List<ComponentInspectorEntry> _componentEntries = new List<ComponentInspectorEntry>();
        private static readonly Dictionary<uint, string> _scriptAssignmentErrors = new Dictionary<uint, string>();
        private static readonly Dictionary<string, bool> _componentFoldoutStates = new Dictionary<string, bool>();

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

        private static int SelectedEntityId
        {
            get => EditorContext.SelectedEntityId;
            set => EditorContext.SelectedEntityId = value;
        }

        private sealed class ComponentInspectorEntry
        {
            public string Name;
            public string Category;
            public int ComponentType;
            public bool CanRemove;
            public Action<uint> Draw;
        }

        public static void ResetState()
        {
            ResetTransientState();
        }

        public static void ResetTransientState()
        {
            _scriptAssignmentErrors.Clear();
            _componentFoldoutStates.Clear();
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

                        for (int i = 0; i < _componentEntries.Count; ++i)
                        {
                            ComponentInspectorEntry entry = _componentEntries[i];
                            if (!EntityManager.HasComponent(entityId, entry.ComponentType))
                                continue;

                            DrawComponentCard(entityId, entry);
                        }
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
            InitializeComponentRegistry();
        }

        private static bool DrawEntityHeaderInspector(uint entityId)
        {
            ImGui.Text("Entity " + entityId);
            ImGui.SameLine();

            if (ImGui.Button("Delete Entity"))
            {
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

        private static void DrawComponentCard(uint entityId, ComponentInspectorEntry entry)
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

            if (entry.CanRemove)
            {
                ImGui.SameLine();
                if (ImGui.Button("Remove##" + foldoutKey))
                {
                    EntityManager.RemoveComponent(entityId, entry.ComponentType);
                    _componentFoldoutStates.Remove(foldoutKey);
                    return;
                }
            }

            if (expanded)
                entry.Draw(entityId);
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

        private static void InitializeComponentRegistry()
        {
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

            RegisterInspectorComponent("Animator",
                                       "Animation",
                                       ComponentType.Animator,
                                       DrawAnimatorInspector,
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

                    for (int i = 0; i < _componentEntries.Count; ++i)
                    {
                        ComponentInspectorEntry entry = _componentEntries[i];
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
            cameraChanged |= InspectorInputs.Bool("Enabled", ref enabled);
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

            ImGui.Text("Lifecycle: toggling Script Enabled invokes OnEnable/OnDisable when implemented.");

            if (ImGui.Button("Refresh Script Fields"))
            {
                ScriptFieldInspector.InvalidateCache();
                ScriptComponentValidation.RequestImmediateRuntimeReload();
                ProjectOperations.SetStatusMessage("Requested script assembly reload and inspector field refresh.");
            }

            if (currentTypeValidation.IsValid)
                ScriptFieldInspector.DrawScriptFields(entityId, currentTypeValidation.NormalizedTypeName, validationSnapshot);

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
