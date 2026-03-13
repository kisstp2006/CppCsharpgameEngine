using System;
using System.Collections.Generic;

namespace EngineEditor
{
    internal interface IMenuCommand
    {
        bool IsEnabled();
        void Execute();
    }

    internal sealed class DelegateMenuCommand : IMenuCommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _isEnabled;

        public DelegateMenuCommand(Action execute, Func<bool> isEnabled = null)
        {
            _execute = execute;
            _isEnabled = isEnabled;
        }

        public bool IsEnabled()
        {
            return _isEnabled == null || _isEnabled();
        }

        public void Execute()
        {
            _execute?.Invoke();
        }
    }

    internal static class MenuRegistry
    {
        internal sealed class MenuNode
        {
            public string Label;
            public string Path;
            public int Order;
            public IMenuCommand Command;
            public readonly List<MenuNode> Children = new List<MenuNode>();

            public bool HasChildren => Children.Count > 0;
        }

        private sealed class MenuRegistration
        {
            public string Id;
            public string MenuPath;
            public int Order;
            public IMenuCommand Command;
        }

        private static readonly Dictionary<string, MenuRegistration> RegistrationsById = new Dictionary<string, MenuRegistration>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<MenuRegistration> Registrations = new List<MenuRegistration>();

        private static bool _dirty = true;
        private static MenuNode[] _rootNodes = Array.Empty<MenuNode>();

        public static void Register(string id, string menuPath, Action execute, int order = 0)
        {
            Register(id, menuPath, new DelegateMenuCommand(execute), order);
        }

        public static void Register(string id,
                                    string menuPath,
                                    Action execute,
                                    Func<bool> isEnabled,
                                    int order = 0)
        {
            Register(id, menuPath, new DelegateMenuCommand(execute, isEnabled), order);
        }

        public static void Register(string id, string menuPath, IMenuCommand command, int order = 0)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(menuPath) || command == null)
                return;

            string normalizedPath = NormalizePath(menuPath);
            if (string.IsNullOrEmpty(normalizedPath))
                return;

            string normalizedId = id.Trim();

            if (RegistrationsById.TryGetValue(normalizedId, out MenuRegistration existing))
            {
                existing.MenuPath = normalizedPath;
                existing.Order = order;
                existing.Command = command;
                _dirty = true;
                return;
            }

            var registration = new MenuRegistration();
            registration.Id = normalizedId;
            registration.MenuPath = normalizedPath;
            registration.Order = order;
            registration.Command = command;

            RegistrationsById[registration.Id] = registration;
            Registrations.Add(registration);
            _dirty = true;
        }

        public static void Unregister(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return;

            string normalizedId = id.Trim();
            if (!RegistrationsById.TryGetValue(normalizedId, out MenuRegistration existing))
                return;

            RegistrationsById.Remove(normalizedId);
            Registrations.Remove(existing);
            _dirty = true;
        }

        public static void Clear()
        {
            RegistrationsById.Clear();
            Registrations.Clear();
            _rootNodes = Array.Empty<MenuNode>();
            _dirty = true;
        }

        public static MenuNode[] GetRootNodes()
        {
            if (_dirty)
                RebuildTree();

            return _rootNodes;
        }

        private static void RebuildTree()
        {
            var nodesByPath = new Dictionary<string, MenuNode>(StringComparer.OrdinalIgnoreCase);
            var roots = new List<MenuNode>();

            for (int i = 0; i < Registrations.Count; ++i)
            {
                MenuRegistration registration = Registrations[i];
                string[] segments = SplitSegments(registration.MenuPath);
                if (segments.Length == 0)
                    continue;

                MenuNode parentNode = null;
                string currentPath = string.Empty;

                for (int s = 0; s < segments.Length; ++s)
                {
                    string segment = segments[s];
                    currentPath = currentPath.Length == 0 ? segment : currentPath + "/" + segment;

                    if (!nodesByPath.TryGetValue(currentPath, out MenuNode node))
                    {
                        node = new MenuNode();
                        node.Label = segment;
                        node.Path = currentPath;
                        node.Order = registration.Order;

                        nodesByPath[currentPath] = node;
                        if (parentNode == null)
                            roots.Add(node);
                        else
                            parentNode.Children.Add(node);
                    }
                    else if (registration.Order < node.Order)
                    {
                        node.Order = registration.Order;
                    }

                    parentNode = node;
                }

                if (parentNode != null)
                    parentNode.Command = registration.Command;
            }

            SortNodesRecursive(roots);
            _rootNodes = roots.ToArray();
            _dirty = false;
        }

        private static void SortNodesRecursive(List<MenuNode> nodes)
        {
            nodes.Sort(CompareNodes);

            for (int i = 0; i < nodes.Count; ++i)
                SortNodesRecursive(nodes[i].Children);
        }

        private static int CompareNodes(MenuNode lhs, MenuNode rhs)
        {
            int orderCompare = lhs.Order.CompareTo(rhs.Order);
            if (orderCompare != 0)
                return orderCompare;

            return string.Compare(lhs.Label, rhs.Label, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string menuPath)
        {
            string[] segments = SplitSegments(menuPath);
            return string.Join("/", segments);
        }

        private static string[] SplitSegments(string menuPath)
        {
            if (string.IsNullOrWhiteSpace(menuPath))
                return Array.Empty<string>();

            string[] rawSegments = menuPath.Split('/');
            var cleaned = new List<string>(rawSegments.Length);

            for (int i = 0; i < rawSegments.Length; ++i)
            {
                string segment = rawSegments[i].Trim();
                if (segment.Length == 0)
                    continue;

                cleaned.Add(segment);
            }

            return cleaned.ToArray();
        }
    }
}