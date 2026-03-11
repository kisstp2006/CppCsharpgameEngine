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
        private static float _autoRefreshSeconds = 1.0f;
        private static bool _optionsRegistered = false;

        private static string[] _assetEntries = new string[0];
        private static string[] _codeEntries = new string[0];

        private const string CreateScenePopupId = "Create Scene";
        private const string CreateScriptPopupId = "Create Script";
        private const string AssetContextPopupId = "Asset Context Menu";
        private static string _newSceneName = "Scene";
        private static int _newSceneFormat = 0;
        private static string _newScriptName = "NewScript";
        private static string _assetContextPath = string.Empty;
        private static bool _openCreateScenePopupRequested = false;
        private static bool _openCreateScriptPopupRequested = false;

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
                _nextAutoRefreshUtc = DateTime.UtcNow.AddSeconds(_autoRefreshSeconds);
            }

            ImGui.SetNextItemWidth(280.0f);
            string updatedSearch = ImGui.InputText("Filter", _searchText);
            if (updatedSearch != null)
                _searchText = updatedSearch;

            ImGui.SameLine();
            if (ImGui.Button("Refresh"))
            {
                Refresh(projectPath);
                _nextAutoRefreshUtc = DateTime.UtcNow.AddSeconds(_autoRefreshSeconds);
            }

            ImGui.Separator();
            ImGui.Text("Assets + Scenes (" + _assetEntries.Length + ")");
            DrawAssetEntriesWithContextMenu(projectPath);

            ImGui.Separator();
            ImGui.Text("Solutions + C# Scripts (" + _codeEntries.Length + ")");
            DrawEntries(_codeEntries, "Code", projectPath, openOnSelect: true);

            ImGui.Separator();
            if (string.IsNullOrEmpty(_selectedPath))
                ImGui.Text("Selected: <none>");
            else
                ImGui.Text("Selected: " + _selectedPath);

            if (_openCreateScenePopupRequested)
            {
                ImGui.OpenPopup(CreateScenePopupId);
                _openCreateScenePopupRequested = false;
            }

            if (_openCreateScriptPopupRequested)
            {
                ImGui.OpenPopup(CreateScriptPopupId);
                _openCreateScriptPopupRequested = false;
            }

            DrawCreateScenePopup(projectPath);
            DrawCreateScriptPopup(projectPath);
            DrawAssetContextMenu(projectPath);

            ImGui.End();
        }

        private static void DrawAssetEntriesWithContextMenu(string projectPath)
        {
            float listHeight = ImGui.GetContentRegionAvailY() * 0.45f;
            if (listHeight < 140.0f)
                listHeight = 140.0f;

            if (ImGui.BeginChild("##AssetEntriesChild", 0.0f, listHeight, true))
            {
                DrawEntries(_assetEntries, "Asset", projectPath, openOnSelect: false);

                if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(MouseButton.Right))
                {
                    _assetContextPath = string.Empty;
                    ImGui.OpenPopup(AssetContextPopupId);
                }

                ImGui.EndChild();
            }
        }

        internal static void RequestOpenCreateScenePopup(int preferredFormat = -1)
        {
            if (preferredFormat == 0 || preferredFormat == 1)
                _newSceneFormat = preferredFormat;

            _openCreateScenePopupRequested = true;
        }

        internal static void RequestOpenCreateScriptPopup()
        {
            _openCreateScriptPopupRequested = true;
        }

        private static void DrawAssetContextMenu(string projectPath)
        {
            if (!ImGui.BeginPopupModal(AssetContextPopupId))
                return;

            ImGui.Text("Asset Menu");

            if (!string.IsNullOrEmpty(_assetContextPath))
            {
                ImGui.SameLine();
                if (ImGui.Button("Back"))
                    _assetContextPath = ParentPath(_assetContextPath);
            }

            ImGui.Separator();

            AssetPanelContextMenuRegistry.MenuNodeEntry[] entries = AssetPanelContextMenuRegistry.GetMenuEntries(_assetContextPath);
            if (entries.Length == 0)
            {
                ImGui.Text("No actions registered.");
            }
            else
            {
                var context = new AssetPanelContextMenuRegistry.AssetPanelContext();
                context.ProjectPath = projectPath;
                context.SelectedPath = _selectedPath;

                for (int i = 0; i < entries.Length; ++i)
                {
                    AssetPanelContextMenuRegistry.MenuNodeEntry entry = entries[i];
                    string label = entry.HasChildren ? entry.Label + " >" : entry.Label;

                    if (!ImGui.Selectable(label + "##AssetMenu" + entry.Path, false))
                        continue;

                    if (entry.HasChildren)
                    {
                        _assetContextPath = entry.Path;
                        break;
                    }

                    if (entry.Action != null)
                        entry.Action(context);

                    ImGui.CloseCurrentPopup();
                    ImGui.EndPopup();
                    return;
                }
            }

            ImGui.Separator();
            if (ImGui.Button("Close"))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }

        public static void RegisterEditorOptions()
        {
            if (_optionsRegistered)
                return;

            _optionsRegistered = true;
            EditorOptionsRegistry.Register("assetpanel.general",
                                           "Asset Panel",
                                           "General",
                                           DrawAssetPanelOptions,
                                           10);
        }

        private static void DrawAssetPanelOptions()
        {
            float refreshSeconds = _autoRefreshSeconds;
            if (ImGui.InputFloat("Auto refresh interval (sec)", ref refreshSeconds, 0.1f))
            {
                if (refreshSeconds < 0.1f)
                    refreshSeconds = 0.1f;
                if (refreshSeconds > 30.0f)
                    refreshSeconds = 30.0f;

                _autoRefreshSeconds = refreshSeconds;
            }

            ImGui.Text("Controls periodic rescanning of Assets and Scenes folders.");
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

        private static string ParentPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            int index = path.LastIndexOf('/');
            if (index <= 0)
                return string.Empty;

            return path.Substring(0, index);
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
                    _nextAutoRefreshUtc = DateTime.UtcNow.AddSeconds(_autoRefreshSeconds);
                    ImGui.CloseCurrentPopup();
                }
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }

        private static void DrawCreateScriptPopup(string projectPath)
        {
            if (!ImGui.BeginPopupModal(CreateScriptPopupId))
                return;

            ImGui.Text("Create C# Script");
            ImGui.Separator();

            ImGui.SetNextItemWidth(280.0f);
            string updatedName = ImGui.InputText("Script Name", _newScriptName);
            if (updatedName != null)
                _newScriptName = updatedName;

            ImGui.Separator();
            if (ImGui.Button("Create"))
            {
                if (CreateScriptAsset(projectPath, _newScriptName))
                {
                    Refresh(projectPath);
                    _nextAutoRefreshUtc = DateTime.UtcNow.AddSeconds(_autoRefreshSeconds);
                    ImGui.CloseCurrentPopup();
                }
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }

        private static bool CreateScriptAsset(string projectPath, string requestedName)
        {
            try
            {
                string normalizedName = (requestedName ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(normalizedName))
                    normalizedName = "NewScript";

                if (!StringUtilities.IsValidProjectName(normalizedName))
                {
                    ProjectOperations.SetStatusMessage("Create script failed: invalid script name.");
                    return false;
                }

                string scriptsRoot = Path.Combine(projectPath, "Scripts");
                Directory.CreateDirectory(scriptsRoot);

                string className = BuildSafeScriptClassName(normalizedName);
                string candidatePath = Path.Combine(scriptsRoot, className + ".cs");
                if (File.Exists(candidatePath))
                {
                    int suffix = 1;
                    while (true)
                    {
                        string nextPath = Path.Combine(scriptsRoot, className + "_" + suffix + ".cs");
                        if (!File.Exists(nextPath))
                        {
                            candidatePath = nextPath;
                            break;
                        }

                        ++suffix;
                    }
                }

                string finalClassName = Path.GetFileNameWithoutExtension(candidatePath);
                string fileContent = BuildScriptFileContent(finalClassName);
                File.WriteAllText(candidatePath, fileContent);

                _selectedPath = Path.GetFullPath(candidatePath);
                ProjectOperations.SetStatusMessage("Created script: " + Path.GetFileName(candidatePath));
                return true;
            }
            catch (Exception ex)
            {
                ProjectOperations.SetStatusMessage("Create script failed: " + ex.Message);
                return false;
            }
        }

        private static string BuildSafeScriptClassName(string value)
        {
            string className = StringUtilities.BuildSafeAssemblyName(value);
            if (string.IsNullOrEmpty(className))
                className = "NewScript";

            if (char.IsDigit(className[0]))
                className = "_" + className;

            return className;
        }

        private static string BuildScriptFileContent(string className)
        {
            string escapedClassName = StringUtilities.EscapeCSharpString(className);
            return "using System;\n\n" +
                   "namespace GameScripts\n" +
                   "{\n" +
                   "    public sealed class " + className + "\n" +
                   "    {\n" +
                   "        public void OnCreate(uint entityId)\n" +
                   "        {\n" +
                   "            Console.WriteLine(\"[GameScripts] " + escapedClassName + " created for entity \" + entityId + \".\");\n" +
                   "        }\n\n" +
                   "        public void OnUpdate(uint entityId, float deltaTime)\n" +
                   "        {\n" +
                   "        }\n\n" +
                   "        public void OnDestroy(uint entityId)\n" +
                   "        {\n" +
                   "            Console.WriteLine(\"[GameScripts] " + escapedClassName + " destroyed for entity \" + entityId + \".\");\n" +
                   "        }\n" +
                   "    }\n" +
                   "}\n";
        }
    }
}
