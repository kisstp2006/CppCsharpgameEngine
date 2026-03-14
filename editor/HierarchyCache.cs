using Engine;
using System.Collections.Generic;

namespace EngineEditor
{
    /// <summary>
    /// Per-frame cache of the entity hierarchy tree.
    /// Avoids O(n) child lookups on every tree node draw call.
    /// Call Rebuild() once per frame before rendering.
    /// </summary>
    internal sealed class HierarchyCache
    {
        private readonly List<uint> _roots = new List<uint>(64);
        private readonly Dictionary<uint, List<uint>> _childrenOf = new Dictionary<uint, List<uint>>(128);
        private readonly Dictionary<uint, uint> _parentOf = new Dictionary<uint, uint>(128);
        private readonly Dictionary<uint, string> _nameOf = new Dictionary<uint, string>(128);
        private readonly HashSet<uint> _allEntities = new HashSet<uint>();

        private int _lastEntityCount = -1;
        private bool _dirty = true;

        public IReadOnlyList<uint> Roots => _roots;
        public int TotalEntityCount => _allEntities.Count;

        public void Invalidate()
        {
            _dirty = true;
        }

        public void Rebuild()
        {
            int currentCount = EntityManager.GetEntityCount();
            if (!_dirty && currentCount == _lastEntityCount)
                return;

            _roots.Clear();
            _childrenOf.Clear();
            _parentOf.Clear();
            _nameOf.Clear();
            _allEntities.Clear();
            _lastEntityCount = currentCount;
            _dirty = false;

            int rootCount = EditorBridge.GetRootEntityCount();
            for (int i = 0; i < rootCount; i++)
            {
                uint rootId = EditorBridge.GetRootEntityAt(i);
                if (!EntityManager.IsEntityValid(rootId))
                    continue;

                _roots.Add(rootId);
                CacheSubtree(rootId, 0);
            }
        }

        private void CacheSubtree(uint entityId, uint parentId)
        {
            _allEntities.Add(entityId);

            string name = EntityManager.GetEntityName(entityId);
            if (string.IsNullOrWhiteSpace(name))
                name = "Entity " + entityId;
            _nameOf[entityId] = name;

            if (parentId != 0)
                _parentOf[entityId] = parentId;

            int childCount = EditorBridge.GetChildEntityCount(entityId);
            if (childCount <= 0)
                return;

            var children = new List<uint>(childCount);
            for (int i = 0; i < childCount; i++)
            {
                uint childId = EditorBridge.GetChildEntityAt(entityId, i);
                if (!EntityManager.IsEntityValid(childId))
                    continue;
                children.Add(childId);
            }

            if (children.Count > 0)
            {
                _childrenOf[entityId] = children;
                for (int i = 0; i < children.Count; i++)
                    CacheSubtree(children[i], entityId);
            }
        }

        public IReadOnlyList<uint> GetChildren(uint entityId)
        {
            List<uint> children;
            if (_childrenOf.TryGetValue(entityId, out children))
                return children;
            return System.Array.Empty<uint>();
        }

        public bool HasChildren(uint entityId)
        {
            return _childrenOf.ContainsKey(entityId);
        }

        public uint GetParent(uint entityId)
        {
            uint parent;
            return _parentOf.TryGetValue(entityId, out parent) ? parent : 0;
        }

        public string GetName(uint entityId)
        {
            string name;
            return _nameOf.TryGetValue(entityId, out name) ? name : "Entity " + entityId;
        }

        public bool IsValid(uint entityId)
        {
            return _allEntities.Contains(entityId);
        }

        /// <summary>
        /// Build a flat ordered list of all visible entity IDs as they appear in
        /// the tree (respecting expand state). Used for Shift+Click range selection.
        /// </summary>
        public List<uint> BuildFlatVisibleOrder(Dictionary<uint, bool> expandedStates)
        {
            var result = new List<uint>(_allEntities.Count);
            for (int i = 0; i < _roots.Count; i++)
                FlattenSubtree(_roots[i], expandedStates, result);
            return result;
        }

        private void FlattenSubtree(uint entityId, Dictionary<uint, bool> expandedStates, List<uint> result)
        {
            result.Add(entityId);
            bool expanded;
            if (!expandedStates.TryGetValue(entityId, out expanded))
                expanded = false;
            if (!expanded || !HasChildren(entityId))
                return;

            var children = GetChildren(entityId);
            for (int i = 0; i < children.Count; i++)
                FlattenSubtree(children[i], expandedStates, result);
        }

        /// <summary>
        /// Collect all ancestor entity IDs of the given set (for search filter display).
        /// </summary>
        public void CollectAncestors(HashSet<uint> entities, HashSet<uint> result)
        {
            foreach (uint entityId in entities)
            {
                uint current = GetParent(entityId);
                while (current != 0)
                {
                    if (!result.Add(current))
                        break;
                    current = GetParent(current);
                }
            }
        }
    }
}
