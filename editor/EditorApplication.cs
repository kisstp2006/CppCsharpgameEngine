using System;
using System.IO;

using Engine;

namespace EngineEditor
{
    internal static class EditorApplication
    {
        private static string _editorConfigDir = string.Empty;
        private static string _playSnapshotPath = string.Empty;
        private static float _compileProgressPulse = 0.0f;
        private static bool _compileDialogOpen;
        private static bool _menuItemsRegistered;

        private const string CompilePopupId = "##ScriptCompileBlockingModal";

        public static void OnEditorStart()
        {
            string cwd = Directory.GetCurrentDirectory();
            _editorConfigDir = Path.Combine(cwd, ".editor");
            Directory.CreateDirectory(_editorConfigDir);
            _playSnapshotPath = Path.Combine(_editorConfigDir, "playmode_snapshot.scene.bin");
            DockspaceManager.Initialize(_editorConfigDir);

            ProjectManager.Initialize(_editorConfigDir);
            ProjectOperations.Initialize(_editorConfigDir);
            ScriptComponentValidation.Initialize(_editorConfigDir);

            ImGuiThemeSettings.Initialize(_editorConfigDir);
            ImGuiThemeSettings.RegisterEditorOptions();

            AssetBrowserSystem.RegisterEditorOptions();
            AssetBrowserSystem.RegisterAssetContextMenu();
            SceneEditor.RegisterEditorOptions();
            SceneEditor.RegisterAssetContextMenu();
            AnimationSystem.RegisterAssetContextMenu();
            ScriptComponentValidation.RegisterEditorOptions();
            ScriptFieldInspector.RegisterEditorOptions();
            ScriptFieldInspector.RegisterAssetContextMenu();

            EditorWindowCatalog.RegisterDefaults();

            RegisterMenuItems();

            int bootPhase = EditorBridge.GetBootPhase();
            BootManager.OnEditorStart(bootPhase);
            EditorContext.PlayModeState = EditorBridge.GetSimulationState();
        }

        public static void OnEditorUpdate(float deltaTime)
        {
            EditorContext.PlayModeState = EditorBridge.GetSimulationState();

            int bootPhase = EditorBridge.GetBootPhase();
            if (bootPhase == 0)
            {
                EditorContext.ShowProjectManagerView = true;
                DockspaceManager.DisableRuntimeDockspace();
                ProjectManager.DrawProjectPanel();
                OptionsSystem.DrawWindow();
                AboutSystem.DrawWindow();
                return;
            }

            if (bootPhase != 2)
                return;

            BootManager.TryAttachProjectForEditorPhase();
            UpdateMainEditorStage(deltaTime);
        }

        public static void OnEditorShutdown()
        {
            StopPlayMode();
            Console.WriteLine("[Editor] OnEditorShutdown called.");
        }

        public static string StatusMessage => EditorContext.StatusMessage;

        public static void SetStatusMessage(string message)
        {
            EditorContext.StatusMessage = message;
        }

        public static void SetShowProjectManagerView(bool show)
        {
            EditorContext.ShowProjectManagerView = show;
        }

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
                DockspaceManager.DisableRuntimeDockspace();
                EditorContext.ShowProjectManagerView = true;
                SceneEditor.ResetEditorState();
                ProjectManager.DrawProjectPanel();
                OptionsSystem.DrawWindow();
                AboutSystem.DrawWindow();
                return;
            }

            if (EditorContext.ShowProjectManagerView)
            {
                DockspaceManager.DisableRuntimeDockspace();
                SceneEditor.ResetEditorState();
                ProjectManager.DrawProjectPanel();
                OptionsSystem.DrawWindow();
                AboutSystem.DrawWindow();
                return;
            }

            DockspaceManager.EnableRuntimeDockspace();
            EditorWindowManager.DrawAll(deltaTime);
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
                                  () => SetStatusMessage("Use Inspector -> Add Component -> Script."),
                                  () => IsSceneWorkspaceActive(),
                                  10);

            MenuRegistry.Register("menu.window.scene",
                                  "Window/Open Scene Workspace",
                                  () => SetShowProjectManagerView(false),
                                  () => ProjectOperations.HasOpenProject(),
                                  10);

            MenuRegistry.Register("menu.window.projectManager",
                                  "Window/Project Manager",
                                  () => SetShowProjectManagerView(true),
                                  () => ProjectOperations.HasOpenProject(),
                                  60);

            EditorWindowManager.RegisterWindowMenuItems("Window",
                                                        () => ProjectOperations.HasOpenProject(),
                                                        100);

            MenuRegistry.Register("menu.window.layout.saveDefault",
                                  "Window/Layout/Save Default",
                                  () =>
                                  {
                                      DockspaceManager.SaveLayout("default", out string saveMessage);
                                      SetStatusMessage(saveMessage);
                                  },
                                  () => ProjectOperations.HasOpenProject(),
                                  500);

            MenuRegistry.Register("menu.window.layout.loadDefault",
                                  "Window/Layout/Load Default",
                                  () =>
                                  {
                                      DockspaceManager.LoadLayout("default", out string loadMessage);
                                      SetStatusMessage(loadMessage);
                                  },
                                  () => ProjectOperations.HasOpenProject(),
                                  510);

            MenuRegistry.Register("menu.window.layout.reset",
                                  "Window/Layout/Reset",
                                  () =>
                                  {
                                      DockspaceManager.ResetLayout(out string resetMessage);
                                      SetStatusMessage(resetMessage);
                                  },
                                  () => ProjectOperations.HasOpenProject(),
                                  520);

            MenuRegistry.Register("menu.help.about",
                                  "Help/About",
                                  new DelegateMenuCommand(() =>
                                  {
                                      AboutSystem.Open();
                                      SetStatusMessage("Opened About window.");
                                  }),
                                  10);
        }

        private static bool IsSceneWorkspaceActive()
        {
            return ProjectOperations.HasOpenProject() && !EditorContext.ShowProjectManagerView;
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
                SetStatusMessage("Paused play mode.");
            }
            else if (simulationState == EditorBridge.SimulationPause)
            {
                EditorBridge.SetSimulationPaused(false);
                SetStatusMessage("Resumed play mode.");
            }
        }

        private static void DrawMenuBar()
        {
            if (!ImGui.BeginTopBar("##EditorMenuBar", 34.0f))
                return;

            MenuRegistry.MenuNode[] roots = MenuRegistry.GetRootNodes();
            for (int i = 0; i < roots.Length; ++i)
                DrawMenuNode(roots[i]);

            ImGui.SameLine();
            string status = EditorContext.StatusMessage;
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
                SetStatusMessage("Menu action failed: " + ex.Message);
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

                SetStatusMessage(compileGateMessage);
                return false;
            }

            ScriptValidationSnapshot scriptSnapshot = ScriptComponentValidation.GetSnapshot();
            if (scriptSnapshot.IsCompiling)
            {
                SetStatusMessage("Play blocked: script compilation is still running.");
                return false;
            }

            if (scriptSnapshot.HasCompileErrors)
            {
                SetStatusMessage("Play blocked: fix script compile errors first.");
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

                SetStatusMessage(error);
                return false;
            }

            EditorBridge.RequestScriptAssemblyReload();

            if (!EditorBridge.StartPlayMode())
            {
                string error = EditorBridge.GetLastSceneIoStatus();
                if (string.IsNullOrEmpty(error))
                    error = "Play failed: runtime rejected play mode transition.";

                SetStatusMessage(error);
                return false;
            }

            EditorContext.PlayModeState = EditorBridge.GetSimulationState();
            SetStatusMessage("Entered play mode.");
            return true;
        }

        private static bool StopPlayMode()
        {
            int simulationState = EditorBridge.GetSimulationState();
            if (simulationState == EditorBridge.SimulationEdit)
                return true;

            EditorBridge.StopPlayMode();
            EditorContext.PlayModeState = EditorBridge.GetSimulationState();

            if (!SceneEditor.RestoreSceneFromPlaySnapshot(_playSnapshotPath))
                return false;

            SetStatusMessage("Stopped play mode and restored pre-play scene state.");
            return true;
        }
    }
}
