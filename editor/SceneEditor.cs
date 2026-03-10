using System;

using Engine;

namespace EngineEditor
{
    internal static class SceneEditor
    {
        private static int _selectedEntityId = -1;
        private static float _tickAccumulator = 0.0f;

        public static int SelectedEntityId => _selectedEntityId;

        public static void ResetSelection()
        {
            _selectedEntityId = -1;
        }

        public static void UpdateTick(float deltaTime)
        {
            _tickAccumulator += deltaTime;
            if (_tickAccumulator < 1.0f)
                return;

            _tickAccumulator = 0.0f;

            int totalEntitiesLog = EditorBridge.GetEntityCount();
            int scriptedEntitiesLog = EditorBridge.GetScriptedEntityCount();

            Console.WriteLine("[Editor] Entities=" + totalEntitiesLog + ", Scripted=" + scriptedEntitiesLog);
        }

        public static void DrawSceneTreePanel()
        {
            if (ImGui.Begin("Scene Tree"))
            {
                if (ImGui.Button("Project Manager"))
                {
                    EditorHost.SetShowProjectManagerView(true);
                    ImGui.End();
                    return;
                }

                ImGui.SameLine();
                if (ImGui.Button("Create Entity"))
                {
                    uint created = EditorBridge.CreateEntity();
                    _selectedEntityId = (int)created;
                }

                ImGui.Separator();

                int entityCount = EditorBridge.GetEntityCount();
                for (int i = 0; i < entityCount; ++i)
                {
                    uint entityId = EditorBridge.GetEntityIdAtIndex(i);
                    string label = "Entity " + entityId;
                    bool selected = ((uint)_selectedEntityId == entityId);
                    if (ImGui.Selectable(label, selected))
                        _selectedEntityId = (int)entityId;
                }
            }
            ImGui.End();
        }

        public static void DrawInspectorPanel()
        {
            if (ImGui.Begin("Inspector"))
            {
                if (_selectedEntityId < 0)
                {
                    ImGui.Text("No entity selected.");
                }
                else
                {
                    uint entityId = (uint)_selectedEntityId;
                    bool valid = EditorBridge.IsEntityValid(entityId);
                    if (!valid)
                    {
                        _selectedEntityId = -1;
                        ImGui.Text("Selected entity is no longer valid.");
                    }
                    else
                    {
                        ImGui.Text("Entity: " + entityId);

                        if (ImGui.Button("Delete Entity"))
                        {
                            EditorBridge.DestroyEntity(entityId);
                            _selectedEntityId = -1;
                            ImGui.End();
                            return;
                        }

                        ImGui.Separator();
                        ImGui.Text("Transform");
                        bool hasTransform = EditorBridge.HasTransform(entityId);
                        if (!hasTransform)
                        {
                            ImGui.Text("Missing TransformComponent");
                            if (ImGui.Button("Add Transform"))
                                EditorBridge.AddTransform(entityId);
                        }
                        else
                        {
                            float x;
                            float y;
                            float width;
                            float height;
                            if (EditorBridge.GetTransform(entityId, out x, out y, out width, out height))
                            {
                                bool changed = false;
                                changed |= ImGui.InputFloat("X", ref x, 1.0f);
                                changed |= ImGui.InputFloat("Y", ref y, 1.0f);
                                changed |= ImGui.InputFloat("Width", ref width, 1.0f);
                                changed |= ImGui.InputFloat("Height", ref height, 1.0f);

                                if (changed)
                                    EditorBridge.SetTransform(entityId, x, y, width, height);
                            }
                        }

                        ImGui.Separator();
                        ImGui.Text("Script");
                        bool hasScript = EditorBridge.HasScript(entityId);
                        if (!hasScript)
                        {
                            ImGui.Text("Missing ScriptComponent");
                            if (ImGui.Button("Add Script"))
                                EditorBridge.AddScript(entityId);
                        }
                        else
                        {
                            bool scriptEnabled = EditorBridge.GetScriptEnabled(entityId);
                            ImGui.Text("Enabled: " + (scriptEnabled ? "yes" : "no"));

                            if (ImGui.Button(scriptEnabled ? "Disable Script" : "Enable Script"))
                                EditorBridge.SetScriptEnabled(entityId, !scriptEnabled);

                            if (ImGui.Button("Remove Script"))
                                EditorBridge.RemoveScript(entityId);
                        }
                    }
                }
            }
            ImGui.End();
        }
    }
}
