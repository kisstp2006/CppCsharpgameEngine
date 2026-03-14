using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

using Engine;

namespace EngineEditor
{
    internal static class ProjectManager
    {
        private enum SortMode
        {
            NameAsc = 0,
            LastModifiedDesc = 1,
        }

        private enum ModalType
        {
            None = 0,
            Rename = 1,
            Delete = 2,
            NewProject = 3,
        }

        private struct ListViewState
        {
            public int page;
            public float scrollOffset;
        }

        private struct ProjectMetadataSummary
        {
            public string name;
            public string path;
            public string template;
            public string engineVersion;
            public string createdAtRaw;
            public DateTime createdAtLocal;
            public bool hasCreatedAt;
            public DateTime lastModifiedLocal;
        }

        private static string _editorConfigDir = string.Empty;
        private static string _favoritesFile = string.Empty;
        private static readonly HashSet<string> _favoriteProjectPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static string _projectsRoot = string.Empty;
        private static string[] _projectPaths = new string[0];
        private static int _selectedProjectIndex = -1;
        private static string _selectedProjectPathOverride = string.Empty;
        private static string _projectSearchText = string.Empty;
        private static string _renameProjectName = string.Empty;

        private static bool _builtInUiInitialized;
        private static float _uiClock;

        private const int SidebarItemGetSetUp = 0;
        private const int SidebarItemProjects = 1;
        private const int SidebarItemInstalls = 2;
        private const int SidebarItemLearn = 3;
        private const int SidebarItemResources = 4;
        private const int SidebarItemLicenses = 5;
        private const int SidebarItemSettings = 6;
        private const int SidebarItemCount = 7;

        private static int _activeSidebarItem = SidebarItemProjects;
        private static readonly float[] _sidebarHoverAnim = new float[SidebarItemCount];
        private static readonly float[] _sidebarActiveAnim = new float[SidebarItemCount];

        private static ListViewState _recentListState;
        private static ListViewState _allListState;

        private static SortMode _sortMode = SortMode.NameAsc;

        private static readonly Dictionary<string, ProjectMetadataSummary> _metadataByPath = new Dictionary<string, ProjectMetadataSummary>(StringComparer.OrdinalIgnoreCase);

        private static string _lastLeftClickProjectPath = string.Empty;
        private static float _lastLeftClickTime;

        private static bool _contextMenuOpen;
        private static string _contextMenuProjectPath = string.Empty;
        private static Rect _contextMenuRect;

        private static ModalType _activeModal;
        private static string _modalProjectPath = string.Empty;

        private static string _renameInputValue = string.Empty;
        private static string _newProjectName = "NewProject";
        private static string _newProjectLocation = string.Empty;
        private static int _newTemplateIndex;

        private static readonly TextInput _searchInput = new TextInput();
        private static readonly TextInput _renameInput = new TextInput();
        private static readonly TextInput _newNameInput = new TextInput();
        private static readonly TextInput _newLocationInput = new TextInput();

        private static readonly Button _refreshButton = new Button();
        private static readonly Button _openLastButton = new Button();
        private static readonly Button _browseButton = new Button();
        private static readonly Button _newProjectButton = new Button();
        private static readonly Button _sortButton = new Button();
        private static readonly Button _settingsButton = new Button();
        private static readonly Button _openEditorButton = new Button();

        private static readonly Button _recentPrevButton = new Button();
        private static readonly Button _recentNextButton = new Button();
        private static readonly Button _allPrevButton = new Button();
        private static readonly Button _allNextButton = new Button();

        private static readonly Button _detailsOpenButton = new Button();
        private static readonly Button _detailsRenameButton = new Button();
        private static readonly Button _detailsDeleteButton = new Button();

        private static readonly Button _contextOpenButton = new Button();
        private static readonly Button _contextRenameButton = new Button();
        private static readonly Button _contextDeleteButton = new Button();
        private static readonly Button _contextShowFolderButton = new Button();

        private static readonly Button _renameCancelButton = new Button();
        private static readonly Button _renameConfirmButton = new Button();
        private static readonly Button _deleteCancelButton = new Button();
        private static readonly Button _deleteConfirmButton = new Button();
        private static readonly Button _newCancelButton = new Button();
        private static readonly Button _newCreateButton = new Button();
        private static readonly Button _newBrowseButton = new Button();
        private static readonly Button _templatePrevButton = new Button();
        private static readonly Button _templateNextButton = new Button();

        private const float ProjectManagerTopInset = 34.0f;
        private const float RowHeight = 46.0f;
        private const float RowSpacing = 6.0f;
        private const float DoubleClickThresholdSeconds = 0.30f;

        public static string ProjectsRoot => _projectsRoot;
        public static string[] ProjectPaths => _projectPaths;
        public static int SelectedProjectIndex => _selectedProjectIndex;

        public static void Initialize(string editorConfigDir)
        {
            _editorConfigDir = editorConfigDir ?? string.Empty;
            if (string.IsNullOrEmpty(_editorConfigDir))
                _editorConfigDir = Path.Combine(Directory.GetCurrentDirectory(), ".editor");

            Directory.CreateDirectory(_editorConfigDir);

            _favoritesFile = Path.Combine(_editorConfigDir, "project_favorites.txt");
            LoadFavorites();

            _projectsRoot = ResolveProjectsRoot();
            Directory.CreateDirectory(_projectsRoot);

            _projectSearchText = string.Empty;
            _searchInput.text = string.Empty;
            _searchInput.placeholder = "Search projects";
            _searchInput.maxLength = 256;

            _renameInput.placeholder = "Project Name";
            _renameInput.maxLength = 128;

            _newNameInput.placeholder = "Project Name";
            _newNameInput.maxLength = 128;
            _newLocationInput.placeholder = "Location";
            _newLocationInput.maxLength = 512;

            _newProjectLocation = _projectsRoot;
            _newTemplateIndex = 0;

            RefreshProjectList();
        }

        private static string ResolveProjectsRoot()
        {
            string cwd = SafeGetFullPath(Directory.GetCurrentDirectory());
            string fromCwd = TryResolveProjectsRootFromStart(cwd);
            if (!string.IsNullOrEmpty(fromCwd))
                return fromCwd;

            string baseDir = SafeGetFullPath(AppContext.BaseDirectory);
            string fromBaseDir = TryResolveProjectsRootFromStart(baseDir);
            if (!string.IsNullOrEmpty(fromBaseDir))
                return fromBaseDir;

            return Path.Combine(cwd, "projects");
        }

        private static string TryResolveProjectsRootFromStart(string startPath)
        {
            if (string.IsNullOrWhiteSpace(startPath))
                return string.Empty;

            string current = startPath;
            while (!string.IsNullOrEmpty(current))
            {
                string projectsPath = Path.Combine(current, "projects");
                bool hasProjects = Directory.Exists(projectsPath);
                bool hasRepoMarkers = File.Exists(Path.Combine(current, "CMakeLists.txt")) ||
                                      Directory.Exists(Path.Combine(current, "src"));

                if (hasProjects && hasRepoMarkers)
                    return projectsPath;

                DirectoryInfo parent = Directory.GetParent(current);
                if (parent == null)
                    break;

                current = parent.FullName;
            }

            return string.Empty;
        }

        private static string SafeGetFullPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return path;
            }
        }

        public static void RefreshProjectList()
        {
            try
            {
                if (!Directory.Exists(_projectsRoot))
                    Directory.CreateDirectory(_projectsRoot);

                _projectPaths = Directory.GetDirectories(_projectsRoot);
                RebuildMetadataCache();
                SortProjectPathsInPlace();

                if (_selectedProjectIndex >= _projectPaths.Length)
                    _selectedProjectIndex = _projectPaths.Length - 1;

                if (!string.IsNullOrEmpty(_selectedProjectPathOverride) && !Directory.Exists(_selectedProjectPathOverride))
                    _selectedProjectPathOverride = string.Empty;
            }
            catch (Exception ex)
            {
                ProjectOperations.SetStatusMessage("Refresh failed: " + ex.Message);
            }
        }

        private static void RebuildMetadataCache()
        {
            _metadataByPath.Clear();
            for (int i = 0; i < _projectPaths.Length; ++i)
            {
                string path = _projectPaths[i];
                if (!Directory.Exists(path))
                    continue;

                _metadataByPath[path] = BuildMetadata(path);
            }

            string[] recent = ProjectOperations.GetRecentProjects();
            for (int i = 0; i < recent.Length; ++i)
            {
                string recentPath = recent[i];
                if (!Directory.Exists(recentPath) || _metadataByPath.ContainsKey(recentPath))
                    continue;

                _metadataByPath[recentPath] = BuildMetadata(recentPath);
            }
        }

        private static void SortProjectPathsInPlace()
        {
            Array.Sort(_projectPaths, CompareProjectPaths);
        }

        private static int CompareProjectPaths(string a, string b)
        {
            if (_sortMode == SortMode.LastModifiedDesc)
            {
                DateTime aTime = GetMetadata(a).lastModifiedLocal;
                DateTime bTime = GetMetadata(b).lastModifiedLocal;

                int timeCompare = DateTime.Compare(bTime, aTime);
                if (timeCompare != 0)
                    return timeCompare;
            }

            return StringComparer.OrdinalIgnoreCase.Compare(Path.GetFileName(a), Path.GetFileName(b));
        }

        public static bool TryGetSelectedProjectPath(out string projectPath)
        {
            if (_selectedProjectIndex >= 0 && _selectedProjectIndex < _projectPaths.Length)
            {
                projectPath = _projectPaths[_selectedProjectIndex];
                return true;
            }

            if (!string.IsNullOrEmpty(_selectedProjectPathOverride) && Directory.Exists(_selectedProjectPathOverride))
            {
                projectPath = _selectedProjectPathOverride;
                return true;
            }

            projectPath = string.Empty;
            return false;
        }

        public static void SelectProject(int index, string projectName)
        {
            _selectedProjectIndex = index;
            _renameProjectName = projectName;
            if (index >= 0 && index < _projectPaths.Length)
                _selectedProjectPathOverride = _projectPaths[index];
        }

        public static void ClearSelection()
        {
            _selectedProjectIndex = -1;
            _renameProjectName = string.Empty;
            _selectedProjectPathOverride = string.Empty;
        }

        public static bool MatchesProjectSearch(string value)
        {
            if (string.IsNullOrWhiteSpace(_projectSearchText))
                return true;

            string search = _projectSearchText.Trim();
            if (search.Length == 0)
                return true;

            return value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static void DrawProjectPanelBuiltInUi(float deltaTime)
        {
            EnsureBuiltInUiInitialized();
            _uiClock += deltaTime > 0.0f ? deltaTime : (1.0f / 60.0f);

            BuiltInUiPalette palette = BuiltInUiTheme.Capture();

            Canvas canvas = new Canvas();
            Rect canvasRect = canvas.Bounds;

            UI.DrawFilledRoundedRect(canvasRect, BuiltInUiTheme.WithAlpha(palette.windowBg, 255), 0.0f);
            float sidebarWidth = canvasRect.width < 980.0f ? 178.0f : 224.0f;
            Rect sidebarRect = new Rect(0.0f, 0.0f, sidebarWidth, canvasRect.height);
            Rect mainRect = new Rect(sidebarRect.Right,
                                     0.0f,
                                     canvasRect.width - sidebarRect.width,
                                     canvasRect.height);

            DrawHubSidebar(sidebarRect, palette);
            DrawHubMain(mainRect, palette);

            DrawContextMenu(palette, canvasRect);
            DrawModalLayer(palette, canvasRect);

            Rect statusRect = new Rect(mainRect.x + 20.0f,
                                       mainRect.Bottom - 26.0f,
                                       mainRect.width - 40.0f,
                                       20.0f);
            UI.DrawLabel(statusRect,
                         "Status: " + EditorContext.StatusMessage,
                         BuiltInUiTheme.WithAlpha(palette.textDisabled, 255),
                         11.0f,
                         false,
                         TextAlign.Left);
        }

        private static void DrawHubSidebar(Rect sidebarRect, BuiltInUiPalette palette)
        {
            UI.DrawFilledRoundedRect(sidebarRect, BuiltInUiTheme.WithAlpha(BuiltInUiTheme.TintTowardBlack(palette.windowBg, 0.14f), 255), 0.0f);
            UI.DrawRoundedRect(new Rect(sidebarRect.Right - 1.0f, sidebarRect.y, 1.0f, sidebarRect.height),
                               BuiltInUiTheme.WithAlpha(palette.border, 180),
                               0.0f,
                               1.0f);

            Rect hubRect = new Rect(sidebarRect.x + 16.0f, sidebarRect.y + 12.0f, sidebarRect.width - 32.0f, 40.0f);
            UI.DrawLabel(hubRect,
                         "Hub",
                         BuiltInUiTheme.WithAlpha(palette.text, 255),
                         30.0f,
                         true,
                         TextAlign.Left);

            float y = sidebarRect.y + 70.0f;
            float itemHeight = 36.0f;
            float itemGap = 38.0f;

            DrawSidebarItem(new Rect(sidebarRect.x + 10.0f, y, sidebarRect.width - 20.0f, itemHeight), "Get set up", SidebarItemGetSetUp, palette);
            y += itemGap;
            DrawSidebarItem(new Rect(sidebarRect.x + 10.0f, y, sidebarRect.width - 20.0f, itemHeight), "Projects", SidebarItemProjects, palette);
            y += itemGap;
            DrawSidebarItem(new Rect(sidebarRect.x + 10.0f, y, sidebarRect.width - 20.0f, itemHeight), "Installs", SidebarItemInstalls, palette);
            y += itemGap;
            DrawSidebarItem(new Rect(sidebarRect.x + 10.0f, y, sidebarRect.width - 20.0f, itemHeight), "Learn", SidebarItemLearn, palette);
            y += itemGap;
            DrawSidebarItem(new Rect(sidebarRect.x + 10.0f, y, sidebarRect.width - 20.0f, itemHeight), "Resources", SidebarItemResources, palette);
            y += itemGap;
            DrawSidebarItem(new Rect(sidebarRect.x + 10.0f, y, sidebarRect.width - 20.0f, itemHeight), "Licenses", SidebarItemLicenses, palette);

            Rect settingsRect = new Rect(sidebarRect.x + 10.0f,
                                         sidebarRect.Bottom - 54.0f,
                                         sidebarRect.width - 20.0f,
                                         36.0f);
            DrawSidebarItem(settingsRect, "Settings", SidebarItemSettings, palette);
        }

        private static void DrawSidebarItem(Rect itemRect, string text, int itemId, BuiltInUiPalette palette)
        {
            if (itemId < 0 || itemId >= SidebarItemCount)
                return;

            bool hovered = itemRect.Contains(Input.GetMousePosX(), Input.GetMousePosY());
            if (hovered && Input.GetMouseButtonDown(MouseButton.Left))
                ActivateSidebarItem(itemId, text);

            float dt = Time.deltaTime > 0.0f ? Time.deltaTime : (1.0f / 60.0f);
            float blend = Clamp(dt * 11.0f, 0.0f, 1.0f);

            float hoverTarget = hovered ? 1.0f : 0.0f;
            float activeTarget = _activeSidebarItem == itemId ? 1.0f : 0.0f;

            _sidebarHoverAnim[itemId] += (hoverTarget - _sidebarHoverAnim[itemId]) * blend;
            _sidebarActiveAnim[itemId] += (activeTarget - _sidebarActiveAnim[itemId]) * blend;

            uint transparent = UI.Rgba(0, 0, 0, 0);
            uint hoverColor = BuiltInUiTheme.WithAlpha(BuiltInUiTheme.TintTowardWhite(palette.frameBg, 0.08f), 150);
            uint activeColor = BuiltInUiTheme.WithAlpha(BuiltInUiTheme.TintTowardWhite(palette.frameBg, 0.15f), 230);

            uint bg = UI.LerpColor(transparent, hoverColor, _sidebarHoverAnim[itemId]);
            bg = UI.LerpColor(bg, activeColor, _sidebarActiveAnim[itemId]);

            if (_sidebarHoverAnim[itemId] > 0.01f || _sidebarActiveAnim[itemId] > 0.01f)
            {
                UI.DrawFilledRoundedRect(itemRect, bg, 6.0f);

                float accentWidth = 2.0f + (_sidebarActiveAnim[itemId] * 2.5f);
                Rect accentRect = new Rect(itemRect.x + 2.0f,
                                           itemRect.y + 5.0f,
                                           accentWidth,
                                           itemRect.height - 10.0f);
                uint accentColor = UI.LerpColor(BuiltInUiTheme.WithAlpha(palette.separator, 0),
                                                BuiltInUiTheme.WithAlpha(palette.dockingPreview, 255),
                                                Max(_sidebarHoverAnim[itemId] * 0.55f, _sidebarActiveAnim[itemId]));
                UI.DrawFilledRoundedRect(accentRect, accentColor, 2.0f);
            }

            Rect textRect = new Rect(itemRect.x + 14.0f, itemRect.y, itemRect.width - 20.0f, itemRect.height);
            uint textColor = UI.LerpColor(BuiltInUiTheme.WithAlpha(palette.textDisabled, 240),
                                          BuiltInUiTheme.WithAlpha(palette.text, 255),
                                          Max(_sidebarHoverAnim[itemId] * 0.4f, _sidebarActiveAnim[itemId]));

            UI.DrawLabel(textRect,
                         text,
                         textColor,
                         12.7f,
                         _sidebarActiveAnim[itemId] > 0.52f,
                         TextAlign.Left);
        }

        private static void ActivateSidebarItem(int itemId, string label)
        {
            _activeSidebarItem = itemId;
            if (itemId != SidebarItemProjects)
                ProjectOperations.SetStatusMessage(label + " panel is not available yet.");
        }

        private static void DrawHubMain(Rect mainRect, BuiltInUiPalette palette)
        {
            if (_recentListState.page < 0)
                _recentListState.page = 0;

            Rect topRect = new Rect(mainRect.x + 20.0f, mainRect.y + 10.0f, mainRect.width - 40.0f, 62.0f);
            DrawHubTopBar(topRect, palette);

            Rect tableRect = new Rect(mainRect.x + 20.0f,
                                      topRect.Bottom + 10.0f,
                                      mainRect.width - 40.0f,
                                      mainRect.height - topRect.height - 46.0f);
            DrawHubProjectTable(tableRect, palette);
        }

        private static void DrawHubTopBar(Rect topRect, BuiltInUiPalette palette)
        {
            Rect titleRect = new Rect(topRect.x, topRect.y + 8.0f, 180.0f, 38.0f);
            UI.DrawLabel(titleRect,
                         "Projects",
                         BuiltInUiTheme.WithAlpha(palette.text, 255),
                         23.0f,
                         true,
                         TextAlign.Left);

            float buttonHeight = 32.0f;
            float cursorX = topRect.Right;

            _newProjectButton.text = "+ New project";
            _newProjectButton.rect = new Rect(cursorX - 136.0f, topRect.y + 8.0f, 136.0f, buttonHeight);
            _newProjectButton.interactable = true;
            _newProjectButton.normalColor = UI.Rgba(22, 122, 228, 255);
            _newProjectButton.highlightedColor = UI.Rgba(39, 138, 241, 255);
            _newProjectButton.pressedColor = UI.Rgba(16, 102, 194, 255);
            _newProjectButton.borderColor = UI.Rgba(110, 187, 255, 240);
            _newProjectButton.textColor = UI.Rgba(246, 251, 255, 255);
            if (_newProjectButton.Draw())
                OpenNewProjectModal();
            cursorX -= 140.0f;

            _browseButton.text = "Add";
            _browseButton.rect = new Rect(cursorX - 76.0f, topRect.y + 8.0f, 76.0f, buttonHeight);
            BuiltInUiTheme.ApplyActionButtonTheme(_browseButton, palette);
            if (_browseButton.Draw())
                BrowseAndSelectProject();
            cursorX -= 80.0f;

            _searchInput.rect = new Rect(cursorX - 292.0f, topRect.y + 8.0f, 292.0f, buttonHeight);
            _searchInput.hitTestOrder = 5600;
            _searchInput.consumeInput = true;
            _searchInput.placeholder = "Search";
            _searchInput.text = _projectSearchText;
            _searchInput.normalColor = UI.Rgba(44, 49, 57, 255);
            _searchInput.hoveredColor = UI.Rgba(56, 62, 72, 255);
            _searchInput.focusedColor = UI.Rgba(60, 67, 78, 255);
            _searchInput.borderColor = UI.Rgba(77, 85, 99, 230);
            _searchInput.textColor = BuiltInUiTheme.WithAlpha(palette.text, 255);
            _searchInput.placeholderColor = BuiltInUiTheme.WithAlpha(palette.textDisabled, 235);
            if (_searchInput.Draw())
            {
                _projectSearchText = _searchInput.text ?? string.Empty;
                _allListState.scrollOffset = 0.0f;
                _allListState.page = 0;
            }
        }

        private static void DrawHubProjectTable(Rect tableRect, BuiltInUiPalette palette)
        {
            if (tableRect.width < 220.0f || tableRect.height < 120.0f)
                return;

            UI.DrawFilledRoundedRect(tableRect, BuiltInUiTheme.WithAlpha(BuiltInUiTheme.TintTowardBlack(palette.childBg, 0.08f), 250), 8.0f);
            UI.DrawRoundedRect(tableRect, BuiltInUiTheme.WithAlpha(palette.border, 205), 8.0f, 1.0f);

            float headerHeight = 44.0f;
            Rect headerRect = new Rect(tableRect.x, tableRect.y, tableRect.width, headerHeight);
            Rect bodyRect = new Rect(tableRect.x,
                                     headerRect.Bottom,
                                     tableRect.width,
                                     tableRect.height - headerHeight);

            UI.DrawFilledRoundedRect(headerRect,
                                     BuiltInUiTheme.WithAlpha(BuiltInUiTheme.TintTowardWhite(palette.frameBg, 0.04f), 245),
                                     8.0f);

            float colStar = 38.0f;
            float colLink = 38.0f;
            float colCloud = 40.0f;
            float colModified = 176.0f;
            float colVersion = 162.0f;
            float colMenu = 36.0f;

            float nameWidth = tableRect.width - (colStar + colLink + colCloud + colModified + colVersion + colMenu);
            if (nameWidth < 260.0f)
            {
                float deficit = 260.0f - nameWidth;
                float modReduction = deficit * 0.62f;
                float verReduction = deficit * 0.38f;
                colModified = Max(126.0f, colModified - modReduction);
                colVersion = Max(116.0f, colVersion - verReduction);
                nameWidth = tableRect.width - (colStar + colLink + colCloud + colModified + colVersion + colMenu);
            }

            float xStar = tableRect.x;
            float xLink = xStar + colStar;
            float xCloud = xLink + colLink;
            float xName = xCloud + colCloud;
            float xModified = xName + nameWidth;
            float xVersion = xModified + colModified;
            float xMenu = xVersion + colVersion;

            UI.DrawLabel(new Rect(xStar, headerRect.y, colStar, headerRect.height), "*", BuiltInUiTheme.WithAlpha(palette.textDisabled, 235), 11.8f, false, TextAlign.Center);
            UI.DrawLabel(new Rect(xLink, headerRect.y, colLink, headerRect.height), "o-o", BuiltInUiTheme.WithAlpha(palette.textDisabled, 235), 10.8f, false, TextAlign.Center);
            UI.DrawLabel(new Rect(xCloud, headerRect.y, colCloud, headerRect.height), "o", BuiltInUiTheme.WithAlpha(palette.textDisabled, 235), 10.8f, false, TextAlign.Center);
            UI.DrawLabel(new Rect(xName + 10.0f, headerRect.y, nameWidth - 20.0f, headerRect.height), "Name", BuiltInUiTheme.WithAlpha(palette.text, 255), 12.4f, true, TextAlign.Left);

            string modifiedHeader = _sortMode == SortMode.LastModifiedDesc ? "Modified  v" : "Modified";
            UI.DrawLabel(new Rect(xModified + 10.0f, headerRect.y, colModified - 20.0f, headerRect.height), modifiedHeader, BuiltInUiTheme.WithAlpha(palette.text, 255), 12.4f, true, TextAlign.Left);
            UI.DrawLabel(new Rect(xVersion + 10.0f, headerRect.y, colVersion - 20.0f, headerRect.height), "Editor version", BuiltInUiTheme.WithAlpha(palette.text, 255), 12.4f, true, TextAlign.Left);

            _sortButton.text = _sortMode == SortMode.NameAsc ? "N" : "M";
            _sortButton.rect = new Rect(xMenu + 2.0f, headerRect.y + 6.0f, colMenu - 4.0f, headerRect.height - 12.0f);
            BuiltInUiTheme.ApplyActionButtonTheme(_sortButton, palette);
            if (_sortButton.Draw())
                CycleSortMode();

            string[] rows = BuildHubProjectList();
            if (rows.Length == 0)
            {
                UI.DrawLabel(bodyRect,
                             "No projects found.",
                             BuiltInUiTheme.WithAlpha(palette.textDisabled, 255),
                             12.0f,
                             false,
                             TextAlign.Center);
                return;
            }

            float step = 60.0f;
            float contentHeight = rows.Length * step;
            float maxScroll = contentHeight - bodyRect.height;
            if (maxScroll < 0.0f)
                maxScroll = 0.0f;

            if (bodyRect.Contains(Input.GetMousePosX(), Input.GetMousePosY()))
            {
                float wheel = Input.GetMouseWheel();
                if (Math.Abs(wheel) > 0.0001f)
                {
                    _allListState.scrollOffset -= wheel * 36.0f;
                    if (_allListState.scrollOffset < 0.0f)
                        _allListState.scrollOffset = 0.0f;
                    if (_allListState.scrollOffset > maxScroll)
                        _allListState.scrollOffset = maxScroll;
                }
            }

            int startIndex = (int)(_allListState.scrollOffset / step);
            if (startIndex < 0)
                startIndex = 0;

            int visibleCount = (int)(bodyRect.height / step) + 3;
            int endIndex = Min(rows.Length, startIndex + visibleCount);
            float offsetWithinRow = _allListState.scrollOffset - (startIndex * step);

            string selectedPath = GetSelectedProjectPathForUi();
            for (int i = startIndex; i < endIndex; ++i)
            {
                float rowY = bodyRect.y + ((i - startIndex) * step) - offsetWithinRow;
                Rect rowRect = new Rect(bodyRect.x, rowY, bodyRect.width, step - 1.0f);
                DrawHubProjectRow(rowRect,
                                  rows[i],
                                  selectedPath,
                                  i,
                                  xStar,
                                  xLink,
                                  xCloud,
                                  xName,
                                  nameWidth,
                                  xModified,
                                  colModified,
                                  xVersion,
                                  colVersion,
                                  xMenu,
                                  colMenu,
                                  palette);
            }
        }

        private static string[] BuildHubProjectList()
        {
            var result = new List<string>();
            var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string[] recent = ProjectOperations.GetRecentProjects();
            for (int i = 0; i < recent.Length; ++i)
            {
                string path = recent[i];
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                string normalized = NormalizePath(path);
                if (added.Contains(normalized))
                    continue;

                string projectName = Path.GetFileName(path);
                if (!MatchesProjectSearch(projectName) && !MatchesProjectSearch(path))
                    continue;

                result.Add(path);
                added.Add(normalized);
            }

            for (int i = 0; i < _projectPaths.Length; ++i)
            {
                string path = _projectPaths[i];
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                string normalized = NormalizePath(path);
                if (added.Contains(normalized))
                    continue;

                string projectName = Path.GetFileName(path);
                if (!MatchesProjectSearch(projectName) && !MatchesProjectSearch(path))
                    continue;

                result.Add(path);
                added.Add(normalized);
            }

            result.Sort(CompareProjectPaths);
            return result.ToArray();
        }

        private static void DrawHubProjectRow(Rect rowRect,
                                              string projectPath,
                                              string selectedPath,
                                              int rowIndex,
                                              float xStar,
                                              float xLink,
                                              float xCloud,
                                              float xName,
                                              float nameWidth,
                                              float xModified,
                                              float colModified,
                                              float xVersion,
                                              float colVersion,
                                              float xMenu,
                                              float colMenu,
                                              BuiltInUiPalette palette)
        {
            bool exists = Directory.Exists(projectPath);
            bool hovered = rowRect.Contains(Input.GetMousePosX(), Input.GetMousePosY());
            bool selected = string.Equals(selectedPath, projectPath, StringComparison.OrdinalIgnoreCase);
            bool striped = (rowIndex % 2) == 0;

            uint baseColor = striped
                ? UI.Rgba(21, 24, 31, 255)
                : UI.Rgba(17, 20, 27, 255);
            uint rowColor = selected
                ? BuiltInUiTheme.WithAlpha(palette.headerActive, 220)
                : (hovered ? BuiltInUiTheme.WithAlpha(BuiltInUiTheme.TintTowardWhite(baseColor, 0.08f), 255) : baseColor);

            UI.DrawFilledRoundedRect(rowRect, rowColor, 0.0f);

            Rect favoriteRect = new Rect(xStar + 10.0f, rowRect.y + 21.0f, 16.0f, 16.0f);
            Rect linkRect = new Rect(xLink + 6.0f, rowRect.y + 22.0f, 24.0f, 14.0f);
            Rect cloudRect = new Rect(xCloud + 6.0f, rowRect.y + 22.0f, 26.0f, 14.0f);
            Rect nameRect = new Rect(xName + 10.0f, rowRect.y + 9.0f, nameWidth - 16.0f, 22.0f);
            Rect pathRect = new Rect(xName + 10.0f, rowRect.y + 32.0f, nameWidth - 16.0f, 18.0f);
            Rect modifiedRect = new Rect(xModified + 10.0f, rowRect.y, colModified - 16.0f, rowRect.height);
            Rect versionRect = new Rect(xVersion + 10.0f, rowRect.y, colVersion - 16.0f, rowRect.height);
            Rect menuRect = new Rect(xMenu + 2.0f, rowRect.y + 12.0f, colMenu - 4.0f, rowRect.height - 24.0f);

            bool favorite = IsFavorite(projectPath);
            UI.DrawLabel(favoriteRect,
                         favorite ? "*" : "o",
                         favorite ? UI.Rgba(255, 218, 122, 255) : BuiltInUiTheme.WithAlpha(palette.textDisabled, 210),
                         13.0f,
                         true,
                         TextAlign.Center);

            UI.DrawLabel(linkRect,
                         "o-o",
                         BuiltInUiTheme.WithAlpha(palette.textDisabled, 215),
                         10.5f,
                         false,
                         TextAlign.Center);

            UI.DrawLabel(cloudRect,
                         exists ? "o" : "!",
                         exists ? BuiltInUiTheme.WithAlpha(palette.textDisabled, 215) : UI.Rgba(255, 198, 78, 255),
                         11.0f,
                         true,
                         TextAlign.Center);

            ProjectMetadataSummary metadata = GetMetadata(projectPath);
            string projectName = string.IsNullOrEmpty(metadata.name) ? Path.GetFileName(projectPath) : metadata.name;
            string subtitle = exists ? TruncateMiddle(projectPath.Replace('\\', '/'), 82) : "Project not found";

            UI.DrawLabel(nameRect,
                         projectName,
                         BuiltInUiTheme.WithAlpha(palette.text, 255),
                         13.3f,
                         true,
                         TextAlign.Left);

            UI.DrawLabel(pathRect,
                         subtitle,
                         exists ? BuiltInUiTheme.WithAlpha(palette.textDisabled, 235) : UI.Rgba(255, 197, 85, 255),
                         10.8f,
                         false,
                         TextAlign.Left);

            string modified = exists ? FormatDate(metadata.lastModifiedLocal) : "--";
            string version = string.IsNullOrWhiteSpace(metadata.engineVersion) ? "2026.03f1" : metadata.engineVersion;

            UI.DrawLabel(modifiedRect,
                         modified,
                         BuiltInUiTheme.WithAlpha(palette.text, 245),
                         11.2f,
                         false,
                         TextAlign.Left);

            UI.DrawLabel(versionRect,
                         exists ? version : "! " + version,
                         exists ? BuiltInUiTheme.WithAlpha(palette.text, 245) : UI.Rgba(255, 117, 122, 255),
                         11.2f,
                         false,
                         TextAlign.Left);

            UI.DrawLabel(menuRect,
                         "...",
                         BuiltInUiTheme.WithAlpha(palette.textDisabled, 230),
                         12.0f,
                         true,
                         TextAlign.Center);

            if (_activeModal != ModalType.None)
                return;

            if (favoriteRect.Contains(Input.GetMousePosX(), Input.GetMousePosY()) && Input.GetMouseButtonDown(MouseButton.Left))
            {
                ToggleFavorite(projectPath);
                return;
            }

            if ((hovered || menuRect.Contains(Input.GetMousePosX(), Input.GetMousePosY())) && Input.GetMouseButtonDown(MouseButton.Right))
            {
                SelectProjectPath(projectPath);
                OpenContextMenu(projectPath, Input.GetMousePosX(), Input.GetMousePosY());
                return;
            }

            if (hovered && Input.GetMouseButtonDown(MouseButton.Left))
            {
                SelectProjectPath(projectPath);

                if (string.Equals(_lastLeftClickProjectPath, projectPath, StringComparison.OrdinalIgnoreCase)
                    && (_uiClock - _lastLeftClickTime) <= DoubleClickThresholdSeconds)
                {
                    OpenProjectAndEnterEditor(projectPath);
                    _lastLeftClickProjectPath = string.Empty;
                }
                else
                {
                    _lastLeftClickProjectPath = projectPath;
                    _lastLeftClickTime = _uiClock;
                }
            }
        }

        private static string GetSelectedProjectPathForUi()
        {
            if (!string.IsNullOrEmpty(_selectedProjectPathOverride))
                return _selectedProjectPathOverride;

            if (_selectedProjectIndex >= 0 && _selectedProjectIndex < _projectPaths.Length)
                return _projectPaths[_selectedProjectIndex];

            return string.Empty;
        }

        private static void EnsureBuiltInUiInitialized()
        {
            if (_builtInUiInitialized)
                return;

            _builtInUiInitialized = true;
            EditorBridge.SetWindowBackgroundImage("assets/editor/splash_logo.png");
            EditorBridge.SetWindowBackgroundVisible(true);

            ConfigureActionButton(_refreshButton, "Refresh", 5000);
            ConfigureActionButton(_openLastButton, "Open Last", 5001);
            ConfigureActionButton(_browseButton, "Browse", 5002);
            ConfigureActionButton(_newProjectButton, "New Project", 5003);
            ConfigureActionButton(_sortButton, "Sort", 5004);
            ConfigureActionButton(_settingsButton, "Settings", 5005);
            ConfigureActionButton(_openEditorButton, "Open Editor", 5006);

            ConfigureActionButton(_recentPrevButton, "<", 5100);
            ConfigureActionButton(_recentNextButton, ">", 5101);
            ConfigureActionButton(_allPrevButton, "<", 5102);
            ConfigureActionButton(_allNextButton, ">", 5103);

            ConfigureActionButton(_detailsOpenButton, "Open", 5200);
            ConfigureActionButton(_detailsRenameButton, "Rename", 5201);
            ConfigureDangerButton(_detailsDeleteButton, "Delete", 5202);

            ConfigureActionButton(_contextOpenButton, "Open", 5300);
            ConfigureActionButton(_contextRenameButton, "Rename", 5301);
            ConfigureDangerButton(_contextDeleteButton, "Delete", 5302);
            ConfigureActionButton(_contextShowFolderButton, "Show in Folder", 5303);

            ConfigureActionButton(_renameCancelButton, "Cancel", 5400);
            ConfigureActionButton(_renameConfirmButton, "Rename", 5401);

            ConfigureActionButton(_deleteCancelButton, "Cancel", 5402);
            ConfigureDangerButton(_deleteConfirmButton, "Delete", 5403);

            ConfigureActionButton(_newCancelButton, "Cancel", 5404);
            ConfigureActionButton(_newCreateButton, "Create", 5405);
            ConfigureActionButton(_newBrowseButton, "Browse", 5406);
            ConfigureActionButton(_templatePrevButton, "<", 5407);
            ConfigureActionButton(_templateNextButton, ">", 5408);
        }

        private static void ConfigureActionButton(Button button, string text, int order)
        {
            button.text = text;
            button.interactable = true;
            button.transition = ButtonTransition.ColorTint;
            button.normalColor = UI.Rgba(74, 80, 92, 255);
            button.highlightedColor = UI.Rgba(95, 102, 116, 255);
            button.pressedColor = UI.Rgba(107, 115, 131, 255);
            button.disabledColor = UI.Rgba(185, 197, 214, 255);
            button.borderColor = UI.Rgba(120, 132, 152, 230);
            button.textColor = UI.Rgba(236, 242, 252, 255);
            button.fadeDuration = 0.08f;
            button.cornerRadius = 6.0f;
            button.hitTestOrder = order;
            button.consumeInput = true;
        }

        private static void ConfigureDangerButton(Button button, string text, int order)
        {
            ConfigureActionButton(button, text, order);
            button.normalColor = UI.Rgba(153, 57, 64, 245);
            button.highlightedColor = UI.Rgba(171, 66, 74, 255);
            button.pressedColor = UI.Rgba(131, 46, 52, 255);
            button.borderColor = UI.Rgba(213, 118, 127, 235);
            button.textColor = UI.Rgba(255, 236, 238, 255);
        }

        private static float DrawBuiltInToolbar(Rect toolbarRect, BuiltInUiPalette palette)
        {
            float buttonHeight = 32.0f;
            float gap = 6.0f;
            float minButtonWidth = 86.0f;
            float maxButtonWidth = 122.0f;

            float actionAreaWidth = toolbarRect.width * 0.62f;
            if (actionAreaWidth < 320.0f)
                actionAreaWidth = toolbarRect.width;

            Rect actionsRect = new Rect(toolbarRect.x, toolbarRect.y, actionAreaWidth, toolbarRect.height);
            Rect rightRect = new Rect(actionsRect.Right + gap,
                                      toolbarRect.y,
                                      toolbarRect.width - actionsRect.width - gap,
                                      toolbarRect.height);

            if (rightRect.width < 180.0f)
            {
                rightRect = new Rect(toolbarRect.x,
                                     toolbarRect.y + buttonHeight + gap,
                                     toolbarRect.width,
                                     buttonHeight);
            }

            float cursorX = actionsRect.x;
            float cursorY = actionsRect.y;
            float lineBottom = cursorY + buttonHeight;

            DrawToolbarButton(_refreshButton, ref cursorX, ref cursorY, ref lineBottom, actionsRect, minButtonWidth, maxButtonWidth, buttonHeight, gap, palette, RefreshProjectList);
            DrawToolbarButton(_openLastButton, ref cursorX, ref cursorY, ref lineBottom, actionsRect, minButtonWidth, maxButtonWidth, buttonHeight, gap, palette, OpenLastProjectAndEnterEditor);
            DrawToolbarButton(_browseButton, ref cursorX, ref cursorY, ref lineBottom, actionsRect, minButtonWidth, maxButtonWidth, buttonHeight, gap, palette, BrowseAndSelectProject);
            DrawToolbarButton(_newProjectButton, ref cursorX, ref cursorY, ref lineBottom, actionsRect, minButtonWidth + 18.0f, maxButtonWidth + 24.0f, buttonHeight, gap, palette, OpenNewProjectModal);
            DrawToolbarButton(_sortButton, ref cursorX, ref cursorY, ref lineBottom, actionsRect, minButtonWidth + 8.0f, maxButtonWidth + 12.0f, buttonHeight, gap, palette, CycleSortMode);

            if (ProjectOperations.HasOpenProject())
            {
                DrawToolbarButton(_openEditorButton,
                                  ref cursorX,
                                  ref cursorY,
                                  ref lineBottom,
                                  actionsRect,
                                  minButtonWidth + 24.0f,
                                  maxButtonWidth + 36.0f,
                                  buttonHeight,
                                  gap,
                                  palette,
                                  () => EditorHost.SetShowProjectManagerView(false));
            }

            DrawToolbarButton(_settingsButton,
                              ref cursorX,
                              ref cursorY,
                              ref lineBottom,
                              actionsRect,
                              minButtonWidth,
                              maxButtonWidth,
                              buttonHeight,
                              gap,
                              palette,
                              () => ProjectOperations.SetStatusMessage("Settings panel is not available yet."));

            _sortButton.text = _sortMode == SortMode.NameAsc ? "Sort: Name" : "Sort: Modified";

            float rightHeight = 0.0f;
            if (rightRect.width > 120.0f)
            {
                Rect searchRect = new Rect(rightRect.x, rightRect.y, rightRect.width, buttonHeight);
                _searchInput.rect = searchRect;
                _searchInput.hitTestOrder = 5600;
                _searchInput.consumeInput = true;
                _searchInput.text = _projectSearchText;
                if (_searchInput.Draw())
                {
                    _projectSearchText = _searchInput.text ?? string.Empty;
                    _recentListState.page = 0;
                    _recentListState.scrollOffset = 0.0f;
                    _allListState.page = 0;
                    _allListState.scrollOffset = 0.0f;
                }

                string rootLabel = "Root: " + _projectsRoot;
                Rect rootRect = new Rect(rightRect.x,
                                         rightRect.y + buttonHeight + 2.0f,
                                         rightRect.width,
                                         18.0f);
                UI.DrawLabel(rootRect,
                             rootLabel,
                             BuiltInUiTheme.WithAlpha(palette.textDisabled, 255),
                             11.0f,
                             false,
                             TextAlign.Right);

                rightHeight = buttonHeight + 20.0f;
            }

            float actionsHeight = lineBottom - actionsRect.y;
            float usedHeight = Max(actionsHeight, rightHeight);
            if (rightRect.y > actionsRect.y)
                usedHeight = Max(usedHeight, (rightRect.Bottom + 2.0f) - actionsRect.y);

            return usedHeight;
        }

        private static void DrawToolbarButton(Button button,
                                              ref float cursorX,
                                              ref float cursorY,
                                              ref float lineBottom,
                                              Rect actionsRect,
                                              float minWidth,
                                              float maxWidth,
                                              float height,
                                              float gap,
                                              BuiltInUiPalette palette,
                                              Action onClick)
        {
            float width = button.text.Length * 8.2f + 26.0f;
            width = Clamp(width, minWidth, maxWidth);

            if (cursorX + width > actionsRect.Right)
            {
                cursorX = actionsRect.x;
                cursorY = lineBottom + gap;
                lineBottom = cursorY + height;
            }

            button.rect = new Rect(cursorX, cursorY, width, height);
            BuiltInUiTheme.ApplyActionButtonTheme(button, palette);
            if (button.Draw() && onClick != null)
                onClick();

            cursorX += width + gap;
            if (cursorY + height > lineBottom)
                lineBottom = cursorY + height;
        }

        private static void DrawProjectListGroup(Rect groupRect,
                                                 string title,
                                                 string[] sourcePaths,
                                                 ref ListViewState viewState,
                                                 Button prevButton,
                                                 Button nextButton,
                                                 BuiltInUiPalette palette,
                                                 bool showFavorites)
        {
            if (groupRect.width < 16.0f || groupRect.height < 52.0f)
                return;

            UI.DrawLabel(new Rect(groupRect.x, groupRect.y, groupRect.width, 22.0f),
                         title,
                         BuiltInUiTheme.WithAlpha(palette.text, 255),
                         14.0f,
                         true,
                         TextAlign.Left);

            string[] filtered = FilterAndSortProjects(sourcePaths, showFavorites);
            int count = filtered.Length;

            float pagerWidth = 124.0f;
            Rect pagerRect = new Rect(groupRect.Right - pagerWidth, groupRect.y, pagerWidth, 22.0f);

            Rect listRect = new Rect(groupRect.x,
                                     groupRect.y + 26.0f,
                                     groupRect.width,
                                     groupRect.height - 26.0f);

            int visibleRows = (int)(listRect.height / (RowHeight + RowSpacing));
            if (visibleRows < 1)
                visibleRows = 1;

            int pageSize = Max(visibleRows * 3, 18);
            int pageCount = count <= 0 ? 1 : ((count + pageSize - 1) / pageSize);
            if (viewState.page < 0)
                viewState.page = 0;
            if (viewState.page >= pageCount)
                viewState.page = pageCount - 1;

            prevButton.rect = new Rect(pagerRect.x, pagerRect.y, 28.0f, 22.0f);
            prevButton.interactable = viewState.page > 0;
            BuiltInUiTheme.ApplyActionButtonTheme(prevButton, palette);
            if (prevButton.Draw() && viewState.page > 0)
            {
                viewState.page -= 1;
                viewState.scrollOffset = 0.0f;
            }

            nextButton.rect = new Rect(pagerRect.Right - 28.0f, pagerRect.y, 28.0f, 22.0f);
            nextButton.interactable = viewState.page + 1 < pageCount;
            BuiltInUiTheme.ApplyActionButtonTheme(nextButton, palette);
            if (nextButton.Draw() && viewState.page + 1 < pageCount)
            {
                viewState.page += 1;
                viewState.scrollOffset = 0.0f;
            }

            UI.DrawLabel(new Rect(pagerRect.x + 30.0f, pagerRect.y, pagerRect.width - 60.0f, 22.0f),
                         (viewState.page + 1).ToString(CultureInfo.InvariantCulture) + " / " + pageCount.ToString(CultureInfo.InvariantCulture),
                         BuiltInUiTheme.WithAlpha(palette.textDisabled, 255),
                         11.0f,
                         false,
                         TextAlign.Center);

            if (count <= 0)
            {
                UI.DrawLabel(listRect,
                             "No projects found.",
                             BuiltInUiTheme.WithAlpha(palette.textDisabled, 255),
                             12.0f,
                             false,
                             TextAlign.Center);
                return;
            }

            int pageStart = viewState.page * pageSize;
            int pageEndExclusive = Min(count, pageStart + pageSize);
            int pageItemCount = pageEndExclusive - pageStart;

            float step = RowHeight + RowSpacing;
            float contentHeight = pageItemCount * step;
            float maxScroll = contentHeight - listRect.height;
            if (maxScroll < 0.0f)
                maxScroll = 0.0f;

            if (listRect.Contains(Input.GetMousePosX(), Input.GetMousePosY()))
            {
                float wheel = Input.GetMouseWheel();
                if (Math.Abs(wheel) > 0.0001f)
                {
                    viewState.scrollOffset -= wheel * 34.0f;
                    if (viewState.scrollOffset < 0.0f)
                        viewState.scrollOffset = 0.0f;
                    if (viewState.scrollOffset > maxScroll)
                        viewState.scrollOffset = maxScroll;
                }
            }

            int startInPage = (int)(viewState.scrollOffset / step);
            if (startInPage < 0)
                startInPage = 0;

            int visibleCount = visibleRows + 2;
            int endInPage = Min(pageItemCount, startInPage + visibleCount);

            float offsetWithinRow = viewState.scrollOffset - (startInPage * step);
            string selectedPath = GetSelectedProjectPathOrEmpty();

            for (int i = startInPage; i < endInPage; ++i)
            {
                int sourceIndex = pageStart + i;
                string projectPath = filtered[sourceIndex];

                float rowY = listRect.y + ((i - startInPage) * step) - offsetWithinRow;
                Rect rowRect = new Rect(listRect.x, rowY, listRect.width, RowHeight);
                DrawProjectRow(rowRect, projectPath, selectedPath, palette);
            }
        }

        private static string[] FilterAndSortProjects(string[] sourcePaths, bool showFavorites)
        {
            if (sourcePaths == null || sourcePaths.Length == 0)
                return Array.Empty<string>();

            List<string> filtered = new List<string>(sourcePaths.Length);
            for (int i = 0; i < sourcePaths.Length; ++i)
            {
                string path = sourcePaths[i];
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                    continue;

                string projectName = Path.GetFileName(path);
                if (!MatchesProjectSearch(projectName) && !MatchesProjectSearch(path))
                    continue;

                filtered.Add(path);
            }

            filtered.Sort(CompareProjectPaths);

            if (showFavorites && filtered.Count > 1)
            {
                filtered.Sort((a, b) =>
                {
                    bool aFav = IsFavorite(a);
                    bool bFav = IsFavorite(b);
                    if (aFav == bFav)
                        return CompareProjectPaths(a, b);
                    return aFav ? -1 : 1;
                });
            }

            return filtered.ToArray();
        }

        private static void DrawProjectRow(Rect rowRect,
                                           string projectPath,
                                           string selectedPath,
                                           BuiltInUiPalette palette)
        {
            bool selected = string.Equals(selectedPath, projectPath, StringComparison.OrdinalIgnoreCase);
            bool hovered = rowRect.Contains(Input.GetMousePosX(), Input.GetMousePosY());

            uint rowColor = selected
                ? BuiltInUiTheme.WithAlpha(palette.headerActive, 242)
                : BuiltInUiTheme.WithAlpha(palette.header, hovered ? (byte)236 : (byte)210);

            uint borderColor = selected
                ? BuiltInUiTheme.WithAlpha(palette.separator, 238)
                : BuiltInUiTheme.WithAlpha(palette.border, hovered ? (byte)225 : (byte)205);

            UI.DrawFilledRoundedRect(rowRect, rowColor, 5.0f);
            UI.DrawRoundedRect(rowRect, borderColor, 5.0f, 1.0f);

            Rect favoriteRect = new Rect(rowRect.x + 8.0f, rowRect.y + 12.0f, 16.0f, 16.0f);
            Rect iconRect = new Rect(rowRect.x + 28.0f, rowRect.y + 10.0f, 20.0f, 20.0f);
            Rect nameRect = new Rect(rowRect.x + 54.0f, rowRect.y + 6.0f, rowRect.width - 62.0f, 18.0f);
            Rect pathRect = new Rect(rowRect.x + 54.0f, rowRect.y + 24.0f, rowRect.width - 62.0f, 16.0f);

            bool favorite = IsFavorite(projectPath);
            UI.DrawLabel(favoriteRect,
                         favorite ? "*" : "o",
                         favorite ? UI.Rgba(255, 219, 119, 255) : BuiltInUiTheme.WithAlpha(palette.textDisabled, 220),
                         13.0f,
                         true,
                         TextAlign.Center);

            UI.DrawFilledRoundedRect(iconRect,
                                     BuiltInUiTheme.WithAlpha(BuiltInUiTheme.TintTowardBlack(palette.frameBg, 0.08f), 255),
                                     4.0f);
            UI.DrawLabel(iconRect,
                         "P",
                         BuiltInUiTheme.WithAlpha(palette.text, 255),
                         11.0f,
                         true,
                         TextAlign.Center);

            ProjectMetadataSummary metadata = GetMetadata(projectPath);

            UI.DrawLabel(nameRect,
                         metadata.name,
                         BuiltInUiTheme.WithAlpha(palette.text, 255),
                         12.8f,
                         true,
                         TextAlign.Left);
            UI.DrawLabel(pathRect,
                         TruncateMiddle(projectPath.Replace('\\', '/'), 96),
                         BuiltInUiTheme.WithAlpha(palette.textDisabled, 235),
                         10.5f,
                         false,
                         TextAlign.Left);

            if (_activeModal != ModalType.None)
                return;

            if (favoriteRect.Contains(Input.GetMousePosX(), Input.GetMousePosY()) && Input.GetMouseButtonDown(MouseButton.Left))
            {
                ToggleFavorite(projectPath);
                return;
            }

            if (hovered && Input.GetMouseButtonDown(MouseButton.Left))
            {
                SelectProjectPath(projectPath);

                if (string.Equals(_lastLeftClickProjectPath, projectPath, StringComparison.OrdinalIgnoreCase)
                    && (_uiClock - _lastLeftClickTime) <= DoubleClickThresholdSeconds)
                {
                    OpenProjectAndEnterEditor(projectPath);
                    _lastLeftClickProjectPath = string.Empty;
                }
                else
                {
                    _lastLeftClickProjectPath = projectPath;
                    _lastLeftClickTime = _uiClock;
                }
            }

            if (hovered && Input.GetMouseButtonDown(MouseButton.Right))
            {
                SelectProjectPath(projectPath);
                OpenContextMenu(projectPath, Input.GetMousePosX(), Input.GetMousePosY());
            }
        }

        private static void OpenContextMenu(string projectPath, float mouseX, float mouseY)
        {
            _contextMenuOpen = true;
            _contextMenuProjectPath = projectPath ?? string.Empty;
            _contextMenuRect = new Rect(mouseX, mouseY, 184.0f, 132.0f);
        }

        private static void DrawContextMenu(BuiltInUiPalette palette, Rect canvasRect)
        {
            if (!_contextMenuOpen || _activeModal != ModalType.None)
                return;

            float x = _contextMenuRect.x;
            float y = _contextMenuRect.y;
            float width = _contextMenuRect.width;
            float height = _contextMenuRect.height;

            if (x + width > canvasRect.Right - 8.0f)
                x = canvasRect.Right - width - 8.0f;
            if (y + height > canvasRect.Bottom - 8.0f)
                y = canvasRect.Bottom - height - 8.0f;
            if (x < 8.0f)
                x = 8.0f;
            if (y < 8.0f)
                y = 8.0f;

            Rect menuRect = new Rect(x, y, width, height);
            _contextMenuRect = menuRect;

            if (Input.GetMouseButtonDown(MouseButton.Left) && !menuRect.Contains(Input.GetMousePosX(), Input.GetMousePosY()))
            {
                _contextMenuOpen = false;
                return;
            }

            if (Input.GetMouseButtonDown(MouseButton.Right) && !menuRect.Contains(Input.GetMousePosX(), Input.GetMousePosY()))
            {
                _contextMenuOpen = false;
                return;
            }

            UI.DrawFilledRoundedRect(menuRect, BuiltInUiTheme.WithAlpha(palette.childBg, 244), 6.0f);
            UI.DrawRoundedRect(menuRect, BuiltInUiTheme.WithAlpha(palette.border, 230), 6.0f, 1.0f);

            Rect itemRect = new Rect(menuRect.x + 8.0f, menuRect.y + 8.0f, menuRect.width - 16.0f, 26.0f);

            DrawContextMenuButton(_contextOpenButton, itemRect, palette, () =>
            {
                OpenProjectAndEnterEditor(_contextMenuProjectPath);
                _contextMenuOpen = false;
            });

            itemRect.y += 30.0f;
            DrawContextMenuButton(_contextRenameButton, itemRect, palette, () =>
            {
                OpenRenameModal(_contextMenuProjectPath);
                _contextMenuOpen = false;
            });

            itemRect.y += 30.0f;
            DrawContextMenuButton(_contextDeleteButton, itemRect, palette, () =>
            {
                OpenDeleteModal(_contextMenuProjectPath);
                _contextMenuOpen = false;
            });

            itemRect.y += 30.0f;
            DrawContextMenuButton(_contextShowFolderButton, itemRect, palette, () =>
            {
                ShowProjectInFolder(_contextMenuProjectPath);
                _contextMenuOpen = false;
            });
        }

        private static void DrawContextMenuButton(Button button, Rect rect, BuiltInUiPalette palette, Action onClick)
        {
            button.rect = rect;
            if (!ReferenceEquals(button, _contextDeleteButton))
                BuiltInUiTheme.ApplyActionButtonTheme(button, palette);

            if (button.Draw() && onClick != null)
                onClick();
        }

        private static void DrawBuiltInDetailsPane(Rect paneRect, BuiltInUiPalette palette)
        {
            Rect titleRect = new Rect(paneRect.x + 12.0f, paneRect.y + 10.0f, paneRect.width - 24.0f, 22.0f);
            UI.DrawLabel(titleRect,
                         "Project Details",
                         BuiltInUiTheme.WithAlpha(palette.text, 255),
                         14.0f,
                         true,
                         TextAlign.Left);

            Rect bodyRect = new Rect(paneRect.x + 12.0f, paneRect.y + 38.0f, paneRect.width - 24.0f, paneRect.height - 50.0f);
            UI.DrawFilledRoundedRect(bodyRect, BuiltInUiTheme.WithAlpha(palette.childBg, 188), 6.0f);
            UI.DrawRoundedRect(bodyRect, BuiltInUiTheme.WithAlpha(palette.border, 210), 6.0f, 1.0f);

            if (!TryGetSelectedProjectPath(out string selectedProjectPath))
            {
                UI.DrawLabel(bodyRect,
                             "Select a project from the list.",
                             BuiltInUiTheme.WithAlpha(palette.textDisabled, 255),
                             12.0f,
                             false,
                             TextAlign.Center);
                return;
            }

            ProjectMetadataSummary metadata = GetMetadata(selectedProjectPath);

            float y = bodyRect.y + 10.0f;
            DrawDetailsField(bodyRect, "Name", metadata.name, ref y, palette, true);
            DrawDetailsField(bodyRect, "Path", selectedProjectPath, ref y, palette, false);
            DrawDetailsField(bodyRect, "Created", metadata.hasCreatedAt ? FormatDate(metadata.createdAtLocal) : "Unknown", ref y, palette, false);
            DrawDetailsField(bodyRect, "Modified", FormatDate(metadata.lastModifiedLocal), ref y, palette, false);
            DrawDetailsField(bodyRect, "Engine", string.IsNullOrEmpty(metadata.engineVersion) ? "2026.03" : metadata.engineVersion, ref y, palette, false);
            DrawDetailsField(bodyRect, "Template", string.IsNullOrEmpty(metadata.template) ? "Unknown" : metadata.template, ref y, palette, false);

            Rect actionsRect = new Rect(bodyRect.x + 10.0f, bodyRect.Bottom - 86.0f, bodyRect.width - 20.0f, 30.0f);
            StackPanel actions = new StackPanel(actionsRect, StackOrientation.Horizontal, 8.0f, 0.0f, 0.0f);

            _detailsOpenButton.rect = actions.Next(82.0f, actionsRect.height);
            BuiltInUiTheme.ApplyActionButtonTheme(_detailsOpenButton, palette);
            if (_detailsOpenButton.Draw())
                OpenProjectAndEnterEditor(selectedProjectPath);

            _detailsRenameButton.rect = actions.Next(98.0f, actionsRect.height);
            BuiltInUiTheme.ApplyActionButtonTheme(_detailsRenameButton, palette);
            if (_detailsRenameButton.Draw())
                OpenRenameModal(selectedProjectPath);

            _detailsDeleteButton.rect = actions.Next(82.0f, actionsRect.height);
            if (_detailsDeleteButton.Draw())
                OpenDeleteModal(selectedProjectPath);

            Rect hintRect = new Rect(bodyRect.x + 10.0f, bodyRect.Bottom - 46.0f, bodyRect.width - 20.0f, 36.0f);
            UI.DrawLabel(hintRect,
                         "Tip: double click a project row to open it.",
                         BuiltInUiTheme.WithAlpha(palette.textDisabled, 235),
                         10.5f,
                         false,
                         TextAlign.Left);
        }

        private static void DrawDetailsField(Rect bodyRect,
                                             string label,
                                             string value,
                                             ref float y,
                                             BuiltInUiPalette palette,
                                             bool titleValue)
        {
            Rect labelRect = new Rect(bodyRect.x + 10.0f, y, bodyRect.width - 20.0f, 14.0f);
            UI.DrawLabel(labelRect,
                         label,
                         BuiltInUiTheme.WithAlpha(palette.textDisabled, 255),
                         10.5f,
                         false,
                         TextAlign.Left);

            Rect valueRect = new Rect(bodyRect.x + 10.0f, y + 12.0f, bodyRect.width - 20.0f, titleValue ? 20.0f : 18.0f);
            UI.DrawLabel(valueRect,
                         TruncateMiddle(value.Replace('\\', '/'), titleValue ? 72 : 88),
                         BuiltInUiTheme.WithAlpha(palette.text, 255),
                         titleValue ? 14.0f : 11.5f,
                         titleValue,
                         TextAlign.Left);

            y += titleValue ? 40.0f : 34.0f;
        }

        private static void DrawModalLayer(BuiltInUiPalette palette, Rect canvasRect)
        {
            if (_activeModal == ModalType.None)
                return;

            UI.DrawFilledRoundedRect(canvasRect,
                                     BuiltInUiTheme.WithAlpha(BuiltInUiTheme.TintTowardBlack(palette.windowBg, 0.05f), 188),
                                     0.0f);

            if (_activeModal == ModalType.Rename)
                DrawRenameModal(palette, canvasRect);
            else if (_activeModal == ModalType.Delete)
                DrawDeleteModal(palette, canvasRect);
            else if (_activeModal == ModalType.NewProject)
                DrawNewProjectModal(palette, canvasRect);
        }

        private static void DrawRenameModal(BuiltInUiPalette palette, Rect canvasRect)
        {
            Rect modalRect = UI.CenterRect(canvasRect, 440.0f, 220.0f);
            DrawModalFrame(modalRect, palette, "Rename Project");

            Rect targetRect = new Rect(modalRect.x + 16.0f, modalRect.y + 46.0f, modalRect.width - 32.0f, 18.0f);
            UI.DrawLabel(targetRect,
                         "Target: " + Path.GetFileName(_modalProjectPath),
                         BuiltInUiTheme.WithAlpha(palette.textDisabled, 255),
                         11.5f,
                         false,
                         TextAlign.Left);

            _renameInput.rect = new Rect(modalRect.x + 16.0f, modalRect.y + 72.0f, modalRect.width - 32.0f, 34.0f);
            _renameInput.text = _renameInputValue;
            _renameInput.placeholder = "Project Name";
            _renameInput.hitTestOrder = 6100;
            if (_renameInput.Draw())
                _renameInputValue = _renameInput.text ?? string.Empty;

            Rect footerRect = new Rect(modalRect.x + 16.0f, modalRect.Bottom - 48.0f, modalRect.width - 32.0f, 32.0f);
            StackPanel footer = new StackPanel(footerRect, StackOrientation.Horizontal, 8.0f, 0.0f, 0.0f);

            _renameCancelButton.rect = footer.Next(92.0f, footerRect.height);
            BuiltInUiTheme.ApplyActionButtonTheme(_renameCancelButton, palette);
            if (_renameCancelButton.Draw() || Input.GetKeyDown(KeyCode.Escape))
            {
                CloseModal();
                return;
            }

            _renameConfirmButton.rect = footer.Next(104.0f, footerRect.height);
            BuiltInUiTheme.ApplyActionButtonTheme(_renameConfirmButton, palette);
            if (_renameConfirmButton.Draw() || Input.GetKeyDown(KeyCode.Enter))
            {
                TryConfirmRename();
            }
        }

        private static void TryConfirmRename()
        {
            string nextName = (_renameInputValue ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(_modalProjectPath) || !Directory.Exists(_modalProjectPath))
            {
                ProjectOperations.SetStatusMessage("Rename failed: project path is invalid.");
                CloseModal();
                return;
            }

            if (!StringUtilities.IsValidProjectName(nextName))
            {
                ProjectOperations.SetStatusMessage("Rename failed: invalid project name.");
                return;
            }

            string parent = Path.GetDirectoryName(_modalProjectPath);
            string expectedPath = Path.Combine(parent ?? string.Empty, nextName);

            ProjectOperations.RenameProject(_modalProjectPath, nextName);
            RefreshProjectList();
            TrySelectProjectByPath(expectedPath);
            CloseModal();
        }

        private static void DrawDeleteModal(BuiltInUiPalette palette, Rect canvasRect)
        {
            Rect modalRect = UI.CenterRect(canvasRect, 420.0f, 196.0f);
            DrawModalFrame(modalRect, palette, "Delete Project");

            Rect textRect = new Rect(modalRect.x + 16.0f, modalRect.y + 48.0f, modalRect.width - 32.0f, 52.0f);
            UI.DrawLabel(textRect,
                         "Are you sure you want to delete this project?\nThis action cannot be undone.",
                         BuiltInUiTheme.WithAlpha(palette.text, 255),
                         11.8f,
                         false,
                         TextAlign.Left);

            Rect targetRect = new Rect(modalRect.x + 16.0f, modalRect.y + 102.0f, modalRect.width - 32.0f, 20.0f);
            UI.DrawLabel(targetRect,
                         Path.GetFileName(_modalProjectPath),
                         BuiltInUiTheme.WithAlpha(palette.textDisabled, 255),
                         11.0f,
                         true,
                         TextAlign.Left);

            Rect footerRect = new Rect(modalRect.x + 16.0f, modalRect.Bottom - 48.0f, modalRect.width - 32.0f, 32.0f);
            StackPanel footer = new StackPanel(footerRect, StackOrientation.Horizontal, 8.0f, 0.0f, 0.0f);

            _deleteCancelButton.rect = footer.Next(92.0f, footerRect.height);
            BuiltInUiTheme.ApplyActionButtonTheme(_deleteCancelButton, palette);
            if (_deleteCancelButton.Draw() || Input.GetKeyDown(KeyCode.Escape))
            {
                CloseModal();
                return;
            }

            _deleteConfirmButton.rect = footer.Next(96.0f, footerRect.height);
            if (_deleteConfirmButton.Draw() || Input.GetKeyDown(KeyCode.Enter))
                ConfirmDelete();
        }

        private static void ConfirmDelete()
        {
            if (string.IsNullOrWhiteSpace(_modalProjectPath))
            {
                CloseModal();
                return;
            }

            ProjectOperations.DeleteProject(_modalProjectPath);
            ClearSelection();
            RefreshProjectList();
            CloseModal();
        }

        private static void DrawNewProjectModal(BuiltInUiPalette palette, Rect canvasRect)
        {
            Rect modalRect = UI.CenterRect(canvasRect, 500.0f, 292.0f);
            DrawModalFrame(modalRect, palette, "Create New Project");

            Rect nameLabelRect = new Rect(modalRect.x + 16.0f, modalRect.y + 48.0f, modalRect.width - 32.0f, 16.0f);
            UI.DrawLabel(nameLabelRect,
                         "Project Name",
                         BuiltInUiTheme.WithAlpha(palette.textDisabled, 255),
                         11.0f,
                         false,
                         TextAlign.Left);

            _newNameInput.rect = new Rect(modalRect.x + 16.0f, modalRect.y + 66.0f, modalRect.width - 32.0f, 34.0f);
            _newNameInput.text = _newProjectName;
            _newNameInput.hitTestOrder = 6200;
            if (_newNameInput.Draw())
                _newProjectName = _newNameInput.text ?? string.Empty;

            Rect locationLabelRect = new Rect(modalRect.x + 16.0f, modalRect.y + 106.0f, modalRect.width - 32.0f, 16.0f);
            UI.DrawLabel(locationLabelRect,
                         "Location",
                         BuiltInUiTheme.WithAlpha(palette.textDisabled, 255),
                         11.0f,
                         false,
                         TextAlign.Left);

            _newLocationInput.rect = new Rect(modalRect.x + 16.0f, modalRect.y + 124.0f, modalRect.width - 116.0f, 34.0f);
            _newLocationInput.text = _newProjectLocation;
            _newLocationInput.hitTestOrder = 6201;
            if (_newLocationInput.Draw())
                _newProjectLocation = _newLocationInput.text ?? string.Empty;

            _newBrowseButton.rect = new Rect(modalRect.Right - 92.0f, modalRect.y + 124.0f, 76.0f, 34.0f);
            BuiltInUiTheme.ApplyActionButtonTheme(_newBrowseButton, palette);
            if (_newBrowseButton.Draw())
            {
                string selectedFolder = Explorer.PickFolder("Select project folder", _newProjectLocation);
                if (!string.IsNullOrEmpty(selectedFolder))
                    _newProjectLocation = selectedFolder;
            }

            string[] templates = ProjectCodeGenerator.GetProjectTemplates();
            if (templates.Length == 0)
                templates = new[] { "Minimal Empty" };

            if (_newTemplateIndex >= templates.Length)
                _newTemplateIndex = 0;

            Rect templateLabelRect = new Rect(modalRect.x + 16.0f, modalRect.y + 166.0f, modalRect.width - 32.0f, 16.0f);
            UI.DrawLabel(templateLabelRect,
                         "Template",
                         BuiltInUiTheme.WithAlpha(palette.textDisabled, 255),
                         11.0f,
                         false,
                         TextAlign.Left);

            Rect templateRect = new Rect(modalRect.x + 16.0f, modalRect.y + 184.0f, modalRect.width - 32.0f, 32.0f);
            _templatePrevButton.rect = new Rect(templateRect.x, templateRect.y, 30.0f, templateRect.height);
            BuiltInUiTheme.ApplyActionButtonTheme(_templatePrevButton, palette);
            if (_templatePrevButton.Draw())
            {
                _newTemplateIndex -= 1;
                if (_newTemplateIndex < 0)
                    _newTemplateIndex = templates.Length - 1;
            }

            _templateNextButton.rect = new Rect(templateRect.Right - 30.0f, templateRect.y, 30.0f, templateRect.height);
            BuiltInUiTheme.ApplyActionButtonTheme(_templateNextButton, palette);
            if (_templateNextButton.Draw())
            {
                _newTemplateIndex += 1;
                if (_newTemplateIndex >= templates.Length)
                    _newTemplateIndex = 0;
            }

            Rect templateValueRect = new Rect(templateRect.x + 34.0f, templateRect.y, templateRect.width - 68.0f, templateRect.height);
            UI.DrawFilledRoundedRect(templateValueRect, BuiltInUiTheme.WithAlpha(palette.frameBg, 220), 6.0f);
            UI.DrawRoundedRect(templateValueRect, BuiltInUiTheme.WithAlpha(palette.border, 220), 6.0f, 1.0f);
            UI.DrawLabel(templateValueRect,
                         templates[_newTemplateIndex],
                         BuiltInUiTheme.WithAlpha(palette.text, 255),
                         11.5f,
                         false,
                         TextAlign.Center);

            Rect footerRect = new Rect(modalRect.x + 16.0f, modalRect.Bottom - 48.0f, modalRect.width - 32.0f, 32.0f);
            StackPanel footer = new StackPanel(footerRect, StackOrientation.Horizontal, 8.0f, 0.0f, 0.0f);

            _newCancelButton.rect = footer.Next(92.0f, footerRect.height);
            BuiltInUiTheme.ApplyActionButtonTheme(_newCancelButton, palette);
            if (_newCancelButton.Draw() || Input.GetKeyDown(KeyCode.Escape))
            {
                CloseModal();
                return;
            }

            _newCreateButton.rect = footer.Next(96.0f, footerRect.height);
            BuiltInUiTheme.ApplyActionButtonTheme(_newCreateButton, palette);
            if (_newCreateButton.Draw() || Input.GetKeyDown(KeyCode.Enter))
                TryCreateProjectFromModal(templates[_newTemplateIndex]);
        }

        private static void TryCreateProjectFromModal(string templateName)
        {
            string projectName = (_newProjectName ?? string.Empty).Trim();
            string location = (_newProjectLocation ?? string.Empty).Trim();

            if (!StringUtilities.IsValidProjectName(projectName))
            {
                ProjectOperations.SetStatusMessage("Create failed: invalid project name.");
                return;
            }

            if (string.IsNullOrWhiteSpace(location))
            {
                ProjectOperations.SetStatusMessage("Create failed: invalid location.");
                return;
            }

            try
            {
                Directory.CreateDirectory(location);
                string projectPath = Path.Combine(location, projectName);
                ProjectOperations.CreateProject(projectPath,
                                               templateName,
                                               true,
                                               true);
                RefreshProjectList();
                TrySelectProjectByPath(projectPath);
                CloseModal();
            }
            catch (Exception ex)
            {
                ProjectOperations.SetStatusMessage("Create failed: " + ex.Message);
            }
        }

        private static void DrawModalFrame(Rect modalRect, BuiltInUiPalette palette, string title)
        {
            UI.DrawFilledRoundedRect(modalRect, BuiltInUiTheme.WithAlpha(palette.childBg, 250), 8.0f);
            UI.DrawRoundedRect(modalRect, BuiltInUiTheme.WithAlpha(palette.border, 230), 8.0f, 1.0f);

            Rect titleRect = new Rect(modalRect.x + 14.0f, modalRect.y + 12.0f, modalRect.width - 28.0f, 24.0f);
            UI.DrawLabel(titleRect,
                         title,
                         BuiltInUiTheme.WithAlpha(palette.text, 255),
                         14.0f,
                         true,
                         TextAlign.Left);
        }

        private static void OpenRenameModal(string projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                return;

            TextInput.ClearFocus();
            _contextMenuOpen = false;
            _activeModal = ModalType.Rename;
            _modalProjectPath = projectPath;
            _renameInputValue = Path.GetFileName(projectPath) ?? string.Empty;
            _renameInput.text = _renameInputValue;
        }

        private static void OpenDeleteModal(string projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                return;

            TextInput.ClearFocus();
            _contextMenuOpen = false;
            _activeModal = ModalType.Delete;
            _modalProjectPath = projectPath;
        }

        private static void OpenNewProjectModal()
        {
            TextInput.ClearFocus();
            _contextMenuOpen = false;
            _activeModal = ModalType.NewProject;
            _modalProjectPath = string.Empty;

            if (string.IsNullOrWhiteSpace(_newProjectLocation))
                _newProjectLocation = _projectsRoot;

            if (string.IsNullOrWhiteSpace(_newProjectName))
                _newProjectName = "NewProject";
        }

        private static void CloseModal()
        {
            TextInput.ClearFocus();
            _activeModal = ModalType.None;
            _modalProjectPath = string.Empty;
        }

        private static string GetSelectedProjectPathOrEmpty()
        {
            return TryGetSelectedProjectPath(out string projectPath) ? projectPath : string.Empty;
        }

        private static void SelectProjectPath(string projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                return;

            TrySelectProjectByPath(projectPath);
            _renameProjectName = Path.GetFileName(projectPath);
            ProjectOperations.SetStatusMessage("Selected project: " + _renameProjectName);
        }

        private static void BrowseAndSelectProject()
        {
            string selectedFilePath = Explorer.PickFile("Select project solution (.sln)", _projectsRoot);
            if (string.IsNullOrWhiteSpace(selectedFilePath))
                return;

            string extension = Path.GetExtension(selectedFilePath);
            if (!string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase))
            {
                ProjectOperations.SetStatusMessage("Open failed: please select a .sln file.");
                return;
            }

            string projectDirectory = Path.GetDirectoryName(selectedFilePath);
            if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory))
            {
                ProjectOperations.SetStatusMessage("Open failed: solution folder is invalid.");
                return;
            }

            TrySelectProjectByPath(projectDirectory);
            OpenProjectAndEnterEditor(projectDirectory);
            RefreshProjectList();
            TrySelectProjectByPath(projectDirectory);
        }

        private static void ShowProjectInFolder(string projectPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
                {
                    ProjectOperations.SetStatusMessage("Show in folder failed: invalid project path.");
                    return;
                }

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "\"" + projectPath + "\"",
                    UseShellExecute = true,
                };

                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                ProjectOperations.SetStatusMessage("Show in folder failed: " + ex.Message);
            }
        }

        private static void ToggleFavorite(string projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                return;

            string normalized = NormalizePath(projectPath);
            if (_favoriteProjectPaths.Contains(normalized))
            {
                _favoriteProjectPaths.Remove(normalized);
                ProjectOperations.SetStatusMessage("Removed favorite: " + Path.GetFileName(projectPath));
            }
            else
            {
                _favoriteProjectPaths.Add(normalized);
                ProjectOperations.SetStatusMessage("Favorited: " + Path.GetFileName(projectPath));
            }

            SaveFavorites();
        }

        private static bool IsFavorite(string projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                return false;

            return _favoriteProjectPaths.Contains(NormalizePath(projectPath));
        }

        private static void LoadFavorites()
        {
            _favoriteProjectPaths.Clear();

            if (string.IsNullOrWhiteSpace(_favoritesFile) || !File.Exists(_favoritesFile))
                return;

            try
            {
                string[] lines = File.ReadAllLines(_favoritesFile);
                for (int i = 0; i < lines.Length; ++i)
                {
                    string path = lines[i].Trim();
                    if (string.IsNullOrWhiteSpace(path))
                        continue;

                    _favoriteProjectPaths.Add(NormalizePath(path));
                }
            }
            catch
            {
            }
        }

        private static void SaveFavorites()
        {
            if (string.IsNullOrWhiteSpace(_favoritesFile))
                return;

            try
            {
                string[] values = new string[_favoriteProjectPaths.Count];
                _favoriteProjectPaths.CopyTo(values);
                Array.Sort(values, StringComparer.OrdinalIgnoreCase);
                File.WriteAllLines(_favoritesFile, values);
            }
            catch
            {
            }
        }

        private static ProjectMetadataSummary GetMetadata(string projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                return default;

            if (_metadataByPath.TryGetValue(projectPath, out ProjectMetadataSummary cached))
                return cached;

            ProjectMetadataSummary created = BuildMetadata(projectPath);
            _metadataByPath[projectPath] = created;
            return created;
        }

        private static ProjectMetadataSummary BuildMetadata(string projectPath)
        {
            ProjectMetadataSummary metadata = new ProjectMetadataSummary();
            metadata.path = projectPath ?? string.Empty;
            metadata.name = Path.GetFileName(projectPath ?? string.Empty);
            metadata.template = string.Empty;
            metadata.engineVersion = "2026.03";
            metadata.createdAtRaw = string.Empty;
            metadata.createdAtLocal = DateTime.MinValue;
            metadata.hasCreatedAt = false;

            try
            {
                metadata.lastModifiedLocal = Directory.GetLastWriteTime(projectPath);
            }
            catch
            {
                metadata.lastModifiedLocal = DateTime.MinValue;
            }

            try
            {
                string projectJsonPath = Path.Combine(projectPath, "project.json");
                if (!File.Exists(projectJsonPath))
                    return metadata;

                string content = File.ReadAllText(projectJsonPath);
                metadata.template = ExtractJsonString(content, "template");

                string engineVersion = ExtractJsonString(content, "engineVersion");
                if (!string.IsNullOrWhiteSpace(engineVersion))
                    metadata.engineVersion = engineVersion;

                string createdAtRaw = ExtractJsonString(content, "createdAt");
                metadata.createdAtRaw = createdAtRaw;

                if (!string.IsNullOrWhiteSpace(createdAtRaw)
                    && DateTimeOffset.TryParse(createdAtRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsedCreatedAt))
                {
                    metadata.createdAtLocal = parsedCreatedAt.LocalDateTime;
                    metadata.hasCreatedAt = true;
                }
            }
            catch
            {
            }

            return metadata;
        }

        private static string ExtractJsonString(string jsonContent, string key)
        {
            if (string.IsNullOrEmpty(jsonContent) || string.IsNullOrEmpty(key))
                return string.Empty;

            string token = "\"" + key + "\"";
            int keyIndex = jsonContent.IndexOf(token, StringComparison.Ordinal);
            if (keyIndex < 0)
                return string.Empty;

            int colonIndex = jsonContent.IndexOf(':', keyIndex + token.Length);
            if (colonIndex < 0)
                return string.Empty;

            int firstQuoteIndex = jsonContent.IndexOf('"', colonIndex + 1);
            if (firstQuoteIndex < 0)
                return string.Empty;

            int cursor = firstQuoteIndex + 1;
            bool escaped = false;
            var value = new System.Text.StringBuilder();
            while (cursor < jsonContent.Length)
            {
                char c = jsonContent[cursor++];
                if (escaped)
                {
                    value.Append(c);
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                    break;

                value.Append(c);
            }

            return value.ToString();
        }

        private static void CycleSortMode()
        {
            _sortMode = _sortMode == SortMode.NameAsc ? SortMode.LastModifiedDesc : SortMode.NameAsc;
            _recentListState.page = 0;
            _recentListState.scrollOffset = 0.0f;
            _allListState.page = 0;
            _allListState.scrollOffset = 0.0f;
            RefreshProjectList();
        }

        public static void DrawProjectPanel()
        {
            DrawProjectPanelBuiltInUi(Time.deltaTime);
        }

        private static void TrySelectProjectByPath(string projectPath)
        {
            _selectedProjectPathOverride = projectPath ?? string.Empty;

            for (int i = 0; i < _projectPaths.Length; ++i)
            {
                if (string.Equals(_projectPaths[i], projectPath, StringComparison.OrdinalIgnoreCase))
                {
                    SelectProject(i, Path.GetFileName(projectPath));
                    return;
                }
            }

            _selectedProjectIndex = -1;
        }

        private static void OpenProjectAndEnterEditor(string projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                return;

            if (!Directory.Exists(projectPath))
            {
                ProjectOperations.SetStatusMessage("Open failed: project path not found.");
                return;
            }

            EditorBridge.SetSelectedProjectPath(projectPath);
        }

        private static void OpenLastProjectAndEnterEditor()
        {
            string lastPath = ProjectOperations.GetLastProjectPath();
            if (string.IsNullOrEmpty(lastPath))
                return;

            if (!Directory.Exists(lastPath))
            {
                ProjectOperations.SetStatusMessage("Open failed: last project path not found.");
                return;
            }

            EditorBridge.SetSelectedProjectPath(lastPath);
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        private static int Min(int a, int b)
        {
            return a < b ? a : b;
        }

        private static int Max(int a, int b)
        {
            return a > b ? a : b;
        }

        private static float Max(float a, float b)
        {
            return a > b ? a : b;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            try
            {
                return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path;
            }
        }

        private static string TruncateMiddle(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || maxLength < 5 || value.Length <= maxLength)
                return value;

            int side = (maxLength - 3) / 2;
            return value.Substring(0, side) + "..." + value.Substring(value.Length - side);
        }

        private static string FormatDate(DateTime value)
        {
            if (value == DateTime.MinValue)
                return "Unknown";

            return value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }
    }
}
