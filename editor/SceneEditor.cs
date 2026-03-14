using Engine;

namespace EngineEditor
{
    // Transitional facade to keep legacy SceneEditor call sites stable while
    // implementation is split across modular systems.
    internal static class SceneEditor
    {
        public static int SelectedEntityId => EditorContext.SelectedEntityId;
        public static string ActiveScenePath => SceneSystem.ActiveScenePath;

        public static void ResetEditorState()
        {
            HierarchySystem.ResetSelection();
            SceneViewportSystem.ResetState();
            InspectorSystem.ResetState();
            DebugDraw.Clear();
        }

        public static void RegisterEditorOptions()
        {
            SceneViewportSystem.RegisterEditorOptions();
        }

        public static void RegisterAssetContextMenu()
        {
            SceneSystem.RegisterAssetContextMenu();
        }

        public static void ResetSelection()
        {
            HierarchySystem.ResetSelection();
        }

        public static void CreateEntityAndSelect()
        {
            HierarchySystem.CreateEntityAndSelect();
        }

        public static void NewScene()
        {
            SceneSystem.NewScene();
        }

        public static bool CreateSceneAsset(string sceneName, int storageFormat)
        {
            return SceneSystem.CreateSceneAsset(sceneName, storageFormat);
        }

        public static bool LoadSceneFromPath(string scenePath)
        {
            return SceneSystem.LoadSceneFromPath(scenePath);
        }

        public static void LoadSceneFromPicker()
        {
            SceneSystem.LoadSceneFromPicker();
        }

        public static bool RestoreSceneFromPlaySnapshot(string snapshotPath)
        {
            return SceneSystem.RestoreSceneFromPlaySnapshot(snapshotPath);
        }

        public static bool SaveScene()
        {
            return SceneSystem.SaveScene();
        }

        public static bool IsSceneFilePath(string path)
        {
            return SceneSystem.IsSceneFilePath(path);
        }

        public static void SaveSceneAsJson()
        {
            SceneSystem.SaveSceneAsJson();
        }

        public static void SaveSceneAsBinary()
        {
            SceneSystem.SaveSceneAsBinary();
        }

        public static void UpdateTick(float deltaTime)
        {
            SceneViewportSystem.UpdateTick(deltaTime);
        }

        public static void DrawWorldViewportPanel(float deltaTime)
        {
            SceneViewportSystem.DrawWorldViewportPanel(deltaTime);
        }

        public static void DrawRuntimeGamePanel()
        {
            SceneViewportSystem.DrawRuntimeGamePanel();
        }

        public static void DrawSceneTreePanel()
        {
            HierarchySystem.DrawSceneTreePanel();
        }

        public static void DrawInspectorPanel()
        {
            InspectorSystem.DrawInspectorPanel();
        }
    }
}
