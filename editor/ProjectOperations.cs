using System;
using System.IO;

namespace EngineEditor
{
    internal static class ProjectOperations
    {
        private static string _activeProjectPath = string.Empty;
        private static string _editorConfigDir = string.Empty;
        private static string _lastProjectFile = string.Empty;
        private static string _statusMessage = "No project loaded.";

        public static string ActiveProjectPath => _activeProjectPath;
        public static string StatusMessage => _statusMessage;

        public static void Initialize(string editorConfigDir)
        {
            _editorConfigDir = editorConfigDir;
            _lastProjectFile = Path.Combine(editorConfigDir, "last_project.txt");
            Directory.CreateDirectory(editorConfigDir);
        }

        public static bool HasOpenProject()
        {
            return !string.IsNullOrEmpty(_activeProjectPath) && Directory.Exists(_activeProjectPath);
        }

        public static void SetStatusMessage(string message)
        {
            _statusMessage = message;
        }

        public static void OpenProject(string projectPath)
        {
            try
            {
                if (!Directory.Exists(projectPath))
                {
                    _statusMessage = "Open failed: missing folder.";
                    return;
                }

                _activeProjectPath = projectPath;
                _statusMessage = "Opened project: " + Path.GetFileName(projectPath);
                SaveLastProjectPath(projectPath);
            }
            catch (Exception ex)
            {
                _statusMessage = "Open failed: " + ex.Message;
            }
        }

        public static void OpenLastProject()
        {
            try
            {
                if (!File.Exists(_lastProjectFile))
                {
                    _statusMessage = "No last project saved.";
                    return;
                }

                string projectPath = File.ReadAllText(_lastProjectFile).Trim();
                if (string.IsNullOrEmpty(projectPath) || !Directory.Exists(projectPath))
                {
                    _statusMessage = "Last project path is invalid.";
                    return;
                }

                OpenProject(projectPath);
            }
            catch (Exception ex)
            {
                _statusMessage = "Open last failed: " + ex.Message;
            }
        }

        public static void OpenLastProjectSilently()
        {
            if (!File.Exists(_lastProjectFile))
                return;

            try
            {
                string projectPath = File.ReadAllText(_lastProjectFile).Trim();
                if (!string.IsNullOrEmpty(projectPath) && Directory.Exists(projectPath))
                    OpenProject(projectPath);
            }
            catch
            {
                // Ignore startup restore errors and keep editor usable.
            }
        }

        public static void CreateProject(string projectPath, string templateName)
        {
            try
            {
                string requestedName = Path.GetFileName(projectPath);
                string finalProjectPath = GetUniqueProjectPath(projectPath);
                bool resolvedConflict = !string.Equals(finalProjectPath, projectPath, StringComparison.OrdinalIgnoreCase);

                ProjectCodeGenerator.GenerateProject(finalProjectPath, Path.GetFileName(finalProjectPath), templateName);

                ProjectManager.RefreshProjectList();
                OpenProject(finalProjectPath);

                if (resolvedConflict)
                    _statusMessage = "Project exists, created as: " + Path.GetFileName(finalProjectPath) + " (requested: " + requestedName + ").";
                else
                    _statusMessage = "Created project: " + Path.GetFileName(finalProjectPath);
            }
            catch (Exception ex)
            {
                _statusMessage = "Create failed: " + ex.Message;
            }
        }

        public static void RenameProject(string oldProjectPath, string newProjectName)
        {
            try
            {
                if (!Directory.Exists(oldProjectPath))
                {
                    _statusMessage = "Rename failed: source project missing.";
                    return;
                }

                if (!StringUtilities.IsValidProjectName(newProjectName))
                {
                    _statusMessage = "Rename failed: invalid name.";
                    return;
                }

                string parentPath = Path.GetDirectoryName(oldProjectPath);
                if (string.IsNullOrEmpty(parentPath))
                {
                    _statusMessage = "Rename failed: invalid project parent path.";
                    return;
                }

                string requestedProjectPath = Path.Combine(parentPath, newProjectName);
                if (string.Equals(oldProjectPath, requestedProjectPath, StringComparison.OrdinalIgnoreCase))
                {
                    _statusMessage = "Rename skipped: same name.";
                    return;
                }

                string newProjectPath = GetUniqueProjectPath(requestedProjectPath);
                bool resolvedConflict = !string.Equals(newProjectPath, requestedProjectPath, StringComparison.OrdinalIgnoreCase);

                Directory.Move(oldProjectPath, newProjectPath);

                if (string.Equals(_activeProjectPath, oldProjectPath, StringComparison.OrdinalIgnoreCase))
                {
                    _activeProjectPath = newProjectPath;
                    SaveLastProjectPath(newProjectPath);
                }

                ProjectManager.RefreshProjectList();
                OpenProject(newProjectPath);

                if (resolvedConflict)
                    _statusMessage = "Name existed, renamed as: " + Path.GetFileName(newProjectPath);
                else
                    _statusMessage = "Renamed project to: " + newProjectName;
            }
            catch (Exception ex)
            {
                _statusMessage = "Rename failed: " + ex.Message;
            }
        }

        public static void DeleteProject(string projectPath)
        {
            try
            {
                if (!Directory.Exists(projectPath))
                {
                    _statusMessage = "Delete failed: project missing.";
                    return;
                }

                string projectName = Path.GetFileName(projectPath);
                Directory.Delete(projectPath, true);

                if (string.Equals(_activeProjectPath, projectPath, StringComparison.OrdinalIgnoreCase))
                {
                    _activeProjectPath = string.Empty;
                    if (File.Exists(_lastProjectFile))
                        File.Delete(_lastProjectFile);
                }

                ProjectManager.RefreshProjectList();
                _statusMessage = "Deleted project: " + projectName;
            }
            catch (Exception ex)
            {
                _statusMessage = "Delete failed: " + ex.Message;
            }
        }

        private static string GetUniqueProjectPath(string requestedPath)
        {
            if (!Directory.Exists(requestedPath))
                return requestedPath;

            string parentPath = Path.GetDirectoryName(requestedPath);
            if (string.IsNullOrEmpty(parentPath))
                return requestedPath;

            string baseName = Path.GetFileName(requestedPath);
            int index = 1;

            while (true)
            {
                string candidateName = baseName + " (" + index + ")";
                string candidatePath = Path.Combine(parentPath, candidateName);
                if (!Directory.Exists(candidatePath))
                    return candidatePath;
                ++index;
            }
        }

        private static void SaveLastProjectPath(string projectPath)
        {
            try
            {
                File.WriteAllText(_lastProjectFile, projectPath + Environment.NewLine);
            }
            catch (Exception ex)
            {
                _statusMessage = "Warning: could not save last project path: " + ex.Message;
            }
        }
    }
}
