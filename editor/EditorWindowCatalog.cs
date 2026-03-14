using Engine;

namespace EngineEditor
{
    internal static class EditorWindowCatalog
    {
        private const string IdSceneTree = "scene.tree";
        private const string IdSceneViewport = "scene.viewport";
        private const string IdRuntimeView = "scene.runtime";
        private const string IdInspector = "scene.inspector";
        private const string IdAnimation = "animation.timeline";
        private const string IdAssetBrowser = "assets.browser";
        private const string IdConsole = "tools.console";
        private const string IdEditorOptions = "tools.options";
        private const string IdAbout = "help.about";

        public static void RegisterDefaults()
        {
            EditorWindowManager.Clear();

            EditorWindowManager.Register(IdSceneTree, new HierarchyWindow(), "Scene Tree", 10);
            EditorWindowManager.Register(IdSceneViewport, new SceneViewportWindow(), "Game View", 20);
            EditorWindowManager.Register(IdRuntimeView, new RuntimeViewWindow(), "Game Runtime", 30);
            EditorWindowManager.Register(IdInspector, new InspectorWindow(), "Inspector", 40);
            EditorWindowManager.Register(IdAnimation, new AnimationTimelineWindowWrapper(), "Animation Timeline", 50);
            EditorWindowManager.Register(IdAssetBrowser, new AssetBrowserWindow(), "Asset Panel", 60);
            EditorWindowManager.Register(IdConsole, new ConsoleWindow(), "Console", 70);
            EditorWindowManager.Register(IdEditorOptions, new OptionsWindow(), "Editor Options", 80);
            EditorWindowManager.Register(IdAbout, new AboutWindow(), "About", 90);
        }

        private static bool IsSceneWorkspaceVisible()
        {
            return ProjectOperations.HasOpenProject() && !EditorContext.ShowProjectManagerView;
        }

        private sealed class HierarchyWindow : EditorWindow
        {
            public override string Title => "Scene Tree";

            public override int Order => 10;

            public override bool ShouldDisplay()
            {
                return IsSceneWorkspaceVisible();
            }

            public override void OnGUI()
            {
                SceneEditor.DrawSceneTreePanel();
            }
        }

        private sealed class SceneViewportWindow : EditorWindow
        {
            private float _deltaTime;

            public override string Title => "Game View";

            public override int Order => 20;

            public override bool ShouldDisplay()
            {
                return IsSceneWorkspaceVisible();
            }

            public override void OnUpdate(float deltaTime)
            {
                _deltaTime = deltaTime;
                SceneEditor.UpdateTick(deltaTime);
            }

            public override void OnGUI()
            {
                SceneEditor.DrawWorldViewportPanel(_deltaTime);
            }
        }

        private sealed class RuntimeViewWindow : EditorWindow
        {
            public override string Title => "Game Runtime";

            public override int Order => 30;

            public override bool ShouldDisplay()
            {
                return IsSceneWorkspaceVisible();
            }

            public override void OnGUI()
            {
                SceneEditor.DrawRuntimeGamePanel();
            }
        }

        private sealed class InspectorWindow : EditorWindow
        {
            public override string Title => "Inspector";

            public override int Order => 40;

            public override bool ShouldDisplay()
            {
                return IsSceneWorkspaceVisible();
            }

            public override void OnGUI()
            {
                SceneEditor.DrawInspectorPanel();
            }
        }

        private sealed class AnimationTimelineWindowWrapper : EditorWindow
        {
            private float _deltaTime;

            public override string Title => "Animation Timeline";

            public override int Order => 50;

            public override bool ShouldDisplay()
            {
                return IsSceneWorkspaceVisible();
            }

            public override void OnUpdate(float deltaTime)
            {
                _deltaTime = deltaTime;
            }

            public override void OnGUI()
            {
                AnimationSystem.DrawWindow(_deltaTime);
            }
        }

        private sealed class AssetBrowserWindow : EditorWindow
        {
            public override string Title => "Asset Panel";

            public override int Order => 60;

            public override bool ShouldDisplay()
            {
                return IsSceneWorkspaceVisible();
            }

            public override void OnGUI()
            {
                AssetBrowserSystem.DrawWindow();
            }
        }

        private sealed class ConsoleWindow : EditorWindow
        {
            public override string Title => "Console";

            public override bool IsOpen
            {
                get => ConsoleSystem.IsOpen;
                set => ConsoleSystem.IsOpen = value;
            }

            public override int Order => 70;

            public override bool ShouldDisplay()
            {
                return IsSceneWorkspaceVisible();
            }

            public override void OnGUI()
            {
                ConsoleSystem.DrawWindow();
            }
        }

        private sealed class OptionsWindow : EditorWindow
        {
            public override string Title => "Editor Options";

            public override bool IsOpen
            {
                get => OptionsSystem.IsOpen;
                set => OptionsSystem.IsOpen = value;
            }

            public override int Order => 80;

            public override void OnGUI()
            {
                OptionsSystem.DrawWindow();
            }
        }

        private sealed class AboutWindow : EditorWindow
        {
            public override string Title => "About";

            public override bool IsOpen
            {
                get => AboutSystem.IsOpen;
                set => AboutSystem.IsOpen = value;
            }

            public override int Order => 90;

            public override void OnGUI()
            {
                AboutSystem.DrawWindow();
            }
        }
    }
}
