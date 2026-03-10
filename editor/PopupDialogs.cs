using System;
using System.IO;

using Engine;

namespace EngineEditor
{
    internal static class PopupDialogs
    {
        private const string CreateProjectPopupId = "Create New Project";
        private const string DeleteProjectPopupId = "Confirm Delete Project";

        private static string _newProjectName = "NewProject";
        private static string _newProjectLocation = string.Empty;
        private static int _newProjectTemplateIndex = 0;
        private static bool _openAfterCreate = true;
        private static string _pendingDeleteProjectPath = string.Empty;

        public static void OpenCreateProjectPopup(string projectsRoot)
        {
            if (string.IsNullOrWhiteSpace(_newProjectLocation))
                _newProjectLocation = projectsRoot;
            ImGui.OpenPopup(CreateProjectPopupId);
        }

        public static void OpenDeleteProjectPopup(string projectPath)
        {
            _pendingDeleteProjectPath = projectPath;
            ImGui.OpenPopup(DeleteProjectPopupId);
        }

        public static void DrawPopups()
        {
            DrawCreateProjectPopup();
            DrawDeleteProjectPopup();
        }

        private static void DrawCreateProjectPopup()
        {
            if (!ImGui.BeginPopupModal(CreateProjectPopupId))
                return;

            ImGui.Text("Create New Project");
            ImGui.Separator();

            ImGui.SetNextItemWidth(360.0f);
            string updatedName = ImGui.InputText("Project Name", _newProjectName);
            if (updatedName != null)
                _newProjectName = updatedName;

            ImGui.SetNextItemWidth(360.0f);
            string updatedLocation = ImGui.InputText("Location", _newProjectLocation);
            if (updatedLocation != null)
                _newProjectLocation = updatedLocation;

            ImGui.SameLine();
            if (ImGui.Button("Browse..."))
            {
                string selectedFolder = Explorer.PickFolder("Select project folder", _newProjectLocation);
                if (!string.IsNullOrEmpty(selectedFolder))
                    _newProjectLocation = selectedFolder;
            }

            ImGui.Text("Template");
            string[] templates = ProjectCodeGenerator.GetProjectTemplates();
            for (int i = 0; i < templates.Length; ++i)
            {
                bool selected = i == _newProjectTemplateIndex;
                if (ImGui.SelectableNoClose(templates[i] + "##Template" + i, selected))
                    _newProjectTemplateIndex = i;
            }

            ImGui.Checkbox("Open project after creation", ref _openAfterCreate);

            if (ImGui.Button("Create Project"))
            {
                string projectName = _newProjectName.Trim();
                string location = _newProjectLocation.Trim();

                if (!StringUtilities.IsValidProjectName(projectName))
                {
                    ProjectOperations.SetStatusMessage("Invalid project name.");
                }
                else if (string.IsNullOrWhiteSpace(location))
                {
                    ProjectOperations.SetStatusMessage("Invalid location path.");
                }
                else
                {
                    try
                    {
                        Directory.CreateDirectory(location);
                        string[] templates_copy = ProjectCodeGenerator.GetProjectTemplates();
                        string templateName = templates_copy[_newProjectTemplateIndex];
                        ProjectOperations.CreateProject(Path.Combine(location, projectName), templateName);
                        ImGui.CloseCurrentPopup();
                        if (_openAfterCreate)
                            EditorHost.SetShowProjectManagerView(false);
                    }
                    catch (Exception ex)
                    {
                        ProjectOperations.SetStatusMessage("Create failed: " + ex.Message);
                    }
                }
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }

        private static void DrawDeleteProjectPopup()
        {
            if (!ImGui.BeginPopupModal(DeleteProjectPopupId))
                return;

            if (string.IsNullOrEmpty(_pendingDeleteProjectPath))
            {
                ImGui.Text("No project selected for deletion.");
            }
            else
            {
                ImGui.Text("Delete project: " + Path.GetFileName(_pendingDeleteProjectPath));
                ImGui.Text("This will remove the folder and all files.");
                if (ImGui.Button("Confirm Delete"))
                {
                    ProjectOperations.DeleteProject(_pendingDeleteProjectPath);
                    ImGui.CloseCurrentPopup();
                }
            }

            if (ImGui.Button("Cancel"))
            {
                _pendingDeleteProjectPath = string.Empty;
                ProjectOperations.SetStatusMessage("Delete cancelled.");
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }
}
