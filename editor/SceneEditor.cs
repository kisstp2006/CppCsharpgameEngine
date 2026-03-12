using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

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
        private static readonly Dictionary<string, bool> _componentFoldoutStates = new Dictionary<string, bool>();
        private static string _activeScenePath = string.Empty;
        private static int _activeSceneStorageFormat = SceneStorageJson;
        private static bool _optionsRegistered = false;
        private static bool _assetContextMenuRegistered = false;
        private static bool _defaultGizmoSnapEnabled = false;
        private static float _defaultGizmoSnapStep = 32.0f;
        private static string _loadedProjectSettingsPath = string.Empty;
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

        private const int SceneStorageJson = 0;
        private const int SceneStorageBinary = 1;
        private const string ProjectSettingDefaultSnapEnabled = "editor.scene.defaultSnapEnabled";
        private const string ProjectSettingDefaultSnapStep = "editor.scene.defaultSnapStep";

        private sealed class ComponentInspectorEntry
        {
            public string Name;
            public string Category;
            public int ComponentType;
            public bool CanRemove;
            public Action<uint> Draw;
        }

        public static int SelectedEntityId => _selectedEntityId;
        public static string ActiveScenePath => _activeScenePath;

        public static void ResetEditorState()
        {
            EnsureProjectSettingsLoaded();

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
            _gizmoSnapEnabled = _defaultGizmoSnapEnabled;
            _gizmoSnapStep = _defaultGizmoSnapStep;
            _scriptAssignmentErrors.Clear();
            _componentFoldoutStates.Clear();
            DebugDraw.Clear();
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

        public static void RegisterAssetContextMenu()
        {
            if (_assetContextMenuRegistered)
                return;

            _assetContextMenuRegistered = true;
            AssetPanelContextMenuRegistry.Register("sceneeditor.createScene",
                                                   "Create/Scene",
                                                   _ => AssetPanel.RequestOpenCreateScenePopup(),
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

        private static void EnsureProjectSettingsLoaded()
        {
            string activeProjectPath = NormalizeProjectPath(ProjectOperations.ActiveProjectPath);
            if (string.Equals(_loadedProjectSettingsPath, activeProjectPath, StringComparison.OrdinalIgnoreCase))
                return;

            _loadedProjectSettingsPath = activeProjectPath;

            _defaultGizmoSnapEnabled = ReadProjectBoolSetting(ProjectSettingDefaultSnapEnabled, false);
            _defaultGizmoSnapStep = ReadProjectFloatSetting(ProjectSettingDefaultSnapStep, 32.0f, 1.0f, 1024.0f);

            _gizmoSnapEnabled = _defaultGizmoSnapEnabled;
            _gizmoSnapStep = _defaultGizmoSnapStep;
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

        public static void ResetSelection()
        {
            _selectedEntityId = -1;
        }

        public static void CreateEntityAndSelect()
        {
            uint created = EntityManager.CreateEntity();
            if (EntityManager.IsEntityValid(created))
                _selectedEntityId = (int)created;
        }

        public static void NewScene()
        {
            EditorBridge.NewScene();
            _selectedEntityId = -1;
            _scriptAssignmentErrors.Clear();
            _componentFoldoutStates.Clear();
            _activeScenePath = string.Empty;
            _activeSceneStorageFormat = SceneStorageJson;

            string status = EditorBridge.GetLastSceneIoStatus();
            if (string.IsNullOrEmpty(status))
                status = "New scene created.";

            ProjectOperations.SetStatusMessage(status);
        }

        public static bool CreateSceneAsset(string sceneName, int storageFormat)
        {
            string trimmedName = (sceneName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmedName))
                trimmedName = "Scene";

            if (!StringUtilities.IsValidProjectName(trimmedName))
            {
                ProjectOperations.SetStatusMessage("Create scene failed: invalid scene name.");
                return false;
            }

            string extension = storageFormat == SceneStorageBinary ? ".scene.bin" : ".scene.json";
            string scenesRoot = GetDefaultScenesRootPath();

            string candidatePath = Path.Combine(scenesRoot, trimmedName + extension);
            if (File.Exists(candidatePath))
            {
                int suffix = 1;
                while (true)
                {
                    string nextCandidate = Path.Combine(scenesRoot, trimmedName + "_" + suffix + extension);
                    if (!File.Exists(nextCandidate))
                    {
                        candidatePath = nextCandidate;
                        break;
                    }

                    ++suffix;
                }
            }

            EditorBridge.NewScene();
            if (!EditorBridge.SaveScene(candidatePath, storageFormat))
            {
                string error = EditorBridge.GetLastSceneIoStatus();
                if (string.IsNullOrEmpty(error))
                    error = "Create scene failed.";

                ProjectOperations.SetStatusMessage(error);
                return false;
            }

            _selectedEntityId = -1;
            _scriptAssignmentErrors.Clear();
            _activeScenePath = Path.GetFullPath(candidatePath);
            _activeSceneStorageFormat = storageFormat;
            _componentFoldoutStates.Clear();

            ProjectOperations.SetStatusMessage("Created scene: " + Path.GetFileName(_activeScenePath));
            return true;
        }

        public static bool LoadSceneFromPath(string scenePath)
        {
            if (string.IsNullOrWhiteSpace(scenePath))
            {
                ProjectOperations.SetStatusMessage("Load failed: scene path is empty.");
                return false;
            }

            string resolvedPath = Path.GetFullPath(scenePath);
            int format = DetectStorageFormatFromPath(resolvedPath);
            if (format < 0)
            {
                ProjectOperations.SetStatusMessage("Load failed: use .scene.json or .scene.bin file.");
                return false;
            }

            if (EditorBridge.LoadScene(resolvedPath, format))
            {
                _activeScenePath = resolvedPath;
                _activeSceneStorageFormat = format;
                _selectedEntityId = -1;
                _scriptAssignmentErrors.Clear();
                _componentFoldoutStates.Clear();
                ProjectOperations.SetStatusMessage("Loaded scene: " + Path.GetFileName(resolvedPath));
                return true;
            }

            string error = EditorBridge.GetLastSceneIoStatus();
            if (string.IsNullOrEmpty(error))
                error = "Load scene failed.";

            ProjectOperations.SetStatusMessage(error);
            return false;
        }

        public static void LoadSceneFromPicker()
        {
            string initialPath = _activeScenePath;
            if (string.IsNullOrEmpty(initialPath))
                initialPath = GetDefaultScenesRootPath();

            string picked = Explorer.PickFile("Load scene (.scene.json or .scene.bin)", initialPath);
            if (string.IsNullOrWhiteSpace(picked))
                return;

            LoadSceneFromPath(picked);
        }

        public static bool RestoreSceneFromPlaySnapshot(string snapshotPath)
        {
            if (string.IsNullOrWhiteSpace(snapshotPath) || !File.Exists(snapshotPath))
            {
                ProjectOperations.SetStatusMessage("Stop failed: play snapshot missing.");
                return false;
            }

            if (!EditorBridge.LoadScene(snapshotPath, SceneStorageBinary))
            {
                string error = EditorBridge.GetLastSceneIoStatus();
                if (string.IsNullOrEmpty(error))
                    error = "Stop failed: could not restore play snapshot.";

                ProjectOperations.SetStatusMessage(error);
                return false;
            }

            _selectedEntityId = -1;
            _scriptAssignmentErrors.Clear();
            _componentFoldoutStates.Clear();
            return true;
        }

        public static bool SaveScene()
        {
            if (string.IsNullOrWhiteSpace(_activeScenePath))
            {
                ProjectOperations.SetStatusMessage("Save failed: no active scene. Create or load one from Asset Panel.");
                return false;
            }

            string resolvedPath = Path.GetFullPath(_activeScenePath);
            if (!EditorBridge.SaveScene(resolvedPath, _activeSceneStorageFormat))
            {
                string error = EditorBridge.GetLastSceneIoStatus();
                if (string.IsNullOrEmpty(error))
                    error = "Save scene failed.";

                ProjectOperations.SetStatusMessage(error);
                return false;
            }

            ProjectOperations.SetStatusMessage("Saved scene: " + Path.GetFileName(resolvedPath));
            return true;
        }

        public static bool IsSceneFilePath(string path)
        {
            return DetectStorageFormatFromPath(path) >= 0;
        }

        public static void SaveSceneAsJson()
        {
            SaveSceneForFormat(SceneStorageJson);
        }

        public static void SaveSceneAsBinary()
        {
            SaveSceneForFormat(SceneStorageBinary);
        }

        private static void SaveSceneForFormat(int format)
        {
            string targetPath = ResolveSavePath(format);
            if (string.IsNullOrEmpty(targetPath))
            {
                ProjectOperations.SetStatusMessage("Save failed: invalid scene path.");
                return;
            }

            if (EditorBridge.SaveScene(targetPath, format))
            {
                _activeScenePath = targetPath;
                _activeSceneStorageFormat = format;
                ProjectOperations.SetStatusMessage("Saved scene: " + Path.GetFileName(targetPath));
                return;
            }

            string error = EditorBridge.GetLastSceneIoStatus();
            if (string.IsNullOrEmpty(error))
                error = "Save scene failed.";

            ProjectOperations.SetStatusMessage(error);
        }

        private static string ResolveSavePath(int format)
        {
            string activePath = _activeScenePath;
            if (!string.IsNullOrWhiteSpace(activePath))
            {
                int activeFormat = DetectStorageFormatFromPath(activePath);
                if (activeFormat == format)
                    return Path.GetFullPath(activePath);
            }

            string scenesRoot = GetDefaultScenesRootPath();
            string baseName = "Scene";

            if (!string.IsNullOrWhiteSpace(activePath))
            {
                string activeName = Path.GetFileName(activePath);
                if (!string.IsNullOrEmpty(activeName))
                {
                    if (activeName.EndsWith(".scene.json", StringComparison.OrdinalIgnoreCase))
                        baseName = activeName.Substring(0, activeName.Length - ".scene.json".Length);
                    else if (activeName.EndsWith(".scene.bin", StringComparison.OrdinalIgnoreCase))
                        baseName = activeName.Substring(0, activeName.Length - ".scene.bin".Length);
                    else
                        baseName = Path.GetFileNameWithoutExtension(activeName);
                }
            }

            if (string.IsNullOrWhiteSpace(baseName))
                baseName = "Scene";

            string extension = format == SceneStorageBinary ? ".scene.bin" : ".scene.json";
            string candidate = Path.Combine(scenesRoot, baseName + extension);

            if (string.IsNullOrWhiteSpace(_activeScenePath) && File.Exists(candidate))
            {
                int suffix = 1;
                while (true)
                {
                    string nextCandidate = Path.Combine(scenesRoot, baseName + "_" + suffix + extension);
                    if (!File.Exists(nextCandidate))
                        return nextCandidate;

                    ++suffix;
                }
            }

            return candidate;
        }

        private static string GetDefaultScenesRootPath()
        {
            string projectPath = ProjectOperations.ActiveProjectPath;
            if (!string.IsNullOrWhiteSpace(projectPath))
            {
                string scenesRoot = Path.Combine(projectPath, "Scenes");
                Directory.CreateDirectory(scenesRoot);
                return scenesRoot;
            }

            return Directory.GetCurrentDirectory();
        }

        private static int DetectStorageFormatFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return -1;

            string fullPath = Path.GetFullPath(path);
            if (fullPath.EndsWith(".scene.bin", StringComparison.OrdinalIgnoreCase) ||
                fullPath.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
            {
                return SceneStorageBinary;
            }

            if (fullPath.EndsWith(".scene.json", StringComparison.OrdinalIgnoreCase) ||
                fullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                return SceneStorageJson;
            }

            return -1;
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
            bool simulationRunning = simulationState == EditorBridge.SimulationPlay || simulationState == EditorBridge.SimulationPause;

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

            if (!simulationRunning)
            {
                EditorBridge.SetGameViewSize(_gameViewWidth, _gameViewHeight);
                EditorBridge.SetEditorPreviewCamera(_editorCamera.X, _editorCamera.Y, _editorCamera.Zoom, true);
            }
            else
            {
                EditorBridge.SetEditorPreviewCamera(0.0f, 0.0f, 1.0f, false);
            }

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

        public static void DrawRuntimeGamePanel()
        {
            int simulationState = EditorBridge.GetSimulationState();
            bool playing = simulationState == EditorBridge.SimulationPlay;
            bool paused = simulationState == EditorBridge.SimulationPause;
            if (!playing && !paused)
                return;

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

        public static void DrawSceneTreePanel()
        {
            if (ImGui.Begin("Scene Tree"))
            {

                if (ImGui.Button("Create Entity"))
                {
                    uint created = EntityManager.CreateEntity();
                    _selectedEntityId = (int)created;
                }

                ImGui.Separator();

                int entityCount = EntityManager.GetEntityCount();
                for (int i = 0; i < entityCount; ++i)
                {
                    uint entityId = EntityManager.GetEntityIdAtIndex(i);
                    string entityName = EntityManager.GetEntityName(entityId);
                    if (string.IsNullOrWhiteSpace(entityName))
                        entityName = "Entity " + entityId;

                    string label = entityName + "##SceneTreeEntity" + entityId;
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
                    bool valid = EntityManager.IsEntityValid(entityId);
                    if (!valid)
                    {
                        _selectedEntityId = -1;
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

        private static bool DrawEntityHeaderInspector(uint entityId)
        {
            ImGui.Text("Entity " + entityId);
            ImGui.SameLine();

            if (ImGui.Button("Delete Entity"))
            {
                EntityManager.DestroyEntity(entityId);
                _selectedEntityId = -1;
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

        private static void EnsureWorldInitialized()
        {
            if (_worldInitialized)
                return;

            _worldInitialized = true;
            _editorCamera.MinZoom = MinZoom;
            _editorCamera.MaxZoom = MaxZoom;
            _editorCamera.Reset();
            InitializeComponentRegistry();
            if (_selectedEntityId >= 0 && !EntityManager.IsEntityValid((uint)_selectedEntityId))
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

            bool changed = false;
            changed |= InspectorInputs.Vector2("Position", ref x, ref y, 1.0f);
            changed |= InspectorInputs.Vector2("Size", ref width, ref height, 1.0f);

            if (changed)
                EntityManager.SetTransform(entityId, x, y, width, height);
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

            float clampedZoom = Clamp(camZoom, MinZoom, MaxZoom);
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

                _selectedEntityId = hitEntityId;
                if (_selectedEntityId >= 0)
                {
                    uint selectedEntity = (uint)_selectedEntityId;
                    float tx;
                    float ty;
                    float tw;
                    float th;
                    if (EntityManager.GetTransform(selectedEntity, out tx, out ty, out tw, out th))
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

                bool selected = (_selectedEntityId >= 0) && ((uint)_selectedEntityId == entityId);
                float r = selected ? 1.0f : 0.75f;
                float g = selected ? 0.85f : 0.75f;
                float b = selected ? 0.2f : 1.0f;
                float a = selected ? 1.0f : 0.8f;

                ImGui.DrawRect(drawX, drawY, sw, sh, r, g, b, a, selected ? 2.0f : 1.0f);

                if (selected)
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

                        EntityManager.SetTransform(entityId, gizmoX, gizmoY, width, height);
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
