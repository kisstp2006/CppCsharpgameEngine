using Engine;
using System;
using System.Collections.Generic;

namespace EngineEditor
{
    internal sealed class HierarchyWindow : EditorWindow
    {
        // ── Constants ──────────────────────────────────────────────────
        private const string DragDropType = "HIERARCHY_ENTITY";
        private const float ToolbarHeight = 26.0f;
        private const float RowHeight = 22.0f;
        private const float IndentGuideAlpha = 0.18f;

        // ── Colors (RGBA) ──────────────────────────────────────────────
        private static readonly float[] ColSelectionBg = { 0.18f, 0.40f, 0.75f, 0.45f };
        private static readonly float[] ColHoverBg = { 0.30f, 0.30f, 0.30f, 0.30f };
        private static readonly float[] ColDropTarget = { 0.30f, 0.60f, 1.00f, 0.35f };
        private static readonly float[] ColDropInvalid = { 0.80f, 0.20f, 0.20f, 0.35f };
        private static readonly float[] ColIndentGuide = { 0.50f, 0.50f, 0.50f, IndentGuideAlpha };
        private static readonly float[] ColInsertLine = { 0.30f, 0.60f, 1.00f, 0.90f };

        // ── Sub-systems ────────────────────────────────────────────────
        private readonly HierarchyCache _cache = new HierarchyCache();
        private readonly Dictionary<uint, bool> _expandedStates = new Dictionary<uint, bool>();
        private readonly HashSet<uint> _selectedEntities = new HashSet<uint>();
        private uint _lastClickedEntityId;
        private uint _pendingExpandEntityId;

        // ── Search ─────────────────────────────────────────────────────
        private string _searchQuery = string.Empty;
        private readonly HashSet<uint> _searchMatched = new HashSet<uint>();
        private readonly HashSet<uint> _searchVisible = new HashSet<uint>();

        // ── Rename ─────────────────────────────────────────────────────
        private uint _renameEntityId;
        private string _renameValue = string.Empty;

        // ── Window lifecycle ───────────────────────────────────────────
        public override string Title => "Hierarchy";
        public override int Order => 10;

        public override bool ShouldDisplay()
        {
            return ProjectOperations.HasOpenProject() && !EditorContext.ShowProjectManagerView;
        }

        public override void OnOpened()
        {
            _cache.Invalidate();
        }

        public override void OnUpdate(float deltaTime)
        {
            _cache.Rebuild();
        }

        public override void OnGUI()
        {
            DrawPanel();
        }

        public override void OnClosed()
        {
            ResetState();
        }

        // ── Public API (called from SceneEditor / InspectorSystem) ────

        public static HierarchyWindow Instance { get; private set; }

        public HierarchyWindow()
        {
            Instance = this;
        }

        public void ResetSelection()
        {
            _selectedEntities.Clear();
            _lastClickedEntityId = 0;
            SyncPrimarySelection();
        }

        public void ResetState()
        {
            _expandedStates.Clear();
            _selectedEntities.Clear();
            _lastClickedEntityId = 0;
            _renameEntityId = 0;
            _renameValue = string.Empty;
            _searchQuery = string.Empty;
            _searchMatched.Clear();
            _searchVisible.Clear();
            _cache.Invalidate();
            SyncPrimarySelection();
        }

        public bool HasValidSelection()
        {
            if (_selectedEntities.Count == 0)
                return EditorContext.SelectedEntityId >= 0 &&
                       EntityManager.IsEntityValid((uint)EditorContext.SelectedEntityId);

            foreach (uint id in _selectedEntities)
            {
                if (EntityManager.IsEntityValid(id))
                    return true;
            }
            return false;
        }

        public void CreateEntityAndSelect()
        {
            uint created = EntityManager.CreateEntity();
            if (EntityManager.IsEntityValid(created))
            {
                EditorBridge.SetParentEntity(created, 0);
                SelectSingle(created);
                _cache.Invalidate();
            }
        }

        public void DeleteSelectedEntity()
        {
            if (EditorContext.SelectedEntityId < 0)
                return;
            DeleteEntity((uint)EditorContext.SelectedEntityId);
        }

        public void DuplicateSelectedEntity()
        {
            if (EditorContext.SelectedEntityId < 0)
                return;

            uint duplicated = EditorBridge.DuplicateEntity((uint)EditorContext.SelectedEntityId);
            if (EntityManager.IsEntityValid(duplicated))
            {
                SelectSingle(duplicated);
                _cache.Invalidate();
            }
        }

        public void DeleteEntity(uint entityId)
        {
            if (!EntityManager.IsEntityValid(entityId))
                return;

            _selectedEntities.Remove(entityId);
            _expandedStates.Remove(entityId);

            // Also remove children from tracking
            RemoveSubtreeState(entityId);

            EntityManager.DestroyEntity(entityId);
            _cache.Invalidate();

            if (EditorContext.SelectedEntityId >= 0 && (uint)EditorContext.SelectedEntityId == entityId)
                EditorContext.SelectedEntityId = -1;

            SyncPrimarySelection();
        }

        // ── Main Draw ──────────────────────────────────────────────────

        private void DrawPanel()
        {
            if (!ImGui.Begin("Hierarchy"))
            {
                ImGui.End();
                return;
            }

            DrawToolbar();
            DrawSearchBar();

            ImGui.Separator();

            // Scrolling region for tree
            if (ImGui.BeginChild("##HierarchyTreeRegion", 0, 0, false))
            {
                HandleKeyboardShortcuts();
                DrawHierarchyTree();
                DrawEmptySpaceDropTarget();
                DrawEmptySpaceContextMenu();
            }
            ImGui.EndChild();

            DrawRenamePopup();

            ImGui.End();
        }

        // ── Toolbar ────────────────────────────────────────────────────

        private void DrawToolbar()
        {
            if (ImGui.Button("+"))
                ImGui.OpenPopup("##HierarchyCreateMenu");

            DrawCreateMenu();

            ImGui.SameLine();
            if (ImGui.Button("Expand All"))
                SetAllExpanded(true);

            ImGui.SameLine();
            if (ImGui.Button("Collapse All"))
                SetAllExpanded(false);
        }

        // ── Search Bar ─────────────────────────────────────────────────

        private void DrawSearchBar()
        {
            string newQuery = ImGui.InputText("##HierarchySearch", _searchQuery);
            if (newQuery == null) newQuery = string.Empty;

            if (!string.Equals(newQuery, _searchQuery, StringComparison.Ordinal))
            {
                _searchQuery = newQuery;
                RebuildSearchFilter();
            }

            if (_searchQuery.Length > 0)
            {
                ImGui.SameLine();
                if (ImGui.Button("X##ClearSearch"))
                {
                    _searchQuery = string.Empty;
                    _searchMatched.Clear();
                    _searchVisible.Clear();
                }
            }
        }

        private void RebuildSearchFilter()
        {
            _searchMatched.Clear();
            _searchVisible.Clear();

            if (string.IsNullOrWhiteSpace(_searchQuery))
                return;

            string query = _searchQuery.Trim();
            int entityCount = EntityManager.GetEntityCount();
            for (int i = 0; i < entityCount; i++)
            {
                uint entityId = EntityManager.GetEntityIdAtIndex(i);
                if (!EntityManager.IsEntityValid(entityId))
                    continue;

                string name = _cache.GetName(entityId);
                if (name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    _searchMatched.Add(entityId);
            }

            // Add all ancestors of matches so tree structure is preserved
            _searchVisible.UnionWith(_searchMatched);
            _cache.CollectAncestors(_searchMatched, _searchVisible);
        }

        private bool IsSearchActive => _searchQuery.Length > 0;

        private bool IsVisibleInSearch(uint entityId)
        {
            return !IsSearchActive || _searchVisible.Contains(entityId);
        }

        // ── Tree Rendering ─────────────────────────────────────────────

        private void DrawHierarchyTree()
        {
            var roots = _cache.Roots;
            if (roots.Count == 0)
            {
                ImGui.Text("Scene is empty.");
                return;
            }

            // Apply any pending expand
            if (_pendingExpandEntityId != 0)
            {
                _expandedStates[_pendingExpandEntityId] = true;
                _pendingExpandEntityId = 0;
            }

            for (int i = 0; i < roots.Count; i++)
            {
                if (IsVisibleInSearch(roots[i]))
                    DrawEntityNode(roots[i], 0);
            }
        }

        private void DrawEntityNode(uint entityId, int depth)
        {
            string entityName = _cache.GetName(entityId);
            bool hasChildren = _cache.HasChildren(entityId);
            bool isSelected = _selectedEntities.Contains(entityId);
            bool isSearchMatch = IsSearchActive && _searchMatched.Contains(entityId);

            // When searching, auto-expand parents of matches
            if (IsSearchActive && hasChildren)
                _expandedStates[entityId] = true;

            // Compute tree node flags
            int flags = ImGui.TreeNodeFlags_OpenOnArrow
                      | ImGui.TreeNodeFlags_OpenOnDoubleClick
                      | ImGui.TreeNodeFlags_SpanAvailWidth
                      | ImGui.TreeNodeFlags_AllowOverlap;

            if (!hasChildren)
                flags |= ImGui.TreeNodeFlags_Leaf | ImGui.TreeNodeFlags_NoTreePushOnOpen;

            if (isSelected)
                flags |= ImGui.TreeNodeFlags_Selected;

            // Honor explicit expand/collapse state
            bool wantExpanded;
            if (_expandedStates.TryGetValue(entityId, out wantExpanded))
                ImGui.SetNextItemOpen(wantExpanded, ImGui.Cond_Always);

            // Row background for hover/selection (pre-draw)
            float rowY = ImGui.GetCursorScreenPosY();
            float rowX = ImGui.GetCursorScreenPosX();
            float contentWidth = Math.Max(100.0f, ImGui.GetContentRegionAvailX());

            // Draw the tree node
            string label = entityName + "##HierarchyNode" + entityId;
            bool nodeOpen = ImGui.TreeNodeEx(label, flags);

            // Track expanded state from ImGui's own toggle
            if (hasChildren)
                _expandedStates[entityId] = nodeOpen;

            // Draw indent guides
            float itemMinY = ImGui.GetItemRectMinY();
            float itemMaxY = ImGui.GetItemRectMaxY();

            DrawIndentGuides(depth, rowX, itemMinY, itemMaxY);

            // Selection highlight overlay
            if (isSelected)
            {
                ImGui.DrawRectFilled(rowX, itemMinY, contentWidth, itemMaxY - itemMinY,
                    ColSelectionBg[0], ColSelectionBg[1], ColSelectionBg[2], ColSelectionBg[3]);
            }

            // Search match accent (subtle left border)
            if (isSearchMatch)
            {
                ImGui.DrawRectFilled(rowX, itemMinY, 3.0f, itemMaxY - itemMinY,
                    0.90f, 0.70f, 0.10f, 0.80f);
            }

            // Handle selection click
            if (ImGui.IsItemClicked(0))
                HandleSelectionClick(entityId);

            // Handle double-click to focus entity
            // (IsItemClicked only fires once, so use hovered + double-click check)
            if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(0))
            {
                // Single click is handled above; double-click focus could be added
                // when a double-click API is available
            }

            // Context menu per entity
            if (ImGui.BeginPopupContextItem("##HCtx" + entityId, ImGui.PopupFlags_MouseButtonRight))
            {
                DrawEntityContextMenu(entityId, entityName);
                ImGui.EndPopup();
            }

            // Drag source
            HandleDragSource(entityId, entityName);

            // Drop target
            HandleDropTarget(entityId);

            // Draw children if expanded
            if (nodeOpen && hasChildren)
            {
                var children = _cache.GetChildren(entityId);
                for (int i = 0; i < children.Count; i++)
                {
                    if (IsVisibleInSearch(children[i]))
                        DrawEntityNode(children[i], depth + 1);
                }
                ImGui.TreePop();
            }
        }

        // ── Indent Guides ──────────────────────────────────────────────

        private void DrawIndentGuides(int depth, float baseX, float rowMinY, float rowMaxY)
        {
            if (depth <= 0)
                return;

            float indentPerLevel = ImGui.GetTreeNodeToLabelSpacing();
            if (indentPerLevel < 1.0f)
                indentPerLevel = 16.0f;

            for (int i = 1; i <= depth; i++)
            {
                float x = baseX + i * indentPerLevel - indentPerLevel * 0.5f;
                ImGui.DrawLine(x, rowMinY, x, rowMaxY,
                    ColIndentGuide[0], ColIndentGuide[1], ColIndentGuide[2], ColIndentGuide[3],
                    1.0f);
            }
        }

        // ── Selection Logic ────────────────────────────────────────────

        private void HandleSelectionClick(uint entityId)
        {
            bool ctrlHeld = ImGui.IsKeyDown(ImGui.Key_LeftCtrl) || ImGui.IsKeyDown(ImGui.Key_RightCtrl);
            bool shiftHeld = ImGui.IsKeyDown(ImGui.Key_LeftShift) || ImGui.IsKeyDown(ImGui.Key_RightShift);

            if (ctrlHeld)
            {
                // Toggle selection
                if (_selectedEntities.Contains(entityId))
                    _selectedEntities.Remove(entityId);
                else
                    _selectedEntities.Add(entityId);
            }
            else if (shiftHeld && _lastClickedEntityId != 0)
            {
                // Range selection
                var flatOrder = _cache.BuildFlatVisibleOrder(_expandedStates);
                int startIdx = flatOrder.IndexOf(_lastClickedEntityId);
                int endIdx = flatOrder.IndexOf(entityId);
                if (startIdx >= 0 && endIdx >= 0)
                {
                    int lo = Math.Min(startIdx, endIdx);
                    int hi = Math.Max(startIdx, endIdx);
                    _selectedEntities.Clear();
                    for (int i = lo; i <= hi; i++)
                        _selectedEntities.Add(flatOrder[i]);
                }
                else
                {
                    SelectSingle(entityId);
                }
            }
            else
            {
                SelectSingle(entityId);
            }

            _lastClickedEntityId = entityId;
            SyncPrimarySelection();
        }

        private void SelectSingle(uint entityId)
        {
            _selectedEntities.Clear();
            _selectedEntities.Add(entityId);
            _lastClickedEntityId = entityId;
            SyncPrimarySelection();
        }

        private void SyncPrimarySelection()
        {
            if (_selectedEntities.Count == 0)
            {
                EditorContext.SelectedEntityId = -1;
                return;
            }

            // Use last-clicked as primary if in set, otherwise first element
            if (_lastClickedEntityId != 0 && _selectedEntities.Contains(_lastClickedEntityId))
            {
                EditorContext.SelectedEntityId = (int)_lastClickedEntityId;
                return;
            }

            foreach (uint id in _selectedEntities)
            {
                EditorContext.SelectedEntityId = (int)id;
                return;
            }
        }

        // ── Drag & Drop ────────────────────────────────────────────────

        private void HandleDragSource(uint entityId, string entityName)
        {
            if (ImGui.BeginDragDropSource(ImGui.DragDropFlags_SourceNoPreviewTooltip))
            {
                ImGui.SetDragDropPayloadUint(DragDropType, entityId);
                ImGui.Text("Move: " + entityName);
                ImGui.EndDragDropSource();
            }
        }

        private void HandleDropTarget(uint entityId)
        {
            if (!ImGui.BeginDragDropTarget())
                return;

            uint draggedId = ImGui.AcceptDragDropPayloadUint(DragDropType);
            if (draggedId != 0 && draggedId != entityId)
            {
                if (!WouldCreateCycle(draggedId, entityId))
                {
                    if (EditorBridge.SetParentEntity(draggedId, entityId))
                    {
                        _expandedStates[entityId] = true;
                        _cache.Invalidate();
                    }
                    else
                    {
                        ProjectOperations.SetStatusMessage("Reparent failed.");
                    }
                }
                else
                {
                    ProjectOperations.SetStatusMessage("Cannot parent entity under its own descendant.");
                }
            }

            ImGui.EndDragDropTarget();
        }

        private void DrawEmptySpaceDropTarget()
        {
            // Invisible area filling remaining space to accept "unparent" drops
            float remaining = Math.Max(40.0f, ImGui.GetContentRegionAvailX());
            ImGui.Dummy(remaining, Math.Max(40.0f, ImGui.GetContentRegionAvailX()));
            if (ImGui.BeginDragDropTarget())
            {
                uint draggedId = ImGui.AcceptDragDropPayloadUint(DragDropType);
                if (draggedId != 0)
                {
                    if (EditorBridge.SetParentEntity(draggedId, 0))
                        _cache.Invalidate();
                    else
                        ProjectOperations.SetStatusMessage("Move to root failed.");
                }
                ImGui.EndDragDropTarget();
            }
        }

        // ── Context Menus ──────────────────────────────────────────────

        private void DrawEntityContextMenu(uint entityId, string entityName)
        {
            if (ImGui.Selectable("Create Empty Child", false))
            {
                uint created = EntityManager.CreateEntity();
                if (EntityManager.IsEntityValid(created))
                {
                    if (EditorBridge.SetParentEntity(created, entityId))
                    {
                        _expandedStates[entityId] = true;
                        SelectSingle(created);
                        _cache.Invalidate();
                    }
                    else
                    {
                        EntityManager.DestroyEntity(created);
                        ProjectOperations.SetStatusMessage("Create child failed: parent rejected.");
                    }
                }
            }

            if (ImGui.Selectable("Duplicate", false))
            {
                uint duplicated = EditorBridge.DuplicateEntity(entityId);
                if (EntityManager.IsEntityValid(duplicated))
                {
                    SelectSingle(duplicated);
                    _cache.Invalidate();
                }
            }

            if (ImGui.Selectable("Rename", false))
            {
                _renameEntityId = entityId;
                _renameValue = entityName;
                ImGui.OpenPopup("Rename Entity");
            }

            if (ImGui.Selectable("Unparent", false))
            {
                if (_cache.GetParent(entityId) != 0)
                {
                    if (EditorBridge.SetParentEntity(entityId, 0))
                        _cache.Invalidate();
                    else
                        ProjectOperations.SetStatusMessage("Unparent failed.");
                }
            }

            if (ImGui.Selectable("Delete", false))
            {
                // Delete all selected if this entity is in the selection, otherwise just this one
                if (_selectedEntities.Contains(entityId) && _selectedEntities.Count > 1)
                    DeleteAllSelected();
                else
                    DeleteEntity(entityId);
            }

            ImGui.Separator();

            if (ImGui.Selectable("Focus", false))
                FocusEntity(entityId);
        }

        private void DrawEmptySpaceContextMenu()
        {
            if (!ImGui.BeginPopupContextWindow("##HierarchyBgCtx", ImGui.PopupFlags_MouseButtonRight))
                return;

            if (ImGui.Selectable("Create Empty Entity", false))
                CreateEntityAndSelect();

            ImGui.Separator();

            if (ImGui.Selectable("Expand All", false))
                SetAllExpanded(true);

            if (ImGui.Selectable("Collapse All", false))
                SetAllExpanded(false);

            ImGui.EndPopup();
        }

        // ── Rename ─────────────────────────────────────────────────────

        private void DrawRenamePopup()
        {
            if (!ImGui.BeginPopupModal("Rename Entity"))
                return;

            _renameValue = ImGui.InputText("Name", _renameValue ?? string.Empty) ?? string.Empty;

            if (ImGui.Button("Apply"))
            {
                if (_renameEntityId != 0 && EntityManager.IsEntityValid(_renameEntityId))
                {
                    string trimmed = (_renameValue ?? string.Empty).Trim();
                    if (trimmed.Length == 0)
                        trimmed = "Entity " + _renameEntityId;
                    EntityManager.SetEntityName(_renameEntityId, trimmed);
                    _cache.Invalidate();
                }
                _renameEntityId = 0;
                _renameValue = string.Empty;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                _renameEntityId = 0;
                _renameValue = string.Empty;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        // ── Keyboard Shortcuts ─────────────────────────────────────────

        private void HandleKeyboardShortcuts()
        {
            if (!ImGui.IsWindowHovered())
                return;

            // Delete key
            if (ImGui.IsKeyPressed(ImGui.Key_Delete, false))
            {
                if (_selectedEntities.Count > 0)
                    DeleteAllSelected();
                else if (EditorContext.SelectedEntityId >= 0)
                    DeleteEntity((uint)EditorContext.SelectedEntityId);
            }

            // F2 → rename
            if (ImGui.IsKeyPressed(ImGui.Key_F2, false) && EditorContext.SelectedEntityId >= 0)
            {
                uint entityId = (uint)EditorContext.SelectedEntityId;
                _renameEntityId = entityId;
                _renameValue = _cache.GetName(entityId);
                ImGui.OpenPopup("Rename Entity");
            }

            // Ctrl+D → duplicate
            if ((ImGui.IsKeyDown(ImGui.Key_LeftCtrl) || ImGui.IsKeyDown(ImGui.Key_RightCtrl))
                && ImGui.IsKeyPressed(ImGui.Key_D, false))
            {
                if (_selectedEntities.Count > 0)
                    DuplicateAllSelected();
                else if (EditorContext.SelectedEntityId >= 0)
                    DuplicateSelectedEntity();
            }

            // Ctrl+A → select all visible
            if ((ImGui.IsKeyDown(ImGui.Key_LeftCtrl) || ImGui.IsKeyDown(ImGui.Key_RightCtrl))
                && ImGui.IsKeyPressed(ImGui.Key_A, false))
            {
                SelectAll();
            }

            // Escape → clear selection
            if (ImGui.IsKeyPressed(ImGui.Key_Escape, false))
                ResetSelection();
        }

        // ── Multi-select operations ────────────────────────────────────

        private void DeleteAllSelected()
        {
            var toDelete = new List<uint>(_selectedEntities);
            _selectedEntities.Clear();
            for (int i = 0; i < toDelete.Count; i++)
            {
                if (EntityManager.IsEntityValid(toDelete[i]))
                {
                    _expandedStates.Remove(toDelete[i]);
                    EntityManager.DestroyEntity(toDelete[i]);
                }
            }
            _cache.Invalidate();
            EditorContext.SelectedEntityId = -1;
        }

        private void DuplicateAllSelected()
        {
            var toDuplicate = new List<uint>(_selectedEntities);
            _selectedEntities.Clear();

            for (int i = 0; i < toDuplicate.Count; i++)
            {
                if (!EntityManager.IsEntityValid(toDuplicate[i]))
                    continue;

                uint duplicated = EditorBridge.DuplicateEntity(toDuplicate[i]);
                if (EntityManager.IsEntityValid(duplicated))
                    _selectedEntities.Add(duplicated);
            }

            _cache.Invalidate();
            SyncPrimarySelection();
        }

        private void SelectAll()
        {
            _selectedEntities.Clear();
            var flatOrder = _cache.BuildFlatVisibleOrder(_expandedStates);
            for (int i = 0; i < flatOrder.Count; i++)
                _selectedEntities.Add(flatOrder[i]);
            SyncPrimarySelection();
        }

        // ── Create Menu ────────────────────────────────────────────────

        private void DrawCreateMenu()
        {
            if (!ImGui.BeginPopup("##HierarchyCreateMenu"))
                return;

            ComponentRegistry.EnsureInitialized();

            bool hasSelection = EditorContext.SelectedEntityId >= 0 &&
                                EntityManager.IsEntityValid((uint)EditorContext.SelectedEntityId);
            uint selectedId = hasSelection ? (uint)EditorContext.SelectedEntityId : 0;

            // ── Basic entities ──
            if (ImGui.Selectable("Create Empty", false))
                CreateEntityAndSelect();

            if (hasSelection && ImGui.Selectable("Create Empty Child", false))
                CreateChildEntity(selectedId);

            ImGui.Separator();

            // ── Dynamic categories from registry ──
            var categories = ComponentRegistry.GetCategories();
            for (int c = 0; c < categories.Count; c++)
            {
                string category = categories[c];
                var entries = ComponentRegistry.GetEntriesByCategory(category);

                // Collect creatable entries
                bool hasCreatable = false;
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].CanCreate)
                    {
                        hasCreatable = true;
                        break;
                    }
                }
                if (!hasCreatable)
                    continue;

                if (ImGui.BeginMenu(category))
                {
                    for (int i = 0; i < entries.Count; i++)
                    {
                        var entry = entries[i];
                        if (!entry.CanCreate)
                            continue;

                        if (ImGui.Selectable(entry.Name, false))
                            CreateEntityWithComponent(entry.Name, entry.ComponentType, selectedId, false);
                    }
                    ImGui.EndMenu();
                }
            }

            ImGui.Separator();

            // ── Parenting ──
            if (hasSelection && ImGui.Selectable("Clear Parent", false))
            {
                if (_cache.GetParent(selectedId) != 0)
                {
                    EditorBridge.SetParentEntity(selectedId, 0);
                    _cache.Invalidate();
                }
            }

            ImGui.EndPopup();
        }

        private void CreateChildEntity(uint parentId)
        {
            uint created = EntityManager.CreateEntity();
            if (!EntityManager.IsEntityValid(created))
                return;

            if (EditorBridge.SetParentEntity(created, parentId))
            {
                _expandedStates[parentId] = true;
                SelectSingle(created);
                _cache.Invalidate();
            }
            else
            {
                EntityManager.DestroyEntity(created);
                ProjectOperations.SetStatusMessage("Create child failed.");
            }
        }

        private void CreateEntityWithComponent(string name, int componentType, uint parentId, bool asChild)
        {
            uint created = EntityManager.CreateEntity();
            if (!EntityManager.IsEntityValid(created))
                return;

            EntityManager.SetEntityName(created, name);
            EntityManager.AddComponent(created, ComponentType.Transform);
            EntityManager.AddComponent(created, componentType);

            if (asChild && parentId != 0)
            {
                if (!EditorBridge.SetParentEntity(created, parentId))
                {
                    EntityManager.DestroyEntity(created);
                    ProjectOperations.SetStatusMessage("Create entity failed: parent rejected.");
                    return;
                }
                _expandedStates[parentId] = true;
            }

            SelectSingle(created);
            _cache.Invalidate();
        }

        // ── Helpers ────────────────────────────────────────────────────

        private void SetAllExpanded(bool expanded)
        {
            _expandedStates.Clear();
            // Set expand state for all entities that have children
            SetSubtreeExpanded(_cache.Roots, expanded);
            _cache.Invalidate(); // re-cache will pick up new state
        }

        private void SetSubtreeExpanded(IReadOnlyList<uint> entities, bool expanded)
        {
            for (int i = 0; i < entities.Count; i++)
            {
                uint id = entities[i];
                if (_cache.HasChildren(id))
                {
                    _expandedStates[id] = expanded;
                    SetSubtreeExpanded(_cache.GetChildren(id), expanded);
                }
            }
        }

        private void FocusEntity(uint entityId)
        {
            if (!EntityManager.IsEntityValid(entityId))
                return;

            if (!EntityManager.HasTransform(entityId))
                return;

            float x, y, w, h;
            EntityManager.GetTransform(entityId, out x, out y, out w, out h);
            EditorContext.PreviewCameraX = x;
            EditorContext.PreviewCameraY = y;
        }

        private void RemoveSubtreeState(uint entityId)
        {
            var children = _cache.GetChildren(entityId);
            for (int i = 0; i < children.Count; i++)
            {
                _selectedEntities.Remove(children[i]);
                _expandedStates.Remove(children[i]);
                RemoveSubtreeState(children[i]);
            }
        }

        private static bool WouldCreateCycle(uint childEntityId, uint targetParentEntityId)
        {
            if (!EntityManager.IsEntityValid(childEntityId) || !EntityManager.IsEntityValid(targetParentEntityId))
                return true;

            if (childEntityId == targetParentEntityId)
                return true;

            uint current = targetParentEntityId;
            int guard = Math.Max(1, EntityManager.GetEntityCount() + 1);
            while (guard-- > 0 && EntityManager.IsEntityValid(current))
            {
                if (current == childEntityId)
                    return true;

                uint parent = EditorBridge.GetParentEntity(current);
                if (!EntityManager.IsEntityValid(parent))
                    break;

                current = parent;
            }

            return false;
        }
    }
}
