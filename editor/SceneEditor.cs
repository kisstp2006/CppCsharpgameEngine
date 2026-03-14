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
            if (HierarchyWindow.Instance != null)
                HierarchyWindow.Instance.ResetSelection();
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
            if (HierarchyWindow.Instance != null)
                HierarchyWindow.Instance.ResetSelection();
        }

        public static void CreateEntityAndSelect()
        {
            if (HierarchyWindow.Instance != null)
                HierarchyWindow.Instance.CreateEntityAndSelect();
        }

        public static bool HasSelectedEntity()
        {
            return HierarchyWindow.Instance != null && HierarchyWindow.Instance.HasValidSelection();
        }

        public static void DuplicateSelectedEntity()
        {
            if (HierarchyWindow.Instance != null)
                HierarchyWindow.Instance.DuplicateSelectedEntity();
        }

        public static void DeleteSelectedEntity()
        {
            if (HierarchyWindow.Instance != null)
                HierarchyWindow.Instance.DeleteSelectedEntity();
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

        public static void FocusCamera()
        {
            SceneViewportSystem.FocusCamera();
        }

        public static bool FrameSelectedEntity()
        {
            return SceneViewportSystem.FrameSelectedEntity();
        }

        public static void DrawSceneTreePanel()
        {
            // HierarchyWindow now draws itself via the EditorWindowManager lifecycle.
            // This method is kept for API compatibility but is no longer called.
        }

        public static void DrawInspectorPanel()
        {
            InspectorSystem.DrawInspectorPanel();
        }
    }
}
