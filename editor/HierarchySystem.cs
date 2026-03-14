using Engine;
using System;
using System.Collections.Generic;

namespace EngineEditor
{
    internal static class HierarchySystem
    {
        private const float TreeIndentPixels = 14.0f;
        private const float TreeRowMinHeight = 18.0f;

        private static readonly Dictionary<uint, bool> _expandedByEntity = new Dictionary<uint, bool>();
        private static uint _dragCandidateEntityId;
        private static uint _draggingEntityId;
        private static uint _hoveredDropTargetId;
        private static float _dragStartMouseX;
        private static float _dragStartMouseY;
        private static uint _renameEntityId;
        private static string _renameValue = string.Empty;

        private static int SelectedEntityId
        {
            get => EditorContext.SelectedEntityId;
            set => EditorContext.SelectedEntityId = value;
        }

        public static void ResetSelection()
        {
            SelectedEntityId = -1;
        }

        public static void ResetState()
        {
            _expandedByEntity.Clear();
            ResetTransientState();
            SelectedEntityId = -1;
        }

        public static bool HasValidSelection()
        {
            if (SelectedEntityId < 0)
                return false;

            return EntityManager.IsEntityValid((uint)SelectedEntityId);
        }

        public static void DeleteSelectedEntity()
        {
            if (!HasValidSelection())
                return;

            DeleteEntity((uint)SelectedEntityId);
        }

        public static bool DeleteEntity(uint entityId)
        {
            if (!EntityManager.IsEntityValid(entityId))
                return false;

            var subtreeEntities = new List<uint>(8);
            CollectSubtreeEntities(entityId, subtreeEntities);
            if (subtreeEntities.Count == 0)
                subtreeEntities.Add(entityId);

            var subtreeSet = new HashSet<uint>(subtreeEntities);
            EntityManager.DestroyEntity(entityId);

            for (int i = 0; i < subtreeEntities.Count; ++i)
                _expandedByEntity.Remove(subtreeEntities[i]);

            if (SelectedEntityId >= 0 && subtreeSet.Contains((uint)SelectedEntityId))
                SelectedEntityId = -1;

            if (_renameEntityId != 0 && subtreeSet.Contains(_renameEntityId))
            {
                _renameEntityId = 0;
                _renameValue = string.Empty;
            }

            if ((_dragCandidateEntityId != 0 && subtreeSet.Contains(_dragCandidateEntityId)) ||
                (_draggingEntityId != 0 && subtreeSet.Contains(_draggingEntityId)) ||
                (_hoveredDropTargetId != 0 && subtreeSet.Contains(_hoveredDropTargetId)))
            {
                ResetDragState();
            }

            return true;
        }

        public static void DuplicateSelectedEntity()
        {
            if (!HasValidSelection())
                return;

            uint duplicated = EditorBridge.DuplicateEntity((uint)SelectedEntityId);
            if (EntityManager.IsEntityValid(duplicated))
                SelectedEntityId = (int)duplicated;
        }

        public static void CreateEntityAndSelect()
        {
            uint created = EntityManager.CreateEntity();
            if (EntityManager.IsEntityValid(created))
            {
                EditorBridge.SetParentEntity(created, 0);
                SelectedEntityId = (int)created;
            }
        }

        public static void DrawSceneTreePanel()
        {
            if (ImGui.Begin("Hierarchy"))
            {
                if (ImGui.Button("Create Empty"))
                    CreateEntityAndSelect();

                ImGui.SameLine();
                if (ImGui.Button("Duplicate") && HasValidSelection())
                    DuplicateSelectedEntity();

                ImGui.SameLine();
                if (ImGui.Button("Delete") && HasValidSelection())
                    DeleteSelectedEntity();

                ImGui.Separator();

                _hoveredDropTargetId = 0;

                int rootCount = EditorBridge.GetRootEntityCount();
                var rootEntities = new List<uint>(Math.Max(0, rootCount));
                for (int i = 0; i < rootCount; ++i)
                {
                    uint rootEntityId = EditorBridge.GetRootEntityAt(i);
                    if (!EntityManager.IsEntityValid(rootEntityId))
                        continue;

                    rootEntities.Add(rootEntityId);
                }

                for (int i = 0; i < rootEntities.Count; ++i)
                {
                    bool isLastRoot = i == rootEntities.Count - 1;
                    DrawEntityNode(rootEntities[i],
                                   0,
                                   new List<bool>(),
                                   isLastRoot);
                }

                if (rootEntities.Count == 0)
                    ImGui.Text("Scene is empty.");

                DrawRenamePopup();
                DrawDragPreview();
                HandleDragState();
            }

            ImGui.End();
        }

        private static void DrawEntityNode(uint entityId,
                                           int depth,
                                           List<bool> ancestorHasMoreSiblings,
                                           bool isLastChild)
        {
            if (!EntityManager.IsEntityValid(entityId))
                return;

            string entityName = GetEntityDisplayName(entityId);

            bool expanded = ResolveExpanded(entityId);
            int childCount = EditorBridge.GetChildEntityCount(entityId);
            bool hasChildren = childCount > 0;
            string rowIndent = new string(' ', Math.Max(0, depth) * 4);

            float rowStartX = ImGui.GetCursorScreenPosX();
            float rowStartY = ImGui.GetCursorScreenPosY();
            float rowWidth = Math.Max(120.0f, ImGui.GetContentRegionAvailX());

            string foldLabel = hasChildren
                ? (expanded ? "[-]##Fold" : "[+]##Fold") + entityId
                : "   ##Leaf" + entityId;
            bool rowHovered;

            if (ImGui.Button(rowIndent + foldLabel) && hasChildren)
            {
                expanded = !expanded;
                _expandedByEntity[entityId] = expanded;
            }
            rowHovered = ImGui.IsItemHovered();

            ImGui.SameLine();

            bool selected = SelectedEntityId >= 0 && (uint)SelectedEntityId == entityId;
            string childCountLabel = hasChildren ? " (" + childCount + ")" : string.Empty;
            string nodeLabel = entityName + childCountLabel;
            if (ImGui.Selectable(nodeLabel + "##HierarchyEntity" + entityId, selected))
                SelectedEntityId = (int)entityId;
            rowHovered |= ImGui.IsItemHovered();

            HandleNodeDragAndDrop(entityId, rowHovered);

            string popupId = "HierarchyContext##" + entityId;
            if (rowHovered && ImGui.IsMouseClicked(1))
                ImGui.OpenPopup(popupId);

            DrawEntityContextMenu(entityId, popupId, entityName);

            float rowEndY = ImGui.GetCursorScreenPosY();
            float rowHeight = Math.Max(TreeRowMinHeight, rowEndY - rowStartY);
            DrawTreeConnectors(depth,
                               ancestorHasMoreSiblings,
                               isLastChild,
                               rowStartX,
                               rowStartY,
                               rowHeight);

            if (selected)
            {
                ImGui.DrawLine(rowStartX,
                               rowStartY + 1.0f,
                               rowStartX,
                               rowStartY + rowHeight - 1.0f,
                               0.35f,
                               0.75f,
                               0.95f,
                               0.95f,
                               2.0f);
            }

            if (_draggingEntityId != 0 &&
                _hoveredDropTargetId == entityId &&
                _draggingEntityId != entityId)
            {
                ImGui.DrawRect(rowStartX,
                               rowStartY,
                               rowWidth,
                               rowHeight,
                               0.80f,
                               0.88f,
                               1.0f,
                               0.95f,
                               1.5f);
            }

            if (expanded && hasChildren)
            {
                var validChildren = new List<uint>(childCount);
                for (int i = 0; i < childCount; ++i)
                {
                    uint childEntityId = EditorBridge.GetChildEntityAt(entityId, i);
                    if (!EntityManager.IsEntityValid(childEntityId))
                        continue;

                    validChildren.Add(childEntityId);
                }

                ancestorHasMoreSiblings.Add(!isLastChild);
                for (int i = 0; i < validChildren.Count; ++i)
                {
                    bool isLast = i == validChildren.Count - 1;
                    DrawEntityNode(validChildren[i],
                                   depth + 1,
                                   ancestorHasMoreSiblings,
                                   isLast);
                }

                if (ancestorHasMoreSiblings.Count > 0)
                    ancestorHasMoreSiblings.RemoveAt(ancestorHasMoreSiblings.Count - 1);
            }
        }

        private static void DrawTreeConnectors(int depth,
                                               List<bool> ancestorHasMoreSiblings,
                                               bool isLastChild,
                                               float rowStartX,
                                               float rowStartY,
                                               float rowHeight)
        {
            if (depth <= 0)
                return;

            float rowEndY = rowStartY + rowHeight;
            float middleY = rowStartY + rowHeight * 0.5f;

            for (int i = 0; i < ancestorHasMoreSiblings.Count; ++i)
            {
                if (!ancestorHasMoreSiblings[i])
                    continue;

                float x = rowStartX + 6.0f + i * TreeIndentPixels;
                ImGui.DrawLine(x,
                               rowStartY,
                               x,
                               rowEndY,
                               0.45f,
                               0.47f,
                               0.52f,
                               0.95f,
                               1.0f);
            }

            float branchX = rowStartX + 6.0f + ancestorHasMoreSiblings.Count * TreeIndentPixels;
            float branchEndY = isLastChild ? middleY : rowEndY;
            ImGui.DrawLine(branchX,
                           rowStartY,
                           branchX,
                           branchEndY,
                           0.55f,
                           0.58f,
                           0.64f,
                           0.95f,
                           1.1f);
            ImGui.DrawLine(branchX,
                           middleY,
                           branchX + 8.0f,
                           middleY,
                           0.55f,
                           0.58f,
                           0.64f,
                           0.95f,
                           1.1f);
        }

        private static string GetEntityDisplayName(uint entityId)
        {
            string entityName = EntityManager.GetEntityName(entityId);
            if (string.IsNullOrWhiteSpace(entityName))
                entityName = "Entity " + entityId;

            return entityName;
        }

        private static void CollectSubtreeEntities(uint entityId, List<uint> entities)
        {
            if (!EntityManager.IsEntityValid(entityId))
                return;

            entities.Add(entityId);
            int childCount = EditorBridge.GetChildEntityCount(entityId);
            for (int i = 0; i < childCount; ++i)
            {
                uint childEntityId = EditorBridge.GetChildEntityAt(entityId, i);
                if (!EntityManager.IsEntityValid(childEntityId))
                    continue;

                CollectSubtreeEntities(childEntityId, entities);
            }
        }

        private static void DrawEntityContextMenu(uint entityId, string popupId, string entityName)
        {
            if (!ImGui.BeginPopup(popupId))
                return;

            if (ImGui.Selectable("Create Empty Child", false))
                TryCreateChildEntity(entityId);

            if (ImGui.Selectable("Duplicate", false))
            {
                uint duplicated = EditorBridge.DuplicateEntity(entityId);
                if (EntityManager.IsEntityValid(duplicated))
                    SelectedEntityId = (int)duplicated;
            }

            if (ImGui.Selectable("Delete", false))
            {
                DeleteEntity(entityId);
                ImGui.EndPopup();
                return;
            }

            if (ImGui.Selectable("Rename", false))
            {
                _renameEntityId = entityId;
                _renameValue = entityName;
                ImGui.OpenPopup("Rename Entity");
            }

            ImGui.EndPopup();
        }

        private static void DrawRenamePopup()
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

        private static bool ResolveExpanded(uint entityId)
        {
            bool expanded;
            if (_expandedByEntity.TryGetValue(entityId, out expanded))
                return expanded;

            _expandedByEntity[entityId] = true;
            return true;
        }

        private static void HandleNodeDragAndDrop(uint entityId, bool rowHovered)
        {
            if (!rowHovered)
                return;

            if (ImGui.IsMouseClicked(0))
            {
                _dragCandidateEntityId = entityId;
                _dragStartMouseX = ImGui.GetMousePosX();
                _dragStartMouseY = ImGui.GetMousePosY();
            }

            if (_draggingEntityId != 0)
                _hoveredDropTargetId = entityId;
        }

        private static void HandleDragState()
        {
            if (_dragCandidateEntityId != 0 && _draggingEntityId == 0 && ImGui.IsMouseDown(0))
            {
                float dx = ImGui.GetMousePosX() - _dragStartMouseX;
                float dy = ImGui.GetMousePosY() - _dragStartMouseY;
                float dragDistanceSq = dx * dx + dy * dy;
                if (dragDistanceSq >= 25.0f)
                    _draggingEntityId = _dragCandidateEntityId;
            }

            if (ImGui.IsMouseDown(0))
                return;

            if (_draggingEntityId != 0)
            {
                if (_hoveredDropTargetId != 0 && _hoveredDropTargetId != _draggingEntityId)
                {
                    if (!WouldCreateCycle(_draggingEntityId, _hoveredDropTargetId))
                    {
                        if (!EditorBridge.SetParentEntity(_draggingEntityId, _hoveredDropTargetId))
                        {
                            ProjectOperations.SetStatusMessage("Reparent failed. The selected target is invalid.");
                        }
                        else
                        {
                            _expandedByEntity[_hoveredDropTargetId] = true;
                        }
                    }
                    else
                    {
                        ProjectOperations.SetStatusMessage("Reparent blocked to prevent cyclic hierarchy.");
                    }
                }
                else if (ImGui.IsWindowHovered())
                {
                    if (!EditorBridge.SetParentEntity(_draggingEntityId, 0))
                        ProjectOperations.SetStatusMessage("Reparent to root failed.");
                }
            }

            ResetDragState();
        }

        private static bool TryCreateChildEntity(uint parentEntityId)
        {
            if (!EntityManager.IsEntityValid(parentEntityId))
            {
                ProjectOperations.SetStatusMessage("Create child failed: parent entity is invalid.");
                return false;
            }

            uint created = EntityManager.CreateEntity();
            if (!EntityManager.IsEntityValid(created))
            {
                ProjectOperations.SetStatusMessage("Create child failed: could not create entity.");
                return false;
            }

            if (!EditorBridge.SetParentEntity(created, parentEntityId))
            {
                EntityManager.DestroyEntity(created);
                ProjectOperations.SetStatusMessage("Create child failed: parent assignment was rejected.");
                return false;
            }

            _expandedByEntity[parentEntityId] = true;
            SelectedEntityId = (int)created;
            return true;
        }

        private static void DrawDragPreview()
        {
            if (_draggingEntityId == 0 || !EntityManager.IsEntityValid(_draggingEntityId))
                return;

            string draggingName = GetEntityDisplayName(_draggingEntityId);
            if (_hoveredDropTargetId != 0 &&
                _hoveredDropTargetId != _draggingEntityId &&
                EntityManager.IsEntityValid(_hoveredDropTargetId))
            {
                string targetName = GetEntityDisplayName(_hoveredDropTargetId);
                ImGui.SetTooltip("Move '" + draggingName + "' under '" + targetName + "'");
                return;
            }

            if (ImGui.IsWindowHovered())
            {
                ImGui.SetTooltip("Move '" + draggingName + "' to root");
                return;
            }

            ImGui.SetTooltip("Dragging '" + draggingName + "'");
        }

        private static void ResetDragState()
        {
            _dragCandidateEntityId = 0;
            _draggingEntityId = 0;
            _hoveredDropTargetId = 0;
        }

        private static void ResetTransientState()
        {
            ResetDragState();
            _renameEntityId = 0;
            _renameValue = string.Empty;
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
