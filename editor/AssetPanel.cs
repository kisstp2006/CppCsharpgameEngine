using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using Engine;

namespace EngineEditor
{
    internal static class AssetPanel
    {
        private sealed class BrowserItem
        {
            public string Path;
            public string Name;
            public bool IsDirectory;
        }

        private static string _cachedProjectPath = string.Empty;
        private static string _searchText = string.Empty;
        private static string _selectedPath = string.Empty;
        private static string _contextTargetPath = string.Empty;
        private static string _currentFolderPath = string.Empty;

        private static DateTime _nextAutoRefreshUtc = DateTime.MinValue;
        private static DateTime _lastClickUtc = DateTime.MinValue;
        private static float _autoRefreshSeconds = 1.0f;
        private static bool _optionsRegistered = false;
        private static bool _assetContextMenuRegistered = false;

        private static readonly List<string> _folderHistory = new List<string>();
        private static int _folderHistoryIndex = -1;
        private static readonly Dictionary<string, bool> _folderExpandState = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private static string[] _rootFolders = new string[0];
        private static BrowserItem[] _currentItems = new BrowserItem[0];
        private static string _lastClickedPath = string.Empty;

        private const string CreateScenePopupId = "Create Scene";
        private const string CreateScriptPopupId = "Create Script";
        private const string CreateFolderPopupId = "Create Folder";
        private const string RenamePopupId = "Rename Asset";
        private const string DeletePopupId = "Delete Asset";
        private const string AssetContextPopupId = "Asset Context Menu";
        private const double DoubleClickThresholdMs = 350.0;

        private static string _newSceneName = "Scene";
        private static int _newSceneFormat = 0;
        private static string _newScriptName = "NewScript";
        private static string _newFolderName = "NewFolder";
        private static string _renameTargetPath = string.Empty;
        private static string _renameValue = string.Empty;
        private static string _deleteTargetPath = string.Empty;
        private static string _assetContextPath = string.Empty;

        private static bool _openCreateScenePopupRequested = false;
        private static bool _openCreateScriptPopupRequested = false;
        private static bool _openCreateFolderPopupRequested = false;
        private static bool _openRenamePopupRequested = false;
        private static bool _openDeletePopupRequested = false;

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
                RefreshPanelData(projectPath, projectChanged);
                _nextAutoRefreshUtc = DateTime.UtcNow.AddSeconds(_autoRefreshSeconds);
            }

            DrawToolbar(projectPath);
            ImGui.Separator();

            float bodyHeight = ImGui.GetContentRegionAvailY() - EditorUIHelpers.AssetBrowserReservedStatusHeight;
            if (bodyHeight < EditorUIHelpers.AssetBrowserMinBodyHeight)
                bodyHeight = EditorUIHelpers.AssetBrowserMinBodyHeight;

            if (ImGui.BeginChild("##AssetBrowserBody", 0.0f, bodyHeight, false))
            {
                float totalWidth = ImGui.GetContentRegionAvailX();
                if (totalWidth < EditorUIHelpers.AssetBrowserMinWidth)
                    totalWidth = EditorUIHelpers.AssetBrowserMinWidth;

                float treeWidth = totalWidth * 0.28f;
                if (treeWidth < EditorUIHelpers.AssetTreeMinWidth)
                    treeWidth = EditorUIHelpers.AssetTreeMinWidth;
                if (treeWidth > totalWidth - EditorUIHelpers.AssetTreeReservedGridWidth)
                    treeWidth = totalWidth - EditorUIHelpers.AssetTreeReservedGridWidth;

                if (ImGui.BeginChild("##AssetFolderTree", treeWidth, 0.0f, true))
                {
                    DrawFolderTree(projectPath);
                    ImGui.EndChild();
                }

                ImGui.SameLine();

                if (ImGui.BeginChild("##AssetGridPane", 0.0f, 0.0f, true))
                {
                    DrawBreadcrumb(projectPath);
                    ImGui.Separator();
                    DrawAssetGrid(projectPath);
                    ImGui.EndChild();
                }

                ImGui.EndChild();
            }

            ImGui.Separator();
            DrawStatusBar(projectPath);

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

            if (_openCreateFolderPopupRequested)
            {
                ImGui.OpenPopup(CreateFolderPopupId);
                _openCreateFolderPopupRequested = false;
            }

            if (_openRenamePopupRequested)
            {
                ImGui.OpenPopup(RenamePopupId);
                _openRenamePopupRequested = false;
            }

            if (_openDeletePopupRequested)
            {
                ImGui.OpenPopup(DeletePopupId);
                _openDeletePopupRequested = false;
            }

            DrawCreateScenePopup(projectPath);
            DrawCreateScriptPopup(projectPath);
            DrawCreateFolderPopup(projectPath);
            DrawRenamePopup(projectPath);
            DrawDeletePopup(projectPath);
            DrawAssetContextMenu(projectPath);

            ImGui.End();
        }

        private static void DrawToolbar(string projectPath)
        {
            if (ImGui.Button("Home##AssetPanelHome"))
            {
                NavigateToFolder(GetDefaultStartFolder(projectPath), true);
                _contextTargetPath = _currentFolderPath;
            }

            ImGui.SameLine();
            if (ImGui.Button("Back##AssetPanelBack"))
            {
                if (!NavigateBack(projectPath))
                    ProjectOperations.SetStatusMessage("No previous folder in history.");
            }

            ImGui.SameLine();
            if (ImGui.Button("Refresh##AssetPanelRefresh"))
            {
                RefreshPanelData(projectPath, false);
                _nextAutoRefreshUtc = DateTime.UtcNow.AddSeconds(_autoRefreshSeconds);
            }

            ImGui.SameLine();
            if (ImGui.Button("Create##AssetPanelCreate"))
            {
                _contextTargetPath = ResolveContextPath(projectPath);
                _assetContextPath = "Create";
                ImGui.OpenPopup(AssetContextPopupId);
            }

            ImGui.SameLine();
            if (ImGui.Button("Open In Explorer##AssetPanelOpenExplorer"))
            {
                string target = ResolveContextPath(projectPath);
                OpenPathInShell(target);
            }

            ImGui.SameLine();
            EditorUIHelpers.InputTextWithWidth("Search##AssetPanelSearch", ref _searchText, EditorUIHelpers.CompactSearchWidth);
        }

        private static void DrawFolderTree(string projectPath)
        {
            if (_rootFolders.Length == 0)
            {
                ImGui.Text("No folders to display.");
                return;
            }

            for (int i = 0; i < _rootFolders.Length; ++i)
                DrawFolderNode(projectPath, _rootFolders[i], 0);
        }

        private static void DrawFolderNode(string projectPath, string folderPath, int depth)
        {
            string[] children = GetDirectoriesSafe(folderPath);
            bool hasChildren = children.Length > 0;
            bool expanded = GetFolderExpanded(folderPath, depth == 0);

            string labelName = Path.GetFileName(folderPath);
            if (string.IsNullOrEmpty(labelName))
                labelName = ToDisplayPath(projectPath, folderPath);
            if (string.IsNullOrEmpty(labelName))
                labelName = folderPath;

            bool selected = PathEquals(_currentFolderPath, folderPath);

            string folderLabel = EditorUIHelpers.BuildTreeNodeLabel(depth, hasChildren, expanded, labelName, "FolderNode" + folderPath);
            bool clicked = ImGui.Selectable(folderLabel, selected);
            if (clicked)
            {
                if (selected && hasChildren)
                    SetFolderExpanded(folderPath, !expanded);

                NavigateToFolder(folderPath, true);
                _contextTargetPath = folderPath;
            }

            if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(MouseButton.Right))
            {
                _selectedPath = folderPath;
                _contextTargetPath = folderPath;
                _assetContextPath = string.Empty;
                ImGui.OpenPopup(AssetContextPopupId);
            }

            if (!expanded)
                return;

            Array.Sort(children, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < children.Length; ++i)
                DrawFolderNode(projectPath, children[i], depth + 1);
        }

        private static void DrawBreadcrumb(string projectPath)
        {
            string current = NormalizePath(_currentFolderPath);
            if (string.IsNullOrEmpty(current))
                current = NormalizePath(projectPath);

            if (ImGui.Button("Project##AssetBreadcrumbProject"))
                NavigateToFolder(projectPath, true);

            string normalizedProject = NormalizePath(projectPath);
            string relative = ToDisplayPath(normalizedProject, current);
            if (string.IsNullOrEmpty(relative))
                return;

            string[] segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string running = normalizedProject;
            for (int i = 0; i < segments.Length; ++i)
            {
                string segment = segments[i].Trim();
                if (segment.Length == 0)
                    continue;

                running = Path.Combine(running, segment);

                EditorUIHelpers.DrawBreadcrumbSeparator();
                if (ImGui.Button(segment + "##AssetBreadcrumb" + i))
                    NavigateToFolder(running, true);
            }
        }

        private static void DrawAssetGrid(string projectPath)
        {
            int columns = (int)(ImGui.GetContentRegionAvailX() / EditorUIHelpers.AssetGridItemWidth);
            if (columns < 1)
                columns = 1;

            int visibleCount = 0;
            bool openedContextFromItem = false;
            int columnIndex = 0;
            string filter = (_searchText ?? string.Empty).Trim();

            for (int i = 0; i < _currentItems.Length; ++i)
            {
                BrowserItem item = _currentItems[i];

                if (!string.IsNullOrEmpty(filter) && item.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                bool selected = PathEquals(_selectedPath, item.Path);
                string label = BuildGridLabel(item, i);

                if (columnIndex > 0)
                    ImGui.SameLine();

                bool clicked = ImGui.Button(label);
                if (clicked)
                {
                    bool isDoubleClick = IsDoubleClick(item.Path);
                    _selectedPath = item.Path;
                    _contextTargetPath = item.Path;

                    if (isDoubleClick)
                        ActivateItem(item);
                }

                if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(MouseButton.Right))
                {
                    _selectedPath = item.Path;
                    _contextTargetPath = item.Path;
                    _assetContextPath = string.Empty;
                    ImGui.OpenPopup(AssetContextPopupId);
                    openedContextFromItem = true;
                }

                ++visibleCount;
                ++columnIndex;
                if (columnIndex >= columns)
                    columnIndex = 0;
            }

            if (visibleCount == 0)
                ImGui.Text("No items in this folder.");

            if (!openedContextFromItem && ImGui.IsWindowHovered() && ImGui.IsMouseClicked(MouseButton.Right))
            {
                _contextTargetPath = _currentFolderPath;
                _assetContextPath = string.Empty;
                ImGui.OpenPopup(AssetContextPopupId);
            }
        }

        private static string BuildGridLabel(BrowserItem item, int index)
        {
            string badge = item.IsDirectory ? "[DIR]" : GetFileBadge(item.Path);
            string selectedPrefix = PathEquals(_selectedPath, item.Path) ? "> " : "  ";
            return selectedPrefix + badge + " " + item.Name + "##AssetGridItem" + index;
        }

        private static string GetFileBadge(string fullPath)
        {
            string lower = (Path.GetFileName(fullPath) ?? string.Empty).ToLowerInvariant();

            if (lower.EndsWith(".scene.json") || lower.EndsWith(".scene.bin"))
                return "[SCN]";

            string extension = Path.GetExtension(lower);
            switch (extension)
            {
                case ".cs":
                    return "[CS]";
                case ".csproj":
                    return "[CSP]";
                case ".sln":
                    return "[SLN]";
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".bmp":
                case ".tga":
                case ".dds":
                    return "[IMG]";
                default:
                    return "[FIL]";
            }
        }

        private static bool IsDoubleClick(string path)
        {
            DateTime now = DateTime.UtcNow;
            bool isDouble = PathEquals(_lastClickedPath, path)
                            && (now - _lastClickUtc).TotalMilliseconds <= DoubleClickThresholdMs;

            _lastClickedPath = path;
            _lastClickUtc = now;
            return isDouble;
        }

        private static void ActivateItem(BrowserItem item)
        {
            if (item.IsDirectory)
            {
                NavigateToFolder(item.Path, true);
                return;
            }

            if (SceneEditor.IsSceneFilePath(item.Path))
            {
                SceneEditor.LoadSceneFromPath(item.Path);
                return;
            }

            OpenPathInShell(item.Path);
        }

        private static void DrawStatusBar(string projectPath)
        {
            string currentDisplay = ToDisplayPath(projectPath, _currentFolderPath);
            if (string.IsNullOrEmpty(currentDisplay))
                currentDisplay = "<none>";

            string selectedDisplay = ToDisplayPath(projectPath, _selectedPath);
            if (string.IsNullOrEmpty(selectedDisplay))
                selectedDisplay = "<none>";

            ImGui.Text("Folder: " + currentDisplay + " | Selected: " + selectedDisplay);
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

        internal static void RequestOpenCreateFolderPopup()
        {
            _openCreateFolderPopupRequested = true;
        }

        private static void DrawAssetContextMenu(string projectPath)
        {
            if (!ImGui.BeginPopupModal(AssetContextPopupId))
                return;

            EditorUIHelpers.DrawPopupHeader("Asset Menu");

            string contextPath = ResolveContextPath(projectPath);
            string contextLabel = ToDisplayPath(projectPath, contextPath);
            if (string.IsNullOrEmpty(contextLabel))
                contextLabel = "<none>";

            ImGui.Text("Target: " + contextLabel);

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
                context.SelectedPath = contextPath;

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

                    _assetContextPath = string.Empty;
                    ImGui.CloseCurrentPopup();
                    ImGui.EndPopup();
                    return;
                }
            }

            if (string.IsNullOrEmpty(_assetContextPath) && ExistsPath(contextPath))
            {
                ImGui.Separator();

                if (ImGui.Selectable("Reveal In Explorer", false))
                {
                    RevealInExplorer(contextPath);
                    _assetContextPath = string.Empty;
                    ImGui.CloseCurrentPopup();
                    ImGui.EndPopup();
                    return;
                }

                if (ImGui.Selectable("Rename", false))
                {
                    RequestRename(contextPath);
                    _assetContextPath = string.Empty;
                    ImGui.CloseCurrentPopup();
                    ImGui.EndPopup();
                    return;
                }

                if (ImGui.Selectable("Delete", false))
                {
                    RequestDelete(contextPath);
                    _assetContextPath = string.Empty;
                    ImGui.CloseCurrentPopup();
                    ImGui.EndPopup();
                    return;
                }
            }

            ImGui.Separator();
            if (ImGui.Button("Close"))
            {
                _assetContextPath = string.Empty;
                ImGui.CloseCurrentPopup();
            }

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

        public static void RegisterAssetContextMenu()
        {
            if (_assetContextMenuRegistered)
                return;

            _assetContextMenuRegistered = true;
            AssetPanelContextMenuRegistry.Register("assetpanel.createFolder",
                                                   "Create/Folder",
                                                   _ => RequestOpenCreateFolderPopup(),
                                                   30);
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

            ImGui.Text("Controls periodic rescanning of current folder view.");
        }

        private static void RefreshPanelData(string projectPath, bool projectChanged)
        {
            _cachedProjectPath = projectPath;

            if (projectChanged)
            {
                _selectedPath = string.Empty;
                _contextTargetPath = string.Empty;
                _currentFolderPath = string.Empty;
                _lastClickedPath = string.Empty;
                _folderHistory.Clear();
                _folderHistoryIndex = -1;
                _folderExpandState.Clear();
            }

            InitializeRoots(projectPath);

            string defaultFolder = GetDefaultStartFolder(projectPath);
            if (string.IsNullOrEmpty(_currentFolderPath) || !Directory.Exists(_currentFolderPath))
            {
                _currentFolderPath = defaultFolder;
                PushFolderHistory(_currentFolderPath);
            }

            RefreshCurrentFolderItems();
        }

        private static void InitializeRoots(string projectPath)
        {
            var roots = new List<string>(4);
            string assetsRoot = Path.Combine(projectPath, "Assets");
            string scenesRoot = Path.Combine(projectPath, "Scenes");
            string scriptsRoot = Path.Combine(projectPath, "Scripts");

            if (Directory.Exists(assetsRoot))
                roots.Add(NormalizePath(assetsRoot));
            if (Directory.Exists(scenesRoot))
                roots.Add(NormalizePath(scenesRoot));
            if (Directory.Exists(scriptsRoot))
                roots.Add(NormalizePath(scriptsRoot));

            if (roots.Count == 0)
                roots.Add(NormalizePath(projectPath));

            _rootFolders = roots.ToArray();

            for (int i = 0; i < _rootFolders.Length; ++i)
            {
                string root = _rootFolders[i];
                if (!_folderExpandState.ContainsKey(root))
                    _folderExpandState[root] = true;
            }
        }

        private static string GetDefaultStartFolder(string projectPath)
        {
            if (_rootFolders.Length > 0)
                return _rootFolders[0];

            return NormalizePath(projectPath);
        }

        private static void RefreshCurrentFolderItems()
        {
            if (string.IsNullOrEmpty(_currentFolderPath) || !Directory.Exists(_currentFolderPath))
            {
                _currentItems = new BrowserItem[0];
                return;
            }

            string[] directories = GetDirectoriesSafe(_currentFolderPath);
            string[] files = GetFilesSafe(_currentFolderPath, "*", SearchOption.TopDirectoryOnly);

            Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            var items = new List<BrowserItem>(directories.Length + files.Length);

            for (int i = 0; i < directories.Length; ++i)
            {
                string directoryPath = directories[i];
                var item = new BrowserItem();
                item.Path = directoryPath;
                item.Name = Path.GetFileName(directoryPath);
                item.IsDirectory = true;
                items.Add(item);
            }

            for (int i = 0; i < files.Length; ++i)
            {
                string filePath = files[i];
                var item = new BrowserItem();
                item.Path = filePath;
                item.Name = Path.GetFileName(filePath);
                item.IsDirectory = false;
                items.Add(item);
            }

            _currentItems = items.ToArray();
        }

        private static bool NavigateToFolder(string folderPath, bool addToHistory)
        {
            string normalized = NormalizePath(folderPath);
            if (string.IsNullOrEmpty(normalized) || !Directory.Exists(normalized))
                return false;

            if (PathEquals(_currentFolderPath, normalized))
            {
                if (addToHistory)
                    PushFolderHistory(normalized);

                RefreshCurrentFolderItems();
                return true;
            }

            _currentFolderPath = normalized;
            if (addToHistory)
                PushFolderHistory(normalized);

            if (!StartsWithPath(_selectedPath, _currentFolderPath) && !PathEquals(_selectedPath, _currentFolderPath))
                _selectedPath = string.Empty;

            RefreshCurrentFolderItems();
            return true;
        }

        private static bool NavigateBack(string projectPath)
        {
            if (_folderHistoryIndex <= 0 || _folderHistory.Count == 0)
                return false;

            --_folderHistoryIndex;
            string target = _folderHistory[_folderHistoryIndex];
            if (!Directory.Exists(target))
            {
                RefreshPanelData(projectPath, false);
                return false;
            }

            _currentFolderPath = target;
            RefreshCurrentFolderItems();
            return true;
        }

        private static void PushFolderHistory(string folderPath)
        {
            string normalized = NormalizePath(folderPath);
            if (string.IsNullOrEmpty(normalized))
                return;

            if (_folderHistoryIndex >= 0 && _folderHistoryIndex < _folderHistory.Count)
            {
                if (PathEquals(_folderHistory[_folderHistoryIndex], normalized))
                    return;

                if (_folderHistoryIndex < _folderHistory.Count - 1)
                    _folderHistory.RemoveRange(_folderHistoryIndex + 1, _folderHistory.Count - _folderHistoryIndex - 1);
            }

            _folderHistory.Add(normalized);
            _folderHistoryIndex = _folderHistory.Count - 1;
        }

        private static bool GetFolderExpanded(string folderPath, bool defaultValue)
        {
            if (_folderExpandState.TryGetValue(folderPath, out bool expanded))
                return expanded;

            _folderExpandState[folderPath] = defaultValue;
            return defaultValue;
        }

        private static void SetFolderExpanded(string folderPath, bool value)
        {
            _folderExpandState[folderPath] = value;
        }

        private static string ResolveContextPath(string projectPath)
        {
            if (ExistsPath(_contextTargetPath))
                return _contextTargetPath;

            if (ExistsPath(_selectedPath))
                return _selectedPath;

            if (ExistsPath(_currentFolderPath))
                return _currentFolderPath;

            return GetDefaultStartFolder(projectPath);
        }

        private static string ResolveCreateParentPath(string projectPath, string sourcePath)
        {
            string candidate = sourcePath;
            if (string.IsNullOrWhiteSpace(candidate))
                candidate = _currentFolderPath;

            if (File.Exists(candidate))
                candidate = Path.GetDirectoryName(candidate);

            if (Directory.Exists(candidate))
                return candidate;

            string fallback = Path.Combine(projectPath, "Assets");
            if (!Directory.Exists(fallback))
                fallback = GetDefaultStartFolder(projectPath);

            return fallback;
        }

        private static void RequestRename(string targetPath)
        {
            if (!ExistsPath(targetPath))
                return;

            _renameTargetPath = targetPath;
            _renameValue = Path.GetFileName(targetPath);
            _openRenamePopupRequested = true;
        }

        private static void RequestDelete(string targetPath)
        {
            if (!ExistsPath(targetPath))
                return;

            _deleteTargetPath = targetPath;
            _openDeletePopupRequested = true;
        }

        private static bool ExecuteRename(string projectPath)
        {
            try
            {
                string target = _renameTargetPath;
                bool isDirectory = Directory.Exists(target);
                bool isFile = File.Exists(target);
                if (!isDirectory && !isFile)
                {
                    ProjectOperations.SetStatusMessage("Rename failed: target missing.");
                    return false;
                }

                string trimmedName = (_renameValue ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(trimmedName))
                {
                    ProjectOperations.SetStatusMessage("Rename failed: invalid name.");
                    return false;
                }

                string directoryPath = Path.GetDirectoryName(target);
                if (string.IsNullOrEmpty(directoryPath))
                {
                    ProjectOperations.SetStatusMessage("Rename failed: invalid target path.");
                    return false;
                }

                if (isFile && string.IsNullOrEmpty(Path.GetExtension(trimmedName)))
                    trimmedName += Path.GetExtension(target);

                if (!StringUtilities.IsValidProjectName(trimmedName))
                {
                    ProjectOperations.SetStatusMessage("Rename failed: invalid file name.");
                    return false;
                }

                string destination = Path.Combine(directoryPath, trimmedName);
                if (PathEquals(target, destination))
                {
                    ProjectOperations.SetStatusMessage("Rename skipped: same name.");
                    return false;
                }

                if (ExistsPath(destination))
                {
                    ProjectOperations.SetStatusMessage("Rename failed: target already exists.");
                    return false;
                }

                if (isDirectory)
                    Directory.Move(target, destination);
                else
                    File.Move(target, destination);

                ReplacePathReferences(target, destination);
                RefreshPanelData(projectPath, false);
                ProjectOperations.SetStatusMessage("Renamed: " + Path.GetFileName(destination));
                return true;
            }
            catch (Exception ex)
            {
                ProjectOperations.SetStatusMessage("Rename failed: " + ex.Message);
                return false;
            }
        }

        private static bool ExecuteDelete(string projectPath)
        {
            try
            {
                string target = _deleteTargetPath;
                bool isDirectory = Directory.Exists(target);
                bool isFile = File.Exists(target);
                if (!isDirectory && !isFile)
                {
                    ProjectOperations.SetStatusMessage("Delete failed: target missing.");
                    return false;
                }

                if (isDirectory)
                    Directory.Delete(target, true);
                else
                    File.Delete(target);

                if (StartsWithPath(_currentFolderPath, target) || PathEquals(_currentFolderPath, target))
                {
                    string parent = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                        _currentFolderPath = parent;
                    else
                        _currentFolderPath = GetDefaultStartFolder(projectPath);

                    PushFolderHistory(_currentFolderPath);
                }

                if (StartsWithPath(_selectedPath, target) || PathEquals(_selectedPath, target))
                    _selectedPath = string.Empty;

                if (StartsWithPath(_contextTargetPath, target) || PathEquals(_contextTargetPath, target))
                    _contextTargetPath = _currentFolderPath;

                RefreshPanelData(projectPath, false);
                ProjectOperations.SetStatusMessage("Deleted: " + Path.GetFileName(target));
                return true;
            }
            catch (Exception ex)
            {
                ProjectOperations.SetStatusMessage("Delete failed: " + ex.Message);
                return false;
            }
        }

        private static void ReplacePathReferences(string oldPath, string newPath)
        {
            if (PathEquals(_selectedPath, oldPath))
                _selectedPath = newPath;
            else if (StartsWithPath(_selectedPath, oldPath))
                _selectedPath = ReplacePathPrefix(_selectedPath, oldPath, newPath);

            if (PathEquals(_contextTargetPath, oldPath))
                _contextTargetPath = newPath;
            else if (StartsWithPath(_contextTargetPath, oldPath))
                _contextTargetPath = ReplacePathPrefix(_contextTargetPath, oldPath, newPath);

            if (PathEquals(_currentFolderPath, oldPath))
                _currentFolderPath = newPath;
            else if (StartsWithPath(_currentFolderPath, oldPath))
                _currentFolderPath = ReplacePathPrefix(_currentFolderPath, oldPath, newPath);

            for (int i = 0; i < _folderHistory.Count; ++i)
            {
                string entry = _folderHistory[i];
                if (PathEquals(entry, oldPath))
                    _folderHistory[i] = newPath;
                else if (StartsWithPath(entry, oldPath))
                    _folderHistory[i] = ReplacePathPrefix(entry, oldPath, newPath);
            }
        }

        private static string ReplacePathPrefix(string value, string oldPrefix, string newPrefix)
        {
            string normalizedValue = NormalizePath(value);
            string normalizedOld = NormalizePath(oldPrefix);
            string normalizedNew = NormalizePath(newPrefix);

            if (string.IsNullOrEmpty(normalizedValue) || string.IsNullOrEmpty(normalizedOld) || string.IsNullOrEmpty(normalizedNew))
                return value;

            if (!normalizedValue.StartsWith(normalizedOld, StringComparison.OrdinalIgnoreCase))
                return value;

            string suffix = normalizedValue.Substring(normalizedOld.Length);
            return NormalizePath(normalizedNew + suffix);
        }

        private static void OpenPathInShell(string fullPath)
        {
            try
            {
                if (!ExistsPath(fullPath))
                {
                    ProjectOperations.SetStatusMessage("Open failed: path missing.");
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

        private static void RevealInExplorer(string fullPath)
        {
            try
            {
                if (!ExistsPath(fullPath))
                {
                    ProjectOperations.SetStatusMessage("Reveal failed: path missing.");
                    return;
                }

                string arguments = "/select,\"" + fullPath + "\"";
                var startInfo = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = arguments,
                    UseShellExecute = true,
                };

                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                ProjectOperations.SetStatusMessage("Reveal failed: " + ex.Message);
            }
        }

        private static string[] GetDirectoriesSafe(string rootPath)
        {
            try
            {
                if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
                    return new string[0];

                return Directory.GetDirectories(rootPath, "*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                return new string[0];
            }
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
            if (fullPath.Length > relativeStart
                && (fullPath[relativeStart] == Path.DirectorySeparatorChar || fullPath[relativeStart] == Path.AltDirectorySeparatorChar))
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

            EditorUIHelpers.DrawPopupHeader("Create Scene");

            EditorUIHelpers.InputTextWithWidth("Scene Name", ref _newSceneName, EditorUIHelpers.CompactPopupFieldWidth);

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
                    RefreshPanelData(projectPath, false);
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

            EditorUIHelpers.DrawPopupHeader("Create C# Script");

            EditorUIHelpers.InputTextWithWidth("Script Name", ref _newScriptName, EditorUIHelpers.CompactPopupFieldWidth);

            ImGui.Separator();
            if (ImGui.Button("Create"))
            {
                if (CreateScriptAsset(projectPath, _newScriptName))
                {
                    RefreshPanelData(projectPath, false);
                    _nextAutoRefreshUtc = DateTime.UtcNow.AddSeconds(_autoRefreshSeconds);
                    ImGui.CloseCurrentPopup();
                }
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }

        private static void DrawCreateFolderPopup(string projectPath)
        {
            if (!ImGui.BeginPopupModal(CreateFolderPopupId))
                return;

            EditorUIHelpers.DrawPopupHeader("Create Folder");

            EditorUIHelpers.InputTextWithWidth("Folder Name", ref _newFolderName, EditorUIHelpers.CompactPopupFieldWidth);

            ImGui.Separator();
            if (ImGui.Button("Create"))
            {
                string parentPath = ResolveCreateParentPath(projectPath, _contextTargetPath);
                string trimmedName = (_newFolderName ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(trimmedName))
                    trimmedName = "NewFolder";

                if (!StringUtilities.IsValidProjectName(trimmedName))
                {
                    ProjectOperations.SetStatusMessage("Create folder failed: invalid folder name.");
                }
                else
                {
                    string candidatePath = Path.Combine(parentPath, trimmedName);
                    if (Directory.Exists(candidatePath))
                    {
                        int suffix = 1;
                        while (true)
                        {
                            string nextPath = Path.Combine(parentPath, trimmedName + "_" + suffix);
                            if (!Directory.Exists(nextPath))
                            {
                                candidatePath = nextPath;
                                break;
                            }

                            ++suffix;
                        }
                    }

                    try
                    {
                        Directory.CreateDirectory(candidatePath);
                        _selectedPath = NormalizePath(candidatePath);
                        NavigateToFolder(parentPath, true);
                        RefreshPanelData(projectPath, false);
                        ProjectOperations.SetStatusMessage("Created folder: " + Path.GetFileName(candidatePath));
                        ImGui.CloseCurrentPopup();
                    }
                    catch (Exception ex)
                    {
                        ProjectOperations.SetStatusMessage("Create folder failed: " + ex.Message);
                    }
                }
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }

        private static void DrawRenamePopup(string projectPath)
        {
            if (!ImGui.BeginPopupModal(RenamePopupId))
                return;

            EditorUIHelpers.DrawPopupHeader("Rename Asset");
            ImGui.Text("Target: " + Path.GetFileName(_renameTargetPath));

            EditorUIHelpers.InputTextWithWidth("New Name", ref _renameValue, EditorUIHelpers.MediumPopupFieldWidth);

            ImGui.Separator();
            if (ImGui.Button("Rename"))
            {
                if (ExecuteRename(projectPath))
                    ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }

        private static void DrawDeletePopup(string projectPath)
        {
            if (!ImGui.BeginPopupModal(DeletePopupId))
                return;

            EditorUIHelpers.DrawPopupHeader("Delete Asset");
            ImGui.Text("Are you sure you want to delete:");
            ImGui.Text(Path.GetFileName(_deleteTargetPath));
            ImGui.Text("This cannot be undone.");

            ImGui.Separator();
            if (ImGui.Button("Delete"))
            {
                if (ExecuteDelete(projectPath))
                    ImGui.CloseCurrentPopup();
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

                string targetFolder = ResolveCreateParentPath(projectPath, _contextTargetPath);
                if (!Directory.Exists(targetFolder) || !StartsWithPath(targetFolder, projectPath))
                {
                    targetFolder = Path.Combine(projectPath, "Scripts");
                    Directory.CreateDirectory(targetFolder);
                }

                string className = BuildSafeScriptClassName(normalizedName);
                string candidatePath = Path.Combine(targetFolder, className + ".cs");
                if (File.Exists(candidatePath))
                {
                    int suffix = 1;
                    while (true)
                    {
                        string nextPath = Path.Combine(targetFolder, className + "_" + suffix + ".cs");
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
                NavigateToFolder(targetFolder, true);
                RefreshPanelData(projectPath, false);
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
            return "using Engine;\n\n"
                  + "namespace GameScripts\n"
                  + "{\n"
                  + "    public sealed class " + className + "\n"
                  + "    {\n"
                  + "        public void OnCreate(uint entityId)\n"
                  + "        {\n"
                + "            Debug.Log(\"[GameScripts] " + escapedClassName + " created for entity \" + entityId + \".\");\n"
                  + "        }\n\n"
                  + "        public void OnEnable(uint entityId)\n"
                  + "        {\n"
                + "            Debug.Log(\"[GameScripts] " + escapedClassName + " enabled on entity \" + entityId + \".\");\n"
                  + "        }\n\n"
                  + "        public void OnDisable(uint entityId)\n"
                  + "        {\n"
                + "            Debug.Log(\"[GameScripts] " + escapedClassName + " disabled on entity \" + entityId + \".\");\n"
                  + "        }\n\n"
                  + "        public void OnUpdate(uint entityId, float deltaTime)\n"
                  + "        {\n"
                  + "        }\n\n"
                  + "        public void OnDestroy(uint entityId)\n"
                  + "        {\n"
                + "            Debug.Log(\"[GameScripts] " + escapedClassName + " destroyed for entity \" + entityId + \".\");\n"
                  + "        }\n"
                  + "    }\n"
                  + "}\n";
        }

        private static bool ExistsPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            return File.Exists(path) || Directory.Exists(path);
        }

        private static bool PathEquals(string lhs, string rhs)
        {
            if (string.IsNullOrWhiteSpace(lhs) || string.IsNullOrWhiteSpace(rhs))
                return false;

            return string.Equals(NormalizePath(lhs), NormalizePath(rhs), StringComparison.OrdinalIgnoreCase);
        }

        private static bool StartsWithPath(string childPath, string parentPath)
        {
            if (string.IsNullOrWhiteSpace(childPath) || string.IsNullOrWhiteSpace(parentPath))
                return false;

            string child = NormalizePath(childPath);
            string parent = NormalizePath(parentPath);
            if (string.IsNullOrEmpty(child) || string.IsNullOrEmpty(parent))
                return false;

            if (string.Equals(child, parent, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!child.StartsWith(parent, StringComparison.OrdinalIgnoreCase))
                return false;

            if (child.Length <= parent.Length)
                return false;

            char separator = child[parent.Length];
            return separator == Path.DirectorySeparatorChar || separator == Path.AltDirectorySeparatorChar;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            try
            {
                string full = Path.GetFullPath(path);
                return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
