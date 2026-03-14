using System;

namespace EngineEditor
{
    internal enum EditorTool
    {
        None = 0,
        Move = 1,
        Rotate = 2,
        Scale = 3
    }

    internal static class EditorContext
    {
        private static int _selectedEntityId = -1;
        private static string _currentScenePath = string.Empty;
        private static int _playModeState;
        private static string _selectedAssetPath = string.Empty;
        private static EditorTool _activeTool = EditorTool.Move;
        private static int _currentSceneStorageFormat;
        private static bool _gizmoSnapEnabled;
        private static float _gizmoSnapStep = 32.0f;
        private static bool _sceneViewportFocused;
        private static float _previewCameraX;
        private static float _previewCameraY;
        private static float _previewCameraZoom = 1.0f;
        private static bool _previewCameraEnabled;
        private static string _statusMessage = "No project loaded.";
        private static string _activeProjectPath = string.Empty;
        private static bool _showProjectManagerView = true;

        public static event Action SelectionChanged;
        public static event Action SceneChanged;
        public static event Action PlayModeChanged;
        public static event Action AssetSelectionChanged;
        public static event Action ActiveToolChanged;
        public static event Action SceneStorageFormatChanged;
        public static event Action GizmoSettingsChanged;
        public static event Action SceneViewportFocusChanged;
        public static event Action PreviewCameraChanged;
        public static event Action StatusChanged;
        public static event Action ProjectChanged;
        public static event Action ShowProjectManagerViewChanged;

        public static int SelectedEntityId
        {
            get => _selectedEntityId;
            set
            {
                if (_selectedEntityId == value)
                    return;

                _selectedEntityId = value;
                SelectionChanged?.Invoke();
            }
        }

        public static string CurrentScenePath
        {
            get => _currentScenePath;
            set
            {
                string next = value ?? string.Empty;
                if (string.Equals(_currentScenePath, next, StringComparison.Ordinal))
                    return;

                _currentScenePath = next;
                SceneChanged?.Invoke();
            }
        }

        public static int PlayModeState
        {
            get => _playModeState;
            set
            {
                if (_playModeState == value)
                    return;

                _playModeState = value;
                PlayModeChanged?.Invoke();
            }
        }

        public static string SelectedAssetPath
        {
            get => _selectedAssetPath;
            set
            {
                string next = value ?? string.Empty;
                if (string.Equals(_selectedAssetPath, next, StringComparison.Ordinal))
                    return;

                _selectedAssetPath = next;
                AssetSelectionChanged?.Invoke();
            }
        }

        public static EditorTool ActiveTool
        {
            get => _activeTool;
            set
            {
                if (_activeTool == value)
                    return;

                _activeTool = value;
                ActiveToolChanged?.Invoke();
            }
        }

        public static int CurrentSceneStorageFormat
        {
            get => _currentSceneStorageFormat;
            set
            {
                if (_currentSceneStorageFormat == value)
                    return;

                _currentSceneStorageFormat = value;
                SceneStorageFormatChanged?.Invoke();
            }
        }

        public static bool GizmoSnapEnabled
        {
            get => _gizmoSnapEnabled;
            set
            {
                if (_gizmoSnapEnabled == value)
                    return;

                _gizmoSnapEnabled = value;
                GizmoSettingsChanged?.Invoke();
            }
        }

        public static float GizmoSnapStep
        {
            get => _gizmoSnapStep;
            set
            {
                if (Math.Abs(_gizmoSnapStep - value) <= 0.0001f)
                    return;

                _gizmoSnapStep = value;
                GizmoSettingsChanged?.Invoke();
            }
        }

        public static bool SceneViewportFocused
        {
            get => _sceneViewportFocused;
            set
            {
                if (_sceneViewportFocused == value)
                    return;

                _sceneViewportFocused = value;
                SceneViewportFocusChanged?.Invoke();
            }
        }

        public static float PreviewCameraX
        {
            get => _previewCameraX;
            set
            {
                if (Math.Abs(_previewCameraX - value) <= 0.0001f)
                    return;

                _previewCameraX = value;
                PreviewCameraChanged?.Invoke();
            }
        }

        public static float PreviewCameraY
        {
            get => _previewCameraY;
            set
            {
                if (Math.Abs(_previewCameraY - value) <= 0.0001f)
                    return;

                _previewCameraY = value;
                PreviewCameraChanged?.Invoke();
            }
        }

        public static float PreviewCameraZoom
        {
            get => _previewCameraZoom;
            set
            {
                if (Math.Abs(_previewCameraZoom - value) <= 0.0001f)
                    return;

                _previewCameraZoom = value;
                PreviewCameraChanged?.Invoke();
            }
        }

        public static bool PreviewCameraEnabled
        {
            get => _previewCameraEnabled;
            set
            {
                if (_previewCameraEnabled == value)
                    return;

                _previewCameraEnabled = value;
                PreviewCameraChanged?.Invoke();
            }
        }

        public static string StatusMessage
        {
            get => _statusMessage;
            set
            {
                string next = value ?? string.Empty;
                if (string.Equals(_statusMessage, next, StringComparison.Ordinal))
                    return;

                _statusMessage = next;
                StatusChanged?.Invoke();
            }
        }

        public static string ActiveProjectPath
        {
            get => _activeProjectPath;
            set
            {
                string next = value ?? string.Empty;
                if (string.Equals(_activeProjectPath, next, StringComparison.OrdinalIgnoreCase))
                    return;

                _activeProjectPath = next;
                ProjectChanged?.Invoke();
            }
        }

        public static bool ShowProjectManagerView
        {
            get => _showProjectManagerView;
            set
            {
                if (_showProjectManagerView == value)
                    return;

                _showProjectManagerView = value;
                ShowProjectManagerViewChanged?.Invoke();
            }
        }

        public static void ResetForNoProject()
        {
            SelectedEntityId = -1;
            CurrentScenePath = string.Empty;
            CurrentSceneStorageFormat = 0;
            SelectedAssetPath = string.Empty;
            ActiveProjectPath = string.Empty;
            GizmoSnapEnabled = false;
            GizmoSnapStep = 32.0f;
            SceneViewportFocused = false;
            PreviewCameraX = 0.0f;
            PreviewCameraY = 0.0f;
            PreviewCameraZoom = 1.0f;
            PreviewCameraEnabled = false;
            ShowProjectManagerView = true;
            StatusMessage = "No project loaded.";
        }
    }
}
