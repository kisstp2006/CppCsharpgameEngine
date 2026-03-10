using System;
using System.IO;

using Engine;

namespace EngineEditor
{
    internal static class ProjectManager
    {
        private static string _projectsRoot = string.Empty;
        private static string[] _projectPaths = new string[0];
        private static int _selectedProjectIndex = -1;
        private static string _projectSearchText = string.Empty;
        private static string _renameProjectName = string.Empty;

        public static string ProjectsRoot => _projectsRoot;
        public static string[] ProjectPaths => _projectPaths;
        public static int SelectedProjectIndex => _selectedProjectIndex;

        public static void Initialize(string editorConfigDir)
        {
            string cwd = Directory.GetCurrentDirectory();
            _projectsRoot = Path.Combine(cwd, "projects");
            Directory.CreateDirectory(_projectsRoot);
            RefreshProjectList();
        }

        public static void RefreshProjectList()
        {
            try
            {
                if (!Directory.Exists(_projectsRoot))
                    Directory.CreateDirectory(_projectsRoot);

                _projectPaths = Directory.GetDirectories(_projectsRoot);
                Array.Sort(_projectPaths, StringComparer.OrdinalIgnoreCase);

                if (_selectedProjectIndex >= _projectPaths.Length)
                    _selectedProjectIndex = _projectPaths.Length - 1;
            }
            catch (Exception ex)
            {
                ProjectOperations.SetStatusMessage("Refresh failed: " + ex.Message);
            }
        }

        public static bool TryGetSelectedProjectPath(out string projectPath)
        {
            if (_selectedProjectIndex >= 0 && _selectedProjectIndex < _projectPaths.Length)
            {
                projectPath = _projectPaths[_selectedProjectIndex];
                return true;
            }

            projectPath = string.Empty;
            return false;
        }

        public static void SelectProject(int index, string projectName)
        {
            _selectedProjectIndex = index;
            _renameProjectName = projectName;
        }

        public static void ClearSelection()
        {
            _renameProjectName = string.Empty;
        }

        public static bool MatchesProjectSearch(string projectName)
        {
            if (string.IsNullOrWhiteSpace(_projectSearchText))
                return true;

            return projectName.IndexOf(_projectSearchText.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static void DrawProjectPanel()
        {
            if (ImGui.Begin("Project Manager"))
            {
                DrawProjectToolbar();
                ImGui.Separator();

                if (ImGui.BeginChild("ProjectListPane", 420.0f, 0.0f, true))
                {
                    DrawProjectListPane();
                }
                ImGui.EndChild();

                ImGui.SameLine();

                if (ImGui.BeginChild("ProjectDetailsPane", 0.0f, 0.0f, true))
                {
                    DrawProjectDetailsPane();
                }
                ImGui.EndChild();

                ImGui.Separator();
                ImGui.Text("Status: " + ProjectOperations.StatusMessage);
            }
            ImGui.End();
        }

        private static void DrawProjectToolbar()
        {
            ImGui.SetNextItemWidth(280.0f);
            string updatedSearch = ImGui.InputText("Search Projects", _projectSearchText);
            if (updatedSearch != null)
                _projectSearchText = updatedSearch;

            ImGui.SameLine();
            if (ImGui.Button("Refresh"))
                RefreshProjectList();

            ImGui.SameLine();
            if (ImGui.Button("Open Last"))
                ProjectOperations.OpenLastProject();

            ImGui.SameLine();
            if (ImGui.Button("New Project"))
            {
                PopupDialogs.OpenCreateProjectPopup(_projectsRoot);
            }

            if (ProjectOperations.HasOpenProject())
            {
                ImGui.SameLine();
                if (ImGui.Button("Open Editor"))
                    EditorHost.SetShowProjectManagerView(false);
            }

            ImGui.Text("Root: " + _projectsRoot);
            ImGui.Text("Active: " + (string.IsNullOrEmpty(ProjectOperations.ActiveProjectPath) ? "<none>" : Path.GetFileName(ProjectOperations.ActiveProjectPath)));
        }

        private static void DrawProjectListPane()
        {
            ImGui.Text("All Projects");
            ImGui.Separator();

            bool anyVisible = false;

            for (int i = 0; i < _projectPaths.Length; ++i)
            {
                string projectPath = _projectPaths[i];
                string projectName = Path.GetFileName(projectPath);
                if (!MatchesProjectSearch(projectName))
                    continue;

                anyVisible = true;
                bool selected = i == _selectedProjectIndex;

                if (ImGui.Selectable(projectName + "##ProjectListItem" + i, selected))
                {
                    SelectProject(i, projectName);
                }
            }

            if (!anyVisible)
                ImGui.Text("No projects match the current search.");
        }

        private static void DrawProjectDetailsPane()
        {
            ImGui.Text("Project Settings");
            ImGui.Separator();

            if (!TryGetSelectedProjectPath(out string selectedProjectPath))
            {
                _renameProjectName = string.Empty;
                ImGui.Text("Select a project from the left list.");
                return;
            }

            if (!Directory.Exists(selectedProjectPath))
            {
                ImGui.Text("Selected project path no longer exists.");
                return;
            }

            ImGui.Text("Project Name");
            ImGui.SetNextItemWidth(320.0f);
            string updatedRename = ImGui.InputText("##RenameProjectName", _renameProjectName);
            if (updatedRename != null)
                _renameProjectName = updatedRename;

            ImGui.Text("Location");
            string parentPath = Path.GetDirectoryName(selectedProjectPath);
            ImGui.Text(string.IsNullOrEmpty(parentPath) ? "<unknown>" : parentPath);

            ImGui.Text("Path");
            ImGui.Text(selectedProjectPath);

            if (ImGui.Button("Open Project"))
                ProjectOperations.OpenProject(selectedProjectPath);

            ImGui.SameLine();
            if (ImGui.Button("Rename"))
                ProjectOperations.RenameProject(selectedProjectPath, _renameProjectName.Trim());

            ImGui.SameLine();
            if (ImGui.Button("Delete"))
            {
                PopupDialogs.OpenDeleteProjectPopup(selectedProjectPath);
            }
        }
    }
}
