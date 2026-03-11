using System;

using Engine;

namespace EngineEditor
{
    internal static class EditorConsoleWindow
    {
        private static bool _open = true;
        private static bool _showLogs = true;
        private static bool _showWarnings = true;
        private static bool _showErrors = true;
        private static string _filterText = string.Empty;
        private static int _selectedIndex = -1;

        public static bool IsOpen => _open;

        public static void Toggle()
        {
            _open = !_open;
        }

        public static void SetOpen(bool open)
        {
            _open = open;
        }

        public static void Draw()
        {
            if (!_open)
                return;

            if (!ImGui.Begin("Console"))
            {
                ImGui.End();
                return;
            }

            if (ImGui.Button("Clear"))
            {
                Debug.ClearLogs();
                _selectedIndex = -1;
            }

            ImGui.SameLine();
            bool showLogs = _showLogs;
            if (ImGui.Checkbox("Log", ref showLogs))
                _showLogs = showLogs;

            ImGui.SameLine();
            bool showWarnings = _showWarnings;
            if (ImGui.Checkbox("Warning", ref showWarnings))
                _showWarnings = showWarnings;

            ImGui.SameLine();
            bool showErrors = _showErrors;
            if (ImGui.Checkbox("Error", ref showErrors))
                _showErrors = showErrors;

            ImGui.SameLine();
            EditorUIHelpers.InputTextWithWidth("Search##Console", ref _filterText, EditorUIHelpers.CompactSearchWidth);

            ImGui.Separator();

            string filter = (_filterText ?? string.Empty).Trim();
            int count = Debug.GetLogCount();
            bool anyVisible = false;

            for (int i = 0; i < count; ++i)
            {
                int level = Debug.GetLogLevel(i);
                if (!IsLevelVisible(level))
                    continue;

                string line = Debug.GetLogMessage(i) ?? string.Empty;
                if (!string.IsNullOrEmpty(filter) && line.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                anyVisible = true;

                bool selected = _selectedIndex == i;
                if (ImGui.Selectable(line + "##ConsoleLog" + i, selected))
                    _selectedIndex = i;
            }

            if (!anyVisible)
                ImGui.Text("No logs to display.");

            if (_selectedIndex >= 0)
            {
                int latestCount = Debug.GetLogCount();
                if (_selectedIndex < latestCount)
                {
                    string selectedLine = Debug.GetLogMessage(_selectedIndex) ?? string.Empty;
                    if (!string.IsNullOrEmpty(selectedLine))
                    {
                        EditorUIHelpers.DrawSectionHeader("Selected Log");
                        ImGui.Text(selectedLine);
                    }
                }
            }

            ImGui.End();
        }

        private static bool IsLevelVisible(int level)
        {
            switch (level)
            {
                case 2:
                    return _showErrors;
                case 1:
                    return _showWarnings;
                default:
                    return _showLogs;
            }
        }
    }
}
