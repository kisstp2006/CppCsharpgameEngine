using System;
using System.IO;

using Engine;

namespace EngineEditor
{
    public static class EditorHost
    {
        private enum BootStage
        {
            ProjectSelector,
            Loading,
            MainEditor,
        }

        private static string _statusMessage = "No project loaded.";
        private static bool _showProjectManagerView = true;
        private static string _editorConfigDir = string.Empty;
        private static string _playSnapshotPath = string.Empty;
        private static float _compileProgressPulse = 0.0f;
        private static bool _compileDialogOpen;
        private static bool _menuItemsRegistered;
        private static bool _backgroundSplashInitialized;
        private static bool _backgroundSplashReady;

        private static BootStage _bootStage = BootStage.ProjectSelector;
        private static BootStage _lastWindowStyleStage = (BootStage)(-1);
        private static float _loadingTimer = 0.0f;
        private static string _loadingProjectName = string.Empty;

        private const float LoadingMinDuration = 1.0f;
        private const string CompilePopupId = "##ScriptCompileBlockingModal";
        private const string SplashLogoRelativePath = "assets/editor/splash_logo.png";
        private const int ProjectSelectorWindowWidth = 960;
        private const int ProjectSelectorWindowHeight = 640;

        public static string StatusMessage => _statusMessage;

        public static void OnEditorStart()
        {
            string cwd = Directory.GetCurrentDirectory();
            _editorConfigDir = Path.Combine(cwd, ".editor");
            Directory.CreateDirectory(_editorConfigDir);
            _playSnapshotPath = Path.Combine(_editorConfigDir, "playmode_snapshot.scene.bin");

            ProjectManager.Initialize(_editorConfigDir);
            ProjectOperations.Initialize(_editorConfigDir);
            ScriptComponentValidation.Initialize(_editorConfigDir);

            ImGuiThemeSettings.Initialize(_editorConfigDir);
            ImGuiThemeSettings.RegisterEditorOptions();

            AssetPanel.RegisterEditorOptions();
            AssetPanel.RegisterAssetContextMenu();
            SceneEditor.RegisterEditorOptions();
            SceneEditor.RegisterAssetContextMenu();
            ScriptComponentValidation.RegisterEditorOptions();
            ScriptFieldInspector.RegisterEditorOptions();
            ScriptFieldInspector.RegisterAssetContextMenu();

            _backgroundSplashInitialized = false;
            _backgroundSplashReady = false;
            EditorBridge.SetWindowBackgroundVisible(false);

            RegisterMenuItems();

            _lastWindowStyleStage = (BootStage)(-1);

            // Detect hot reload only when native runtime reports an active script
            // reload, a native project context is open, and the last-project file
            // points to a valid directory. In that case restore managed project
            // state and jump directly to MainEditor without showing loading splash.
            string restoredProjectPath = TryReadLastProjectPath();
            if (EditorBridge.IsScriptReloadInProgress()
                && !string.IsNullOrEmpty(restoredProjectPath))
            {
                ProjectOperations.RestoreFromHotReload(restoredProjectPath);
                _bootStage = BootStage.MainEditor;
                _showProjectManagerView = false;

                string projectName = Path.GetFileName(restoredProjectPath);
                if (string.IsNullOrEmpty(projectName))
                    projectName = "Untitled";

                _statusMessage = "Project loaded: " + projectName;
                EditorBridge.SetDockspaceEnabled(true);
                EditorBridge.SetMainWindowBorderless(false);
                EditorBridge.SetMainWindowResizable(true);
                EditorBridge.SetWindowBackgroundVisible(false);
                EditorBridge.SetMainWindowTitle("CppCSharp Editor \u2014 " + projectName);
                EditorBridge.MaximizeMainWindow();
                Console.WriteLine("[Editor] OnEditorStart (hot reload) -> MainEditor: " + restoredProjectPath);
            }
            else
            {
                _bootStage = BootStage.ProjectSelector;
                EditorBridge.SetDockspaceEnabled(false);
                EditorBridge.SetWindowBackgroundVisible(false);

                // Enforce a predictable starting window size for the Project Selector.
                EditorBridge.SetMainWindowBorderless(false);
                EditorBridge.SetMainWindowResizable(true);
                EditorBridge.SetMainWindowSize(ProjectSelectorWindowWidth, ProjectSelectorWindowHeight);
                EditorBridge.CenterMainWindow();

                _showProjectManagerView = true;
                _statusMessage = "No project loaded.";
                Console.WriteLine("[Editor] OnEditorStart called. Boot stage: " + _bootStage);
            }
        }

        private static string TryReadLastProjectPath()
        {
            if (string.IsNullOrEmpty(_editorConfigDir))
                return string.Empty;

            string lastProjectFile = Path.Combine(_editorConfigDir, "last_project.txt");
            if (!File.Exists(lastProjectFile))
                return string.Empty;

            try
            {
                string path = File.ReadAllText(lastProjectFile).Trim();
                return (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                    ? string.Empty
                    : path;
            }
            catch
            {
                return string.Empty;
            }
        }

        public static void OnEditorUpdate(float deltaTime)
        {
            EnsureBackgroundSplashInitialized();
            ApplyWindowStyleForBootStage();

            switch (_bootStage)
            {
                case BootStage.ProjectSelector:
                    UpdateProjectSelectorStage(deltaTime);
                    break;
                case BootStage.Loading:
                    UpdateLoadingStage(deltaTime);
                    break;
                case BootStage.MainEditor:
                    UpdateMainEditorStage(deltaTime);
                    break;
            }
        }

        // ----- Stage 1: Project Selector (small window, no dockspace) -----
        private static void UpdateProjectSelectorStage(float deltaTime)
        {
            if (ProjectOperations.HasOpenProject())
            {
                TransitionToLoading();
                return;
            }

            ProjectManager.DrawProjectPanel();
            EditorOptionsWindow.Draw();

            if (ProjectOperations.HasOpenProject())
                TransitionToLoading();
        }

        // ----- Stage 2: Loading / Splash screen -----
        private static void UpdateLoadingStage(float deltaTime)
        {
            _loadingTimer += deltaTime;

            DrawLoadingSplash(deltaTime);

            ScriptValidationSnapshot snapshot = ScriptComponentValidation.GetSnapshot();
            bool compileFinished = !snapshot.IsCompiling;

            if (_loadingTimer >= LoadingMinDuration && compileFinished)
                TransitionToMainEditor();
        }

        private static void DrawLoadingSplash(float deltaTime)
        {
            if (ImGui.BeginCenteredFixed("##LoadingSplash", 560.0f, 270.0f, true))
            {
                ImGui.Text("");
                ImGui.Text("          CppCSharp Engine");
                ImGui.Text("");
                ImGui.Separator();
                ImGui.Text("");

                string projectLabel = string.IsNullOrEmpty(_loadingProjectName)
                    ? "Loading project..."
                    : "Loading: " + _loadingProjectName;
                ImGui.Text("  " + projectLabel);

                ImGui.Text("");

                _compileProgressPulse += deltaTime * 0.65f;
                while (_compileProgressPulse > 1.0f)
                    _compileProgressPulse -= 1.0f;

                float progress = 0.1f + (_compileProgressPulse * 0.8f);
                ImGui.Text("  " + BuildProgressBarText(progress, 36));

                ImGui.Text("");

                ScriptValidationSnapshot snapshot = ScriptComponentValidation.GetSnapshot();
                string status = snapshot.IsCompiling ? "  Compiling C# scripts..." : "  Initializing editor...";
                ImGui.Text(status);
            }
            ImGui.End();
        }

        private static void EnsureBackgroundSplashInitialized()
        {
            if (_backgroundSplashInitialized)
                return;

            _backgroundSplashInitialized = true;

            string splashLogoPath = ResolveSplashLogoPath();
            _backgroundSplashReady = EditorBridge.SetWindowBackgroundImage(splashLogoPath);

            if (_bootStage == BootStage.Loading && _backgroundSplashReady)
                EditorBridge.SetWindowBackgroundVisible(true);
        }

        private static void ApplyWindowStyleForBootStage()
        {
            // Only apply SDL2 window style changes on actual stage transitions to avoid
            // generating redundant Win32 messages every frame.
            if (_lastWindowStyleStage == _bootStage)
                return;
            _lastWindowStyleStage = _bootStage;

            if (_bootStage == BootStage.Loading)
            {
                EditorBridge.SetMainWindowResizable(false);
                EditorBridge.SetMainWindowBorderless(true);
                return;
            }

            EditorBridge.SetMainWindowBorderless(false);
            EditorBridge.SetMainWindowResizable(true);
        }

        private static string ResolveSplashLogoPath()
        {
            // Try the current working directory first (normal editor start path).
            string cwdCandidate = Path.Combine(Directory.GetCurrentDirectory(), SplashLogoRelativePath);
            if (File.Exists(cwdCandidate))
                return cwdCandidate;

            // Fallback: walk upward from managed assembly base directory to find workspace root.
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            try
            {
                DirectoryInfo current = new DirectoryInfo(baseDirectory);
                for (int i = 0; i < 10 && current != null; ++i)
                {
                    string candidate = Path.Combine(current.FullName, SplashLogoRelativePath);
                    if (File.Exists(candidate))
                        return candidate;

                    current = current.Parent;
                }
            }
            catch
            {
                // Keep startup robust even if directory probing fails.
            }

            // Return the canonical relative fallback; engine-side loader will fail silently.
            return SplashLogoRelativePath;
        }

        // ----- Stage transitions -----
        private static void TransitionToLoading()
        {
            _loadingProjectName = Path.GetFileName(ProjectOperations.ActiveProjectPath);
            _loadingTimer = 0.0f;
            _compileProgressPulse = 0.0f;
            _bootStage = BootStage.Loading;

            // Normalise the OS window to a fixed windowed size before applying the
            // borderless loading style.  This guarantees that – regardless of any
            // resize or maximise the user performed in the Project Selector –
            // SDL2's internal "restore" rect is a known value, so that the
            // SDL_RestoreWindow call inside Maximize() produces a clean transition
            // back to the maximised main-editor window.
            EditorBridge.SetMainWindowBorderless(false);
            EditorBridge.SetMainWindowResizable(true);
            EditorBridge.SetMainWindowSize(ProjectSelectorWindowWidth, ProjectSelectorWindowHeight);
            EditorBridge.CenterMainWindow();

            EditorBridge.SetMainWindowResizable(false);
            EditorBridge.SetMainWindowBorderless(true);
            EditorBridge.SetWindowBackgroundVisible(_backgroundSplashReady);
            Console.WriteLine("[Editor] Boot transition -> Loading (" + _loadingProjectName + ")");
        }

        private static void TransitionToMainEditor()
        {
            _bootStage = BootStage.MainEditor;
            _showProjectManagerView = false;

            string projectName = Path.GetFileName(ProjectOperations.ActiveProjectPath);
            if (string.IsNullOrEmpty(projectName))
                projectName = "Untitled";

            EditorBridge.SetDockspaceEnabled(true);
            EditorBridge.SetMainWindowBorderless(false);
            EditorBridge.SetMainWindowResizable(true);
            EditorBridge.SetWindowBackgroundVisible(false);
            EditorBridge.SetMainWindowTitle("CppCSharp Editor \u2014 " + projectName);
            EditorBridge.MaximizeMainWindow();

            _statusMessage = "Project loaded: " + projectName;
            Console.WriteLine("[Editor] Boot transition -> MainEditor (" + projectName + ")");
        }

        // ----- Stage 3: Main Editor (full window, dockspace, all panels) -----
        private static void UpdateMainEditorStage(float deltaTime)
        {
            bool hasOpenProject = ProjectOperations.HasOpenProject();
            bool compileBlocking = DrawScriptBuildProgressWindow(deltaTime);

            if (compileBlocking)
                return;

            if (hasOpenProject)
                DrawMenuBar();

            if (!hasOpenProject)
            {
                EditorBridge.SetDockspaceEnabled(false);
                EditorBridge.SetWindowBackgroundVisible(false);
                _showProjectManagerView = true;
                SceneEditor.ResetEditorState();
                ProjectManager.DrawProjectPanel();
                EditorOptionsWindow.Draw();
                return;
            }

            if (_showProjectManagerView)
            {
                SceneEditor.ResetEditorState();
                ProjectManager.DrawProjectPanel();
                EditorOptionsWindow.Draw();
                return;
            }

            SceneEditor.DrawSceneTreePanel();
            SceneEditor.DrawWorldViewportPanel(deltaTime);
            SceneEditor.DrawRuntimeGamePanel();
            SceneEditor.DrawInspectorPanel();
            AssetPanel.DrawAssetPanel();
            SceneEditor.UpdateTick(deltaTime);
            EditorOptionsWindow.Draw();
            EditorConsoleWindow.Draw();
        }

        private static bool DrawScriptBuildProgressWindow(float deltaTime)
        {
            ScriptValidationSnapshot snapshot = ScriptComponentValidation.GetSnapshot();
            bool busy = snapshot.IsCompiling;
            if (!busy)
            {
                _compileProgressPulse = 0.0f;

                if (_compileDialogOpen && ImGui.BeginPopupModal(CompilePopupId))
                {
                    ImGui.CloseCurrentPopup();
                    ImGui.EndPopup();
                }

                _compileDialogOpen = false;
                return false;
            }

            _compileProgressPulse += deltaTime * 0.65f;
            while (_compileProgressPulse > 1.0f)
                _compileProgressPulse -= 1.0f;

            float progress = 0.15f + (_compileProgressPulse * 0.75f);
            string message = "Compiling C# scripts...";

            ImGui.OpenPopup(CompilePopupId);
            if (ImGui.BeginPopupModal(CompilePopupId))
            {
                _compileDialogOpen = true;
                ImGui.Text(message);
                ImGui.Text(BuildProgressBarText(progress, 22));
                ImGui.Text(snapshot.CompileSummary);
                ImGui.EndPopup();
            }

            return true;
        }

        private static string BuildProgressBarText(float fraction, int segmentCount)
        {
            if (segmentCount < 4)
                segmentCount = 4;

            float clamped = Clamp01(fraction);
            int filled = (int)Math.Floor(clamped * segmentCount + 0.5f);
            if (filled < 0)
                filled = 0;
            if (filled > segmentCount)
                filled = segmentCount;

            string bar = new string('#', filled) + new string('-', segmentCount - filled);
            int percent = (int)Math.Floor(clamped * 100.0f + 0.5f);
            return "[" + bar + "] " + percent + "%";
        }

        private static float Clamp01(float value)
        {
            if (value < 0.0f)
                return 0.0f;
            if (value > 1.0f)
                return 1.0f;
            return value;
        }

        private static void RegisterMenuItems()
        {
            if (_menuItemsRegistered)
                return;

            _menuItemsRegistered = true;
            MenuRegistry.Clear();

            MenuRegistry.Register("menu.file.newScene",
                                  "File/New Scene",
                                  () => SceneEditor.NewScene(),
                                  () => ProjectOperations.HasOpenProject(),
                                  10);

            MenuRegistry.Register("menu.file.openScene",
                                  "File/Open Scene",
                                  () => SceneEditor.LoadSceneFromPicker(),
                                  () => ProjectOperations.HasOpenProject(),
                                  20);

            MenuRegistry.Register("menu.file.saveScene",
                                  "File/Save Scene",
                                  () => SceneEditor.SaveScene(),
                                  () => ProjectOperations.HasOpenProject(),
                                  30);

            MenuRegistry.Register("menu.file.saveSceneAsJson",
                                  "File/Save Scene As/JSON",
                                  () => SceneEditor.SaveSceneAsJson(),
                                  () => ProjectOperations.HasOpenProject(),
                                  40);

            MenuRegistry.Register("menu.file.saveSceneAsBinary",
                                  "File/Save Scene As/Binary",
                                  () => SceneEditor.SaveSceneAsBinary(),
                                  () => ProjectOperations.HasOpenProject(),
                                  50);

            MenuRegistry.Register("menu.edit.play",
                                  "Edit/Play/Play",
                                  () => EnterPlayMode(),
                                  () => CanEnterPlayMode(),
                                  10);

            MenuRegistry.Register("menu.edit.pauseToggle",
                                  "Edit/Play/Toggle Pause",
                                  () => TogglePauseState(),
                                  () => CanTogglePauseState(),
                                  20);

            MenuRegistry.Register("menu.edit.stop",
                                  "Edit/Play/Stop",
                                  () => StopPlayMode(),
                                  () => CanStopPlayMode(),
                                  30);

            MenuRegistry.Register("menu.assets.refreshProjects",
                                  "Assets/Refresh Project List",
                                  () => ProjectManager.RefreshProjectList(),
                                  () => ProjectOperations.HasOpenProject(),
                                  10);

            MenuRegistry.Register("menu.assets.rebuildScripts",
                                  "Assets/Rebuild Scripts",
                                  () => ScriptComponentValidation.RequestImmediateBuildForActiveProject(),
                                  () => ProjectOperations.HasOpenProject(),
                                  20);

            MenuRegistry.Register("menu.gameObject.createEmpty",
                                  "GameObject/Create Empty",
                                  () => SceneEditor.CreateEntityAndSelect(),
                                  () => IsSceneWorkspaceActive(),
                                  10);

            MenuRegistry.Register("menu.component.addScript",
                                  "Component/Add Script",
                                  () => ProjectOperations.SetStatusMessage("Use Inspector -> Add Component -> Script."),
                                  () => IsSceneWorkspaceActive(),
                                  10);

            MenuRegistry.Register("menu.window.scene",
                                  "Window/Scene",
                                  () => SetShowProjectManagerView(false),
                                  () => ProjectOperations.HasOpenProject(),
                                  10);

            MenuRegistry.Register("menu.window.console",
                                  "Window/Console",
                                  () => EditorConsoleWindow.Toggle(),
                                  () => ProjectOperations.HasOpenProject(),
                                  20);

            MenuRegistry.Register("menu.window.inspector",
                                  "Window/Inspector",
                                  () => SetShowProjectManagerView(false),
                                  () => ProjectOperations.HasOpenProject(),
                                  30);

            MenuRegistry.Register("menu.window.project",
                                  "Window/Project",
                                  () => SetShowProjectManagerView(false),
                                  () => ProjectOperations.HasOpenProject(),
                                  40);

            MenuRegistry.Register("menu.window.projectManager",
                                  "Window/Project Manager",
                                  () => SetShowProjectManagerView(true),
                                  () => ProjectOperations.HasOpenProject(),
                                  50);

            MenuRegistry.Register("menu.window.editorOptions",
                                  "Window/Editor Options",
                                  () => EditorOptionsWindow.Toggle(),
                                  60);

            MenuRegistry.Register("menu.help.about",
                                  "Help/About",
                                  new DelegateMenuCommand(() =>
                                  {
                                      ProjectOperations.SetStatusMessage("CppCSharp Editor: dynamic menu registry active.");
                                  }),
                                  10);
        }

        private static bool IsSceneWorkspaceActive()
        {
            return ProjectOperations.HasOpenProject() && !_showProjectManagerView;
        }

        private static bool CanEnterPlayMode()
        {
            if (!IsSceneWorkspaceActive())
                return false;

            return EditorBridge.GetSimulationState() != EditorBridge.SimulationPlay;
        }

        private static bool CanTogglePauseState()
        {
            if (!IsSceneWorkspaceActive())
                return false;

            int simulationState = EditorBridge.GetSimulationState();
            return simulationState == EditorBridge.SimulationPlay || simulationState == EditorBridge.SimulationPause;
        }

        private static bool CanStopPlayMode()
        {
            if (!IsSceneWorkspaceActive())
                return false;

            return EditorBridge.GetSimulationState() != EditorBridge.SimulationEdit;
        }

        private static void TogglePauseState()
        {
            int simulationState = EditorBridge.GetSimulationState();
            if (simulationState == EditorBridge.SimulationPlay)
            {
                EditorBridge.SetSimulationPaused(true);
                ProjectOperations.SetStatusMessage("Paused play mode.");
            }
            else if (simulationState == EditorBridge.SimulationPause)
            {
                EditorBridge.SetSimulationPaused(false);
                ProjectOperations.SetStatusMessage("Resumed play mode.");
            }
        }

        private static void DrawMenuBar()
        {
            if (!ImGui.BeginTopBar("##EditorMenuBar", 34.0f))
                return;

            MenuRegistry.MenuNode[] roots = MenuRegistry.GetRootNodes();
            for (int i = 0; i < roots.Length; ++i)
            {
                DrawMenuNode(roots[i]);
            }

            ImGui.SameLine();
            string status = ProjectOperations.StatusMessage;
            if (string.IsNullOrEmpty(status))
                status = _statusMessage;
            ImGui.Text(status);

            ImGui.EndTopBar();
        }

        private static void DrawMenuNode(MenuRegistry.MenuNode node)
        {
            if (node == null || string.IsNullOrEmpty(node.Label))
                return;

            if (node.HasChildren)
            {
                if (ImGui.BeginMenu(node.Label))
                {
                    for (int i = 0; i < node.Children.Count; ++i)
                        DrawMenuNode(node.Children[i]);

                    ImGui.EndMenu();
                }

                return;
            }

            bool enabled = node.Command != null && node.Command.IsEnabled();
            if (!ImGui.MenuItem(node.Label, enabled))
                return;

            if (node.Command == null)
                return;

            try
            {
                node.Command.Execute();
            }
            catch (Exception ex)
            {
                ProjectOperations.SetStatusMessage("Menu action failed: " + ex.Message);
            }
        }

        private static bool EnterPlayMode()
        {
            int simulationState = EditorBridge.GetSimulationState();
            if (simulationState == EditorBridge.SimulationPlay)
                return true;

            if (!ScriptComponentValidation.EnsureCompiledForPlay(out string compileGateMessage))
            {
                if (string.IsNullOrEmpty(compileGateMessage))
                    compileGateMessage = "Play blocked: script compile gate rejected start.";

                ProjectOperations.SetStatusMessage(compileGateMessage);
                return false;
            }

            ScriptValidationSnapshot scriptSnapshot = ScriptComponentValidation.GetSnapshot();
            if (scriptSnapshot.IsCompiling)
            {
                ProjectOperations.SetStatusMessage("Play blocked: script compilation is still running.");
                return false;
            }

            if (scriptSnapshot.HasCompileErrors)
            {
                ProjectOperations.SetStatusMessage("Play blocked: fix script compile errors first.");
                return false;
            }

            string snapshotDirectory = Path.GetDirectoryName(_playSnapshotPath);
            if (!string.IsNullOrEmpty(snapshotDirectory))
                Directory.CreateDirectory(snapshotDirectory);

            if (!EditorBridge.SaveScene(_playSnapshotPath, 1))
            {
                string error = EditorBridge.GetLastSceneIoStatus();
                if (string.IsNullOrEmpty(error))
                    error = "Play failed: could not create scene snapshot.";

                ProjectOperations.SetStatusMessage(error);
                return false;
            }

            EditorBridge.RequestScriptAssemblyReload();

            if (!EditorBridge.StartPlayMode())
            {
                string error = EditorBridge.GetLastSceneIoStatus();
                if (string.IsNullOrEmpty(error))
                    error = "Play failed: runtime rejected play mode transition.";

                ProjectOperations.SetStatusMessage(error);
                return false;
            }

            ProjectOperations.SetStatusMessage("Entered play mode.");
            return true;
        }

        private static bool StopPlayMode()
        {
            int simulationState = EditorBridge.GetSimulationState();
            if (simulationState == EditorBridge.SimulationEdit)
                return true;

            EditorBridge.StopPlayMode();

            if (!SceneEditor.RestoreSceneFromPlaySnapshot(_playSnapshotPath))
                return false;

            ProjectOperations.SetStatusMessage("Stopped play mode and restored pre-play scene state.");
            return true;
        }

        public static void SetStatusMessage(string message)
        {
            _statusMessage = message;
        }

        public static void SetShowProjectManagerView(bool show)
        {
            _showProjectManagerView = show;
        }

        public static void OnEditorShutdown()
        {
            EditorBridge.SetWindowBackgroundVisible(false);
            EditorBridge.SetMainWindowBorderless(false);
            EditorBridge.SetMainWindowResizable(true);
            StopPlayMode();
            Console.WriteLine("[Editor] OnEditorShutdown called.");
        }
    }
}


