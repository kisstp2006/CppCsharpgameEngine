#include "MonoRuntime.h"

#include "engine/ECS/Components.h"
#include "engine/ECS/Scene.h"
#include "engine/Platform/ExplorerDialog.h"
#include "engine/Platform/SDLInputState.h"
#include "engine/Render/DebugDraw.h"
#include "engine/Render/Renderer.h"

#include <filesystem>
#include <cstdlib>
#include <cstdio>
#include <cstdint>
#include <chrono>
#include <cmath>
#include <iostream>
#include <string>
#include <thread>
#include <unordered_map>
#include <vector>

#include <entt/entt.hpp>
#include <imgui.h>
#include <ImGuizmo.h>

#if !defined(__INTELLISENSE__) && !defined(ENGINE_MONO_DISABLED) && __has_include(<mono/jit/jit.h>) && __has_include(<mono/metadata/assembly.h>) && __has_include(<mono/metadata/debug-helpers.h>) && __has_include(<mono/metadata/mono-config.h>) && __has_include(<mono/metadata/object.h>)
#define ENGINE_MONO_RUNTIME_AVAILABLE 1
#include <mono/jit/jit.h>
#include <mono/metadata/assembly.h>
#include <mono/metadata/debug-helpers.h>
#include <mono/metadata/mono-config.h>
#include <mono/metadata/object.h>
#else
#define ENGINE_MONO_RUNTIME_AVAILABLE 0
#endif

struct MonoRuntime::Impl
{
#if ENGINE_MONO_RUNTIME_AVAILABLE
    MonoDomain* domain = nullptr;
    MonoAssembly* assembly = nullptr;
    MonoImage* image = nullptr;
    MonoClass* scriptClass = nullptr;
    MonoMethod* onStart = nullptr;
    MonoMethod* onUpdate = nullptr;
    MonoMethod* onShutdown = nullptr;

    MonoAssembly* editorAssembly = nullptr;
    MonoImage* editorImage = nullptr;
    MonoClass* editorClass = nullptr;
    MonoMethod* editorOnStart = nullptr;
    MonoMethod* editorOnUpdate = nullptr;
    MonoMethod* editorOnShutdown = nullptr;

    struct ScriptInstance
    {
        MonoObject* instance = nullptr;
        MonoMethod* onCreate = nullptr;
        MonoMethod* onUpdate = nullptr;
        MonoMethod* onDestroy = nullptr;
        bool created = false;
    };

    std::unordered_map<std::uint32_t, ScriptInstance> entityScripts;

    std::filesystem::path resolvedScriptAssemblyPath;
    std::filesystem::path resolvedEditorAssemblyPath;
    std::filesystem::path shadowCopyDirectory;

    std::filesystem::file_time_type scriptAssemblyWriteTime{};
    std::filesystem::file_time_type editorAssemblyWriteTime{};
    bool hasScriptAssemblyWriteTime = false;
    bool hasEditorAssemblyWriteTime = false;

    float hotReloadPollAccumulator = 0.0f;
#endif
    std::filesystem::path preferredScriptAssemblyPath;
    std::filesystem::path preferredScriptProjectPath;
    bool scriptLoaded = false;
    bool editorLoaded = false;
};

#if ENGINE_MONO_RUNTIME_AVAILABLE
static Scene* g_editorSceneContext = nullptr;
static Renderer* g_editorRendererContext = nullptr;

static std::string MonoStringToUtf8(MonoString* monoString)
{
    if (!monoString)
        return {};

    char* utf8 = mono_string_to_utf8(monoString);
    if (!utf8)
        return {};

    std::string result(utf8);
    mono_free(utf8);
    return result;
}

static int EditorBridge_GetEntityCount()
{
    if (!g_editorSceneContext)
        return 0;

    return static_cast<int>(g_editorSceneContext->EntityCount());
}

static std::uint32_t EditorBridge_GetEntityIdAtIndex(int index)
{
    if (!g_editorSceneContext || index < 0)
        return static_cast<std::uint32_t>(entt::null);

    auto& registry = g_editorSceneContext->Registry();
    auto entities = registry.view<entt::entity>();

    int current = 0;
    for (const auto entity : entities)
    {
        if (current == index)
            return static_cast<std::uint32_t>(entt::to_integral(entity));
        ++current;
    }

    return static_cast<std::uint32_t>(entt::null);
}

static bool EditorBridge_IsEntityValid(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return false;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    return g_editorSceneContext->IsValid(entity);
}

static std::uint32_t EditorBridge_CreateEntity()
{
    if (!g_editorSceneContext)
        return static_cast<std::uint32_t>(entt::null);

    const auto entity = g_editorSceneContext->CreateEntity();
    return static_cast<std::uint32_t>(entt::to_integral(entity));
}

static void EditorBridge_DestroyEntity(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return;

    g_editorSceneContext->DestroyEntity(g_editorSceneContext->FromEntityId(entityId));
}

static int EditorBridge_GetScriptedEntityCount()
{
    if (!g_editorSceneContext)
        return 0;

    auto& registry = g_editorSceneContext->Registry();
    int count = 0;
    auto view = registry.view<ScriptComponent>();
    for (const auto entity : view)
    {
        (void)entity;
        ++count;
    }
    return count;
}

static void EditorBridge_SetScriptEnabled(std::uint32_t entityId, bool enabled)
{
    if (!g_editorSceneContext)
        return;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    ScriptComponent* script = g_editorSceneContext->TryGetScript(entity);
    if (!script)
        return;

    script->enabled = enabled;
}

static bool EditorBridge_GetScriptEnabled(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return false;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    const ScriptComponent* script = g_editorSceneContext->TryGetScript(entity);
    if (!script)
        return false;

    return script->enabled;
}

static void EditorBridge_SetGameViewSize(float width, float height)
{
    if (!g_editorRendererContext)
        return;

    const int w = static_cast<int>(width);
    const int h = static_cast<int>(height);
    if (w < 1 || h < 1)
        return;

    g_editorRendererContext->SetGameViewSize(w, h);
}

static std::uint64_t EditorBridge_GetGameViewTextureHandle()
{
    if (!g_editorRendererContext)
        return 0;

    return static_cast<std::uint64_t>(g_editorRendererContext->GetGameViewTextureHandle());
}

static void EditorDebugDraw_Line(float x0, float y0, float x1, float y1,
                                 float r, float g, float b, float a,
                                 float thickness, float durationSeconds)
{
    DebugDraw::Line(x0, y0, x1, y1, DebugDraw::Color(r, g, b, a), thickness, durationSeconds);
}

static void EditorDebugDraw_Circle(float centerX, float centerY, float radius,
                                   float r, float g, float b, float a,
                                   float thickness, int segments, float durationSeconds)
{
    DebugDraw::Circle(centerX, centerY, radius, DebugDraw::Color(r, g, b, a), thickness, segments, durationSeconds);
}

static void EditorDebugDraw_FilledCircle(float centerX, float centerY, float radius,
                                         float r, float g, float b, float a,
                                         int segments, float durationSeconds)
{
    DebugDraw::FilledCircle(centerX, centerY, radius, DebugDraw::Color(r, g, b, a), segments, durationSeconds);
}

static void EditorDebugDraw_Rect(float x, float y, float width, float height,
                                 float r, float g, float b, float a,
                                 float thickness, float durationSeconds)
{
    DebugDraw::Rect(x, y, width, height, DebugDraw::Color(r, g, b, a), thickness, durationSeconds);
}

static void EditorDebugDraw_FilledRect(float x, float y, float width, float height,
                                       float r, float g, float b, float a,
                                       float durationSeconds)
{
    DebugDraw::FilledRect(x, y, width, height, DebugDraw::Color(r, g, b, a), durationSeconds);
}

static void EditorDebugDraw_Clear()
{
    DebugDraw::Clear();
}

static MonoString* EditorExplorer_PickFolder(MonoString* title, MonoString* initialPath)
{
    const std::string selectedPath = ExplorerDialog::PickFolder(MonoStringToUtf8(title), MonoStringToUtf8(initialPath));
    MonoDomain* domain = mono_domain_get();
    return domain ? mono_string_new(domain, selectedPath.c_str()) : nullptr;
}

static MonoString* EditorExplorer_PickFile(MonoString* title, MonoString* initialPath)
{
    const std::string selectedPath = ExplorerDialog::PickFile(MonoStringToUtf8(title), MonoStringToUtf8(initialPath));
    MonoDomain* domain = mono_domain_get();
    return domain ? mono_string_new(domain, selectedPath.c_str()) : nullptr;
}

static MonoString* EditorExplorer_PickFiles(MonoString* title, MonoString* initialPath)
{
    const std::vector<std::string> selectedPaths = ExplorerDialog::PickFiles(MonoStringToUtf8(title), MonoStringToUtf8(initialPath));

    std::string joinedPaths;
    for (std::size_t index = 0; index < selectedPaths.size(); ++index)
    {
        if (index > 0)
            joinedPaths += '\n';
        joinedPaths += selectedPaths[index];
    }

    MonoDomain* domain = mono_domain_get();
    return domain ? mono_string_new(domain, joinedPaths.c_str()) : nullptr;
}

static bool EditorBridge_HasTransform(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return false;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    return g_editorSceneContext->HasTransform(entity);
}

static void EditorBridge_AddTransform(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    if (!g_editorSceneContext->IsValid(entity) || g_editorSceneContext->HasTransform(entity))
        return;

    auto& transform = g_editorSceneContext->AddTransform(entity);
    transform.x = 0.0f;
    transform.y = 0.0f;
    transform.width = 100.0f;
    transform.height = 100.0f;
}

static bool EditorBridge_GetTransform(std::uint32_t entityId, float* x, float* y, float* width, float* height)
{
    if (!g_editorSceneContext)
        return false;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    const TransformComponent* transform = g_editorSceneContext->TryGetTransform(entity);
    if (!transform)
        return false;

    if (x)
        *x = transform->x;
    if (y)
        *y = transform->y;
    if (width)
        *width = transform->width;
    if (height)
        *height = transform->height;
    return true;
}

static void EditorBridge_SetTransform(std::uint32_t entityId, float x, float y, float width, float height)
{
    if (!g_editorSceneContext)
        return;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    TransformComponent* transform = g_editorSceneContext->TryGetTransform(entity);
    if (!transform)
        return;

    transform->x = x;
    transform->y = y;
    transform->width = width;
    transform->height = height;
}

static bool EditorBridge_HasCamera(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return false;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    return g_editorSceneContext->HasCamera(entity);
}

static void EditorBridge_AddCamera(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    if (!g_editorSceneContext->IsValid(entity) || g_editorSceneContext->HasCamera(entity))
        return;

    auto& camera = g_editorSceneContext->AddCamera(entity);
    camera.x = 0.0f;
    camera.y = 0.0f;
    camera.zoom = 1.0f;
}

static bool EditorBridge_GetCamera(std::uint32_t entityId, float* x, float* y, float* zoom)
{
    if (!g_editorSceneContext)
        return false;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    const CameraComponent* camera = g_editorSceneContext->TryGetCamera(entity);
    if (!camera)
        return false;

    if (x)
        *x = camera->x;
    if (y)
        *y = camera->y;
    if (zoom)
        *zoom = camera->zoom;
    return true;
}

static void EditorBridge_SetCamera(std::uint32_t entityId, float x, float y, float zoom)
{
    if (!g_editorSceneContext)
        return;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    CameraComponent* camera = g_editorSceneContext->TryGetCamera(entity);
    if (!camera)
        return;

    camera->x = x;
    camera->y = y;
    camera->zoom = zoom;
}

static void EditorBridge_RemoveCamera(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    g_editorSceneContext->RemoveCamera(entity);
}

static bool EditorBridge_HasSprite(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return false;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    return g_editorSceneContext->HasSprite(entity);
}

static void EditorBridge_AddSprite(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    if (!g_editorSceneContext->IsValid(entity) || g_editorSceneContext->HasSprite(entity))
        return;

    g_editorSceneContext->AddSprite(entity);
}

static void EditorBridge_RemoveSprite(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    g_editorSceneContext->RemoveSprite(entity);
}

static bool EditorBridge_HasScript(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return false;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    return g_editorSceneContext->HasScript(entity);
}

static void EditorBridge_AddScript(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    if (!g_editorSceneContext->IsValid(entity) || g_editorSceneContext->HasScript(entity))
        return;

    g_editorSceneContext->AddScript(entity);
}

static void EditorBridge_RemoveScript(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    g_editorSceneContext->RemoveScript(entity);
}

static bool EditorImGui_Begin(MonoString* title)
{
    if (!ImGui::GetCurrentContext())
        return false;

    const std::string text = MonoStringToUtf8(title);
    const char* windowTitle = text.empty() ? "C# Window" : text.c_str();
    return ImGui::Begin(windowTitle);
}

static bool EditorImGui_BeginChild(MonoString* id, float width, float height, bool border)
{
    if (!ImGui::GetCurrentContext())
        return false;

    const std::string value = MonoStringToUtf8(id);
    const char* childId = value.empty() ? "Child" : value.c_str();
    return ImGui::BeginChild(childId, ImVec2(width, height), border);
}

static void EditorImGui_End()
{
    if (ImGui::GetCurrentContext())
        ImGui::End();
}

static void EditorImGui_EndChild()
{
    if (ImGui::GetCurrentContext())
        ImGui::EndChild();
}

static void EditorImGui_Text(MonoString* text)
{
    if (!ImGui::GetCurrentContext())
        return;

    const std::string value = MonoStringToUtf8(text);
    ImGui::TextUnformatted(value.c_str());
}

static bool EditorImGui_Button(MonoString* label)
{
    if (!ImGui::GetCurrentContext())
        return false;

    const std::string value = MonoStringToUtf8(label);
    const char* buttonLabel = value.empty() ? "Button" : value.c_str();
    return ImGui::Button(buttonLabel);
}

static void EditorImGui_OpenPopup(MonoString* popupId)
{
    if (!ImGui::GetCurrentContext())
        return;

    const std::string value = MonoStringToUtf8(popupId);
    const char* popupName = value.empty() ? "Popup" : value.c_str();
    ImGui::OpenPopup(popupName);
}

static bool EditorImGui_BeginPopupModal(MonoString* popupId)
{
    if (!ImGui::GetCurrentContext())
        return false;

    const std::string value = MonoStringToUtf8(popupId);
    const char* popupName = value.empty() ? "Popup" : value.c_str();
    return ImGui::BeginPopupModal(popupName, nullptr, ImGuiWindowFlags_AlwaysAutoResize);
}

static void EditorImGui_EndPopup()
{
    if (ImGui::GetCurrentContext())
        ImGui::EndPopup();
}

static void EditorImGui_CloseCurrentPopup()
{
    if (ImGui::GetCurrentContext())
        ImGui::CloseCurrentPopup();
}

static void EditorImGui_SameLine()
{
    if (ImGui::GetCurrentContext())
        ImGui::SameLine();
}

static void EditorImGui_SetNextItemWidth(float width)
{
    if (ImGui::GetCurrentContext())
        ImGui::SetNextItemWidth(width);
}

static bool EditorImGui_Selectable(MonoString* label, bool selected)
{
    if (!ImGui::GetCurrentContext())
        return false;

    const std::string value = MonoStringToUtf8(label);
    const char* selectableLabel = value.empty() ? "Item" : value.c_str();
    return ImGui::Selectable(selectableLabel, selected);
}

static bool EditorImGui_SelectableNoClose(MonoString* label, bool selected)
{
    if (!ImGui::GetCurrentContext())
        return false;

    const std::string value = MonoStringToUtf8(label);
    const char* selectableLabel = value.empty() ? "Item" : value.c_str();
    return ImGui::Selectable(selectableLabel, selected, ImGuiSelectableFlags_DontClosePopups);
}

static MonoString* EditorImGui_InputText(MonoString* label, MonoString* value)
{
    std::string inputValue = MonoStringToUtf8(value);

    if (ImGui::GetCurrentContext())
    {
        const std::string text = MonoStringToUtf8(label);
        const char* inputLabel = text.empty() ? "Text" : text.c_str();

        char buffer[256] = {};
        std::snprintf(buffer, sizeof(buffer), "%s", inputValue.c_str());
        ImGui::InputText(inputLabel, buffer, sizeof(buffer));
        inputValue = buffer;
    }

    MonoDomain* domain = mono_domain_get();
    return domain ? mono_string_new(domain, inputValue.c_str()) : nullptr;
}

static bool EditorImGui_InputFloat(MonoString* label, float* value, float step)
{
    if (!ImGui::GetCurrentContext() || !value)
        return false;

    const std::string text = MonoStringToUtf8(label);
    const char* inputLabel = text.empty() ? "Value" : text.c_str();
    return ImGui::InputFloat(inputLabel, value, step);
}

static void EditorImGui_Separator()
{
    if (ImGui::GetCurrentContext())
        ImGui::Separator();
}

static bool EditorImGui_Checkbox(MonoString* label, MonoBoolean* value)
{
    if (!ImGui::GetCurrentContext() || !value)
        return false;

    const std::string text = MonoStringToUtf8(label);
    const char* checkLabel = text.empty() ? "##cb" : text.c_str();
    bool v = (*value != 0);
    bool changed = ImGui::Checkbox(checkLabel, &v);
    *value = v ? 1 : 0;
    return changed;
}

static bool EditorImGui_IsWindowHovered()
{
    return ImGui::GetCurrentContext() ? ImGui::IsWindowHovered() : false;
}

static bool EditorImGui_GetWantCaptureMouse()
{
    return ImGui::GetCurrentContext() ? ImGui::GetIO().WantCaptureMouse : false;
}

static bool EditorImGui_GetWantCaptureKeyboard()
{
    return ImGui::GetCurrentContext() ? ImGui::GetIO().WantCaptureKeyboard : false;
}

static bool EditorImGui_IsMouseDown(int button)
{
    return ImGui::GetCurrentContext() ? ImGui::IsMouseDown(button) : false;
}

static float EditorImGui_GetMouseDeltaX()
{
    return ImGui::GetCurrentContext() ? ImGui::GetIO().MouseDelta.x : 0.0f;
}

static float EditorImGui_GetMouseDeltaY()
{
    return ImGui::GetCurrentContext() ? ImGui::GetIO().MouseDelta.y : 0.0f;
}

static float EditorImGui_GetMouseWheel()
{
    return ImGui::GetCurrentContext() ? ImGui::GetIO().MouseWheel : 0.0f;
}

static float EditorImGui_GetMousePosX()
{
    return ImGui::GetCurrentContext() ? ImGui::GetIO().MousePos.x : 0.0f;
}

static float EditorImGui_GetMousePosY()
{
    return ImGui::GetCurrentContext() ? ImGui::GetIO().MousePos.y : 0.0f;
}

static float EditorImGui_GetDisplayWidth()
{
    return ImGui::GetCurrentContext() ? ImGui::GetIO().DisplaySize.x : 0.0f;
}

static float EditorImGui_GetDisplayHeight()
{
    return ImGui::GetCurrentContext() ? ImGui::GetIO().DisplaySize.y : 0.0f;
}

static float EditorImGui_GetContentRegionAvailX()
{
    return ImGui::GetCurrentContext() ? ImGui::GetContentRegionAvail().x : 0.0f;
}

static float EditorImGui_GetContentRegionAvailY()
{
    return ImGui::GetCurrentContext() ? ImGui::GetContentRegionAvail().y : 0.0f;
}

static float EditorImGui_GetCursorScreenPosX()
{
    return ImGui::GetCurrentContext() ? ImGui::GetCursorScreenPos().x : 0.0f;
}

static float EditorImGui_GetCursorScreenPosY()
{
    return ImGui::GetCurrentContext() ? ImGui::GetCursorScreenPos().y : 0.0f;
}

static void EditorImGui_Image(std::uint64_t textureHandle, float width, float height)
{
    if (!ImGui::GetCurrentContext())
        return;

    ImTextureID textureId = reinterpret_cast<ImTextureID>(static_cast<uintptr_t>(textureHandle));
    ImGui::Image(textureId, ImVec2(width, height), ImVec2(0.0f, 1.0f), ImVec2(1.0f, 0.0f));
}

static bool EditorImGui_InvisibleButton(MonoString* id, float width, float height)
{
    if (!ImGui::GetCurrentContext())
        return false;

    const std::string value = MonoStringToUtf8(id);
    const char* buttonId = value.empty() ? "##InvisibleButton" : value.c_str();
    return ImGui::InvisibleButton(buttonId, ImVec2(width, height));
}

static bool EditorImGui_IsItemHovered()
{
    return ImGui::GetCurrentContext() ? ImGui::IsItemHovered() : false;
}

static bool EditorImGui_IsItemActive()
{
    return ImGui::GetCurrentContext() ? ImGui::IsItemActive() : false;
}

static bool EditorImGui_IsMouseClicked(int button)
{
    return ImGui::GetCurrentContext() ? ImGui::IsMouseClicked(button) : false;
}

static void EditorImGui_DrawLine(float x0,
                                 float y0,
                                 float x1,
                                 float y1,
                                 float r,
                                 float g,
                                 float b,
                                 float a,
                                 float thickness)
{
    if (!ImGui::GetCurrentContext())
        return;

    ImDrawList* drawList = ImGui::GetWindowDrawList();
    if (!drawList)
        return;

    drawList->AddLine(ImVec2(x0, y0),
                      ImVec2(x1, y1),
                      IM_COL32(static_cast<int>(r * 255.0f),
                               static_cast<int>(g * 255.0f),
                               static_cast<int>(b * 255.0f),
                               static_cast<int>(a * 255.0f)),
                      thickness);
}

static void EditorImGui_DrawRect(float x,
                                 float y,
                                 float width,
                                 float height,
                                 float r,
                                 float g,
                                 float b,
                                 float a,
                                 float thickness)
{
    if (!ImGui::GetCurrentContext())
        return;

    ImDrawList* drawList = ImGui::GetWindowDrawList();
    if (!drawList)
        return;

    drawList->AddRect(ImVec2(x, y),
                      ImVec2(x + width, y + height),
                      IM_COL32(static_cast<int>(r * 255.0f),
                               static_cast<int>(g * 255.0f),
                               static_cast<int>(b * 255.0f),
                               static_cast<int>(a * 255.0f)),
                      0.0f,
                      0,
                      thickness);
}

static bool EditorImGuizmo_IsUsing()
{
    return ImGui::GetCurrentContext() ? ImGuizmo::IsUsing() : false;
}

static bool EditorImGuizmo_Manipulate2DTranslate(float viewportX,
                                                 float viewportY,
                                                 float viewportWidth,
                                                 float viewportHeight,
                                                 float cameraX,
                                                 float cameraY,
                                                 float cameraZoom,
                                                 float* x,
                                                 float* y,
                                                 float objectWidth,
                                                 float objectHeight)
{
    if (!ImGui::GetCurrentContext() || !x || !y)
        return false;

    if (viewportWidth < 1.0f || viewportHeight < 1.0f)
        return false;

    float zoom = cameraZoom;
    if (!std::isfinite(zoom) || zoom < 0.01f)
        zoom = 0.01f;
    else if (zoom > 100.0f)
        zoom = 100.0f;

    const float halfWorldWidth = (viewportWidth * 0.5f) / zoom;
    const float halfWorldHeight = (viewportHeight * 0.5f) / zoom;
    const float left = cameraX - halfWorldWidth;
    const float right = cameraX + halfWorldWidth;
    const float bottom = cameraY - halfWorldHeight;
    const float top = cameraY + halfWorldHeight;

    const float view[16] = {
        1.0f, 0.0f, 0.0f, 0.0f,
        0.0f, 1.0f, 0.0f, 0.0f,
        0.0f, 0.0f, 1.0f, 0.0f,
        0.0f, 0.0f, 0.0f, 1.0f,
    };

    const float rl = 1.0f / (right - left);
    const float tb = 1.0f / (top - bottom);
    const float projection[16] = {
        2.0f * rl, 0.0f, 0.0f, 0.0f,
        0.0f, 2.0f * tb, 0.0f, 0.0f,
        0.0f, 0.0f, -1.0f, 0.0f,
        -(right + left) * rl, -(top + bottom) * tb, 0.0f, 1.0f,
    };

    float matrix[16] = {
        objectWidth, 0.0f, 0.0f, 0.0f,
        0.0f, objectHeight, 0.0f, 0.0f,
        0.0f, 0.0f, 1.0f, 0.0f,
        *x, *y, 0.0f, 1.0f,
    };

    ImGuizmo::SetOrthographic(true);
    ImGuizmo::SetDrawlist(ImGui::GetWindowDrawList());
    ImGuizmo::SetRect(viewportX, viewportY, viewportWidth, viewportHeight);

    const bool changed = ImGuizmo::Manipulate(view,
                                              projection,
                                              ImGuizmo::TRANSLATE,
                                              ImGuizmo::WORLD,
                                              matrix,
                                              nullptr,
                                              nullptr,
                                              nullptr,
                                              nullptr);

    if (changed)
    {
        *x = matrix[12];
        *y = matrix[13];
    }

    return changed;
}

static bool EngineInput_GetMouseButton(int button)
{
    return SDLInputState::GetMouseButton(button);
}

static bool EngineInput_GetMouseButtonDown(int button)
{
    return SDLInputState::GetMouseButtonDown(button);
}

static bool EngineInput_GetMouseButtonUp(int button)
{
    return SDLInputState::GetMouseButtonUp(button);
}

static float EngineInput_GetMouseDeltaX()
{
    return SDLInputState::GetMouseDeltaX();
}

static float EngineInput_GetMouseDeltaY()
{
    return SDLInputState::GetMouseDeltaY();
}

static float EngineInput_GetMouseWheel()
{
    return SDLInputState::GetMouseWheel();
}

static float EngineInput_GetMousePosX()
{
    return SDLInputState::GetMousePosX();
}

static float EngineInput_GetMousePosY()
{
    return SDLInputState::GetMousePosY();
}

static bool EngineInput_GetKey(int scancode)
{
    return SDLInputState::GetKey(scancode);
}

static bool EngineInput_GetKeyDown(int scancode)
{
    return SDLInputState::GetKeyDown(scancode);
}

static bool EngineInput_GetKeyUp(int scancode)
{
    return SDLInputState::GetKeyUp(scancode);
}
#endif

static std::filesystem::path FindScriptAssemblyPath(const std::filesystem::path& preferredPath, bool verbose = true)
{
    if (!preferredPath.empty())
    {
        if (std::filesystem::exists(preferredPath))
        {
            if (verbose)
                std::cout << "[Mono] Using project metadata script assembly: " << preferredPath << std::endl;
            return preferredPath;
        }

        if (verbose)
        {
            std::cout << "[Mono] Preferred script assembly not found: " << preferredPath << std::endl;
            std::cout << "[Mono] Falling back to legacy script assembly candidates..." << std::endl;
        }
    }

    const auto cwd = std::filesystem::current_path();
    const std::vector<std::filesystem::path> candidates = {
        cwd / "scripts" / "bin" / "Debug" / "net472" / "GameScripts.dll",
        cwd / "scripts" / "bin" / "Release" / "net472" / "GameScripts.dll",
        cwd / "scripts" / "bin" / "Debug" / "net8.0" / "GameScripts.dll",
        cwd / "scripts" / "bin" / "Release" / "net8.0" / "GameScripts.dll",
        cwd / "GameScripts.dll",
    };

    for (const auto& candidate : candidates)
    {
        if (std::filesystem::exists(candidate))
        {
            if (verbose)
                std::cout << "[Mono] Using legacy fallback script assembly: " << candidate << std::endl;
            return candidate;
        }
    }

    if (verbose && !preferredPath.empty())
        std::cout << "[Mono] Legacy fallback candidates also failed." << std::endl;

    return {};
}

static std::filesystem::path FindScriptProjectPath(const std::filesystem::path& preferredPath)
{
    if (!preferredPath.empty() && std::filesystem::exists(preferredPath))
        return preferredPath;

    const auto cwd = std::filesystem::current_path();
    const std::vector<std::filesystem::path> candidates = {
        cwd / "scripts" / "GameScripts.csproj",
    };

    for (const auto& candidate : candidates)
    {
        if (std::filesystem::exists(candidate))
            return candidate;
    }

    return {};
}

static std::filesystem::path FindEditorAssemblyPath()
{
    const auto cwd = std::filesystem::current_path();
    const std::vector<std::filesystem::path> candidates = {
        cwd / "editor" / "bin" / "Debug" / "net472" / "EngineEditor.dll",
        cwd / "editor" / "bin" / "Release" / "net472" / "EngineEditor.dll",
        cwd / "editor" / "bin" / "Debug" / "net8.0" / "EngineEditor.dll",
        cwd / "editor" / "bin" / "Release" / "net8.0" / "EngineEditor.dll",
        cwd / "EngineEditor.dll",
    };

    for (const auto& candidate : candidates)
    {
        if (std::filesystem::exists(candidate))
            return candidate;
    }

    return {};
}

static std::filesystem::path FindEditorProjectPath()
{
    const auto cwd = std::filesystem::current_path();
    const std::vector<std::filesystem::path> candidates = {
        cwd / "editor" / "EngineEditor.csproj",
    };

    for (const auto& candidate : candidates)
    {
        if (std::filesystem::exists(candidate))
            return candidate;
    }

    return {};
}

static bool TryBuildDotnetProject(const std::filesystem::path& projectPath, const char* projectLabel)
{
    if (projectPath.empty() || !std::filesystem::exists(projectPath))
    {
        std::cout << "[Mono] Skip build (project missing): " << projectLabel << std::endl;
        return false;
    }

    const std::string command = "dotnet build \"" + projectPath.string() + "\" -c Debug -nologo";
    std::cout << "[Mono] Building " << projectLabel << ": " << projectPath << std::endl;

    const int exitCode = std::system(command.c_str());
    if (exitCode == 0)
    {
        std::cout << "[Mono] Build succeeded for " << projectLabel << "." << std::endl;
        return true;
    }

    std::cerr << "[Mono] Build failed for " << projectLabel << " (exit code: " << exitCode << ")." << std::endl;
    return false;
}

static void ReportAssemblyAvailability(const std::filesystem::path& assemblyPath, const char* assemblyLabel)
{
    if (assemblyPath.empty())
    {
        std::cerr << "[Mono] " << assemblyLabel << " path could not be resolved." << std::endl;
        return;
    }

    if (!std::filesystem::exists(assemblyPath))
    {
        std::cerr << "[Mono] Missing " << assemblyLabel << ": " << assemblyPath << std::endl;
        return;
    }

    std::cout << "[Mono] Verified " << assemblyLabel << ": " << assemblyPath << std::endl;
}

#if ENGINE_MONO_RUNTIME_AVAILABLE
struct ScriptAssemblyBindings
{
    MonoAssembly* assembly = nullptr;
    MonoImage* image = nullptr;
    MonoClass* scriptClass = nullptr;
    MonoMethod* onStart = nullptr;
    MonoMethod* onUpdate = nullptr;
    MonoMethod* onShutdown = nullptr;
};

struct EditorAssemblyBindings
{
    MonoAssembly* assembly = nullptr;
    MonoImage* image = nullptr;
    MonoClass* editorClass = nullptr;
    MonoMethod* onStart = nullptr;
    MonoMethod* onUpdate = nullptr;
    MonoMethod* onShutdown = nullptr;
};

static bool TryGetFileWriteTime(const std::filesystem::path& path, std::filesystem::file_time_type& outWriteTime)
{
    std::error_code error;
    outWriteTime = std::filesystem::last_write_time(path, error);
    return !error;
}

static bool TryCreateAssemblyShadowCopy(const std::filesystem::path& sourcePath,
                                        const std::filesystem::path& shadowDirectory,
                                        std::filesystem::path& outShadowPath)
{
    if (sourcePath.empty() || shadowDirectory.empty())
        return false;

    std::error_code error;
    std::filesystem::create_directories(shadowDirectory, error);
    if (error)
        return false;

    static std::uint64_t shadowCopyCounter = 0;
    ++shadowCopyCounter;

    const auto timestampMs = std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now().time_since_epoch()).count();

    const std::string shadowName = sourcePath.stem().string() +
                                   "_shadow_" +
                                   std::to_string(timestampMs) +
                                   "_" +
                                   std::to_string(shadowCopyCounter) +
                                   sourcePath.extension().string();

    outShadowPath = shadowDirectory / shadowName;

    constexpr int maxAttempts = 6;
    for (int attempt = 0; attempt < maxAttempts; ++attempt)
    {
        error.clear();
        std::filesystem::copy_file(sourcePath, outShadowPath, std::filesystem::copy_options::overwrite_existing, error);
        if (!error)
            return true;

        std::this_thread::sleep_for(std::chrono::milliseconds(40));
    }

    return false;
}

static bool TryLoadScriptAssemblyBindings(MonoDomain* domain,
                                          const std::filesystem::path& shadowDirectory,
                                          const std::filesystem::path& sourcePath,
                                          ScriptAssemblyBindings& outBindings)
{
    if (!domain || sourcePath.empty())
        return false;

    std::filesystem::path pathToLoad;
    if (!TryCreateAssemblyShadowCopy(sourcePath, shadowDirectory, pathToLoad))
    {
        std::cerr << "[Mono] Hot-reload copy failed for script assembly: " << sourcePath << std::endl;
        return false;
    }

    const std::string loadPathString = pathToLoad.string();
    outBindings.assembly = mono_domain_assembly_open(domain, loadPathString.c_str());
    if (!outBindings.assembly)
    {
        std::cerr << "[Mono] Failed to load script assembly shadow copy: " << loadPathString << std::endl;
        return false;
    }

    outBindings.image = mono_assembly_get_image(outBindings.assembly);
    if (!outBindings.image)
    {
        std::cerr << "[Mono] Script assembly image is null: " << loadPathString << std::endl;
        return false;
    }

    outBindings.scriptClass = mono_class_from_name(outBindings.image, "GameScripts", "ScriptEntry");
    if (!outBindings.scriptClass)
    {
        std::cerr << "[Mono] Missing GameScripts.ScriptEntry in: " << loadPathString << std::endl;
        return true;
    }

    outBindings.onStart = mono_class_get_method_from_name(outBindings.scriptClass, "OnEngineStart", 0);
    outBindings.onUpdate = mono_class_get_method_from_name(outBindings.scriptClass, "OnEngineUpdate", 1);
    outBindings.onShutdown = mono_class_get_method_from_name(outBindings.scriptClass, "OnEngineShutdown", 0);
    return true;
}

static bool TryLoadEditorAssemblyBindings(MonoDomain* domain,
                                          const std::filesystem::path& shadowDirectory,
                                          const std::filesystem::path& sourcePath,
                                          EditorAssemblyBindings& outBindings)
{
    if (!domain || sourcePath.empty())
        return false;

    std::filesystem::path pathToLoad;
    if (!TryCreateAssemblyShadowCopy(sourcePath, shadowDirectory, pathToLoad))
    {
        std::cerr << "[Mono] Hot-reload copy failed for editor assembly: " << sourcePath << std::endl;
        return false;
    }

    const std::string loadPathString = pathToLoad.string();
    outBindings.assembly = mono_domain_assembly_open(domain, loadPathString.c_str());
    if (!outBindings.assembly)
    {
        std::cerr << "[Mono] Failed to load editor assembly shadow copy: " << loadPathString << std::endl;
        return false;
    }

    outBindings.image = mono_assembly_get_image(outBindings.assembly);
    if (!outBindings.image)
    {
        std::cerr << "[Mono] Editor assembly image is null: " << loadPathString << std::endl;
        return false;
    }

    outBindings.editorClass = mono_class_from_name(outBindings.image, "EngineEditor", "EditorHost");
    if (!outBindings.editorClass)
    {
        std::cerr << "[Mono] Missing EngineEditor.EditorHost in: " << loadPathString << std::endl;
        return false;
    }

    outBindings.onStart = mono_class_get_method_from_name(outBindings.editorClass, "OnEditorStart", 0);
    outBindings.onUpdate = mono_class_get_method_from_name(outBindings.editorClass, "OnEditorUpdate", 1);
    outBindings.onShutdown = mono_class_get_method_from_name(outBindings.editorClass, "OnEditorShutdown", 0);

    if (!outBindings.onUpdate)
    {
        std::cerr << "[Mono] OnEditorUpdate(float) missing in EngineEditor.EditorHost: " << loadPathString << std::endl;
        return false;
    }

    return true;
}
#endif

MonoRuntime::MonoRuntime() = default;
MonoRuntime::~MonoRuntime()
{
    Shutdown();
}

void MonoRuntime::SetPreferredScriptAssemblyPath(const std::string& assemblyPath)
{
    if (!m_impl)
        m_impl = std::make_unique<Impl>();

    m_impl->preferredScriptAssemblyPath = std::filesystem::path(assemblyPath);
}

void MonoRuntime::SetPreferredScriptProjectPath(const std::string& projectPath)
{
    if (!m_impl)
        m_impl = std::make_unique<Impl>();

    m_impl->preferredScriptProjectPath = std::filesystem::path(projectPath);
}

bool MonoRuntime::Initialize()
{
#if !ENGINE_MONO_RUNTIME_AVAILABLE
    std::cout << "[Mono] Scripting disabled at compile time." << std::endl;
    return true;
#else
    if (m_impl && m_impl->domain)
        return true;

    if (!m_impl)
        m_impl = std::make_unique<Impl>();

#ifdef ENGINE_MONO_ROOT_PATH
    const std::string monoRootPath = ENGINE_MONO_ROOT_PATH;
    const std::string monoLibPath = monoRootPath + "/lib";
    const std::string monoEtcPath = monoRootPath + "/etc";
    mono_set_dirs(monoLibPath.c_str(), monoEtcPath.c_str());
#endif

    mono_config_parse(nullptr);
    m_impl->domain = mono_jit_init_version("CppCSharpGameEngine", "v4.0.30319");
    if (!m_impl->domain)
    {
        std::cerr << "[Mono] Failed to initialize Mono JIT domain." << std::endl;
        return false;
    }

    mono_add_internal_call("Engine.EditorBridge::GetEntityCount", (const void*)&EditorBridge_GetEntityCount);
    mono_add_internal_call("Engine.EditorBridge::GetEntityIdAtIndex", (const void*)&EditorBridge_GetEntityIdAtIndex);
    mono_add_internal_call("Engine.EditorBridge::IsEntityValid", (const void*)&EditorBridge_IsEntityValid);
    mono_add_internal_call("Engine.EditorBridge::CreateEntity", (const void*)&EditorBridge_CreateEntity);
    mono_add_internal_call("Engine.EditorBridge::DestroyEntity", (const void*)&EditorBridge_DestroyEntity);
    mono_add_internal_call("Engine.EditorBridge::GetScriptedEntityCount", (const void*)&EditorBridge_GetScriptedEntityCount);
    mono_add_internal_call("Engine.EditorBridge::HasTransform", (const void*)&EditorBridge_HasTransform);
    mono_add_internal_call("Engine.EditorBridge::AddTransform", (const void*)&EditorBridge_AddTransform);
    mono_add_internal_call("Engine.EditorBridge::GetTransform", (const void*)&EditorBridge_GetTransform);
    mono_add_internal_call("Engine.EditorBridge::SetTransform", (const void*)&EditorBridge_SetTransform);
    mono_add_internal_call("Engine.EditorBridge::HasCamera", (const void*)&EditorBridge_HasCamera);
    mono_add_internal_call("Engine.EditorBridge::AddCamera", (const void*)&EditorBridge_AddCamera);
    mono_add_internal_call("Engine.EditorBridge::GetCamera", (const void*)&EditorBridge_GetCamera);
    mono_add_internal_call("Engine.EditorBridge::SetCamera", (const void*)&EditorBridge_SetCamera);
    mono_add_internal_call("Engine.EditorBridge::RemoveCamera", (const void*)&EditorBridge_RemoveCamera);
    mono_add_internal_call("Engine.EditorBridge::HasSprite", (const void*)&EditorBridge_HasSprite);
    mono_add_internal_call("Engine.EditorBridge::AddSprite", (const void*)&EditorBridge_AddSprite);
    mono_add_internal_call("Engine.EditorBridge::RemoveSprite", (const void*)&EditorBridge_RemoveSprite);
    mono_add_internal_call("Engine.EditorBridge::HasScript", (const void*)&EditorBridge_HasScript);
    mono_add_internal_call("Engine.EditorBridge::AddScript", (const void*)&EditorBridge_AddScript);
    mono_add_internal_call("Engine.EditorBridge::RemoveScript", (const void*)&EditorBridge_RemoveScript);
    mono_add_internal_call("Engine.EditorBridge::SetScriptEnabled", (const void*)&EditorBridge_SetScriptEnabled);
    mono_add_internal_call("Engine.EditorBridge::GetScriptEnabled", (const void*)&EditorBridge_GetScriptEnabled);
    mono_add_internal_call("Engine.EditorBridge::SetGameViewSize", (const void*)&EditorBridge_SetGameViewSize);
    mono_add_internal_call("Engine.EditorBridge::GetGameViewTextureHandle", (const void*)&EditorBridge_GetGameViewTextureHandle);

    mono_add_internal_call("Engine.DebugDraw::LineInternal", (const void*)&EditorDebugDraw_Line);
    mono_add_internal_call("Engine.DebugDraw::CircleInternal", (const void*)&EditorDebugDraw_Circle);
    mono_add_internal_call("Engine.DebugDraw::FilledCircleInternal", (const void*)&EditorDebugDraw_FilledCircle);
    mono_add_internal_call("Engine.DebugDraw::RectInternal", (const void*)&EditorDebugDraw_Rect);
    mono_add_internal_call("Engine.DebugDraw::FilledRectInternal", (const void*)&EditorDebugDraw_FilledRect);
    mono_add_internal_call("Engine.DebugDraw::ClearInternal", (const void*)&EditorDebugDraw_Clear);

    mono_add_internal_call("Engine.Explorer::PickFolderInternal", (const void*)&EditorExplorer_PickFolder);
    mono_add_internal_call("Engine.Explorer::PickFileInternal", (const void*)&EditorExplorer_PickFile);
    mono_add_internal_call("Engine.Explorer::PickFilesInternal", (const void*)&EditorExplorer_PickFiles);

    mono_add_internal_call("Engine.ImGui::Begin", (const void*)&EditorImGui_Begin);
    mono_add_internal_call("Engine.ImGui::BeginChild", (const void*)&EditorImGui_BeginChild);
    mono_add_internal_call("Engine.ImGui::End", (const void*)&EditorImGui_End);
    mono_add_internal_call("Engine.ImGui::EndChild", (const void*)&EditorImGui_EndChild);
    mono_add_internal_call("Engine.ImGui::Text", (const void*)&EditorImGui_Text);
    mono_add_internal_call("Engine.ImGui::Button", (const void*)&EditorImGui_Button);
    mono_add_internal_call("Engine.ImGui::OpenPopup", (const void*)&EditorImGui_OpenPopup);
    mono_add_internal_call("Engine.ImGui::BeginPopupModal", (const void*)&EditorImGui_BeginPopupModal);
    mono_add_internal_call("Engine.ImGui::EndPopup", (const void*)&EditorImGui_EndPopup);
    mono_add_internal_call("Engine.ImGui::CloseCurrentPopup", (const void*)&EditorImGui_CloseCurrentPopup);
    mono_add_internal_call("Engine.ImGui::SameLine", (const void*)&EditorImGui_SameLine);
    mono_add_internal_call("Engine.ImGui::SetNextItemWidth", (const void*)&EditorImGui_SetNextItemWidth);
    mono_add_internal_call("Engine.ImGui::Selectable", (const void*)&EditorImGui_Selectable);
    mono_add_internal_call("Engine.ImGui::SelectableNoClose", (const void*)&EditorImGui_SelectableNoClose);
    mono_add_internal_call("Engine.ImGui::InputText", (const void*)&EditorImGui_InputText);
    mono_add_internal_call("Engine.ImGui::InputFloat", (const void*)&EditorImGui_InputFloat);
    mono_add_internal_call("Engine.ImGui::Separator", (const void*)&EditorImGui_Separator);
    mono_add_internal_call("Engine.ImGui::Checkbox", (const void*)&EditorImGui_Checkbox);
    mono_add_internal_call("Engine.ImGui::IsWindowHovered", (const void*)&EditorImGui_IsWindowHovered);
    mono_add_internal_call("Engine.ImGui::GetWantCaptureMouse", (const void*)&EditorImGui_GetWantCaptureMouse);
    mono_add_internal_call("Engine.ImGui::GetWantCaptureKeyboard", (const void*)&EditorImGui_GetWantCaptureKeyboard);
    mono_add_internal_call("Engine.ImGui::IsMouseDown", (const void*)&EditorImGui_IsMouseDown);
    mono_add_internal_call("Engine.ImGui::GetMouseDeltaX", (const void*)&EditorImGui_GetMouseDeltaX);
    mono_add_internal_call("Engine.ImGui::GetMouseDeltaY", (const void*)&EditorImGui_GetMouseDeltaY);
    mono_add_internal_call("Engine.ImGui::GetMouseWheel", (const void*)&EditorImGui_GetMouseWheel);
    mono_add_internal_call("Engine.ImGui::GetMousePosX", (const void*)&EditorImGui_GetMousePosX);
    mono_add_internal_call("Engine.ImGui::GetMousePosY", (const void*)&EditorImGui_GetMousePosY);
    mono_add_internal_call("Engine.ImGui::GetDisplayWidth", (const void*)&EditorImGui_GetDisplayWidth);
    mono_add_internal_call("Engine.ImGui::GetDisplayHeight", (const void*)&EditorImGui_GetDisplayHeight);
    mono_add_internal_call("Engine.ImGui::GetContentRegionAvailX", (const void*)&EditorImGui_GetContentRegionAvailX);
    mono_add_internal_call("Engine.ImGui::GetContentRegionAvailY", (const void*)&EditorImGui_GetContentRegionAvailY);
    mono_add_internal_call("Engine.ImGui::GetCursorScreenPosX", (const void*)&EditorImGui_GetCursorScreenPosX);
    mono_add_internal_call("Engine.ImGui::GetCursorScreenPosY", (const void*)&EditorImGui_GetCursorScreenPosY);
    mono_add_internal_call("Engine.ImGui::Image", (const void*)&EditorImGui_Image);
    mono_add_internal_call("Engine.ImGui::InvisibleButton", (const void*)&EditorImGui_InvisibleButton);
    mono_add_internal_call("Engine.ImGui::IsItemHovered", (const void*)&EditorImGui_IsItemHovered);
    mono_add_internal_call("Engine.ImGui::IsItemActive", (const void*)&EditorImGui_IsItemActive);
    mono_add_internal_call("Engine.ImGui::IsMouseClicked", (const void*)&EditorImGui_IsMouseClicked);
    mono_add_internal_call("Engine.ImGui::DrawLine", (const void*)&EditorImGui_DrawLine);
    mono_add_internal_call("Engine.ImGui::DrawRect", (const void*)&EditorImGui_DrawRect);

    mono_add_internal_call("Engine.ImGuizmo::IsUsing", (const void*)&EditorImGuizmo_IsUsing);
    mono_add_internal_call("Engine.ImGuizmo::Manipulate2DTranslate", (const void*)&EditorImGuizmo_Manipulate2DTranslate);

    mono_add_internal_call("Engine.Input::GetMouseButton", (const void*)&EngineInput_GetMouseButton);
    mono_add_internal_call("Engine.Input::GetMouseButtonDown", (const void*)&EngineInput_GetMouseButtonDown);
    mono_add_internal_call("Engine.Input::GetMouseButtonUp", (const void*)&EngineInput_GetMouseButtonUp);
    mono_add_internal_call("Engine.Input::GetMouseDeltaX", (const void*)&EngineInput_GetMouseDeltaX);
    mono_add_internal_call("Engine.Input::GetMouseDeltaY", (const void*)&EngineInput_GetMouseDeltaY);
    mono_add_internal_call("Engine.Input::GetMouseWheel", (const void*)&EngineInput_GetMouseWheel);
    mono_add_internal_call("Engine.Input::GetMousePosX", (const void*)&EngineInput_GetMousePosX);
    mono_add_internal_call("Engine.Input::GetMousePosY", (const void*)&EngineInput_GetMousePosY);
    mono_add_internal_call("Engine.Input::GetKey", (const void*)&EngineInput_GetKey);
    mono_add_internal_call("Engine.Input::GetKeyDown", (const void*)&EngineInput_GetKeyDown);
    mono_add_internal_call("Engine.Input::GetKeyUp", (const void*)&EngineInput_GetKeyUp);

    m_impl->shadowCopyDirectory = std::filesystem::current_path() / ".mono_cache";
    std::error_code shadowError;
    std::filesystem::create_directories(m_impl->shadowCopyDirectory, shadowError);

    const std::filesystem::path scriptProjectPath = FindScriptProjectPath(m_impl->preferredScriptProjectPath);
    if (!scriptProjectPath.empty())
        TryBuildDotnetProject(scriptProjectPath, "script project");
    else
        std::cout << "[Mono] Script project file not found; skipping script build step." << std::endl;

    const std::filesystem::path editorProjectPath = FindEditorProjectPath();
    if (!editorProjectPath.empty())
        TryBuildDotnetProject(editorProjectPath, "editor project");
    else
        std::cout << "[Mono] Editor project file not found; skipping editor build step." << std::endl;

    const auto assemblyPath = FindScriptAssemblyPath(m_impl->preferredScriptAssemblyPath);
    ReportAssemblyAvailability(assemblyPath, "script assembly");
    if (assemblyPath.empty())
    {
        std::cout << "[Mono] No script assembly found (metadata path + legacy candidates checked)." << std::endl;
    }
    else
    {
        ScriptAssemblyBindings scriptBindings;
        if (!TryLoadScriptAssemblyBindings(m_impl->domain, m_impl->shadowCopyDirectory, assemblyPath, scriptBindings))
        {
            std::cerr << "[Mono] Failed to load assembly: " << assemblyPath << std::endl;
        }
        else
        {
            m_impl->assembly = scriptBindings.assembly;
            m_impl->image = scriptBindings.image;
            m_impl->scriptClass = scriptBindings.scriptClass;
            m_impl->onStart = scriptBindings.onStart;
            m_impl->onUpdate = scriptBindings.onUpdate;
            m_impl->onShutdown = scriptBindings.onShutdown;
            m_impl->scriptLoaded = true;

            m_impl->resolvedScriptAssemblyPath = assemblyPath;
            m_impl->hasScriptAssemblyWriteTime = TryGetFileWriteTime(assemblyPath, m_impl->scriptAssemblyWriteTime);

            if (m_impl->onStart)
                mono_runtime_invoke(m_impl->onStart, nullptr, nullptr, nullptr);

            std::cout << "[Mono] Loaded script assembly: " << assemblyPath << std::endl;
        }
    }

    const auto editorPath = FindEditorAssemblyPath();
    ReportAssemblyAvailability(editorPath, "editor assembly");
    if (editorPath.empty())
    {
        std::cout << "[Mono] No editor assembly found (expected editor/bin/*/net472/EngineEditor.dll)." << std::endl;
        return true;
    }

    EditorAssemblyBindings editorBindings;
    if (!TryLoadEditorAssemblyBindings(m_impl->domain, m_impl->shadowCopyDirectory, editorPath, editorBindings))
    {
        std::cerr << "[Mono] Failed to load editor assembly: " << editorPath << std::endl;
        return true;
    }

    m_impl->editorAssembly = editorBindings.assembly;
    m_impl->editorImage = editorBindings.image;
    m_impl->editorClass = editorBindings.editorClass;
    m_impl->editorOnStart = editorBindings.onStart;
    m_impl->editorOnUpdate = editorBindings.onUpdate;
    m_impl->editorOnShutdown = editorBindings.onShutdown;
    m_impl->editorLoaded = true;

    m_impl->resolvedEditorAssemblyPath = editorPath;
    m_impl->hasEditorAssemblyWriteTime = TryGetFileWriteTime(editorPath, m_impl->editorAssemblyWriteTime);

    if (m_impl->editorOnStart)
        mono_runtime_invoke(m_impl->editorOnStart, nullptr, nullptr, nullptr);

    std::cout << "[Mono] Loaded editor assembly: " << editorPath << std::endl;
    return true;
#endif
}

void MonoRuntime::Update(float deltaTime, Scene* scene, Renderer* renderer)
{
#if !ENGINE_MONO_RUNTIME_AVAILABLE
    (void)deltaTime;
    (void)scene;
    (void)renderer;
#else
    if (!m_impl)
        return;

    m_impl->hotReloadPollAccumulator += deltaTime;
    if (m_impl->hotReloadPollAccumulator >= 0.5f)
    {
        m_impl->hotReloadPollAccumulator = 0.0f;

        const auto scriptPath = FindScriptAssemblyPath(m_impl->preferredScriptAssemblyPath, false);
        if (!scriptPath.empty())
        {
            std::filesystem::file_time_type scriptWriteTime{};
            const bool hasScriptWriteTime = TryGetFileWriteTime(scriptPath, scriptWriteTime);

            const bool scriptPathChanged = !m_impl->resolvedScriptAssemblyPath.empty() &&
                                           (scriptPath != m_impl->resolvedScriptAssemblyPath);
            const bool scriptTimeChanged = hasScriptWriteTime &&
                                           m_impl->hasScriptAssemblyWriteTime &&
                                           (scriptWriteTime != m_impl->scriptAssemblyWriteTime);
            const bool scriptNeedsLoad = !m_impl->scriptLoaded;

            if (scriptNeedsLoad || scriptPathChanged || scriptTimeChanged)
            {
                ScriptAssemblyBindings newScriptBindings;
                if (TryLoadScriptAssemblyBindings(m_impl->domain, m_impl->shadowCopyDirectory, scriptPath, newScriptBindings))
                {
                    if (scene)
                    {
                        auto& registry = scene->Registry();
                        for (auto& [entityId, instance] : m_impl->entityScripts)
                        {
                            const auto entity = static_cast<entt::entity>(entityId);
                            if (instance.created && instance.onDestroy && registry.valid(entity))
                            {
                                std::uint32_t destroyEntityId = entityId;
                                void* destroyArgs[1] = { (void*)&destroyEntityId };
                                mono_runtime_invoke(instance.onDestroy, instance.instance, destroyArgs, nullptr);
                            }
                        }
                    }

                    m_impl->entityScripts.clear();

                    if (m_impl->scriptLoaded && m_impl->onShutdown)
                        mono_runtime_invoke(m_impl->onShutdown, nullptr, nullptr, nullptr);

                    m_impl->assembly = newScriptBindings.assembly;
                    m_impl->image = newScriptBindings.image;
                    m_impl->scriptClass = newScriptBindings.scriptClass;
                    m_impl->onStart = newScriptBindings.onStart;
                    m_impl->onUpdate = newScriptBindings.onUpdate;
                    m_impl->onShutdown = newScriptBindings.onShutdown;
                    m_impl->scriptLoaded = true;

                    m_impl->resolvedScriptAssemblyPath = scriptPath;
                    m_impl->hasScriptAssemblyWriteTime = hasScriptWriteTime;
                    if (hasScriptWriteTime)
                        m_impl->scriptAssemblyWriteTime = scriptWriteTime;

                    if (m_impl->onStart)
                        mono_runtime_invoke(m_impl->onStart, nullptr, nullptr, nullptr);

                    if (scriptNeedsLoad)
                        std::cout << "[Mono] Script assembly became available: " << scriptPath << std::endl;
                    else
                        std::cout << "[Mono] Hot-reloaded script assembly: " << scriptPath << std::endl;
                }
            }
        }

        const auto editorPath = FindEditorAssemblyPath();
        if (!editorPath.empty())
        {
            std::filesystem::file_time_type editorWriteTime{};
            const bool hasEditorWriteTime = TryGetFileWriteTime(editorPath, editorWriteTime);

            const bool editorPathChanged = !m_impl->resolvedEditorAssemblyPath.empty() &&
                                           (editorPath != m_impl->resolvedEditorAssemblyPath);
            const bool editorTimeChanged = hasEditorWriteTime &&
                                           m_impl->hasEditorAssemblyWriteTime &&
                                           (editorWriteTime != m_impl->editorAssemblyWriteTime);
            const bool editorNeedsLoad = !m_impl->editorLoaded;

            if (editorNeedsLoad || editorPathChanged || editorTimeChanged)
            {
                EditorAssemblyBindings newEditorBindings;
                if (TryLoadEditorAssemblyBindings(m_impl->domain, m_impl->shadowCopyDirectory, editorPath, newEditorBindings))
                {
                    if (m_impl->editorLoaded && m_impl->editorOnShutdown)
                        mono_runtime_invoke(m_impl->editorOnShutdown, nullptr, nullptr, nullptr);

                    m_impl->editorAssembly = newEditorBindings.assembly;
                    m_impl->editorImage = newEditorBindings.image;
                    m_impl->editorClass = newEditorBindings.editorClass;
                    m_impl->editorOnStart = newEditorBindings.onStart;
                    m_impl->editorOnUpdate = newEditorBindings.onUpdate;
                    m_impl->editorOnShutdown = newEditorBindings.onShutdown;
                    m_impl->editorLoaded = true;

                    m_impl->resolvedEditorAssemblyPath = editorPath;
                    m_impl->hasEditorAssemblyWriteTime = hasEditorWriteTime;
                    if (hasEditorWriteTime)
                        m_impl->editorAssemblyWriteTime = editorWriteTime;

                    if (m_impl->editorOnStart)
                        mono_runtime_invoke(m_impl->editorOnStart, nullptr, nullptr, nullptr);

                    if (editorNeedsLoad)
                        std::cout << "[Mono] Editor assembly became available: " << editorPath << std::endl;
                    else
                        std::cout << "[Mono] Hot-reloaded editor assembly: " << editorPath << std::endl;
                }
            }
        }
    }

    g_editorSceneContext = scene;
    g_editorRendererContext = nullptr;
    g_editorRendererContext = renderer;

    if (m_impl->editorLoaded && m_impl->editorOnUpdate)
    {
        void* editorArgs[1] = { &deltaTime };
        mono_runtime_invoke(m_impl->editorOnUpdate, nullptr, editorArgs, nullptr);
    }

    if (m_impl->onUpdate)
    {
        void* args[1] = { &deltaTime };
        mono_runtime_invoke(m_impl->onUpdate, nullptr, args, nullptr);
    }

    if (!scene || !m_impl->scriptLoaded || !m_impl->image)
    {
        g_editorSceneContext = nullptr;
        g_editorRendererContext = nullptr;
        return;
    }

    auto& registry = scene->Registry();
    auto view = registry.view<ScriptComponent>();

    for (const auto entity : view)
    {
        auto& script = view.get<ScriptComponent>(entity);
        if (!script.enabled)
            continue;

        const std::uint32_t entityId = static_cast<std::uint32_t>(entt::to_integral(entity));
        auto [instanceIt, inserted] = m_impl->entityScripts.try_emplace(entityId);
        auto& instance = instanceIt->second;

        if (inserted)
        {
            MonoClass* klass = mono_class_from_name(m_impl->image, script.classNamespace.c_str(), script.className.c_str());
            if (!klass)
            {
                std::cerr << "[Mono] Script class not found: "
                          << script.classNamespace << "." << script.className << std::endl;
                m_impl->entityScripts.erase(instanceIt);
                continue;
            }

            instance.instance = mono_object_new(m_impl->domain, klass);
            if (!instance.instance)
            {
                std::cerr << "[Mono] Failed to create script instance for entity." << std::endl;
                m_impl->entityScripts.erase(instanceIt);
                continue;
            }

            mono_runtime_object_init(instance.instance);
            instance.onCreate = mono_class_get_method_from_name(klass, "OnCreate", 1);
            instance.onUpdate = mono_class_get_method_from_name(klass, "OnUpdate", 2);
            instance.onDestroy = mono_class_get_method_from_name(klass, "OnDestroy", 1);
        }

        if (!instance.created)
        {
            if (instance.onCreate)
            {
                void* createArgs[1] = { (void*)&entityId };
                mono_runtime_invoke(instance.onCreate, instance.instance, createArgs, nullptr);
            }
            instance.created = true;
        }

        if (instance.onUpdate)
        {
            void* updateArgs[2] = { (void*)&entityId, (void*)&deltaTime };
            mono_runtime_invoke(instance.onUpdate, instance.instance, updateArgs, nullptr);
        }
    }

    for (auto it = m_impl->entityScripts.begin(); it != m_impl->entityScripts.end();)
    {
        const auto entity = static_cast<entt::entity>(it->first);
        const bool hasComponent = registry.valid(entity) && registry.all_of<ScriptComponent>(entity);
        const bool isEnabled = hasComponent ? registry.get<ScriptComponent>(entity).enabled : false;

        if (!hasComponent || !isEnabled)
        {
            if (it->second.created && it->second.onDestroy)
            {
                const std::uint32_t entityId = it->first;
                void* destroyArgs[1] = { (void*)&entityId };
                mono_runtime_invoke(it->second.onDestroy, it->second.instance, destroyArgs, nullptr);
            }

            it = m_impl->entityScripts.erase(it);
        }
        else
        {
            ++it;
        }
    }

    g_editorSceneContext = nullptr;
    g_editorRendererContext = nullptr;
    g_editorRendererContext = nullptr;
#endif
}

void MonoRuntime::Shutdown(Scene* scene)
{
#if !ENGINE_MONO_RUNTIME_AVAILABLE
    (void)scene;
    return;
#else
    if (!m_impl)
        return;

    g_editorSceneContext = scene;

    if (m_impl->editorLoaded && m_impl->editorOnShutdown)
        mono_runtime_invoke(m_impl->editorOnShutdown, nullptr, nullptr, nullptr);

    g_editorSceneContext = nullptr;

    if (scene)
    {
        auto& registry = scene->Registry();
        for (auto& [entityId, instance] : m_impl->entityScripts)
        {
            const auto entity = static_cast<entt::entity>(entityId);
            if (instance.created && instance.onDestroy && registry.valid(entity))
            {
                std::uint32_t destroyEntityId = entityId;
                void* destroyArgs[1] = { (void*)&destroyEntityId };
                mono_runtime_invoke(instance.onDestroy, instance.instance, destroyArgs, nullptr);
            }
        }
    }

    m_impl->entityScripts.clear();

    if (m_impl->scriptLoaded && m_impl->onShutdown)
        mono_runtime_invoke(m_impl->onShutdown, nullptr, nullptr, nullptr);

    if (m_impl->domain)
    {
        mono_jit_cleanup(m_impl->domain);
        m_impl->domain = nullptr;
    }

    m_impl.reset();
#endif
}

bool MonoRuntime::IsScriptLoaded() const
{
    return m_impl && m_impl->scriptLoaded;
}

bool MonoRuntime::IsEditorLoaded() const
{
    return m_impl && m_impl->editorLoaded;
}
