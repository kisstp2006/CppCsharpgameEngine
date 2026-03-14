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

        private const int UiCanvasRenderModeScreenSpaceOverlay = 0;

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
        private static bool _pixelSnapEnabled;
        private static int _selectedResolutionIndex;
        private static bool _showStats;

        private static readonly string[] _resolutionLabels = new string[]
        {
            "Free Aspect",
            "16:9  (1920x1080)",
            "16:10 (1920x1200)",
            "4:3   (1024x768)",
            "1:1   (512x512)",
            "Custom"
        };

        private static readonly float[] _resolutionAspects = new float[]
        {
            0.0f,
            16.0f / 9.0f,
            16.0f / 10.0f,
            4.0f / 3.0f,
            1.0f,
            0.0f
        };

        private static float _customWidth = 1280;
        private static float _customHeight = 720;

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
            _pixelSnapEnabled = false;
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

        public static void FocusCamera()
        {
            GameViewFocused = true;
            SyncPreviewCameraState(true);
        }

        public static bool FrameSelectedEntity()
        {
            if (SelectedEntityId < 0)
                return false;

            uint entityId = (uint)SelectedEntityId;
            if (!EntityManager.IsEntityValid(entityId))
                return false;

            float x;
            float y;
            float width;
            float height;
            if (!EntityManager.GetTransform(entityId, out x, out y, out width, out height))
                return false;

            float rectX;
            float rectY;
            float rectWidth;
            float rectHeight;
            ResolveEntityVisualRect(entityId, x, y, width, height, out rectX, out rectY, out rectWidth, out rectHeight);

            _editorCamera.X = rectX + (rectWidth * 0.5f);
            _editorCamera.Y = rectY + (rectHeight * 0.5f);

            if (_gameViewWidth > 1.0f && _gameViewHeight > 1.0f)
            {
                float fitZoomX = (_gameViewWidth * 0.70f) / Math.Max(1.0f, Math.Abs(rectWidth));
                float fitZoomY = (_gameViewHeight * 0.70f) / Math.Max(1.0f, Math.Abs(rectHeight));
                float fitZoom = Math.Min(fitZoomX, fitZoomY);
                if (!float.IsNaN(fitZoom) && !float.IsInfinity(fitZoom))
                    _editorCamera.Zoom = Clamp(fitZoom, MinZoom, MaxZoom);
            }

            FocusCamera();
            return true;
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

            // ── Toolbar row 1: Play controls + Resolution ──
            DrawPlayControls();
            ImGui.SameLine();
            ImGui.Text("|");
            ImGui.SameLine();
            DrawResolutionSelector();
            ImGui.SameLine();
            if (ImGui.Button(_showStats ? "[Stats]" : "Stats"))
                _showStats = !_showStats;

            // ── Toolbar row 2: Gizmo tools ──
            if (!simulationRunning)
            {
                if (ImGui.Button("Reset Camera"))
                {
                    _editorCamera.Reset();
                    SyncPreviewCameraState(true);
                }

                ImGui.SameLine();
                DrawGameViewTopBar();
            }

            ImGui.Separator();

            // ── Check for script errors ──
            ScriptValidationSnapshot snapshot = ScriptComponentValidation.GetSnapshot();
            bool hasErrors = snapshot.HasCompileErrors;

            float availWidth = ImGui.GetContentRegionAvailX();
            float availHeight = ImGui.GetContentRegionAvailY();
            if (availWidth < 1.0f)
                availWidth = 1.0f;
            if (availHeight < 1.0f)
                availHeight = 1.0f;

            // Calculate actual render size with aspect ratio
            float renderWidth = availWidth;
            float renderHeight = availHeight;
            float offsetX = 0.0f;
            float offsetY = 0.0f;
            ComputeViewportLayout(availWidth, availHeight, out renderWidth, out renderHeight, out offsetX, out offsetY);

            _gameViewPosX = ImGui.GetCursorScreenPosX() + offsetX;
            _gameViewPosY = ImGui.GetCursorScreenPosY() + offsetY;
            _gameViewWidth = renderWidth;
            _gameViewHeight = renderHeight;

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
                ImGui.InvisibleButton("##GameViewPlayModePlaceholder", availWidth, availHeight);
                ImGui.End();
                return;
            }

            // ── Draw letterbox bars if aspect ratio constrains the view ──
            float cursorScreenX = ImGui.GetCursorScreenPosX();
            float cursorScreenY = ImGui.GetCursorScreenPosY();

            if (offsetX > 0.5f || offsetY > 0.5f)
            {
                ImGui.DrawRectFilled(cursorScreenX, cursorScreenY, availWidth, availHeight,
                                     0.08f, 0.08f, 0.08f, 1.0f);
            }

            if (offsetX > 0.0f)
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offsetX);
            if (offsetY > 0.0f)
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + offsetY);

            ulong gameViewTextureHandle = EditorBridge.GetGameViewTextureHandle();
            if (gameViewTextureHandle != 0)
                ImGui.Image(gameViewTextureHandle, renderWidth, renderHeight);
            else
                ImGui.InvisibleButton("##GameViewImagePlaceholder", renderWidth, renderHeight);

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
            HandleAssetDropOnViewport(itemHovered);
            DrawAssetDropOverlay(itemHovered);

            // ── Error overlay ──
            if (hasErrors)
                DrawErrorOverlay(cursorScreenX + offsetX, cursorScreenY + offsetY,
                                 renderWidth, renderHeight, snapshot);

            // ── Stats overlay ──
            if (_showStats)
                DrawStatsOverlay(cursorScreenX + offsetX, cursorScreenY + offsetY,
                                 renderWidth, renderHeight);

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

            // ── Toolbar: Play/Pause/Stop + Resolution ──
            DrawPlayControls();
            ImGui.SameLine();
            ImGui.Text("|");
            ImGui.SameLine();
            DrawResolutionSelector();
            ImGui.SameLine();
            if (ImGui.Button(_showStats ? "[Stats]" : "Stats"))
                _showStats = !_showStats;
            ImGui.SameLine();
            ImGui.Text(paused ? "[Paused]" : "[Playing]");

            ImGui.Separator();

            // ── Check for script errors ──
            ScriptValidationSnapshot snapshot = ScriptComponentValidation.GetSnapshot();
            bool hasErrors = snapshot.HasCompileErrors;

            float availWidth = ImGui.GetContentRegionAvailX();
            float availHeight = ImGui.GetContentRegionAvailY();
            if (availWidth < 1.0f)
                availWidth = 1.0f;
            if (availHeight < 1.0f)
                availHeight = 1.0f;

            float renderWidth;
            float renderHeight;
            float offsetX;
            float offsetY;
            ComputeViewportLayout(availWidth, availHeight, out renderWidth, out renderHeight, out offsetX, out offsetY);

            float cursorScreenX = ImGui.GetCursorScreenPosX();
            float cursorScreenY = ImGui.GetCursorScreenPosY();

            // ── Letterbox bars ──
            if (offsetX > 0.5f || offsetY > 0.5f)
            {
                ImGui.DrawRectFilled(cursorScreenX, cursorScreenY, availWidth, availHeight,
                                     0.08f, 0.08f, 0.08f, 1.0f);
            }

            if (offsetX > 0.0f)
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offsetX);
            if (offsetY > 0.0f)
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + offsetY);

            EditorBridge.SetGameViewSize(renderWidth, renderHeight);
            ulong gameViewTextureHandle = EditorBridge.GetGameViewTextureHandle();
            if (gameViewTextureHandle != 0)
                ImGui.Image(gameViewTextureHandle, renderWidth, renderHeight);
            else
                ImGui.InvisibleButton("##RuntimeGameImagePlaceholder", renderWidth, renderHeight);

            // ── Error overlay ──
            if (hasErrors)
                DrawErrorOverlay(cursorScreenX + offsetX, cursorScreenY + offsetY,
                                 renderWidth, renderHeight, snapshot);

            // ── Stats overlay ──
            if (_showStats)
                DrawStatsOverlay(cursorScreenX + offsetX, cursorScreenY + offsetY,
                                 renderWidth, renderHeight);

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
            if (ImGui.Button((EditorContext.ActiveTool == EditorTool.Move ? "[Move]" : "Move") + " (W)"))
                EditorContext.ActiveTool = EditorTool.Move;

            ImGui.SameLine();
            if (ImGui.Button((EditorContext.ActiveTool == EditorTool.Rotate ? "[Rotate]" : "Rotate") + " (E)"))
                EditorContext.ActiveTool = EditorTool.Rotate;

            ImGui.SameLine();
            if (ImGui.Button((EditorContext.ActiveTool == EditorTool.Scale ? "[Scale]" : "Scale") + " (R)"))
                EditorContext.ActiveTool = EditorTool.Scale;

            ImGui.SameLine();
            bool snap = GizmoSnapEnabled;
            if (ImGui.Checkbox("Snap", ref snap))
                GizmoSnapEnabled = snap;

            ImGui.SameLine();
            bool pixelSnap = _pixelSnapEnabled;
            if (ImGui.Checkbox("Pixel", ref pixelSnap))
                _pixelSnapEnabled = pixelSnap;

            ImGui.SameLine();
            ImGui.SetNextItemWidth(90.0f);
            float step = GizmoSnapStep;
            if (ImGui.InputFloat("Step", ref step, 1.0f))
                GizmoSnapStep = Clamp(step, 1.0f, 1024.0f);

            ImGui.SameLine();
            if (ImGui.Button("Focus"))
                FocusCamera();

            ImGui.SameLine();
            if (ImGui.Button("Frame Selected"))
                FrameSelectedEntity();
        }

        // ── Play Controls ──────────────────────────────────────────────

        private static void DrawPlayControls()
        {
            int simulationState = EditorBridge.GetSimulationState();
            bool isPlaying = simulationState == EditorBridge.SimulationPlay;
            bool isPaused = simulationState == EditorBridge.SimulationPause;
            bool isEdit = simulationState == EditorBridge.SimulationEdit;

            if (isEdit)
            {
                if (ImGui.Button("Play"))
                    EditorApplication.EnterPlayMode();
            }
            else
            {
                if (ImGui.Button("Stop"))
                    EditorApplication.StopPlayMode();

                ImGui.SameLine();
                if (isPlaying)
                {
                    if (ImGui.Button("Pause"))
                        EditorApplication.TogglePauseState();
                }
                else if (isPaused)
                {
                    if (ImGui.Button("Resume"))
                        EditorApplication.TogglePauseState();
                }
            }
        }

        // ── Resolution Selector ────────────────────────────────────────

        private static void DrawResolutionSelector()
        {
            ImGui.SetNextItemWidth(160.0f);
            string currentLabel = _selectedResolutionIndex >= 0 && _selectedResolutionIndex < _resolutionLabels.Length
                ? _resolutionLabels[_selectedResolutionIndex]
                : _resolutionLabels[0];

            if (ImGui.BeginCombo("##Resolution", currentLabel))
            {
                for (int i = 0; i < _resolutionLabels.Length; i++)
                {
                    bool selected = (i == _selectedResolutionIndex);
                    if (ImGui.Selectable(_resolutionLabels[i], selected))
                        _selectedResolutionIndex = i;
                }
                ImGui.EndCombo();
            }

            if (_selectedResolutionIndex == _resolutionLabels.Length - 1) // Custom
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(60.0f);
                ImGui.InputFloat("W##CustomRes", ref _customWidth, 0.0f);
                ImGui.SameLine();
                ImGui.Text("x");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(60.0f);
                ImGui.InputFloat("H##CustomRes", ref _customHeight, 0.0f);

                if (_customWidth < 1.0f) _customWidth = 1.0f;
                if (_customHeight < 1.0f) _customHeight = 1.0f;
                if (_customWidth > 7680.0f) _customWidth = 7680.0f;
                if (_customHeight > 4320.0f) _customHeight = 4320.0f;
            }
        }

        // ── Viewport Layout (aspect ratio letterbox) ──────────────────

        private static void ComputeViewportLayout(float availWidth,
                                                  float availHeight,
                                                  out float renderWidth,
                                                  out float renderHeight,
                                                  out float offsetX,
                                                  out float offsetY)
        {
            renderWidth = availWidth;
            renderHeight = availHeight;
            offsetX = 0.0f;
            offsetY = 0.0f;

            float aspect = 0.0f;
            if (_selectedResolutionIndex > 0 && _selectedResolutionIndex < _resolutionAspects.Length)
                aspect = _resolutionAspects[_selectedResolutionIndex];

            // Custom resolution
            if (_selectedResolutionIndex == _resolutionLabels.Length - 1 && _customHeight > 0.0f)
                aspect = _customWidth / _customHeight;

            if (aspect <= 0.0f)
                return; // Free aspect — fill entire area

            float containerAspect = availWidth / availHeight;
            if (containerAspect > aspect)
            {
                // Container is wider — pillarbox
                renderWidth = availHeight * aspect;
                renderHeight = availHeight;
                offsetX = (availWidth - renderWidth) * 0.5f;
            }
            else
            {
                // Container is taller — letterbox
                renderWidth = availWidth;
                renderHeight = availWidth / aspect;
                offsetY = (availHeight - renderHeight) * 0.5f;
            }
        }

        // ── Error Overlay ──────────────────────────────────────────────

        private static void DrawErrorOverlay(float areaX,
                                             float areaY,
                                             float areaW,
                                             float areaH,
                                             ScriptValidationSnapshot snapshot)
        {
            // Build the error message
            string title = "Script Compile Error";
            string detail = string.Empty;
            if (snapshot.CompileErrorLines != null && snapshot.CompileErrorLines.Length > 0)
            {
                // Show first few error lines
                int maxLines = snapshot.CompileErrorLines.Length < 6 ? snapshot.CompileErrorLines.Length : 6;
                for (int i = 0; i < maxLines; i++)
                {
                    if (i > 0) detail += "\n";
                    string line = snapshot.CompileErrorLines[i];
                    if (line.Length > 90)
                        line = line.Substring(0, 87) + "...";
                    detail += line;
                }
                if (snapshot.CompileErrorLines.Length > maxLines)
                    detail += "\n... +" + (snapshot.CompileErrorLines.Length - maxLines) + " more errors";
            }
            else
            {
                detail = "Fix script errors before entering Play mode.\nCheck the Console for details.";
            }

            string fullText = title + "\n\n" + detail;

            float textW;
            float textH;
            ImGui.CalcTextSize(fullText, out textW, out textH);

            float padX = 24.0f;
            float padY = 16.0f;
            float boxW = textW + padX * 2.0f;
            float boxH = textH + padY * 2.0f;

            if (boxW > areaW * 0.9f)
                boxW = areaW * 0.9f;
            if (boxH > areaH * 0.85f)
                boxH = areaH * 0.85f;

            float boxX = areaX + (areaW - boxW) * 0.5f;
            float boxY = areaY + (areaH - boxH) * 0.5f;

            // Dark rounded background
            ImGui.DrawRectFilledRounded(boxX, boxY, boxW, boxH,
                                        0.12f, 0.12f, 0.12f, 0.92f, 10.0f);

            // Title text (white)
            float titleW;
            float titleH;
            ImGui.CalcTextSize(title, out titleW, out titleH);
            float titleX = boxX + (boxW - titleW) * 0.5f;
            float titleY = boxY + padY;
            ImGui.DrawText(titleX, titleY, title, 1.0f, 0.35f, 0.35f, 1.0f);

            // Detail text
            float detailY = titleY + titleH + 8.0f;
            float detailX = boxX + padX;
            ImGui.DrawText(detailX, detailY, detail, 0.85f, 0.85f, 0.85f, 1.0f);
        }

        // ── Stats Overlay ──────────────────────────────────────────────

        private static void DrawStatsOverlay(float areaX,
                                             float areaY,
                                             float areaW,
                                             float areaH)
        {
            int simState = EditorBridge.GetSimulationState();
            string stateText = simState == EditorBridge.SimulationPlay ? "Playing"
                             : simState == EditorBridge.SimulationPause ? "Paused"
                             : "Edit";

            string resText = ((int)areaW) + " x " + ((int)areaH);
            string aspectLabel = _selectedResolutionIndex >= 0 && _selectedResolutionIndex < _resolutionLabels.Length
                ? _resolutionLabels[_selectedResolutionIndex]
                : "Free Aspect";

            string statsText = stateText + "  |  " + resText + "  |  " + aspectLabel;

            float textW;
            float textH;
            ImGui.CalcTextSize(statsText, out textW, out textH);

            float padX = 8.0f;
            float padY = 4.0f;
            float boxW = textW + padX * 2.0f;
            float boxH = textH + padY * 2.0f;

            float boxX = areaX + 4.0f;
            float boxY = areaY + 4.0f;

            ImGui.DrawRectFilledRounded(boxX, boxY, boxW, boxH,
                                        0.0f, 0.0f, 0.0f, 0.55f, 6.0f);
            ImGui.DrawText(boxX + padX, boxY + padY, statsText, 0.9f, 0.9f, 0.9f, 1.0f);
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

        private static void HandleAssetDropOnViewport(bool hovered)
        {
            if (!hovered || !EditorContext.AssetDragActive)
                return;

            if (!Input.GetMouseButtonUp(MouseButton.Left))
                return;

            string droppedPath = EditorContext.DraggedAssetPath;
            EditorContext.ClearDraggedAsset();

            if (string.IsNullOrWhiteSpace(droppedPath) || !File.Exists(droppedPath))
                return;

            if (SceneEditor.IsSceneFilePath(droppedPath))
            {
                SceneEditor.LoadSceneFromPath(droppedPath);
                return;
            }

            if (!TryGetViewportMouseWorldPosition(out float worldX, out float worldY))
                return;

            if (!TryInstantiateDroppedAsset(droppedPath, worldX, worldY))
                ProjectOperations.SetStatusMessage("Drop not supported in Scene View: " + Path.GetFileName(droppedPath));
        }

        private static void DrawAssetDropOverlay(bool hovered)
        {
            if (!EditorContext.AssetDragActive)
                return;

            if (_gameViewWidth <= 1.0f || _gameViewHeight <= 1.0f)
                return;

            string droppedPath = EditorContext.DraggedAssetPath;
            bool supported = IsDroppedAssetSupportedForScene(droppedPath);

            if (!hovered)
                return;

            float r = supported ? 0.20f : 0.95f;
            float g = supported ? 0.85f : 0.35f;
            float b = supported ? 0.35f : 0.25f;
            ImGui.DrawRect(_gameViewPosX,
                           _gameViewPosY,
                           _gameViewWidth,
                           _gameViewHeight,
                           r,
                           g,
                           b,
                           0.95f,
                           3.0f);

            string fileName = Path.GetFileName(droppedPath);
            if (string.IsNullOrEmpty(fileName))
                fileName = "<asset>";

            if (supported)
                ImGui.SetTooltip("Drop to Scene: " + fileName);
            else
                ImGui.SetTooltip("Unsupported in Scene drop: " + fileName);
        }

        private static bool IsDroppedAssetSupportedForScene(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            return SceneEditor.IsSceneFilePath(path)
                   || IsImageAssetPath(path)
                   || IsAnimationAssetPath(path)
                   || IsScriptAssetPath(path);
        }

        private static bool TryGetViewportMouseWorldPosition(out float worldX, out float worldY)
        {
            worldX = 0.0f;
            worldY = 0.0f;

            if (_gameViewWidth <= 1.0f || _gameViewHeight <= 1.0f)
                return false;

            float centerX = _gameViewWidth * 0.5f;
            float centerY = _gameViewHeight * 0.5f;

            float mouseX = Input.GetMousePosX();
            float mouseYTopLeft = Input.GetMousePosY();
            float mouseLocalX = mouseX - _gameViewPosX;
            float mouseLocalYTopLeft = mouseYTopLeft - _gameViewPosY;
            float mouseLocalYBottomLeft = _gameViewHeight - mouseLocalYTopLeft;

            worldX = _editorCamera.ScreenToWorldX(mouseLocalX, centerX);
            worldY = _editorCamera.ScreenToWorldY(mouseLocalYBottomLeft, centerY);
            return true;
        }

        private static bool TryInstantiateDroppedAsset(string droppedPath, float worldX, float worldY)
        {
            string normalizedPath = NormalizeAssetPathForSerialization(droppedPath);
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(droppedPath);

            if (IsImageAssetPath(droppedPath))
            {
                uint entityId = CreateEntityAt(worldX, worldY, 128.0f, 128.0f, fileNameWithoutExt);
                EnsureComponent(entityId, ComponentType.Sprite);
                EditorBridge.SetSpriteTexturePath(entityId, normalizedPath);
                EditorContext.SelectedEntityId = (int)entityId;
                ProjectOperations.SetStatusMessage("Created sprite entity from drop: " + Path.GetFileName(droppedPath));
                return true;
            }

            if (IsAnimationAssetPath(droppedPath))
            {
                uint entityId = CreateEntityAt(worldX, worldY, 128.0f, 128.0f, fileNameWithoutExt);
                EnsureComponent(entityId, ComponentType.Animator);
                EntityManager.SetAnimatorClipPath(entityId, normalizedPath);
                EditorContext.SelectedEntityId = (int)entityId;
                ProjectOperations.SetStatusMessage("Created animator entity from drop: " + Path.GetFileName(droppedPath));
                return true;
            }

            if (IsScriptAssetPath(droppedPath))
            {
                uint entityId = CreateEntityAt(worldX, worldY, 128.0f, 128.0f, fileNameWithoutExt);
                EnsureComponent(entityId, ComponentType.Script);

                string resolvedType = TryResolveScriptTypeFromFileName(fileNameWithoutExt);
                if (!string.IsNullOrEmpty(resolvedType))
                {
                    EditorBridge.SetScriptTypeName(entityId, resolvedType);
                    ProjectOperations.SetStatusMessage("Created scripted entity from drop: " + resolvedType);
                }
                else
                {
                    ProjectOperations.SetStatusMessage("Script dropped, but compiled type not found yet: " + fileNameWithoutExt);
                }

                EditorContext.SelectedEntityId = (int)entityId;
                return true;
            }

            return false;
        }

        private static uint CreateEntityAt(float x, float y, float width, float height, string baseName)
        {
            uint entityId = EntityManager.CreateEntity();
            if (EntityManager.HasTransform(entityId))
                EntityManager.SetTransform(entityId, x, y, width, height);
            else
                EntityManager.AddTransform(entityId);

            EntityManager.SetTransform(entityId, x, y, width, height);

            string entityName = string.IsNullOrWhiteSpace(baseName) ? ("Entity " + entityId) : baseName;
            EntityManager.SetEntityName(entityId, entityName);
            EditorBridge.SetParentEntity(entityId, 0);
            return entityId;
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
            DrawUiCanvasBoundsOverlay();

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
                        if (EditorContext.ActiveTool == EditorTool.Rotate)
                        {
                            gizmoChanged = ImGuizmo.Manipulate2DRotate(_gameViewPosX,
                                                                       _gameViewPosY,
                                                                       _gameViewWidth,
                                                                       _gameViewHeight,
                                                                       _editorCamera.X,
                                                                       _editorCamera.Y,
                                                                       _editorCamera.Zoom,
                                                                       ref gizmoX,
                                                                       ref gizmoY,
                                                                       ref gizmoRotation,
                                                                       width,
                                                                       height);
                        }
                        else if (EditorContext.ActiveTool == EditorTool.Scale)
                        {
                            gizmoChanged = ImGuizmo.Manipulate2DScale(_gameViewPosX,
                                                                      _gameViewPosY,
                                                                      _gameViewWidth,
                                                                      _gameViewHeight,
                                                                      _editorCamera.X,
                                                                      _editorCamera.Y,
                                                                      _editorCamera.Zoom,
                                                                      ref gizmoX,
                                                                      ref gizmoY,
                                                                      ref gizmoWidth,
                                                                      ref gizmoHeight,
                                                                      gizmoRotation);
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
                    }

                    if (gizmoChanged)
                    {
                        if (!timelineGizmoActive && GizmoSnapEnabled)
                        {
                            if (EditorContext.ActiveTool == EditorTool.Scale)
                            {
                                gizmoWidth = SnapValue(gizmoWidth, GizmoSnapStep);
                                gizmoHeight = SnapValue(gizmoHeight, GizmoSnapStep);
                            }
                            else if (EditorContext.ActiveTool == EditorTool.Rotate)
                            {
                                gizmoRotation = SnapValue(gizmoRotation, 15.0f);
                            }
                            else
                            {
                                gizmoX = SnapValue(gizmoX, GizmoSnapStep);
                                gizmoY = SnapValue(gizmoY, GizmoSnapStep);
                            }
                        }

                        if (!timelineGizmoActive && _pixelSnapEnabled)
                        {
                            gizmoX = SnapValue(gizmoX, 1.0f);
                            gizmoY = SnapValue(gizmoY, 1.0f);
                            gizmoWidth = SnapValue(gizmoWidth, 1.0f);
                            gizmoHeight = SnapValue(gizmoHeight, 1.0f);
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

        private static void DrawUiCanvasBoundsOverlay()
        {
            int entityCount = EntityManager.GetEntityCount();
            int canvasIndex = 0;

            for (int i = 0; i < entityCount; ++i)
            {
                uint entityId = EntityManager.GetEntityIdAtIndex(i);
                if (!EntityManager.HasUiCanvas(entityId))
                    continue;

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
                    continue;
                }

                if (!enabled || renderMode != UiCanvasRenderModeScreenSpaceOverlay)
                    continue;

                float inset = 1.0f + (canvasIndex * 3.0f);
                float drawX = _gameViewPosX + inset;
                float drawY = _gameViewPosY + inset;
                float drawWidth = _gameViewWidth - (inset * 2.0f);
                float drawHeight = _gameViewHeight - (inset * 2.0f);
                if (drawWidth < 1.0f || drawHeight < 1.0f)
                    continue;

                bool selected = SelectedEntityId >= 0 && (uint)SelectedEntityId == entityId;
                float alpha = selected ? 0.98f : 0.82f;
                float thickness = selected ? 2.2f : 1.4f;
                ImGui.DrawRect(drawX,
                               drawY,
                               drawWidth,
                               drawHeight,
                               0.96f,
                               0.96f,
                               0.96f,
                               alpha,
                               thickness);

                ++canvasIndex;
            }
        }
    }
}
