using System;
using System.Collections.Generic;

namespace EngineEditor
{
    internal static class AssetPanelContextMenuRegistry
    {
        internal sealed class AssetPanelContext
        {
            public string ProjectPath;
            public string SelectedPath;
        }

        internal sealed class MenuNodeEntry
        {
            public string Label;
            public string Path;
            public int Order;
            public bool HasChildren;
            public Action<AssetPanelContext> Action;
        }

        private sealed class ActionEntry
        {
            public string Id;
            public string MenuPath;
            public int Order;
            public Action<AssetPanelContext> Action;
        }

        private static readonly Dictionary<string, ActionEntry> EntriesById = new Dictionary<string, ActionEntry>();
        private static readonly List<ActionEntry> Entries = new List<ActionEntry>();

        public static void Register(string id, string menuPath, Action<AssetPanelContext> action, int order = 0)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(menuPath) || action == null)
                return;

            string normalizedPath = NormalizePath(menuPath);
            if (string.IsNullOrEmpty(normalizedPath))
                return;

            if (EntriesById.TryGetValue(id, out ActionEntry existing))
            {
                existing.MenuPath = normalizedPath;
                existing.Order = order;
                existing.Action = action;
                return;
            }

            var entry = new ActionEntry();
            entry.Id = id.Trim();
            entry.MenuPath = normalizedPath;
            entry.Order = order;
            entry.Action = action;

            EntriesById[entry.Id] = entry;
            Entries.Add(entry);
        }

        public static MenuNodeEntry[] GetMenuEntries(string parentPath)
        {
            string normalizedParent = NormalizePath(parentPath);
            string[] parentSegments = SplitSegments(normalizedParent);
            int parentDepth = parentSegments.Length;

            var map = new Dictionary<string, MenuNodeEntry>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < Entries.Count; ++i)
            {
                ActionEntry entry = Entries[i];
                string[] segments = SplitSegments(entry.MenuPath);
                if (segments.Length <= parentDepth)
                    continue;

                bool isPrefix = true;
                for (int s = 0; s < parentDepth; ++s)
                {
                    if (!string.Equals(segments[s], parentSegments[s], StringComparison.OrdinalIgnoreCase))
                    {
                        isPrefix = false;
                        break;
                    }
                }

                if (!isPrefix)
                    continue;

                string label = segments[parentDepth];
                string childPath = BuildPath(parentSegments, parentDepth + 1, label);
                bool hasChildren = segments.Length > parentDepth + 1;
                bool isLeafAction = segments.Length == parentDepth + 1;

                if (!map.TryGetValue(label, out MenuNodeEntry node))
                {
                    node = new MenuNodeEntry();
                    node.Label = label;
                    node.Path = childPath;
                    node.Order = entry.Order;
                    node.HasChildren = hasChildren;
                    node.Action = isLeafAction ? entry.Action : null;
                    map[label] = node;
                }
                else
                {
                    if (entry.Order < node.Order)
                        node.Order = entry.Order;

                    node.HasChildren = node.HasChildren || hasChildren;
                    if (node.Action == null && isLeafAction)
                        node.Action = entry.Action;
                }
            }

            MenuNodeEntry[] result = new MenuNodeEntry[map.Count];
            int index = 0;
            foreach (KeyValuePair<string, MenuNodeEntry> kv in map)
                result[index++] = kv.Value;

            Array.Sort(result, CompareMenuNodes);
            return result;
        }

        private static int CompareMenuNodes(MenuNodeEntry lhs, MenuNodeEntry rhs)
        {
            int orderCompare = lhs.Order.CompareTo(rhs.Order);
            if (orderCompare != 0)
                return orderCompare;

            return string.Compare(lhs.Label, rhs.Label, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string menuPath)
        {
            if (string.IsNullOrWhiteSpace(menuPath))
                return string.Empty;

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

        private static string BuildPath(string[] parentSegments, int requiredCount, string nextSegment)
        {
            var parts = new List<string>(requiredCount);
            for (int i = 0; i < parentSegments.Length; ++i)
                parts.Add(parentSegments[i]);

            if (parts.Count < requiredCount)
                parts.Add(nextSegment);

            return string.Join("/", parts);
        }
    }
}
