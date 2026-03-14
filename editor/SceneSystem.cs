using System;
using System.IO;

using Engine;

namespace EngineEditor
{
    internal static class SceneSystem
    {
        private static string _activeScenePath
        {
            get => EditorContext.CurrentScenePath;
            set => EditorContext.CurrentScenePath = value ?? string.Empty;
        }

        private static int _activeSceneStorageFormat
        {
            get => EditorContext.CurrentSceneStorageFormat;
            set => EditorContext.CurrentSceneStorageFormat = value;
        }
        private static bool _assetContextMenuRegistered;

        public const int SceneStorageJson = 0;
        public const int SceneStorageBinary = 1;

        public static string ActiveScenePath => _activeScenePath;

        public static void RegisterAssetContextMenu()
        {
            if (_assetContextMenuRegistered)
                return;

            _assetContextMenuRegistered = true;
            AssetPanelContextMenuRegistry.Register("sceneeditor.createScene",
                                                   "Create/Scene",
                                                   _ => AssetBrowserSystem.RequestOpenCreateScenePopup(),
                                                   10);
        }

        public static void NewScene()
        {
            EditorBridge.NewScene();
            _activeScenePath = string.Empty;
            _activeSceneStorageFormat = SceneStorageJson;
            NotifySceneChanged();

            string status = EditorBridge.GetLastSceneIoStatus();
            if (string.IsNullOrEmpty(status))
                status = "New scene created.";

            ProjectOperations.SetStatusMessage(status);
        }

        public static bool CreateSceneAsset(string sceneName, int storageFormat)
        {
            string trimmedName = (sceneName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmedName))
                trimmedName = "Scene";

            if (!StringUtilities.IsValidProjectName(trimmedName))
            {
                ProjectOperations.SetStatusMessage("Create scene failed: invalid scene name.");
                return false;
            }

            string extension = storageFormat == SceneStorageBinary ? ".scene.bin" : ".scene.json";
            string scenesRoot = GetDefaultScenesRootPath();

            string candidatePath = Path.Combine(scenesRoot, trimmedName + extension);
            if (File.Exists(candidatePath))
            {
                int suffix = 1;
                while (true)
                {
                    string nextCandidate = Path.Combine(scenesRoot, trimmedName + "_" + suffix + extension);
                    if (!File.Exists(nextCandidate))
                    {
                        candidatePath = nextCandidate;
                        break;
                    }

                    ++suffix;
                }
            }

            EditorBridge.NewScene();
            if (!EditorBridge.SaveScene(candidatePath, storageFormat))
            {
                string error = EditorBridge.GetLastSceneIoStatus();
                if (string.IsNullOrEmpty(error))
                    error = "Create scene failed.";

                ProjectOperations.SetStatusMessage(error);
                return false;
            }

            _activeScenePath = Path.GetFullPath(candidatePath);
            _activeSceneStorageFormat = storageFormat;
            NotifySceneChanged();

            ProjectOperations.SetStatusMessage("Created scene: " + Path.GetFileName(_activeScenePath));
            return true;
        }

        public static bool LoadSceneFromPath(string scenePath)
        {
            if (string.IsNullOrWhiteSpace(scenePath))
            {
                ProjectOperations.SetStatusMessage("Load failed: scene path is empty.");
                return false;
            }

            string resolvedPath = Path.GetFullPath(scenePath);
            int format = DetectStorageFormatFromPath(resolvedPath);
            if (format < 0)
            {
                ProjectOperations.SetStatusMessage("Load failed: use .scene.json or .scene.bin file.");
                return false;
            }

            if (EditorBridge.LoadScene(resolvedPath, format))
            {
                _activeScenePath = resolvedPath;
                _activeSceneStorageFormat = format;
                NotifySceneChanged();
                ProjectOperations.SetStatusMessage("Loaded scene: " + Path.GetFileName(resolvedPath));
                return true;
            }

            string error = EditorBridge.GetLastSceneIoStatus();
            if (string.IsNullOrEmpty(error))
                error = "Load scene failed.";

            ProjectOperations.SetStatusMessage(error);
            return false;
        }

        public static void LoadSceneFromPicker()
        {
            string initialPath = _activeScenePath;
            if (string.IsNullOrEmpty(initialPath))
                initialPath = GetDefaultScenesRootPath();

            string picked = Explorer.PickFile("Load scene (.scene.json or .scene.bin)", initialPath);
            if (string.IsNullOrWhiteSpace(picked))
                return;

            LoadSceneFromPath(picked);
        }

        public static bool RestoreSceneFromPlaySnapshot(string snapshotPath)
        {
            if (string.IsNullOrWhiteSpace(snapshotPath) || !File.Exists(snapshotPath))
            {
                ProjectOperations.SetStatusMessage("Stop failed: play snapshot missing.");
                return false;
            }

            if (!EditorBridge.LoadScene(snapshotPath, SceneStorageBinary))
            {
                string error = EditorBridge.GetLastSceneIoStatus();
                if (string.IsNullOrEmpty(error))
                    error = "Stop failed: could not restore play snapshot.";

                ProjectOperations.SetStatusMessage(error);
                return false;
            }

            NotifySceneChanged();
            return true;
        }

        public static bool SaveScene()
        {
            if (string.IsNullOrWhiteSpace(_activeScenePath))
            {
                ProjectOperations.SetStatusMessage("Save failed: no active scene. Create or load one from Asset Panel.");
                return false;
            }

            string resolvedPath = Path.GetFullPath(_activeScenePath);
            if (!EditorBridge.SaveScene(resolvedPath, _activeSceneStorageFormat))
            {
                string error = EditorBridge.GetLastSceneIoStatus();
                if (string.IsNullOrEmpty(error))
                    error = "Save scene failed.";

                ProjectOperations.SetStatusMessage(error);
                return false;
            }

            ProjectOperations.SetStatusMessage("Saved scene: " + Path.GetFileName(resolvedPath));
            return true;
        }

        public static bool IsSceneFilePath(string path)
        {
            return DetectStorageFormatFromPath(path) >= 0;
        }

        public static void SaveSceneAsJson()
        {
            SaveSceneForFormat(SceneStorageJson);
        }

        public static void SaveSceneAsBinary()
        {
            SaveSceneForFormat(SceneStorageBinary);
        }

        private static void SaveSceneForFormat(int format)
        {
            string targetPath = ResolveSavePath(format);
            if (string.IsNullOrEmpty(targetPath))
            {
                ProjectOperations.SetStatusMessage("Save failed: invalid scene path.");
                return;
            }

            if (EditorBridge.SaveScene(targetPath, format))
            {
                _activeScenePath = targetPath;
                _activeSceneStorageFormat = format;
                ProjectOperations.SetStatusMessage("Saved scene: " + Path.GetFileName(targetPath));
                return;
            }

            string error = EditorBridge.GetLastSceneIoStatus();
            if (string.IsNullOrEmpty(error))
                error = "Save scene failed.";

            ProjectOperations.SetStatusMessage(error);
        }

        private static string ResolveSavePath(int format)
        {
            string activePath = _activeScenePath;
            if (!string.IsNullOrWhiteSpace(activePath))
            {
                int activeFormat = DetectStorageFormatFromPath(activePath);
                if (activeFormat == format)
                    return Path.GetFullPath(activePath);
            }

            string scenesRoot = GetDefaultScenesRootPath();
            string baseName = "Scene";

            if (!string.IsNullOrWhiteSpace(activePath))
            {
                string activeName = Path.GetFileName(activePath);
                if (!string.IsNullOrEmpty(activeName))
                {
                    if (activeName.EndsWith(".scene.json", StringComparison.OrdinalIgnoreCase))
                        baseName = activeName.Substring(0, activeName.Length - ".scene.json".Length);
                    else if (activeName.EndsWith(".scene.bin", StringComparison.OrdinalIgnoreCase))
                        baseName = activeName.Substring(0, activeName.Length - ".scene.bin".Length);
                    else
                        baseName = Path.GetFileNameWithoutExtension(activeName);
                }
            }

            if (string.IsNullOrWhiteSpace(baseName))
                baseName = "Scene";

            string extension = format == SceneStorageBinary ? ".scene.bin" : ".scene.json";
            string candidate = Path.Combine(scenesRoot, baseName + extension);

            if (string.IsNullOrWhiteSpace(_activeScenePath) && File.Exists(candidate))
            {
                int suffix = 1;
                while (true)
                {
                    string nextCandidate = Path.Combine(scenesRoot, baseName + "_" + suffix + extension);
                    if (!File.Exists(nextCandidate))
                        return nextCandidate;

                    ++suffix;
                }
            }

            return candidate;
        }

        private static string GetDefaultScenesRootPath()
        {
            string projectPath = ProjectOperations.ActiveProjectPath;
            if (!string.IsNullOrWhiteSpace(projectPath))
            {
                string scenesRoot = Path.Combine(projectPath, "Scenes");
                Directory.CreateDirectory(scenesRoot);
                return scenesRoot;
            }

            return Directory.GetCurrentDirectory();
        }

        private static int DetectStorageFormatFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return -1;

            string fullPath = Path.GetFullPath(path);
            if (fullPath.EndsWith(".scene.bin", StringComparison.OrdinalIgnoreCase)
                || fullPath.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
            {
                return SceneStorageBinary;
            }

            if (fullPath.EndsWith(".scene.json", StringComparison.OrdinalIgnoreCase)
                || fullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                return SceneStorageJson;
            }

            return -1;
        }

        private static void NotifySceneChanged()
        {
            HierarchySystem.ResetSelection();
            InspectorSystem.ResetTransientState();
        }
    }
}
