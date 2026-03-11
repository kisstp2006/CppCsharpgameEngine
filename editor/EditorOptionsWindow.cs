using System;

using Engine;

namespace EngineEditor
{
    internal static class EditorOptionsWindow
    {
        private static bool _open = false;
        private static string _selectedOptionId = string.Empty;
        private static string _filterText = string.Empty;

        public static bool IsOpen => _open;

        public static void Toggle()
        {
            _open = !_open;
        }

        public static void Draw()
        {
            if (!_open)
                return;

            if (!ImGui.Begin("Editor Options"))
            {
                ImGui.End();
                return;
            }

            ImGui.SetNextItemWidth(320.0f);
            string updatedFilter = ImGui.InputText("Filter", _filterText);
            if (updatedFilter != null)
                _filterText = updatedFilter;

            ImGui.Separator();

            float contentWidth = ImGui.GetContentRegionAvailX();
            float contentHeight = ImGui.GetContentRegionAvailY();
            if (contentWidth < 1.0f)
                contentWidth = 1.0f;
            if (contentHeight < 1.0f)
                contentHeight = 1.0f;

            float leftWidth = contentWidth * 0.35f;
            if (leftWidth < 220.0f)
                leftWidth = 220.0f;
            if (leftWidth > contentWidth - 80.0f)
                leftWidth = contentWidth - 80.0f;

            if (leftWidth < 1.0f)
                leftWidth = contentWidth;

            if (ImGui.BeginChild("##OptionsList", leftWidth, contentHeight, true))
            {
                DrawEntryList();
                ImGui.EndChild();
            }

            ImGui.SameLine();

            float rightWidth = contentWidth - leftWidth - 8.0f;
            if (rightWidth < 1.0f)
                rightWidth = 1.0f;

            if (ImGui.BeginChild("##OptionsContent", rightWidth, contentHeight, true))
            {
                DrawSelectedEntry();
                ImGui.EndChild();
            }

            ImGui.End();
        }

        private static void DrawEntryList()
        {
            EditorOptionsRegistry.OptionEntry[] entries = EditorOptionsRegistry.GetEntriesSnapshot();
            bool hasVisibleEntries = false;
            string normalizedFilter = (_filterText ?? string.Empty).Trim();

            for (int i = 0; i < entries.Length; ++i)
            {
                EditorOptionsRegistry.OptionEntry entry = entries[i];
                if (!IsEntryVisible(entry, normalizedFilter))
                    continue;

                hasVisibleEntries = true;

                string label = entry.Category + " / " + entry.Title;
                bool selected = string.Equals(_selectedOptionId, entry.Id, StringComparison.Ordinal);
                if (ImGui.Selectable(label + "##" + entry.Id, selected))
                    _selectedOptionId = entry.Id;
            }

            if (!hasVisibleEntries)
                ImGui.Text("No options found.");

            if (hasVisibleEntries && string.IsNullOrEmpty(_selectedOptionId))
            {
                for (int i = 0; i < entries.Length; ++i)
                {
                    EditorOptionsRegistry.OptionEntry entry = entries[i];
                    if (!IsEntryVisible(entry, normalizedFilter))
                        continue;

                    _selectedOptionId = entry.Id;
                    break;
                }
            }
        }

        private static void DrawSelectedEntry()
        {
            EditorOptionsRegistry.OptionEntry[] entries = EditorOptionsRegistry.GetEntriesSnapshot();
            EditorOptionsRegistry.OptionEntry selected = null;
            for (int i = 0; i < entries.Length; ++i)
            {
                if (!string.Equals(entries[i].Id, _selectedOptionId, StringComparison.Ordinal))
                    continue;

                selected = entries[i];
                break;
            }

            if (selected == null)
            {
                ImGui.Text("Select an option group from the list.");
                return;
            }

            ImGui.Text(selected.Category + " / " + selected.Title);
            ImGui.Separator();

            try
            {
                selected.Draw();
            }
            catch (Exception ex)
            {
                ImGui.Text("Option draw error: " + ex.Message);
            }
        }

        private static bool IsEntryVisible(EditorOptionsRegistry.OptionEntry entry, string filter)
        {
            if (string.IsNullOrEmpty(filter))
                return true;

            if (entry.Title.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (entry.Category.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }
    }
}
