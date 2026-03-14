namespace EngineEditor
{
    internal static class AssetBrowserSystem
    {
        public static void RegisterEditorOptions()
        {
            AssetPanel.RegisterEditorOptions();
        }

        public static void RegisterAssetContextMenu()
        {
            AssetPanel.RegisterAssetContextMenu();
        }

        public static void DrawWindow()
        {
            AssetPanel.DrawAssetPanel();
        }

        public static void RequestOpenCreateScenePopup()
        {
            AssetPanel.RequestOpenCreateScenePopup();
        }

        public static void RequestOpenCreateScriptPopup()
        {
            AssetPanel.RequestOpenCreateScriptPopup();
        }
    }
}
