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
        private static string _selectedProjectPathOverride = string.Empty;
        private static string _projectSearchText = string.Empty;
        private static string _renameProjectName = string.Empty;

        private static bool _builtInUiInitialized;
        private static int _recentPage;
        private static int _allProjectsPage;

        private const int BuiltInRecentPageSize = 5;
        private const int BuiltInAllProjectsPageSize = 10;

        private static readonly Button _refreshButton = new Button();
        private static readonly Button _openLastButton = new Button();
        private static readonly Button _browseButton = new Button();
        private static readonly Button _newProjectButton = new Button();
        private static readonly Button _openEditorButton = new Button();

        private static readonly Button _recentPrevButton = new Button();
        private static readonly Button _recentNextButton = new Button();
        private static readonly Button _allPrevButton = new Button();
        private static readonly Button _allNextButton = new Button();

        private static readonly Button _detailsOpenButton = new Button();
        private static readonly Button _detailsRenameButton = new Button();
        private static readonly Button _detailsDeleteButton = new Button();

        private static readonly Button[] _recentProjectButtons = CreateButtonArray(BuiltInRecentPageSize);
        private static readonly Button[] _allProjectButtons = CreateButtonArray(BuiltInAllProjectsPageSize);

        private const float ProjectManagerTopInset = 34.0f;

        public static string ProjectsRoot => _projectsRoot;
        public static string[] ProjectPaths => _projectPaths;
        public static int SelectedProjectIndex => _selectedProjectIndex;

        public static void Initialize(string editorConfigDir)
        {
            _projectsRoot = ResolveProjectsRoot();
            Directory.CreateDirectory(_projectsRoot);
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
                Array.Sort(_projectPaths, StringComparer.OrdinalIgnoreCase);

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
            _renameProjectName = string.Empty;
            _selectedProjectPathOverride = string.Empty;
        }

        public static bool MatchesProjectSearch(string projectName)
        {
            if (string.IsNullOrWhiteSpace(_projectSearchText))
                return true;

            return projectName.IndexOf(_projectSearchText.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static void DrawProjectPanelBuiltInUi(float deltaTime)
        {
            EnsureBuiltInUiInitialized();
            BuiltInUiPalette palette = BuiltInUiTheme.Capture();

            Canvas canvas = new Canvas();
            Rect canvasRect = canvas.Bounds;

            UI.DrawFilledRoundedRect(canvasRect, BuiltInUiTheme.WithAlpha(palette.windowBg, 255), 0.0f);
            UI.DrawFilledRoundedRect(new Rect(0.0f,
                                              0.0f,
                                              canvasRect.width,
                                              canvasRect.height * 0.36f),
                                     BuiltInUiTheme.WithAlpha(BuiltInUiTheme.TintTowardWhite(palette.windowBg, 0.12f), 124),
                                     0.0f);

            Rect frameRect = UI.Inset(canvasRect, 18.0f);
            UI.DrawFilledRoundedRect(frameRect, BuiltInUiTheme.WithAlpha(palette.childBg, 222), 10.0f);
            UI.DrawRoundedRect(frameRect, BuiltInUiTheme.WithAlpha(palette.border, 225), 10.0f, 1.0f);

            Rect titleRect = new Rect(frameRect.x + 18.0f, frameRect.y + 14.0f, frameRect.width - 36.0f, 28.0f);
            UI.DrawLabel(titleRect,
                         "Project Manager",
                         BuiltInUiTheme.WithAlpha(palette.text, 255),
                         20.0f,
                         true,
                         TextAlign.Left);

            Rect subtitleRect = new Rect(frameRect.x + 18.0f, frameRect.y + 40.0f, frameRect.width - 36.0f, 18.0f);
            UI.DrawLabel(subtitleRect,
                         "Built-in UI mode (non-ImGui)",
                         BuiltInUiTheme.WithAlpha(palette.textDisabled, 255),
                         12.0f,
                         false,
                         TextAlign.Left);

            Rect toolbarRect = new Rect(frameRect.x + 18.0f, frameRect.y + 68.0f, frameRect.width - 36.0f, 40.0f);
            DrawBuiltInToolbar(toolbarRect, palette);

            Rect contentRect = new Rect(frameRect.x + 18.0f,
                                        frameRect.y + 116.0f,
                                        frameRect.width - 36.0f,
                                        frameRect.height - 170.0f);

            float leftWidth = contentRect.width * 0.60f;
            if (leftWidth < 420.0f)
                leftWidth = 420.0f;
            if (leftWidth > contentRect.width - 300.0f)
                leftWidth = contentRect.width - 300.0f;

            Rect leftRect = new Rect(contentRect.x, contentRect.y, leftWidth, contentRect.height);
            Rect rightRect = new Rect(contentRect.x + leftWidth + 12.0f,
                                      contentRect.y,
                                      contentRect.width - leftWidth - 12.0f,
                                      contentRect.height);

            UI.DrawFilledRoundedRect(leftRect, BuiltInUiTheme.WithAlpha(palette.frameBg, 196), 7.0f);
            UI.DrawRoundedRect(leftRect, BuiltInUiTheme.WithAlpha(palette.border, 220), 7.0f, 1.0f);
            UI.DrawFilledRoundedRect(rightRect, BuiltInUiTheme.WithAlpha(palette.frameBg, 196), 7.0f);
            UI.DrawRoundedRect(rightRect, BuiltInUiTheme.WithAlpha(palette.border, 220), 7.0f, 1.0f);

            float innerX = leftRect.x + 12.0f;
            float innerY = leftRect.y + 12.0f;
            float innerWidth = leftRect.width - 24.0f;
            float innerHeight = leftRect.height - 24.0f;

            const float groupGap = 12.0f;
            const float minGroupHeight = 120.0f;

            float recentHeight = innerHeight * 0.38f;
            float maxRecentHeight = innerHeight - minGroupHeight - groupGap;
            if (maxRecentHeight < 48.0f)
                maxRecentHeight = 48.0f;

            if (recentHeight < minGroupHeight)
                recentHeight = minGroupHeight;
            if (recentHeight > maxRecentHeight)
                recentHeight = maxRecentHeight;

            float allHeight = innerHeight - recentHeight - groupGap;
            if (allHeight < 48.0f)
            {
                allHeight = 48.0f;
                recentHeight = innerHeight - allHeight - groupGap;
                if (recentHeight < 48.0f)
                    recentHeight = 48.0f;
            }

            Rect recentRect = new Rect(innerX,
                                       innerY,
                                       innerWidth,
                                       recentHeight);
            Rect allRect = new Rect(innerX,
                                    recentRect.Bottom + groupGap,
                                    innerWidth,
                                    allHeight);

            DrawProjectListGroup(recentRect,
                                 "Recent Projects",
                                 ProjectOperations.GetRecentProjects(),
                                 ref _recentPage,
                                 BuiltInRecentPageSize,
                                 _recentProjectButtons,
                                 _recentPrevButton,
                                 _recentNextButton,
                                 palette);

            DrawProjectListGroup(allRect,
                                 "All Projects",
                                 _projectPaths,
                                 ref _allProjectsPage,
                                 BuiltInAllProjectsPageSize,
                                 _allProjectButtons,
                                 _allPrevButton,
                                 _allNextButton,
                                 palette);

            DrawBuiltInDetailsPane(rightRect, deltaTime, palette);

            Rect statusRect = new Rect(frameRect.x + 18.0f,
                                       frameRect.Bottom - 38.0f,
                                       frameRect.width - 36.0f,
                                       22.0f);
            UI.DrawLabel(statusRect,
                         "Status: " + EditorContext.StatusMessage,
                         BuiltInUiTheme.WithAlpha(palette.textDisabled, 255),
                         12.0f,
                         false,
                         TextAlign.Left);
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
            ConfigureActionButton(_newProjectButton, "New", 5003);
            ConfigureActionButton(_openEditorButton, "Open Editor", 5004);

            ConfigureActionButton(_recentPrevButton, "<", 5010);
            ConfigureActionButton(_recentNextButton, ">", 5011);
            ConfigureActionButton(_allPrevButton, "<", 5012);
            ConfigureActionButton(_allNextButton, ">", 5013);

            ConfigureActionButton(_detailsOpenButton, "Open", 5020);
            ConfigureActionButton(_detailsRenameButton, "Quick Rename", 5021);
            ConfigureActionButton(_detailsDeleteButton, "Delete", 5022);

            for (int i = 0; i < _recentProjectButtons.Length; ++i)
                ConfigureListButton(_recentProjectButtons[i], 5100 + i);

            for (int i = 0; i < _allProjectButtons.Length; ++i)
                ConfigureListButton(_allProjectButtons[i], 5200 + i);
        }

        private static void ConfigureActionButton(Button button, string text, int order)
        {
            button.text = text;
            button.interactable = true;
            button.transition = ButtonTransition.ColorTint;
            button.normalColor = UI.Rgba(241, 246, 255, 255);
            button.highlightedColor = UI.Rgba(255, 255, 255, 255);
            button.pressedColor = UI.Rgba(224, 232, 245, 255);
            button.disabledColor = UI.Rgba(185, 197, 214, 255);
            button.borderColor = UI.Rgba(205, 215, 230, 255);
            button.textColor = UI.Rgba(18, 24, 33, 255);
            button.fadeDuration = 0.08f;
            button.cornerRadius = 6.0f;
            button.hitTestOrder = order;
            button.consumeInput = true;
        }

        private static void ConfigureListButton(Button button, int order)
        {
            ConfigureActionButton(button, "", order);
            button.textColor = UI.Rgba(234, 241, 252, 255);
            button.normalColor = UI.Rgba(29, 39, 54, 220);
            button.highlightedColor = UI.Rgba(47, 61, 82, 235);
            button.pressedColor = UI.Rgba(23, 31, 43, 235);
            button.borderColor = UI.Rgba(70, 89, 116, 220);
            button.cornerRadius = 4.0f;
            button.fadeDuration = 0.10f;
        }

        private static void DrawBuiltInToolbar(Rect toolbarRect, BuiltInUiPalette palette)
        {
            StackPanel toolbar = new StackPanel(toolbarRect, StackOrientation.Horizontal, 8.0f, 0.0f, 0.0f);

            Rect refreshRect = toolbar.Next(96.0f, toolbarRect.height);
            _refreshButton.rect = refreshRect;
            BuiltInUiTheme.ApplyActionButtonTheme(_refreshButton, palette);
            if (_refreshButton.Draw())
                RefreshProjectList();

            Rect openLastRect = toolbar.Next(112.0f, toolbarRect.height);
            _openLastButton.rect = openLastRect;
            BuiltInUiTheme.ApplyActionButtonTheme(_openLastButton, palette);
            if (_openLastButton.Draw())
                OpenLastProjectAndEnterEditor();

            Rect browseRect = toolbar.Next(100.0f, toolbarRect.height);
            _browseButton.rect = browseRect;
            BuiltInUiTheme.ApplyActionButtonTheme(_browseButton, palette);
            if (_browseButton.Draw())
                BrowseAndSelectProject();

            Rect newRect = toolbar.Next(80.0f, toolbarRect.height);
            _newProjectButton.rect = newRect;
            BuiltInUiTheme.ApplyActionButtonTheme(_newProjectButton, palette);
            if (_newProjectButton.Draw())
                CreateDefaultProject();

            if (ProjectOperations.HasOpenProject())
            {
                Rect openEditorRect = toolbar.Next(128.0f, toolbarRect.height);
                _openEditorButton.rect = openEditorRect;
                BuiltInUiTheme.ApplyActionButtonTheme(_openEditorButton, palette);
                if (_openEditorButton.Draw())
                    EditorHost.SetShowProjectManagerView(false);
            }

            Rect rootRect = toolbar.NextFill();
            UI.DrawLabel(rootRect,
                         "Root: " + _projectsRoot,
                         BuiltInUiTheme.WithAlpha(palette.textDisabled, 255),
                         12.0f,
                         false,
                         TextAlign.Right);
        }

        private static void DrawProjectListGroup(Rect groupRect,
                                                 string title,
                                                 string[] projectPaths,
                                                 ref int page,
                                                 int pageSize,
                                                 Button[] slotButtons,
                                                 Button prevButton,
                                                 Button nextButton,
                                                 BuiltInUiPalette palette)
        {
            if (groupRect.width < 8.0f || groupRect.height < 28.0f)
                return;

            UI.DrawLabel(new Rect(groupRect.x, groupRect.y, groupRect.width, 24.0f),
                         title,
                         BuiltInUiTheme.WithAlpha(palette.text, 255),
                         14.0f,
                         true,
                         TextAlign.Left);

            int count = projectPaths == null ? 0 : projectPaths.Length;
            const float rowHeight = 32.0f;
            const float rowSpacing = 7.0f;

            float listHeight = groupRect.height - 30.0f;
            if (listHeight < 0.0f)
                listHeight = 0.0f;

            int maxVisibleRows = (int)((listHeight + rowSpacing) / (rowHeight + rowSpacing));
            if (maxVisibleRows < 1)
                maxVisibleRows = 1;

            int effectivePageSize = pageSize;
            if (effectivePageSize > slotButtons.Length)
                effectivePageSize = slotButtons.Length;
            if (effectivePageSize > maxVisibleRows)
                effectivePageSize = maxVisibleRows;
            if (effectivePageSize < 1)
                effectivePageSize = 1;

            int pageCount = count <= 0 ? 1 : ((count + effectivePageSize - 1) / effectivePageSize);
            if (page < 0)
                page = 0;
            if (page >= pageCount)
                page = pageCount - 1;

            Rect pagerRect = new Rect(groupRect.Right - 124.0f, groupRect.y, 124.0f, 24.0f);
            prevButton.rect = new Rect(pagerRect.x, pagerRect.y, 28.0f, 24.0f);
            prevButton.interactable = page > 0;
            BuiltInUiTheme.ApplyActionButtonTheme(prevButton, palette);
            if (prevButton.Draw() && page > 0)
                page -= 1;

            nextButton.rect = new Rect(pagerRect.Right - 28.0f, pagerRect.y, 28.0f, 24.0f);
            nextButton.interactable = page + 1 < pageCount;
            BuiltInUiTheme.ApplyActionButtonTheme(nextButton, palette);
            if (nextButton.Draw() && page + 1 < pageCount)
                page += 1;

            UI.DrawLabel(new Rect(pagerRect.x + 30.0f, pagerRect.y, pagerRect.width - 60.0f, 24.0f),
                         (page + 1) + "/" + pageCount,
                         BuiltInUiTheme.WithAlpha(palette.textDisabled, 255),
                         11.0f,
                         false,
                         TextAlign.Center);

            Rect listRect = new Rect(groupRect.x,
                                     groupRect.y + 30.0f,
                                     groupRect.width,
                                     listHeight);
            StackPanel listPanel = new StackPanel(listRect, StackOrientation.Vertical, 7.0f, 0.0f, 0.0f);

            string selectedPath = GetSelectedProjectPathOrEmpty();
            int startIndex = page * effectivePageSize;

            for (int slot = 0; slot < effectivePageSize; ++slot)
            {
                Rect rowRect = listPanel.Next(rowHeight, listRect.width);
                int index = startIndex + slot;

                if (index >= count)
                {
                    UI.DrawFilledRoundedRect(rowRect, BuiltInUiTheme.WithAlpha(palette.frameBg, 96), 4.0f);
                    continue;
                }

                string path = projectPaths[index];
                string projectName = Path.GetFileName(path);
                Button rowButton = slotButtons[slot];
                rowButton.rect = rowRect;
                rowButton.text = projectName;

                bool isSelected = string.Equals(selectedPath, path, StringComparison.OrdinalIgnoreCase);
                BuiltInUiTheme.ApplyListButtonTheme(rowButton, palette, isSelected);

                if (rowButton.Draw())
                    SelectProjectPath(path);
            }

            if (count == 0)
            {
                UI.DrawLabel(listRect,
                             "No projects found.",
                             BuiltInUiTheme.WithAlpha(palette.textDisabled, 255),
                             12.0f,
                             false,
                             TextAlign.Center);
            }
        }

        private static void DrawBuiltInDetailsPane(Rect paneRect, float deltaTime, BuiltInUiPalette palette)
        {
            _ = deltaTime;

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
                             "Select a project from the lists.",
                             BuiltInUiTheme.WithAlpha(palette.textDisabled, 255),
                             12.0f,
                             false,
                             TextAlign.Center);
                return;
            }

            string projectName = Path.GetFileName(selectedProjectPath);
            string location = Path.GetDirectoryName(selectedProjectPath) ?? string.Empty;

            Rect projectNameRect = new Rect(bodyRect.x + 10.0f, bodyRect.y + 10.0f, bodyRect.width - 20.0f, 24.0f);
            UI.DrawLabel(projectNameRect,
                         projectName,
                         BuiltInUiTheme.WithAlpha(palette.text, 255),
                         15.0f,
                         true,
                         TextAlign.Left);

            Rect locationRect = new Rect(bodyRect.x + 10.0f, bodyRect.y + 38.0f, bodyRect.width - 20.0f, 40.0f);
            UI.DrawLabel(locationRect,
                         location,
                         BuiltInUiTheme.WithAlpha(palette.textDisabled, 255),
                         11.0f,
                         false,
                         TextAlign.Left);

            Rect actionsRect = new Rect(bodyRect.x + 10.0f, bodyRect.Bottom - 86.0f, bodyRect.width - 20.0f, 30.0f);
            StackPanel actions = new StackPanel(actionsRect, StackOrientation.Horizontal, 8.0f, 0.0f, 0.0f);

            _detailsOpenButton.rect = actions.Next(88.0f, actionsRect.height);
            BuiltInUiTheme.ApplyActionButtonTheme(_detailsOpenButton, palette);
            if (_detailsOpenButton.Draw())
                OpenProjectAndEnterEditor(selectedProjectPath);

            _detailsRenameButton.rect = actions.Next(128.0f, actionsRect.height);
            BuiltInUiTheme.ApplyActionButtonTheme(_detailsRenameButton, palette);
            if (_detailsRenameButton.Draw())
                QuickRenameSelectedProject(selectedProjectPath);

            _detailsDeleteButton.rect = actions.Next(88.0f, actionsRect.height);
            BuiltInUiTheme.ApplyActionButtonTheme(_detailsDeleteButton, palette);
            _detailsDeleteButton.normalColor = BuiltInUiTheme.WithAlpha(BuiltInUiTheme.TintTowardBlack(palette.buttonActive, 0.10f), 245);
            _detailsDeleteButton.highlightedColor = BuiltInUiTheme.WithAlpha(BuiltInUiTheme.TintTowardWhite(palette.buttonActive, 0.06f), 255);
            _detailsDeleteButton.borderColor = BuiltInUiTheme.WithAlpha(BuiltInUiTheme.TintTowardBlack(palette.separator, 0.10f), 235);
            if (_detailsDeleteButton.Draw())
                DeleteSelectedProject(selectedProjectPath);

            Rect hintRect = new Rect(bodyRect.x + 10.0f, bodyRect.Bottom - 46.0f, bodyRect.width - 20.0f, 36.0f);
            UI.DrawLabel(hintRect,
                         "Tip: Use the toolbar actions to manage and open projects quickly.",
                         BuiltInUiTheme.WithAlpha(palette.textDisabled, 235),
                         10.5f,
                         false,
                         TextAlign.Left);
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

        private static void CreateDefaultProject()
        {
            string name = "NewProject_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string projectPath = Path.Combine(_projectsRoot, name);
            ProjectOperations.CreateProject(projectPath,
                                           "Minimal Empty",
                                           true,
                                           true);

            RefreshProjectList();
            TrySelectProjectByPath(projectPath);
        }

        private static void QuickRenameSelectedProject(string selectedProjectPath)
        {
            string currentName = Path.GetFileName(selectedProjectPath);
            if (string.IsNullOrEmpty(currentName))
                return;

            string candidateName = currentName + "_Renamed";
            if (!StringUtilities.IsValidProjectName(candidateName))
                candidateName = "RenamedProject";

            ProjectOperations.RenameProject(selectedProjectPath, candidateName);
            RefreshProjectList();
        }

        private static void DeleteSelectedProject(string selectedProjectPath)
        {
            ProjectOperations.DeleteProject(selectedProjectPath);
            ClearSelection();
            RefreshProjectList();
        }

        private static Button[] CreateButtonArray(int count)
        {
            Button[] buttons = new Button[count];
            for (int i = 0; i < count; ++i)
                buttons[i] = new Button();

            return buttons;
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
        }

        private static void OpenProjectAndEnterEditor(string projectPath)
        {
            EditorBridge.SetSelectedProjectPath(projectPath);
        }

        private static void OpenLastProjectAndEnterEditor()
        {
            string lastPath = ProjectOperations.GetLastProjectPath();
            if (!string.IsNullOrEmpty(lastPath))
                EditorBridge.SetSelectedProjectPath(lastPath);
        }
    }
}



