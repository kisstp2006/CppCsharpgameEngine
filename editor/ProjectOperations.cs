using System;
using System.Collections.Generic;
using System.IO;

using Engine;

namespace EngineEditor
{
    internal static class ProjectOperations
    {
        private static string _activeProjectPath = string.Empty;
        private static string _editorConfigDir = string.Empty;
        private static string _lastProjectFile = string.Empty;
        private static string _recentProjectsFile = string.Empty;
        private static string _statusMessage = "No project loaded.";
        private static readonly List<string> _recentProjectPaths = new List<string>();

        private const int MaxRecentProjects = 10;

        public static string ActiveProjectPath => _activeProjectPath;
        public static string StatusMessage => _statusMessage;

        public static string[] GetRecentProjects()
        {
            return _recentProjectPaths.ToArray();
        }

        public static void Initialize(string editorConfigDir)
        {
            _editorConfigDir = editorConfigDir;
            _lastProjectFile = Path.Combine(editorConfigDir, "last_project.txt");
            _recentProjectsFile = Path.Combine(editorConfigDir, "recent_projects.txt");
            Directory.CreateDirectory(editorConfigDir);
            LoadRecentProjects();
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
                if (string.IsNullOrWhiteSpace(projectPath))
                {
                    _statusMessage = "Open failed: invalid project path.";
                    return;
                }

                string normalizedProjectPath = NormalizeProjectPath(projectPath);
                if (!Directory.Exists(normalizedProjectPath))
                {
                    _statusMessage = "Open failed: missing folder.";
                    return;
                }

                _activeProjectPath = normalizedProjectPath;
                _statusMessage = "Opened project: " + Path.GetFileName(normalizedProjectPath);
                SaveLastProjectPath(normalizedProjectPath);
                AddRecentProjectPath(normalizedProjectPath);
                SyncRuntimeScriptPaths(normalizedProjectPath);
                bool upgradedLegacyTemplate = ProjectCodeGenerator.TryUpgradeLegacyScriptTemplate(normalizedProjectPath);
                ScriptComponentValidation.RequestImmediateBuildForActiveProject();

                if (upgradedLegacyTemplate)
                    _statusMessage += " Migrated legacy script template to MonoBehaviour (backup: ScriptEntry.cs.legacy.bak).";
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

        public static void CreateProject(string projectPath,
                                         string templateName,
                                         bool generateStarterContent,
                                         bool generateStarterScene)
        {
            try
            {
                string requestedName = Path.GetFileName(projectPath);
                string finalProjectPath = GetUniqueProjectPath(projectPath);
                bool resolvedConflict = !string.Equals(finalProjectPath, projectPath, StringComparison.OrdinalIgnoreCase);

                ProjectCodeGenerator.GenerateProject(finalProjectPath,
                                                    Path.GetFileName(finalProjectPath),
                                                    templateName,
                                                    generateStarterContent,
                                                    generateStarterScene);

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
                ReplaceRecentProjectPath(oldProjectPath, newProjectPath);

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

                RemoveRecentProjectPath(projectPath);

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

        private static void LoadRecentProjects()
        {
            _recentProjectPaths.Clear();

            if (!File.Exists(_recentProjectsFile))
                return;

            try
            {
                string[] lines = File.ReadAllLines(_recentProjectsFile);
                for (int i = 0; i < lines.Length; ++i)
                {
                    string candidate = lines[i].Trim();
                    if (string.IsNullOrEmpty(candidate))
                        continue;

                    string normalizedPath;
                    try
                    {
                        normalizedPath = NormalizeProjectPath(candidate);
                    }
                    catch
                    {
                        continue;
                    }

                    if (!Directory.Exists(normalizedPath))
                        continue;

                    if (_recentProjectPaths.Exists(path => string.Equals(path, normalizedPath, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    _recentProjectPaths.Add(normalizedPath);
                    if (_recentProjectPaths.Count >= MaxRecentProjects)
                        break;
                }

                SaveRecentProjects();
            }
            catch (Exception ex)
            {
                _statusMessage = "Warning: could not load recent projects: " + ex.Message;
            }
        }

        private static void AddRecentProjectPath(string projectPath)
        {
            _recentProjectPaths.RemoveAll(path => string.Equals(path, projectPath, StringComparison.OrdinalIgnoreCase));
            _recentProjectPaths.Insert(0, projectPath);

            if (_recentProjectPaths.Count > MaxRecentProjects)
                _recentProjectPaths.RemoveRange(MaxRecentProjects, _recentProjectPaths.Count - MaxRecentProjects);

            SaveRecentProjects();
        }

        private static void ReplaceRecentProjectPath(string oldProjectPath, string newProjectPath)
        {
            string normalizedOldPath = NormalizeProjectPath(oldProjectPath);
            string normalizedNewPath = NormalizeProjectPath(newProjectPath);

            bool removedOld = _recentProjectPaths.RemoveAll(path => string.Equals(path, normalizedOldPath, StringComparison.OrdinalIgnoreCase)) > 0;
            _recentProjectPaths.RemoveAll(path => string.Equals(path, normalizedNewPath, StringComparison.OrdinalIgnoreCase));

            if (removedOld)
            {
                _recentProjectPaths.Insert(0, normalizedNewPath);
                if (_recentProjectPaths.Count > MaxRecentProjects)
                    _recentProjectPaths.RemoveRange(MaxRecentProjects, _recentProjectPaths.Count - MaxRecentProjects);
                SaveRecentProjects();
            }
        }

        private static void RemoveRecentProjectPath(string projectPath)
        {
            string normalizedPath;
            try
            {
                normalizedPath = NormalizeProjectPath(projectPath);
            }
            catch
            {
                return;
            }

            int removed = _recentProjectPaths.RemoveAll(path => string.Equals(path, normalizedPath, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
                SaveRecentProjects();
        }

        private static void SaveRecentProjects()
        {
            try
            {
                File.WriteAllLines(_recentProjectsFile, _recentProjectPaths);
            }
            catch (Exception ex)
            {
                _statusMessage = "Warning: could not save recent projects: " + ex.Message;
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

        private static void SyncRuntimeScriptPaths(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
                return;

            try
            {
                string scriptProjectPath = ResolveScriptProjectPath(projectRoot);
                string scriptAssemblyPath = ResolveScriptAssemblyPath(projectRoot, scriptProjectPath);

                EditorBridge.SetPreferredScriptProjectPath(scriptProjectPath ?? string.Empty);
                EditorBridge.SetPreferredScriptAssemblyPath(scriptAssemblyPath ?? string.Empty);
                EditorBridge.RequestScriptAssemblyReload();
            }
            catch
            {
                // Keep project open flow resilient even if runtime path sync fails.
            }
        }

        private static string ResolveScriptProjectPath(string projectRoot)
        {
            string projectJsonPath = Path.Combine(projectRoot, "project.json");
            string json = string.Empty;

            if (File.Exists(projectJsonPath))
            {
                try
                {
                    json = File.ReadAllText(projectJsonPath);
                }
                catch
                {
                    json = string.Empty;
                }
            }

            string scriptProjectRelative = ExtractJsonString(json, "scriptProject");
            if (!string.IsNullOrEmpty(scriptProjectRelative))
            {
                string candidate = Path.Combine(projectRoot, scriptProjectRelative);
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }

            string[] candidates = new string[0];
            try
            {
                candidates = Directory.GetFiles(projectRoot, "*.csproj", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                candidates = new string[0];
            }

            if (candidates.Length > 0)
                return Path.GetFullPath(candidates[0]);

            return string.Empty;
        }

        private static string ResolveScriptAssemblyPath(string projectRoot, string scriptProjectPath)
        {
            string projectJsonPath = Path.Combine(projectRoot, "project.json");
            string json = string.Empty;

            if (File.Exists(projectJsonPath))
            {
                try
                {
                    json = File.ReadAllText(projectJsonPath);
                }
                catch
                {
                    json = string.Empty;
                }
            }

            string assemblyRelative = ExtractJsonString(json, "assemblyPath");
            if (!string.IsNullOrEmpty(assemblyRelative))
            {
                string candidate = Path.Combine(projectRoot, assemblyRelative);
                return Path.GetFullPath(candidate);
            }

            string targetFramework = ExtractJsonString(json, "targetFramework");
            if (string.IsNullOrWhiteSpace(targetFramework))
                targetFramework = "net472";

            string projectName = string.Empty;
            if (!string.IsNullOrWhiteSpace(scriptProjectPath))
                projectName = Path.GetFileNameWithoutExtension(scriptProjectPath);

            if (string.IsNullOrWhiteSpace(projectName))
                projectName = Path.GetFileName(projectRoot);

            string fallback = Path.Combine(projectRoot, "bin", "Debug", targetFramework, projectName + ".dll");
            return Path.GetFullPath(fallback);
        }

        private static string ExtractJsonString(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
                return string.Empty;

            string needle = "\"" + key + "\"";
            int keyPos = json.IndexOf(needle, StringComparison.Ordinal);
            if (keyPos < 0)
                return string.Empty;

            int colonPos = json.IndexOf(':', keyPos + needle.Length);
            if (colonPos < 0)
                return string.Empty;

            int firstQuotePos = json.IndexOf('"', colonPos + 1);
            if (firstQuotePos < 0)
                return string.Empty;

            var valueChars = new List<char>();
            bool escaped = false;
            for (int i = firstQuotePos + 1; i < json.Length; ++i)
            {
                char c = json[i];
                if (escaped)
                {
                    switch (c)
                    {
                        case 'n':
                            valueChars.Add('\n');
                            break;
                        case 'r':
                            valueChars.Add('\r');
                            break;
                        case 't':
                            valueChars.Add('\t');
                            break;
                        default:
                            valueChars.Add(c);
                            break;
                    }

                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                    return new string(valueChars.ToArray());

                valueChars.Add(c);
            }

            return string.Empty;
        }

        private static string NormalizeProjectPath(string projectPath)
        {
            return Path.GetFullPath(projectPath.Trim());
        }
    }
}
