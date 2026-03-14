using Engine;
using System;
using System.IO;

namespace EngineEditor
{
    internal static class DockspaceManager
    {
        private static string _layoutDirectory = string.Empty;
        private static bool _runtimeDockspaceEnabled;

        private const string ActiveLayoutFileName = "imgui.ini";

        public static void Initialize(string editorConfigDir)
        {
            string root = string.IsNullOrWhiteSpace(editorConfigDir)
                ? Directory.GetCurrentDirectory()
                : editorConfigDir;

            _layoutDirectory = Path.Combine(root, "layouts");
            Directory.CreateDirectory(_layoutDirectory);
        }

        public static void CreateMainDockspace()
        {
            SetEnabled(true);
            SetWindowBackgroundVisible(true);
            _runtimeDockspaceEnabled = true;
        }

        public static void EnableRuntimeDockspace()
        {
            if (_runtimeDockspaceEnabled)
                return;

            CreateMainDockspace();
        }

        public static void DisableRuntimeDockspace()
        {
            if (!_runtimeDockspaceEnabled)
                return;

            DisableDockspace();
            SetWindowBackgroundVisible(false);
            _runtimeDockspaceEnabled = false;
        }

        public static void SetEnabled(bool enabled)
        {
            EditorBridge.SetDockspaceEnabled(enabled);
        }

        public static void DisableDockspace()
        {
            SetEnabled(false);
            _runtimeDockspaceEnabled = false;
        }

        public static void SetWindowBackgroundVisible(bool visible)
        {
            EditorBridge.SetWindowBackgroundVisible(visible);
        }

        public static bool SaveLayout(string layoutName, out string message)
        {
            message = string.Empty;
            string name = NormalizeLayoutName(layoutName);
            if (string.IsNullOrEmpty(name))
            {
                message = "Layout save failed: invalid layout name.";
                return false;
            }

            try
            {
                string sourcePath = GetActiveLayoutPath();
                if (!File.Exists(sourcePath))
                {
                    message = "Layout save failed: active imgui.ini not found.";
                    return false;
                }

                string targetPath = GetStoredLayoutPath(name);
                Directory.CreateDirectory(_layoutDirectory);
                File.Copy(sourcePath, targetPath, true);
                message = "Saved dock layout: " + name;
                return true;
            }
            catch (Exception ex)
            {
                message = "Layout save failed: " + ex.Message;
                return false;
            }
        }

        public static bool LoadLayout(string layoutName, out string message)
        {
            message = string.Empty;
            string name = NormalizeLayoutName(layoutName);
            if (string.IsNullOrEmpty(name))
            {
                message = "Layout load failed: invalid layout name.";
                return false;
            }

            try
            {
                string sourcePath = GetStoredLayoutPath(name);
                if (!File.Exists(sourcePath))
                {
                    message = "Layout load failed: layout does not exist (" + name + ").";
                    return false;
                }

                string targetPath = GetActiveLayoutPath();
                File.Copy(sourcePath, targetPath, true);
                message = "Loaded dock layout: " + name + " (applies fully on next startup).";
                return true;
            }
            catch (Exception ex)
            {
                message = "Layout load failed: " + ex.Message;
                return false;
            }
        }

        public static bool ResetLayout(out string message)
        {
            message = string.Empty;
            try
            {
                string defaultLayoutPath = GetStoredLayoutPath("default");
                string activeLayoutPath = GetActiveLayoutPath();

                if (File.Exists(defaultLayoutPath))
                {
                    File.Copy(defaultLayoutPath, activeLayoutPath, true);
                    message = "Dock layout reset to default (applies fully on next startup).";
                    return true;
                }

                if (File.Exists(activeLayoutPath))
                    File.Delete(activeLayoutPath);

                message = "Dock layout reset: active imgui.ini removed.";
                return true;
            }
            catch (Exception ex)
            {
                message = "Layout reset failed: " + ex.Message;
                return false;
            }
        }

        private static string NormalizeLayoutName(string layoutName)
        {
            string trimmed = (layoutName ?? string.Empty).Trim();
            if (trimmed.Length == 0)
                return string.Empty;

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                if (trimmed.IndexOf(c) >= 0)
                    return string.Empty;
            }

            return trimmed;
        }

        private static string GetActiveLayoutPath()
        {
            return Path.Combine(Directory.GetCurrentDirectory(), ActiveLayoutFileName);
        }

        private static string GetStoredLayoutPath(string layoutName)
        {
            return Path.Combine(_layoutDirectory, layoutName + ".ini");
        }
    }
}
