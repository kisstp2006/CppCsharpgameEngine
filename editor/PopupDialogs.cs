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
        private static bool _generateStarterContent = true;
        private static bool _generateStarterScene = true;
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

            EditorUIHelpers.DrawPopupHeader("Create New Project");

            EditorUIHelpers.InputTextWithWidth("Project Name", ref _newProjectName, EditorUIHelpers.StandardFieldWidth);

            EditorUIHelpers.InputTextWithWidth("Location", ref _newProjectLocation, EditorUIHelpers.StandardFieldWidth);

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

            ImGui.Checkbox("Generate starter content", ref _generateStarterContent);
            if (_generateStarterContent)
            {
                ImGui.Checkbox("Generate starter demo scene (JSON)", ref _generateStarterScene);
            }
            else
            {
                _generateStarterScene = false;
                ImGui.Text("Starter scene generation is disabled when starter content is off.");
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
                        ProjectOperations.CreateProject(Path.Combine(location, projectName),
                                                        templateName,
                                                        _generateStarterContent,
                                                        _generateStarterScene);
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

            EditorUIHelpers.DrawPopupHeader("Delete Project");

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
