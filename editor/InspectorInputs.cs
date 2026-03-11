using Engine;

namespace EngineEditor
{
    internal static class InspectorInputs
    {
        public static bool Vector2(string label, ref float x, ref float y, float step)
        {
            ImGui.Text(label);

            bool changed = false;
            ImGui.SetNextItemWidth(90.0f);
            changed |= ImGui.InputFloat("##" + label + "_x", ref x, step);
            ImGui.SameLine();
            ImGui.Text("X");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(90.0f);
            changed |= ImGui.InputFloat("##" + label + "_y", ref y, step);
            ImGui.SameLine();
            ImGui.Text("Y");

            return changed;
        }

        public static bool Vector3(string label, ref float x, ref float y, ref float z, float step)
        {
            ImGui.Text(label);

            bool changed = false;
            ImGui.SetNextItemWidth(90.0f);
            changed |= ImGui.InputFloat("##" + label + "_x", ref x, step);
            ImGui.SameLine();
            ImGui.Text("X");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(90.0f);
            changed |= ImGui.InputFloat("##" + label + "_y", ref y, step);
            ImGui.SameLine();
            ImGui.Text("Y");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(90.0f);
            changed |= ImGui.InputFloat("##" + label + "_z", ref z, step);
            ImGui.SameLine();
            ImGui.Text("Z");

            return changed;
        }

        public static bool Quaternion(string label, ref float x, ref float y, ref float z, ref float w, float step)
        {
            ImGui.Text(label);

            bool changed = false;
            ImGui.SetNextItemWidth(78.0f);
            changed |= ImGui.InputFloat("##" + label + "_qx", ref x, step);
            ImGui.SameLine();
            ImGui.Text("X");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(78.0f);
            changed |= ImGui.InputFloat("##" + label + "_qy", ref y, step);
            ImGui.SameLine();
            ImGui.Text("Y");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(78.0f);
            changed |= ImGui.InputFloat("##" + label + "_qz", ref z, step);
            ImGui.SameLine();
            ImGui.Text("Z");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(78.0f);
            changed |= ImGui.InputFloat("##" + label + "_qw", ref w, step);
            ImGui.SameLine();
            ImGui.Text("W");

            return changed;
        }

        public static bool Bool(string label, ref bool value)
        {
            return ImGui.Checkbox(label, ref value);
        }

        public static bool ScriptType(string label, ref string value)
        {
            string current = value ?? string.Empty;
            string updated = ImGui.InputText(label, current);
            if (updated == current)
                return false;

            value = updated;
            return true;
        }
    }
}
