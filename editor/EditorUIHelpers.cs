using Engine;

namespace EngineEditor
{
    internal static class EditorUIHelpers
    {
        public const float CompactSearchWidth = 260.0f;
        public const float StandardSearchWidth = 280.0f;
        public const float StandardFieldWidth = 320.0f;
        public const float InspectorFieldWidth = 280.0f;
        public const float CompactPopupFieldWidth = 280.0f;
        public const float MediumPopupFieldWidth = 300.0f;
        public const float AssetBrowserReservedStatusHeight = 30.0f;
        public const float AssetBrowserMinBodyHeight = 120.0f;
        public const float AssetBrowserMinWidth = 320.0f;
        public const float AssetTreeMinWidth = 220.0f;
        public const float AssetTreeReservedGridWidth = 120.0f;
        public const float AssetGridItemWidth = 190.0f;

        public static void DrawSectionHeader(string title)
        {
            ImGui.Separator();
            ImGui.Text(title);
            ImGui.Separator();
        }

        public static void DrawPopupHeader(string title)
        {
            ImGui.Text(title);
            ImGui.Separator();
        }

        public static void DrawBreadcrumbSeparator()
        {
            ImGui.SameLine();
            ImGui.Text(">");
            ImGui.SameLine();
        }

        public static void DrawInlineDivider()
        {
            ImGui.SameLine();
            ImGui.Text("|");
            ImGui.SameLine();
        }

        public static bool InputTextWithWidth(string label, ref string value, float width)
        {
            string current = value ?? string.Empty;
            ImGui.SetNextItemWidth(width);
            string updated = ImGui.InputText(label, current);
            if (updated == null || string.Equals(updated, current, System.StringComparison.Ordinal))
                return false;

            value = updated;
            return true;
        }

        public static string BuildFoldoutButtonLabel(string title, bool expanded, string idSuffix)
        {
            return (expanded ? "v " : "> ") + title + "##" + idSuffix;
        }

        public static string BuildTreeNodeLabel(int depth, bool hasChildren, bool expanded, string title, string idSuffix)
        {
            string indent = new string(' ', depth * 2);
            string marker = hasChildren ? (expanded ? "v" : ">") : "-";
            return indent + marker + " " + title + "##" + idSuffix;
        }
    }
}
