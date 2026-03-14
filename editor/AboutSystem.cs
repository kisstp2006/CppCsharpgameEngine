namespace EngineEditor
{
    internal static class AboutSystem
    {
        public static bool IsOpen
        {
            get => EditorAboutWindow.IsOpen;
            set => EditorAboutWindow.SetOpen(value);
        }

        public static void Toggle()
        {
            EditorAboutWindow.Toggle();
        }

        public static void Open()
        {
            EditorAboutWindow.Open();
        }

        public static void DrawWindow()
        {
            EditorAboutWindow.Draw();
        }
    }
}