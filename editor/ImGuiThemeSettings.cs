using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Engine;

namespace EngineEditor
{
    internal static class ImGuiThemeSettings
    {
        // Matches ImGuiCol_ enum in imgui.h (this ImGui version).
        private const int ColText = 0;
        private const int ColTextDisabled = 1;
        private const int ColWindowBg = 2;
        private const int ColChildBg = 3;
        private const int ColPopupBg = 4;
        private const int ColBorder = 5;
        private const int ColFrameBg = 7;
        private const int ColFrameBgHovered = 8;
        private const int ColFrameBgActive = 9;
        private const int ColTitleBg = 10;
        private const int ColTitleBgActive = 11;
        private const int ColMenuBarBg = 13;
        private const int ColScrollbarBg = 14;
        private const int ColScrollbarGrab = 15;
        private const int ColButton = 21;
        private const int ColButtonHovered = 22;
        private const int ColButtonActive = 23;
        private const int ColHeader = 24;
        private const int ColHeaderHovered = 25;
        private const int ColHeaderActive = 26;
        private const int ColSeparator = 27;
        private const int ColTab = 33;
        private const int ColTabHovered = 34;
        private const int ColTabActive = 35;
        private const int ColTabUnfocused = 36;
        private const int ColTabUnfocusedActive = 37;
        private const int ColDockingPreview = 38;

        private struct ColorEntry
        {
            public string Key;
            public string Label;
            public int ColIdx;
            public float R, G, B, A;
        }

        private static ColorEntry[] _entries;
        private static ColorEntry[] _defaults;
        private static string _settingsPath = string.Empty;

        public static void Initialize(string editorConfigDir)
        {
            _settingsPath = Path.Combine(editorConfigDir, "imgui_theme.settings");

            BuildEntries();
            LoadFromNative();

            // Snapshot the engine defaults before overriding with saved values.
            _defaults = (ColorEntry[])_entries.Clone();

            TryLoadFromDisk();
            ApplyAll();
        }

        public static void RegisterEditorOptions()
        {
            EditorOptionsRegistry.Register(
                "appearance.imgui_theme",
                "Appearance",
                "UI Colors",
                DrawOptionsPanel,
                order: 0);
        }

        // ------------------------------------------------------------------ build

        private static void BuildEntries()
        {
            _entries = new ColorEntry[]
            {
                // Backgrounds
                Make("WindowBg",           "Window Background",      ColWindowBg),
                Make("ChildBg",            "Child Background",       ColChildBg),
                Make("PopupBg",            "Popup Background",       ColPopupBg),
                // Frames
                Make("FrameBg",            "Frame Background",       ColFrameBg),
                Make("FrameBgHovered",     "Frame Bg Hovered",       ColFrameBgHovered),
                Make("FrameBgActive",      "Frame Bg Active",        ColFrameBgActive),
                // Title / Menu
                Make("TitleBg",            "Title Bar",              ColTitleBg),
                Make("TitleBgActive",      "Title Bar (Active)",     ColTitleBgActive),
                Make("MenuBarBg",          "Menu Bar",               ColMenuBarBg),
                // Scrollbar
                Make("ScrollbarBg",        "Scrollbar Background",   ColScrollbarBg),
                Make("ScrollbarGrab",      "Scrollbar Grab",         ColScrollbarGrab),
                // Buttons
                Make("Button",             "Button",                 ColButton),
                Make("ButtonHovered",      "Button Hovered",         ColButtonHovered),
                Make("ButtonActive",       "Button Active",          ColButtonActive),
                // Headers / Selectables
                Make("Header",             "Header",                 ColHeader),
                Make("HeaderHovered",      "Header Hovered",         ColHeaderHovered),
                Make("HeaderActive",       "Header Active",          ColHeaderActive),
                // Tabs
                Make("Tab",                "Tab",                    ColTab),
                Make("TabHovered",         "Tab Hovered",            ColTabHovered),
                Make("TabActive",          "Tab Active",             ColTabActive),
                Make("TabUnfocused",       "Tab Unfocused",          ColTabUnfocused),
                Make("TabUnfocusedActive", "Tab Unfocused Active",   ColTabUnfocusedActive),
                // Text / Other
                Make("Text",               "Text",                   ColText),
                Make("TextDisabled",       "Text (Disabled)",        ColTextDisabled),
                Make("Border",             "Border",                 ColBorder),
                Make("Separator",          "Separator",              ColSeparator),
                Make("DockingPreview",     "Docking Preview",        ColDockingPreview),
            };
        }

        private static ColorEntry Make(string key, string label, int idx)
        {
            ColorEntry e;
            e.Key = key;
            e.Label = label;
            e.ColIdx = idx;
            e.R = e.G = e.B = e.A = 1.0f;
            return e;
        }

        // ------------------------------------------------------------------ load / save

        private static void LoadFromNative()
        {
            if (_entries == null)
                return;

            for (int i = 0; i < _entries.Length; i++)
            {
                float r, g, b, a;
                ImGui.GetStyleColor(_entries[i].ColIdx, out r, out g, out b, out a);
                _entries[i].R = r;
                _entries[i].G = g;
                _entries[i].B = b;
                _entries[i].A = a;
            }
        }

        private static void TryLoadFromDisk()
        {
            if (string.IsNullOrEmpty(_settingsPath) || !File.Exists(_settingsPath))
                return;

            try
            {
                string[] lines = File.ReadAllLines(_settingsPath);
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string line in lines)
                {
                    int eq = line.IndexOf('=');
                    if (eq < 1)
                        continue;
                    map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }

                for (int i = 0; i < _entries.Length; i++)
                {
                    string val;
                    if (!map.TryGetValue(_entries[i].Key, out val))
                        continue;

                    float r, g, b, a;
                    if (TryParseRgba(val, out r, out g, out b, out a))
                    {
                        _entries[i].R = r;
                        _entries[i].G = g;
                        _entries[i].B = b;
                        _entries[i].A = a;
                    }
                }
            }
            catch { }
        }

        private static void SaveToDisk()
        {
            if (string.IsNullOrEmpty(_settingsPath))
                return;

            try
            {
                var lines = new string[_entries.Length];
                for (int i = 0; i < _entries.Length; i++)
                    lines[i] = _entries[i].Key + "=" + FormatRgba(_entries[i].R, _entries[i].G, _entries[i].B, _entries[i].A);
                File.WriteAllLines(_settingsPath, lines);
            }
            catch { }
        }

        private static void ApplyAll()
        {
            if (_entries == null)
                return;

            for (int i = 0; i < _entries.Length; i++)
                ImGui.SetStyleColor(_entries[i].ColIdx, _entries[i].R, _entries[i].G, _entries[i].B, _entries[i].A);
        }

        // ------------------------------------------------------------------ draw

        private static void DrawOptionsPanel()
        {
            if (_entries == null)
                return;

            if (ImGui.Button("Reset to Default"))
            {
                if (_defaults != null)
                {
                    for (int i = 0; i < _entries.Length && i < _defaults.Length; i++)
                    {
                        _entries[i].R = _defaults[i].R;
                        _entries[i].G = _defaults[i].G;
                        _entries[i].B = _defaults[i].B;
                        _entries[i].A = _defaults[i].A;
                    }
                }
                ApplyAll();
                SaveToDisk();
            }

            ImGui.SameLine();

            if (ImGui.Button("Save"))
                SaveToDisk();

            ImGui.Separator();

            DrawColorGroup("Backgrounds", new[] { "WindowBg", "ChildBg", "PopupBg" });
            DrawColorGroup("Frames", new[] { "FrameBg", "FrameBgHovered", "FrameBgActive" });
            DrawColorGroup("Title / Menu Bar", new[] { "TitleBg", "TitleBgActive", "MenuBarBg" });
            DrawColorGroup("Scrollbar", new[] { "ScrollbarBg", "ScrollbarGrab" });
            DrawColorGroup("Buttons", new[] { "Button", "ButtonHovered", "ButtonActive" });
            DrawColorGroup("Headers", new[] { "Header", "HeaderHovered", "HeaderActive" });
            DrawColorGroup("Tabs", new[] { "Tab", "TabHovered", "TabActive", "TabUnfocused", "TabUnfocusedActive" });
            DrawColorGroup("Text / Other", new[] { "Text", "TextDisabled", "Border", "Separator", "DockingPreview" });
        }

        private static void DrawColorGroup(string groupName, string[] keys)
        {
            EditorUIHelpers.DrawSectionHeader(groupName);

            foreach (string key in keys)
            {
                int idx = FindEntry(key);
                if (idx >= 0)
                    DrawColorRow(idx);
            }
        }

        private static void DrawColorRow(int idx)
        {
            ColorEntry e = _entries[idx];
            string popupId = "##ThemePick_" + e.Key;

            if (ImGui.ColorButton("##cbtn_" + e.Key, e.R, e.G, e.B, 1.0f))
                ImGui.OpenPopup(popupId);

            ImGui.SameLine();
            ImGui.Text(e.Label);

            if (ImGui.BeginPopup(popupId))
            {
                float r = e.R, g = e.G, b = e.B, a = e.A;
                if (ImGui.ColorPicker4(e.Label, ref r, ref g, ref b, ref a, false))
                {
                    _entries[idx].R = r;
                    _entries[idx].G = g;
                    _entries[idx].B = b;
                    _entries[idx].A = a;
                    ImGui.SetStyleColor(e.ColIdx, r, g, b, a);
                }
                ImGui.EndPopup();
            }
        }

        // ------------------------------------------------------------------ helpers

        private static int FindEntry(string key)
        {
            for (int i = 0; i < _entries.Length; i++)
                if (string.Equals(_entries[i].Key, key, StringComparison.Ordinal))
                    return i;
            return -1;
        }

        private static bool TryParseRgba(string s, out float r, out float g, out float b, out float a)
        {
            r = g = b = a = 1.0f;
            if (string.IsNullOrEmpty(s))
                return false;

            string[] parts = s.Split(',');
            if (parts.Length < 4)
                return false;

            return float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out r)
                && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out g)
                && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out b)
                && float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out a);
        }

        private static string FormatRgba(float r, float g, float b, float a)
        {
            return r.ToString("F4", CultureInfo.InvariantCulture) + ","
                 + g.ToString("F4", CultureInfo.InvariantCulture) + ","
                 + b.ToString("F4", CultureInfo.InvariantCulture) + ","
                 + a.ToString("F4", CultureInfo.InvariantCulture);
        }
    }
}
