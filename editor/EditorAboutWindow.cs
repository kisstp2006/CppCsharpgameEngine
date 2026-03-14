using Engine;

namespace EngineEditor
{
    internal static class EditorAboutWindow
    {
        private static bool _open;

        public static bool IsOpen => _open;

        public static void SetOpen(bool open)
        {
            _open = open;
        }

        public static void Toggle()
        {
            _open = !_open;
        }

        public static void Open()
        {
            _open = true;
        }

        public static void Draw()
        {
            if (!_open)
                return;

            if (!ImGui.Begin("About"))
            {
                ImGui.End();
                return;
            }

            var assemblyName = typeof(EditorHost).Assembly.GetName();
            string versionText = assemblyName.Version != null ? assemblyName.Version.ToString() : "unknown";

            ImGui.Text("CppCSharpGameEngine");
            ImGui.Text("Editor Module");

            ImGui.Separator();
            ImGui.Text("Assembly: " + assemblyName.Name);
            ImGui.Text("Version: " + versionText);
            ImGui.Text("Runtime: .NET Framework 4.7.2");

            ImGui.Separator();
            ImGui.Text("Core Technology");
            ImGui.Text("- SDL2");
            ImGui.Text("- OpenGL 3.3");
            ImGui.Text("- Dear ImGui");
            ImGui.Text("- EnTT");
            ImGui.Text("- Mono / C#");

            ImGui.Separator();
            if (ImGui.Button("Open Editor Options"))
                OptionsSystem.IsOpen = true;

            ImGui.SameLine();
            if (ImGui.Button("Close"))
                _open = false;

            ImGui.End();
        }
    }
}