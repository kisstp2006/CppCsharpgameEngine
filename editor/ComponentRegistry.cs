using System;
using System.Collections.Generic;

using Engine;

namespace EngineEditor
{
    internal sealed class ComponentRegistryEntry
    {
        public string Name;
        public string Category;
        public int ComponentType;
        public bool CanRemove;
        public bool CanCreate;
        public Action<uint> DrawInspector;
    }

    internal static class ComponentRegistry
    {
        private static readonly List<ComponentRegistryEntry> _entries = new List<ComponentRegistryEntry>();
        private static bool _initialized;

        public static IReadOnlyList<ComponentRegistryEntry> Entries => _entries;

        public static void EnsureInitialized()
        {
            if (_initialized)
                return;
            _initialized = true;

            InspectorSystem.RegisterBuiltInComponents();
        }

        public static void Register(string name,
                                    string category,
                                    int componentType,
                                    Action<uint> drawInspector,
                                    bool canRemove,
                                    bool canCreate = true)
        {
            if (string.IsNullOrEmpty(category))
                category = "Others";

            var entry = new ComponentRegistryEntry();
            entry.Name = name;
            entry.Category = category;
            entry.ComponentType = componentType;
            entry.DrawInspector = drawInspector;
            entry.CanRemove = canRemove;
            entry.CanCreate = canCreate;
            _entries.Add(entry);
        }

        public static List<string> GetCategories()
        {
            var categories = new List<string>();
            for (int i = 0; i < _entries.Count; i++)
            {
                bool found = false;
                for (int c = 0; c < categories.Count; c++)
                {
                    if (categories[c] == _entries[i].Category)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                    categories.Add(_entries[i].Category);
            }
            return categories;
        }

        public static List<ComponentRegistryEntry> GetEntriesByCategory(string category)
        {
            var result = new List<ComponentRegistryEntry>();
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Category == category)
                    result.Add(_entries[i]);
            }
            return result;
        }

        public static ComponentRegistryEntry GetEntry(int componentType)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].ComponentType == componentType)
                    return _entries[i];
            }
            return null;
        }
    }
}
