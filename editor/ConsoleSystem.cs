namespace EngineEditor
{
    internal static class ConsoleSystem
    {
        public static bool IsOpen
        {
            get => EditorConsoleWindow.IsOpen;
            set => EditorConsoleWindow.SetOpen(value);
        }

        public static void Toggle()
        {
            EditorConsoleWindow.Toggle();
        }

        public static void DrawWindow()
        {
            EditorConsoleWindow.Draw();
        }
    }
}
