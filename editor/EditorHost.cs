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

            _showProjectManagerView = true;
            _statusMessage = "No project loaded.";
            Console.WriteLine("[Editor] OnEditorStart called.");
        }

        public static void OnEditorUpdate(float deltaTime)
        {
            bool hasOpenProject = ProjectOperations.HasOpenProject();
            if (!hasOpenProject)
            {
                _showProjectManagerView = true;
                SceneEditor.ResetSelection();
                ProjectManager.DrawProjectPanel();
                return;
            }

            if (_showProjectManagerView)
            {
                ProjectManager.DrawProjectPanel();
                return;
            }

            SceneEditor.DrawSceneTreePanel();
            SceneEditor.DrawInspectorPanel();
            AssetPanel.DrawAssetPanel();
            SceneEditor.UpdateTick(deltaTime);
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


