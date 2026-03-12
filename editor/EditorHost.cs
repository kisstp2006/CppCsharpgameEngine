using System;
using System.IO;

using Engine;

namespace EngineEditor
{
    public static class EditorHost
    {
        private static string _statusMessage = "No project loaded.";
        private static bool _showProjectManagerView = true;
        private static string _editorConfigDir = string.Empty;
        private static string _playSnapshotPath = string.Empty;
        private static float _compileProgressPulse = 0.0f;
        private static bool _compileDialogOpen;

        private const string CompilePopupId = "##ScriptCompileBlockingModal";

        public static string StatusMessage => _statusMessage;

        public static void OnEditorStart()
        {
            string cwd = Directory.GetCurrentDirectory();
            _editorConfigDir = Path.Combine(cwd, ".editor");
            Directory.CreateDirectory(_editorConfigDir);
            _playSnapshotPath = Path.Combine(_editorConfigDir, "playmode_snapshot.scene.bin");

            ProjectManager.Initialize(_editorConfigDir);
            ProjectOperations.Initialize(_editorConfigDir);
            ScriptComponentValidation.Initialize(_editorConfigDir);
            ProjectOperations.OpenLastProjectSilently();

            AssetPanel.RegisterEditorOptions();
            AssetPanel.RegisterAssetContextMenu();
            SceneEditor.RegisterEditorOptions();
            SceneEditor.RegisterAssetContextMenu();
            ScriptComponentValidation.RegisterEditorOptions();
            ScriptFieldInspector.RegisterEditorOptions();
            ScriptFieldInspector.RegisterAssetContextMenu();

            _showProjectManagerView = true;
            _statusMessage = "No project loaded.";
            Console.WriteLine("[Editor] OnEditorStart called.");
        }

        public static void OnEditorUpdate(float deltaTime)
        {
            bool hasOpenProject = ProjectOperations.HasOpenProject();
            bool compileBlocking = DrawScriptBuildProgressWindow(deltaTime);

            if (compileBlocking)
                return;

            if (hasOpenProject)
                DrawTopBar();

            if (!hasOpenProject)
            {
                _showProjectManagerView = true;
                SceneEditor.ResetEditorState();
                ProjectManager.DrawProjectPanel();
                EditorOptionsWindow.Draw();
                return;
            }

            if (_showProjectManagerView)
            {
                SceneEditor.ResetEditorState();
                ProjectManager.DrawProjectPanel();
                EditorOptionsWindow.Draw();
                return;
            }

            SceneEditor.DrawSceneTreePanel();
            SceneEditor.DrawWorldViewportPanel(deltaTime);
            SceneEditor.DrawRuntimeGamePanel();
            SceneEditor.DrawInspectorPanel();
            AssetPanel.DrawAssetPanel();
            SceneEditor.UpdateTick(deltaTime);
            EditorOptionsWindow.Draw();
            EditorConsoleWindow.Draw();
        }

        private static bool DrawScriptBuildProgressWindow(float deltaTime)
        {
            ScriptValidationSnapshot snapshot = ScriptComponentValidation.GetSnapshot();
            bool busy = snapshot.IsCompiling;
            if (!busy)
            {
                _compileProgressPulse = 0.0f;

                if (_compileDialogOpen && ImGui.BeginPopupModal(CompilePopupId))
                {
                    ImGui.CloseCurrentPopup();
                    ImGui.EndPopup();
                }

                _compileDialogOpen = false;
                return false;
            }

            _compileProgressPulse += deltaTime * 0.65f;
            while (_compileProgressPulse > 1.0f)
                _compileProgressPulse -= 1.0f;

            float progress = 0.15f + (_compileProgressPulse * 0.75f);
            string message = "Compiling C# scripts...";

            ImGui.OpenPopup(CompilePopupId);
            if (ImGui.BeginPopupModal(CompilePopupId))
            {
                _compileDialogOpen = true;
                ImGui.Text(message);
                ImGui.Text(BuildProgressBarText(progress, 22));
                ImGui.Text(snapshot.CompileSummary);
                ImGui.EndPopup();
            }

            return true;
        }

        private static string BuildProgressBarText(float fraction, int segmentCount)
        {
            if (segmentCount < 4)
                segmentCount = 4;

            float clamped = Clamp01(fraction);
            int filled = (int)Math.Floor(clamped * segmentCount + 0.5f);
            if (filled < 0)
                filled = 0;
            if (filled > segmentCount)
                filled = segmentCount;

            string bar = new string('#', filled) + new string('-', segmentCount - filled);
            int percent = (int)Math.Floor(clamped * 100.0f + 0.5f);
            return "[" + bar + "] " + percent + "%";
        }

        private static float Clamp01(float value)
        {
            if (value < 0.0f)
                return 0.0f;
            if (value > 1.0f)
                return 1.0f;
            return value;
        }

        private static void DrawTopBar()
        {
            float topBarHeight = _showProjectManagerView ? 30.0f : 40.0f;
            if (!ImGui.BeginTopBar("##EditorTopBar", topBarHeight))
                return;

            string activeProjectName = Path.GetFileName(ProjectOperations.ActiveProjectPath);
            if (string.IsNullOrEmpty(activeProjectName))
                activeProjectName = "<none>";

            ImGui.Text("CppCSharp Editor");
            EditorUIHelpers.DrawInlineDivider();
            if (ImGui.Button("Project Manager"))
                _showProjectManagerView = true;

            ImGui.SameLine();
            if (ImGui.Button("Scene Workspace"))
                _showProjectManagerView = false;

            EditorUIHelpers.DrawInlineDivider();
            if (ImGui.Button("Editor Options"))
                EditorOptionsWindow.Toggle();

            ImGui.SameLine();
            if (ImGui.Button("Console"))
                EditorConsoleWindow.Toggle();

            if (!_showProjectManagerView)
            {
                EditorUIHelpers.DrawInlineDivider();
                if (ImGui.Button("Save Scene"))
                    SceneEditor.SaveScene();

                ImGui.SameLine();
                if (ImGui.Button("Create Entity"))
                    SceneEditor.CreateEntityAndSelect();

                EditorUIHelpers.DrawInlineDivider();
                DrawPlayModeControls();

                EditorUIHelpers.DrawInlineDivider();
                int selectedEntityId = SceneEditor.SelectedEntityId;
                if (selectedEntityId >= 0)
                    ImGui.Text("Selected: Entity " + selectedEntityId);
                else
                    ImGui.Text("Selected: <none>");

                EditorUIHelpers.DrawInlineDivider();
                string activeScenePath = SceneEditor.ActiveScenePath;
                if (string.IsNullOrEmpty(activeScenePath))
                    ImGui.Text("Scene: <unsaved>");
                else
                    ImGui.Text("Scene: " + Path.GetFileName(activeScenePath));
            }

            EditorUIHelpers.DrawInlineDivider();
            ImGui.Text("Project: " + activeProjectName);

            EditorUIHelpers.DrawInlineDivider();
            ImGui.Text("Status: " + ProjectOperations.StatusMessage);

            ImGui.EndTopBar();
        }

        private static void DrawPlayModeControls()
        {
            int simulationState = EditorBridge.GetSimulationState();
            bool playing = simulationState == EditorBridge.SimulationPlay;
            bool paused = simulationState == EditorBridge.SimulationPause;

            string playLabel = paused ? "Resume" : "Play";
            if (ImGui.Button(playLabel))
            {
                if (paused)
                {
                    EditorBridge.SetSimulationPaused(false);
                    ProjectOperations.SetStatusMessage("Resumed play mode.");
                }
                else
                {
                    EnterPlayMode();
                }
            }

            ImGui.SameLine();
            if (ImGui.Button("Pause"))
            {
                if (playing)
                {
                    EditorBridge.SetSimulationPaused(true);
                    ProjectOperations.SetStatusMessage("Paused play mode.");
                }
            }

            ImGui.SameLine();
            if (ImGui.Button("Stop"))
                StopPlayMode();

            ImGui.SameLine();
            if (playing)
                ImGui.Text("Mode: Playing");
            else if (paused)
                ImGui.Text("Mode: Paused");
            else
                ImGui.Text("Mode: Edit");
        }

        private static bool EnterPlayMode()
        {
            int simulationState = EditorBridge.GetSimulationState();
            if (simulationState == EditorBridge.SimulationPlay)
                return true;

            if (!ScriptComponentValidation.EnsureCompiledForPlay(out string compileGateMessage))
            {
                if (string.IsNullOrEmpty(compileGateMessage))
                    compileGateMessage = "Play blocked: script compile gate rejected start.";

                ProjectOperations.SetStatusMessage(compileGateMessage);
                return false;
            }

            ScriptValidationSnapshot scriptSnapshot = ScriptComponentValidation.GetSnapshot();
            if (scriptSnapshot.IsCompiling)
            {
                ProjectOperations.SetStatusMessage("Play blocked: script compilation is still running.");
                return false;
            }

            if (scriptSnapshot.HasCompileErrors)
            {
                ProjectOperations.SetStatusMessage("Play blocked: fix script compile errors first.");
                return false;
            }

            string snapshotDirectory = Path.GetDirectoryName(_playSnapshotPath);
            if (!string.IsNullOrEmpty(snapshotDirectory))
                Directory.CreateDirectory(snapshotDirectory);

            if (!EditorBridge.SaveScene(_playSnapshotPath, 1))
            {
                string error = EditorBridge.GetLastSceneIoStatus();
                if (string.IsNullOrEmpty(error))
                    error = "Play failed: could not create scene snapshot.";

                ProjectOperations.SetStatusMessage(error);
                return false;
            }

            EditorBridge.RequestScriptAssemblyReload();

            if (!EditorBridge.StartPlayMode())
            {
                string error = EditorBridge.GetLastSceneIoStatus();
                if (string.IsNullOrEmpty(error))
                    error = "Play failed: runtime rejected play mode transition.";

                ProjectOperations.SetStatusMessage(error);
                return false;
            }

            ProjectOperations.SetStatusMessage("Entered play mode.");
            return true;
        }

        private static bool StopPlayMode()
        {
            int simulationState = EditorBridge.GetSimulationState();
            if (simulationState == EditorBridge.SimulationEdit)
                return true;

            EditorBridge.StopPlayMode();

            if (!SceneEditor.RestoreSceneFromPlaySnapshot(_playSnapshotPath))
                return false;

            ProjectOperations.SetStatusMessage("Stopped play mode and restored pre-play scene state.");
            return true;
        }

        public static void SetStatusMessage(string message)
        {
            _statusMessage = message;
        }

        public static void SetShowProjectManagerView(bool show)
        {
            _showProjectManagerView = show;
        }

        public static void OnEditorShutdown()
        {
            StopPlayMode();
            Console.WriteLine("[Editor] OnEditorShutdown called.");
        }
    }
}


