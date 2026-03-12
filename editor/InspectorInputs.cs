using System;
using System.Collections.Generic;
using System.Globalization;

using Engine;

namespace EngineEditor
{
    internal static class InspectorInputs
    {
        private static readonly Dictionary<string, string> _colorHexInputState = new Dictionary<string, string>();

        public static bool String(string label, ref string value)
        {
            string current = value ?? string.Empty;
            string updated = ImGui.InputText(label, current);
            if (updated == null || updated == current)
                return false;

            value = updated;
            return true;
        }

        public static bool SelectString(string label, ref string value, string[] options)
        {
            bool changed = false;

            string current = value ?? string.Empty;
            string updated = ImGui.InputText(label, current);
            if (updated != null && updated != current)
            {
                value = updated;
                current = updated;
                changed = true;
            }

            string popupId = "ValueSelector##" + label;
            ImGui.SameLine();
            if (ImGui.Button("Select##" + label))
                ImGui.OpenPopup(popupId);

            if (ImGui.BeginPopupModal(popupId))
            {
                ImGui.Text("Available values");
                ImGui.Separator();

                if (options == null || options.Length == 0)
                {
                    ImGui.Text("No options available.");
                }
                else
                {
                    for (int i = 0; i < options.Length; ++i)
                    {
                        string option = options[i] ?? string.Empty;
                        bool selected = string.Equals(option, current, StringComparison.Ordinal);
                        if (ImGui.Selectable(option + "##SelectOption" + label + i, selected))
                        {
                            if (!string.Equals(value, option, StringComparison.Ordinal))
                            {
                                value = option;
                                changed = true;
                            }

                            ImGui.CloseCurrentPopup();
                            break;
                        }
                    }
                }

                ImGui.Separator();
                if (ImGui.Button("Close##" + popupId))
                    ImGui.CloseCurrentPopup();

                ImGui.EndPopup();
            }

            return changed;
        }

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

        public static bool UInt(string label, ref uint value)
        {
            string current = value.ToString(CultureInfo.InvariantCulture);
            string updated = ImGui.InputText(label, current);
            if (updated == null || updated == current)
                return false;

            if (!uint.TryParse(updated, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint parsed))
                return false;

            value = parsed;
            return true;
        }

        public static bool ColorRgba32(string label, ref uint color)
        {
            float r = ByteToUnit((color >> 24) & 0xFFu);
            float g = ByteToUnit((color >> 16) & 0xFFu);
            float b = ByteToUnit((color >> 8) & 0xFFu);
            float a = ByteToUnit(color & 0xFFu);

            bool changed = ColorFlyout(label, ref r, ref g, ref b, ref a, true);
            uint packed = (UnitToByte(r) << 24) | (UnitToByte(g) << 16) | (UnitToByte(b) << 8) | UnitToByte(a);
            if (packed != color)
            {
                color = packed;
                changed = true;
            }

            return changed;
        }

        public static bool ColorNormalized(string label,
                                           ref float r,
                                           ref float g,
                                           ref float b,
                                           ref float a,
                                           bool showAlpha)
        {
            return ColorFlyout(label, ref r, ref g, ref b, ref a, showAlpha);
        }

        private static bool ColorFlyout(string label,
                                        ref float r,
                                        ref float g,
                                        ref float b,
                                        ref float a,
                                        bool showAlpha)
        {
            r = Clamp01(r);
            g = Clamp01(g);
            b = Clamp01(b);
            a = Clamp01(a);

            string popupId = "ColorFlyout##" + label;
            string buttonId = "##ColorButton_" + label;
            bool changed = false;

            ImGui.Text(label);
            ImGui.SameLine();

            if (ImGui.ColorButton(buttonId, r, g, b, showAlpha ? a : 1.0f))
                ImGui.OpenPopup(popupId);

            ImGui.SameLine();
            ImGui.Text("#" + FormatHexColor(r, g, b, a, showAlpha));

            if (!ImGui.BeginPopup(popupId))
                return changed;

            float pickR = r;
            float pickG = g;
            float pickB = b;
            float pickA = a;
            if (ImGui.ColorPicker4("##" + popupId + "_picker", ref pickR, ref pickG, ref pickB, ref pickA, showAlpha))
            {
                r = Clamp01(pickR);
                g = Clamp01(pickG);
                b = Clamp01(pickB);
                a = showAlpha ? Clamp01(pickA) : 1.0f;
                changed = true;
            }

            uint r8 = UnitToByte(r);
            uint g8 = UnitToByte(g);
            uint b8 = UnitToByte(b);
            uint a8 = UnitToByte(a);

            ImGui.Separator();
            bool channelChanged = false;

            ImGui.SetNextItemWidth(64.0f);
            channelChanged |= UInt("R##" + popupId, ref r8);
            ImGui.SameLine();
            ImGui.Text("R");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(64.0f);
            channelChanged |= UInt("G##" + popupId, ref g8);
            ImGui.SameLine();
            ImGui.Text("G");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(64.0f);
            channelChanged |= UInt("B##" + popupId, ref b8);
            ImGui.SameLine();
            ImGui.Text("B");

            if (showAlpha)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(64.0f);
                channelChanged |= UInt("A##" + popupId, ref a8);
                ImGui.SameLine();
                ImGui.Text("A");
            }

            if (r8 > 255u)
                r8 = 255u;
            if (g8 > 255u)
                g8 = 255u;
            if (b8 > 255u)
                b8 = 255u;
            if (a8 > 255u)
                a8 = 255u;

            if (channelChanged)
            {
                r = ByteToUnit(r8);
                g = ByteToUnit(g8);
                b = ByteToUnit(b8);
                a = showAlpha ? ByteToUnit(a8) : 1.0f;
                changed = true;
            }

            string hexKey = popupId;
            if (!_colorHexInputState.TryGetValue(hexKey, out string hexInput))
                hexInput = FormatHexColor(r, g, b, a, showAlpha);

            if (String("Hex##" + popupId, ref hexInput))
            {
                _colorHexInputState[hexKey] = hexInput;
                if (TryParseHexColor(hexInput, showAlpha, out float parsedR, out float parsedG, out float parsedB, out float parsedA))
                {
                    r = parsedR;
                    g = parsedG;
                    b = parsedB;
                    a = showAlpha ? parsedA : 1.0f;
                    changed = true;
                }
            }

            if (changed)
                _colorHexInputState[hexKey] = FormatHexColor(r, g, b, a, showAlpha);

            if (ImGui.Button("Close##" + popupId))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
            return changed;
        }

        private static float Clamp01(float value)
        {
            if (value < 0.0f)
                return 0.0f;
            if (value > 1.0f)
                return 1.0f;
            return value;
        }

        private static uint UnitToByte(float value)
        {
            float clamped = Clamp01(value);
            return (uint)Math.Round(clamped * 255.0f, MidpointRounding.AwayFromZero);
        }

        private static float ByteToUnit(uint value)
        {
            if (value > 255u)
                value = 255u;
            return value / 255.0f;
        }

        private static string FormatHexColor(float r, float g, float b, float a, bool showAlpha)
        {
            string value = UnitToByte(r).ToString("X2", CultureInfo.InvariantCulture)
                + UnitToByte(g).ToString("X2", CultureInfo.InvariantCulture)
                + UnitToByte(b).ToString("X2", CultureInfo.InvariantCulture);

            if (showAlpha)
                value += UnitToByte(a).ToString("X2", CultureInfo.InvariantCulture);

            return value;
        }

        private static bool TryParseHexColor(string input,
                                             bool showAlpha,
                                             out float r,
                                             out float g,
                                             out float b,
                                             out float a)
        {
            r = 0.0f;
            g = 0.0f;
            b = 0.0f;
            a = 1.0f;

            if (string.IsNullOrWhiteSpace(input))
                return false;

            string trimmed = input.Trim();
            if (trimmed.StartsWith("#", StringComparison.Ordinal))
                trimmed = trimmed.Substring(1);

            if (trimmed.Length != 6 && trimmed.Length != 8)
                return false;

            if (!uint.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint raw))
                return false;

            if (trimmed.Length == 6)
            {
                uint rr = (raw >> 16) & 0xFFu;
                uint gg = (raw >> 8) & 0xFFu;
                uint bb = raw & 0xFFu;

                r = ByteToUnit(rr);
                g = ByteToUnit(gg);
                b = ByteToUnit(bb);
                a = 1.0f;
                return true;
            }

            uint rgba = raw;
            r = ByteToUnit((rgba >> 24) & 0xFFu);
            g = ByteToUnit((rgba >> 16) & 0xFFu);
            b = ByteToUnit((rgba >> 8) & 0xFFu);
            a = showAlpha ? ByteToUnit(rgba & 0xFFu) : 1.0f;
            return true;
        }

        public static bool ScriptType(string label, ref string value, string[] registeredTypes)
        {
            bool changed = false;
            string current = value ?? string.Empty;
            string updated = ImGui.InputText(label, current);
            if (updated != current)
            {
                value = updated;
                current = updated;
                changed = true;
            }

            string popupId = "ScriptTypeSelector##" + label;

            ImGui.SameLine();
            if (ImGui.Button("Select##" + label))
                ImGui.OpenPopup(popupId);

            if (ImGui.BeginPopupModal(popupId))
            {
                ImGui.Text("Registered C# script classes");
                ImGui.Separator();

                if (registeredTypes == null || registeredTypes.Length == 0)
                {
                    ImGui.Text("No script classes found.");
                }
                else
                {
                    for (int i = 0; i < registeredTypes.Length; ++i)
                    {
                        string typeName = registeredTypes[i];
                        bool selected = string.Equals(typeName, current, StringComparison.Ordinal);
                        if (ImGui.Selectable(typeName + "##ScriptTypeOption" + i, selected))
                        {
                            if (!string.Equals(value, typeName, StringComparison.Ordinal))
                            {
                                value = typeName;
                                changed = true;
                            }

                            ImGui.CloseCurrentPopup();
                            break;
                        }
                    }
                }

                ImGui.Separator();
                if (ImGui.Button("Close##" + popupId))
                    ImGui.CloseCurrentPopup();

                ImGui.EndPopup();
            }

            return changed;
        }
    }
}
