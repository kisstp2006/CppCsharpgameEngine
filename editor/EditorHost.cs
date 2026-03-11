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

        public static string StatusMessage => _statusMessage;

        public static void OnEditorStart()
        {
            string cwd = Directory.GetCurrentDirectory();
            _editorConfigDir = Path.Combine(cwd, ".editor");
            Directory.CreateDirectory(_editorConfigDir);

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
            SceneEditor.DrawInspectorPanel();
            AssetPanel.DrawAssetPanel();
            SceneEditor.UpdateTick(deltaTime);
            EditorOptionsWindow.Draw();
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
            ImGui.SameLine();
            if (ImGui.Button("Project Manager"))
                _showProjectManagerView = true;

            ImGui.SameLine();
            if (ImGui.Button("Scene Workspace"))
                _showProjectManagerView = false;

            ImGui.SameLine();
            if (ImGui.Button("Editor Options"))
                EditorOptionsWindow.Toggle();

            if (!_showProjectManagerView)
            {
                ImGui.SameLine();
                if (ImGui.Button("Save Scene"))
                    SceneEditor.SaveScene();

                ImGui.SameLine();
                if (ImGui.Button("Create Entity"))
                    SceneEditor.CreateEntityAndSelect();

                ImGui.SameLine();
                int selectedEntityId = SceneEditor.SelectedEntityId;
                if (selectedEntityId >= 0)
                    ImGui.Text("Selected: Entity " + selectedEntityId);
                else
                    ImGui.Text("Selected: <none>");

                ImGui.SameLine();
                string activeScenePath = SceneEditor.ActiveScenePath;
                if (string.IsNullOrEmpty(activeScenePath))
                    ImGui.Text("Scene: <unsaved>");
                else
                    ImGui.Text("Scene: " + Path.GetFileName(activeScenePath));
            }

            ImGui.SameLine();
            ImGui.Text("Project: " + activeProjectName);

            ImGui.SameLine();
            ImGui.Text("Status: " + ProjectOperations.StatusMessage);

            ImGui.EndTopBar();
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
            Console.WriteLine("[Editor] OnEditorShutdown called.");
        }
    }
}


