using System;
using System.Collections.Generic;

namespace EngineEditor
{
    // Window policy:
    // 1) Global order is Window.Order, then registration sequence as a stable tie-breaker.
    // 2) Visibility transitions trigger OnOpened/OnClosed exactly once per change.
    // 3) Frame execution is two-pass: OnUpdate for all visible windows, then draw pass.
    // 4) Draw pass invokes OnBeforeDraw -> OnGUI -> OnAfterDraw per visible window.
    internal static class EditorWindowManager
    {
        private sealed class WindowRegistration
        {
            public string Id;
            public string MenuLabel;
            public int MenuOrder;
            public int RegistrationSequence;
            public bool WasVisibleLastFrame;
            public EditorWindow Window;
        }

        private static readonly Dictionary<string, WindowRegistration> _windowsById = new Dictionary<string, WindowRegistration>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<WindowRegistration> _orderedWindows = new List<WindowRegistration>();
        private static int _nextRegistrationSequence = 1;

        public static void Clear()
        {
            for (int i = 0; i < _orderedWindows.Count; ++i)
            {
                WindowRegistration registration = _orderedWindows[i];
                if (registration.WasVisibleLastFrame && registration.Window != null)
                    registration.Window.OnClosed();
            }

            _windowsById.Clear();
            _orderedWindows.Clear();
            _nextRegistrationSequence = 1;
        }

        public static void Register(string id,
                                    EditorWindow window,
                                    string menuLabel = null,
                                    int menuOrder = 0)
        {
            if (string.IsNullOrWhiteSpace(id) || window == null)
                return;

            string normalizedId = id.Trim();
            if (!_windowsById.TryGetValue(normalizedId, out WindowRegistration registration))
            {
                registration = new WindowRegistration();
                registration.Id = normalizedId;
                registration.RegistrationSequence = _nextRegistrationSequence++;
                _windowsById[normalizedId] = registration;
            }

            EditorWindow previousWindow = registration.Window;
            if (!ReferenceEquals(previousWindow, window)
                && registration.WasVisibleLastFrame
                && previousWindow != null)
            {
                previousWindow.OnClosed();
                registration.WasVisibleLastFrame = false;
            }

            registration.Window = window;
            registration.MenuLabel = string.IsNullOrWhiteSpace(menuLabel) ? window.Title : menuLabel.Trim();
            registration.MenuOrder = menuOrder;
            RebuildOrder();
        }

        public static bool IsRegistered(string id)
        {
            return !string.IsNullOrWhiteSpace(id) && _windowsById.ContainsKey(id.Trim());
        }

        public static void RegisterWindowMenuItems(string menuRootPath,
                                                   Func<bool> isEnabled,
                                                   int baseOrder = 0)
        {
            string normalizedRoot = string.IsNullOrWhiteSpace(menuRootPath) ? "Window" : menuRootPath.Trim();
            for (int i = 0; i < _orderedWindows.Count; ++i)
            {
                WindowRegistration registration = _orderedWindows[i];
                string menuId = "menu.window.dynamic." + registration.Id;
                string menuPath = normalizedRoot + "/" + registration.MenuLabel;
                int order = baseOrder + registration.MenuOrder;

                string idCapture = registration.Id;
                MenuRegistry.Register(menuId,
                                      menuPath,
                                      () => Toggle(idCapture),
                                      isEnabled,
                                      order);
            }
        }

        public static void Open(string id)
        {
            if (TryGetWindow(id, out WindowRegistration registration))
            {
                EditorWindow window = registration.Window;
                window.IsOpen = true;
            }
        }

        public static void Close(string id)
        {
            if (TryGetWindow(id, out WindowRegistration registration))
            {
                EditorWindow window = registration.Window;
                window.IsOpen = false;
            }
        }

        public static void Toggle(string id)
        {
            if (TryGetWindow(id, out WindowRegistration registration))
            {
                EditorWindow window = registration.Window;
                window.IsOpen = !window.IsOpen;
            }
        }

        public static void DrawAll(float deltaTime)
        {
            // Policy: visibility/lifecycle resolution, then update pass, then draw pass.
            List<WindowRegistration> visibleWindows = new List<WindowRegistration>(_orderedWindows.Count);

            for (int i = 0; i < _orderedWindows.Count; ++i)
            {
                WindowRegistration registration = _orderedWindows[i];
                EditorWindow window = registration.Window;
                if (window == null)
                    continue;

                bool shouldDisplay = window.IsOpen && window.ShouldDisplay();
                if (!shouldDisplay)
                {
                    if (registration.WasVisibleLastFrame)
                    {
                        window.OnClosed();
                        registration.WasVisibleLastFrame = false;
                    }

                    continue;
                }

                if (!registration.WasVisibleLastFrame)
                {
                    window.OnOpened();
                    registration.WasVisibleLastFrame = true;
                }

                visibleWindows.Add(registration);
            }

            for (int i = 0; i < visibleWindows.Count; ++i)
                visibleWindows[i].Window.OnUpdate(deltaTime);

            for (int i = 0; i < visibleWindows.Count; ++i)
            {
                EditorWindow window = visibleWindows[i].Window;
                window.OnBeforeDraw();
                try
                {
                    window.OnGUI();
                }
                finally
                {
                    window.OnAfterDraw();
                }
            }
        }

        private static bool TryGetWindow(string id, out WindowRegistration registration)
        {
            registration = null;
            if (string.IsNullOrWhiteSpace(id))
                return false;

            return _windowsById.TryGetValue(id.Trim(), out registration);
        }

        private static void RebuildOrder()
        {
            _orderedWindows.Clear();
            foreach (KeyValuePair<string, WindowRegistration> pair in _windowsById)
                _orderedWindows.Add(pair.Value);

            _orderedWindows.Sort((lhs, rhs) =>
            {
                int orderCompare = lhs.Window.Order.CompareTo(rhs.Window.Order);
                if (orderCompare != 0)
                    return orderCompare;

                return lhs.RegistrationSequence.CompareTo(rhs.RegistrationSequence);
            });
        }
    }
}
