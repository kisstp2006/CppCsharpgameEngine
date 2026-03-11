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

        public static string StatusMessage => _statusMessage;

        public static void OnEditorStart()
        {
            string cwd = Directory.GetCurrentDirectory();
            _editorConfigDir = Path.Combine(cwd, ".editor");
            Directory.CreateDirectory(_editorConfigDir);
            _playSnapshotPath = Path.Combine(_editorConfigDir, "playmode_snapshot.scene.bin");

            ProjectManager.Initialize(_editorConfigDir);
            ProjectOperations.Initialize(_editorConfigDir);
            ProjectOperations.OpenLastProjectSilently();

            AssetPanel.RegisterEditorOptions();
            AssetPanel.RegisterAssetContextMenu();
            SceneEditor.RegisterEditorOptions();
            SceneEditor.RegisterAssetContextMenu();
            ScriptFieldInspector.RegisterEditorOptions();
            ScriptFieldInspector.RegisterAssetContextMenu();

            _showProjectManagerView = true;
            _statusMessage = "No project loaded.";
            Console.WriteLine("[Editor] OnEditorStart called.");
        }

        public static void OnEditorUpdate(float deltaTime)
        {
            bool hasOpenProject = ProjectOperations.HasOpenProject();

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


