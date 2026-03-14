using Engine;
using System;
using System.Collections.Generic;

namespace EngineEditor
{
    internal static class HierarchySystem
    {
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

            uint selectedEntityId = (uint)SelectedEntityId;
            EntityManager.DestroyEntity(selectedEntityId);
            _expandedByEntity.Remove(selectedEntityId);
            SelectedEntityId = -1;
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
                for (int i = 0; i < rootCount; ++i)
                {
                    uint rootEntityId = EditorBridge.GetRootEntityAt(i);
                    if (!EntityManager.IsEntityValid(rootEntityId))
                        continue;

                    DrawEntityNode(rootEntityId, 0);
                }

                if (rootCount == 0)
                    ImGui.Text("Scene is empty.");

                DrawRenamePopup();
                HandleDragState();
            }

            ImGui.End();
        }

        private static void DrawEntityNode(uint entityId, int depth)
        {
            string entityName = EntityManager.GetEntityName(entityId);
            if (string.IsNullOrWhiteSpace(entityName))
                entityName = "Entity " + entityId;

            bool expanded = ResolveExpanded(entityId);
            int childCount = EditorBridge.GetChildEntityCount(entityId);
            bool hasChildren = childCount > 0;

            string indent = new string(' ', depth * 2);

            if (hasChildren)
            {
                if (ImGui.Button(indent + (expanded ? "v##Fold" : ">##Fold") + entityId))
                {
                    expanded = !expanded;
                    _expandedByEntity[entityId] = expanded;
                }
            }
            else
            {
                ImGui.Button(indent + "-##Leaf" + entityId);
            }

            ImGui.SameLine();

            bool selected = SelectedEntityId >= 0 && (uint)SelectedEntityId == entityId;
            if (ImGui.Selectable(indent + entityName + "##HierarchyEntity" + entityId, selected))
                SelectedEntityId = (int)entityId;

            string popupId = "HierarchyContext##" + entityId;
            if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(1))
                ImGui.OpenPopup(popupId);

            DrawEntityContextMenu(entityId, popupId, entityName);
            HandleNodeDragAndDrop(entityId);

            if (expanded && hasChildren)
            {
                for (int i = 0; i < childCount; ++i)
                {
                    uint childEntityId = EditorBridge.GetChildEntityAt(entityId, i);
                    if (!EntityManager.IsEntityValid(childEntityId))
                        continue;

                    DrawEntityNode(childEntityId, depth + 1);
                }
            }
        }

        private static void DrawEntityContextMenu(uint entityId, string popupId, string entityName)
        {
            if (!ImGui.BeginPopup(popupId))
                return;

            if (ImGui.Selectable("Create Empty Child", false))
            {
                uint created = EntityManager.CreateEntity();
                if (EntityManager.IsEntityValid(created) && EditorBridge.SetParentEntity(created, entityId))
                    SelectedEntityId = (int)created;
            }

            if (ImGui.Selectable("Duplicate", false))
            {
                uint duplicated = EditorBridge.DuplicateEntity(entityId);
                if (EntityManager.IsEntityValid(duplicated))
                    SelectedEntityId = (int)duplicated;
            }

            if (ImGui.Selectable("Delete", false))
            {
                EntityManager.DestroyEntity(entityId);
                if (SelectedEntityId >= 0 && (uint)SelectedEntityId == entityId)
                    SelectedEntityId = -1;
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

        private static void HandleNodeDragAndDrop(uint entityId)
        {
            if (!ImGui.IsItemHovered())
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
                        EditorBridge.SetParentEntity(_draggingEntityId, _hoveredDropTargetId);
                }
                else if (ImGui.IsWindowHovered())
                {
                    EditorBridge.SetParentEntity(_draggingEntityId, 0);
                }
            }

            _dragCandidateEntityId = 0;
            _draggingEntityId = 0;
            _hoveredDropTargetId = 0;
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
