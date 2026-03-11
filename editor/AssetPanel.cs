using System;
using System.Diagnostics;
using System.IO;

using Engine;

namespace EngineEditor
{
    internal static class AssetPanel
    {
        private static string _cachedProjectPath = string.Empty;
        private static string _searchText = string.Empty;
        private static string _selectedPath = string.Empty;

        private static DateTime _nextAutoRefreshUtc = DateTime.MinValue;

        private static string[] _assetEntries = new string[0];
        private static string[] _codeEntries = new string[0];

        private const string CreateScenePopupId = "Create Scene";
        private static string _newSceneName = "Scene";
        private static int _newSceneFormat = 0;

        public static void DrawAssetPanel()
        {
            if (!ImGui.Begin("Asset Panel"))
            {
                ImGui.End();
                return;
            }

            string projectPath = ProjectOperations.ActiveProjectPath;
            if (string.IsNullOrEmpty(projectPath) || !Directory.Exists(projectPath))
            {
                ImGui.Text("No active project.");
                ImGui.End();
                return;
            }

            bool projectChanged = !string.Equals(_cachedProjectPath, projectPath, StringComparison.OrdinalIgnoreCase);
            bool autoRefreshDue = DateTime.UtcNow >= _nextAutoRefreshUtc;

            if (projectChanged || autoRefreshDue)
            {
                Refresh(projectPath);
                _nextAutoRefreshUtc = DateTime.UtcNow.AddSeconds(1.0);
            }

            ImGui.SetNextItemWidth(280.0f);
            string updatedSearch = ImGui.InputText("Filter", _searchText);
            if (updatedSearch != null)
                _searchText = updatedSearch;

            ImGui.SameLine();
            if (ImGui.Button("Refresh"))
            {
                Refresh(projectPath);
                _nextAutoRefreshUtc = DateTime.UtcNow.AddSeconds(1.0);
            }

            ImGui.SameLine();
            if (ImGui.Button("Create Scene"))
                ImGui.OpenPopup(CreateScenePopupId);

            ImGui.Separator();
            ImGui.Text("Assets + Scenes (" + _assetEntries.Length + ")");
            DrawEntries(_assetEntries, "Asset", projectPath, openOnSelect: false);

            ImGui.Separator();
            ImGui.Text("Solutions + C# Scripts (" + _codeEntries.Length + ")");
            DrawEntries(_codeEntries, "Code", projectPath, openOnSelect: true);

            ImGui.Separator();
            if (string.IsNullOrEmpty(_selectedPath))
                ImGui.Text("Selected: <none>");
            else
                ImGui.Text("Selected: " + _selectedPath);

            DrawCreateScenePopup(projectPath);

            ImGui.End();
        }

        private static void Refresh(string projectPath)
        {
            _cachedProjectPath = projectPath;

            string assetsRoot = Path.Combine(projectPath, "Assets");
            string scenesRoot = Path.Combine(projectPath, "Scenes");
            string scriptsRoot = Path.Combine(projectPath, "Scripts");

            string[] assetFiles = GetFilesSafe(assetsRoot, "*", SearchOption.AllDirectories);
            string[] sceneFiles = GetFilesSafe(scenesRoot, "*", SearchOption.AllDirectories);
            _assetEntries = ConcatAndSort(assetFiles, sceneFiles);

            string[] slnFiles = GetFilesSafe(projectPath, "*.sln", SearchOption.TopDirectoryOnly);
            string[] csprojFiles = GetFilesSafe(projectPath, "*.csproj", SearchOption.TopDirectoryOnly);
            string[] scriptFiles = GetFilesSafe(scriptsRoot, "*.cs", SearchOption.AllDirectories);
            _codeEntries = ConcatAndSort(slnFiles, csprojFiles, scriptFiles);
        }

        private static void DrawEntries(string[] entries, string idPrefix, string projectPath, bool openOnSelect)
        {
            bool anyVisible = false;
            string filter = (_searchText ?? string.Empty).Trim();

            for (int i = 0; i < entries.Length; ++i)
            {
                string fullPath = entries[i];
                string displayPath = ToDisplayPath(projectPath, fullPath);

                if (!string.IsNullOrEmpty(filter) && displayPath.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                anyVisible = true;
                bool selected = string.Equals(_selectedPath, fullPath, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable(displayPath + "##" + idPrefix + i, selected))
                {
                    _selectedPath = fullPath;

                    if (!openOnSelect && SceneEditor.IsSceneFilePath(fullPath))
                    {
                        SceneEditor.LoadSceneFromPath(fullPath);
                        continue;
                    }

                    if (openOnSelect)
                        OpenPathInShell(fullPath);
                }
            }

            if (!anyVisible)
                ImGui.Text("No matches.");
        }

        private static void OpenPathInShell(string fullPath)
        {
            try
            {
                if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
                {
                    ProjectOperations.SetStatusMessage("Open failed: file missing.");
                    return;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = fullPath,
                    UseShellExecute = true,
                };

                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                ProjectOperations.SetStatusMessage("Open failed: " + ex.Message);
            }
        }

        private static string[] ConcatAndSort(params string[][] groups)
        {
            int totalCount = 0;
            for (int i = 0; i < groups.Length; ++i)
            {
                if (groups[i] != null)
                    totalCount += groups[i].Length;
            }

            string[] merged = new string[totalCount];
            int writeIndex = 0;

            for (int g = 0; g < groups.Length; ++g)
            {
                string[] group = groups[g] ?? new string[0];
                for (int i = 0; i < group.Length; ++i)
                    merged[writeIndex++] = group[i];
            }

            Array.Sort(merged, StringComparer.OrdinalIgnoreCase);
            return merged;
        }

        private static string[] GetFilesSafe(string rootPath, string pattern, SearchOption searchOption)
        {
            try
            {
                if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
                    return new string[0];

                return Directory.GetFiles(rootPath, pattern, searchOption);
            }
            catch
            {
                return new string[0];
            }
        }

        private static string ToDisplayPath(string projectPath, string fullPath)
        {
            if (string.IsNullOrEmpty(projectPath) || string.IsNullOrEmpty(fullPath))
                return fullPath ?? string.Empty;

            string normalizedProject = projectPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!fullPath.StartsWith(normalizedProject, StringComparison.OrdinalIgnoreCase))
                return fullPath;

            int prefixLength = normalizedProject.Length;
            if (fullPath.Length <= prefixLength)
                return fullPath;

            int relativeStart = prefixLength;
            if (fullPath.Length > relativeStart &&
                (fullPath[relativeStart] == Path.DirectorySeparatorChar || fullPath[relativeStart] == Path.AltDirectorySeparatorChar))
            {
                relativeStart += 1;
            }

            if (relativeStart >= fullPath.Length)
                return fullPath;

            return fullPath.Substring(relativeStart);
        }

        private static void DrawCreateScenePopup(string projectPath)
        {
            if (!ImGui.BeginPopupModal(CreateScenePopupId))
                return;

            ImGui.Text("Create Scene");
            ImGui.Separator();

            ImGui.SetNextItemWidth(280.0f);
            string updatedName = ImGui.InputText("Scene Name", _newSceneName);
            if (updatedName != null)
                _newSceneName = updatedName;

            ImGui.Text("Format");
            if (ImGui.SelectableNoClose("JSON (.scene.json)", _newSceneFormat == 0))
                _newSceneFormat = 0;
            if (ImGui.SelectableNoClose("Binary (.scene.bin)", _newSceneFormat == 1))
                _newSceneFormat = 1;

            ImGui.Separator();
            if (ImGui.Button("Create"))
            {
                if (SceneEditor.CreateSceneAsset(_newSceneName, _newSceneFormat))
                {
                    Refresh(projectPath);
                    _nextAutoRefreshUtc = DateTime.UtcNow.AddSeconds(1.0);
                    ImGui.CloseCurrentPopup();
                }
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }
    }
}
