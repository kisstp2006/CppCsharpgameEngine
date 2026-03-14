using System;
using System.IO;

using Engine;

namespace EngineEditor
{
    internal static class BootManager
    {
        private static string _phase2AttachAttemptedPath = string.Empty;

        public static void OnEditorStart(int bootPhase)
        {
            _phase2AttachAttemptedPath = string.Empty;

            if (bootPhase == 2)
            {
                TryAttachProjectForEditorPhase();
                Console.WriteLine("[Editor] OnEditorStart -> Editor phase.");
                return;
            }

            EditorContext.ShowProjectManagerView = true;
            EditorContext.StatusMessage = "No project loaded.";
            Console.WriteLine("[Editor] OnEditorStart -> Project Selector phase.");
        }

        public static void TryAttachProjectForEditorPhase()
        {
            if (ProjectOperations.HasOpenProject())
            {
                EditorContext.ShowProjectManagerView = false;
                return;
            }

            string projectPath = EditorBridge.GetNativeProjectPath();
            if (string.IsNullOrEmpty(projectPath))
                projectPath = EditorBridge.GetSelectedProjectPath();

            if (string.IsNullOrEmpty(projectPath))
                return;

            if (string.Equals(_phase2AttachAttemptedPath, projectPath, StringComparison.OrdinalIgnoreCase))
                return;

            _phase2AttachAttemptedPath = projectPath;

            if (EditorBridge.IsScriptReloadInProgress())
                ProjectOperations.RestoreFromHotReload(projectPath);
            else
                ProjectOperations.OpenProject(projectPath);

            if (!ProjectOperations.HasOpenProject())
                return;

            string projectName = Path.GetFileName(projectPath);
            if (string.IsNullOrEmpty(projectName))
                projectName = "Untitled";

            EditorContext.StatusMessage = "Project loaded: " + projectName;
            EditorContext.ShowProjectManagerView = false;
        }
    }
}
