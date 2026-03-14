namespace EngineEditor
{
    internal static class OptionsSystem
    {
        public static bool IsOpen
        {
            get => EditorOptionsWindow.IsOpen;
            set => EditorOptionsWindow.SetOpen(value);
        }

        public static void Toggle()
        {
            EditorOptionsWindow.Toggle();
        }

        public static void DrawWindow()
        {
            EditorOptionsWindow.Draw();
        }
    }
}
