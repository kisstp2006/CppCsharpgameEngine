using System;
using System.Collections.Generic;

namespace EngineEditor
{
    internal static class EditorOptionsRegistry
    {
        internal sealed class OptionEntry
        {
            public string Id;
            public string Category;
            public string Title;
            public int Order;
            public Action Draw;
        }

        private static readonly Dictionary<string, OptionEntry> EntriesById = new Dictionary<string, OptionEntry>();
        private static readonly List<OptionEntry> Entries = new List<OptionEntry>();

        public static void Register(string id, string category, string title, Action draw, int order = 0)
        {
            if (string.IsNullOrWhiteSpace(id) || draw == null)
                return;

            string normalizedCategory = string.IsNullOrWhiteSpace(category) ? "General" : category.Trim();
            string normalizedTitle = string.IsNullOrWhiteSpace(title) ? id.Trim() : title.Trim();

            if (EntriesById.TryGetValue(id, out OptionEntry existing))
            {
                existing.Category = normalizedCategory;
                existing.Title = normalizedTitle;
                existing.Order = order;
                existing.Draw = draw;
                return;
            }

            var entry = new OptionEntry();
            entry.Id = id.Trim();
            entry.Category = normalizedCategory;
            entry.Title = normalizedTitle;
            entry.Order = order;
            entry.Draw = draw;

            EntriesById[entry.Id] = entry;
            Entries.Add(entry);
        }

        public static OptionEntry[] GetEntriesSnapshot()
        {
            OptionEntry[] snapshot = Entries.ToArray();
            Array.Sort(snapshot, CompareEntries);
            return snapshot;
        }

        private static int CompareEntries(OptionEntry lhs, OptionEntry rhs)
        {
            int categoryCompare = string.Compare(lhs.Category, rhs.Category, StringComparison.OrdinalIgnoreCase);
            if (categoryCompare != 0)
                return categoryCompare;

            int orderCompare = lhs.Order.CompareTo(rhs.Order);
            if (orderCompare != 0)
                return orderCompare;

            return string.Compare(lhs.Title, rhs.Title, StringComparison.OrdinalIgnoreCase);
        }
    }
}
