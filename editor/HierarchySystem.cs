using Engine;

namespace EngineEditor
{
    internal static class HierarchySystem
    {
        private static int SelectedEntityId
        {
            get => EditorContext.SelectedEntityId;
            set => EditorContext.SelectedEntityId = value;
        }

        public static void ResetSelection()
        {
            SelectedEntityId = -1;
        }

        public static void CreateEntityAndSelect()
        {
            uint created = EntityManager.CreateEntity();
            if (EntityManager.IsEntityValid(created))
                SelectedEntityId = (int)created;
        }

        public static void DrawSceneTreePanel()
        {
            if (ImGui.Begin("Scene Tree"))
            {
                if (ImGui.Button("Create Entity"))
                    CreateEntityAndSelect();

                ImGui.Separator();

                int entityCount = EntityManager.GetEntityCount();
                for (int i = 0; i < entityCount; ++i)
                {
                    uint entityId = EntityManager.GetEntityIdAtIndex(i);
                    string entityName = EntityManager.GetEntityName(entityId);
                    if (string.IsNullOrWhiteSpace(entityName))
                        entityName = "Entity " + entityId;

                    string label = entityName + "##SceneTreeEntity" + entityId;
                    bool selected = ((uint)SelectedEntityId == entityId);
                    if (ImGui.Selectable(label, selected))
                        SelectedEntityId = (int)entityId;
                }
            }

            ImGui.End();
        }
    }
}
