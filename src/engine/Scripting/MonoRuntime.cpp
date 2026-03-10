#include "MonoRuntime.h"

#include "engine/ECS/Components.h"
#include "engine/ECS/Scene.h"
#include "engine/Platform/ExplorerDialog.h"

#include <filesystem>
#include <cstdio>
#include <cstdint>
#include <chrono>
#include <iostream>
#include <string>
#include <thread>
#include <unordered_map>
#include <vector>

#include <entt/entt.hpp>
#include <imgui.h>

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
    bool scriptLoaded = false;
    bool editorLoaded = false;
};

#if ENGINE_MONO_RUNTIME_AVAILABLE
static Scene* g_editorSceneContext = nullptr;

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

    auto& registry = g_editorSceneContext->Registry();
    const auto entity = static_cast<entt::entity>(entityId);
    return registry.valid(entity);
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

    g_editorSceneContext->DestroyEntity(static_cast<entt::entity>(entityId));
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

    auto& registry = g_editorSceneContext->Registry();
    const auto entity = static_cast<entt::entity>(entityId);
    if (!registry.valid(entity) || !registry.all_of<ScriptComponent>(entity))
        return;

    registry.get<ScriptComponent>(entity).enabled = enabled;
}

static bool EditorBridge_GetScriptEnabled(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return false;

    auto& registry = g_editorSceneContext->Registry();
    const auto entity = static_cast<entt::entity>(entityId);
    if (!registry.valid(entity) || !registry.all_of<ScriptComponent>(entity))
        return false;

    return registry.get<ScriptComponent>(entity).enabled;
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

    auto& registry = g_editorSceneContext->Registry();
    const auto entity = static_cast<entt::entity>(entityId);
    return registry.valid(entity) && registry.all_of<TransformComponent>(entity);
}

static void EditorBridge_AddTransform(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return;

    auto& registry = g_editorSceneContext->Registry();
    const auto entity = static_cast<entt::entity>(entityId);
    if (!registry.valid(entity) || registry.all_of<TransformComponent>(entity))
        return;

    auto& transform = registry.emplace<TransformComponent>(entity);
    transform.x = 0.0f;
    transform.y = 0.0f;
    transform.width = 100.0f;
    transform.height = 100.0f;
}

static bool EditorBridge_GetTransform(std::uint32_t entityId, float* x, float* y, float* width, float* height)
{
    if (!g_editorSceneContext)
        return false;

    auto& registry = g_editorSceneContext->Registry();
    const auto entity = static_cast<entt::entity>(entityId);
    if (!registry.valid(entity) || !registry.all_of<TransformComponent>(entity))
        return false;

    const auto& transform = registry.get<TransformComponent>(entity);
    if (x)
        *x = transform.x;
    if (y)
        *y = transform.y;
    if (width)
        *width = transform.width;
    if (height)
        *height = transform.height;
    return true;
}

static void EditorBridge_SetTransform(std::uint32_t entityId, float x, float y, float width, float height)
{
    if (!g_editorSceneContext)
        return;

    auto& registry = g_editorSceneContext->Registry();
    const auto entity = static_cast<entt::entity>(entityId);
    if (!registry.valid(entity) || !registry.all_of<TransformComponent>(entity))
        return;

    auto& transform = registry.get<TransformComponent>(entity);
    transform.x = x;
    transform.y = y;
    transform.width = width;
    transform.height = height;
}

static bool EditorBridge_HasScript(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return false;

    auto& registry = g_editorSceneContext->Registry();
    const auto entity = static_cast<entt::entity>(entityId);
    return registry.valid(entity) && registry.all_of<ScriptComponent>(entity);
}

static void EditorBridge_AddScript(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return;

    auto& registry = g_editorSceneContext->Registry();
    const auto entity = static_cast<entt::entity>(entityId);
    if (!registry.valid(entity) || registry.all_of<ScriptComponent>(entity))
        return;

    registry.emplace<ScriptComponent>(entity);
}

static void EditorBridge_RemoveScript(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return;

    auto& registry = g_editorSceneContext->Registry();
    const auto entity = static_cast<entt::entity>(entityId);
    if (!registry.valid(entity) || !registry.all_of<ScriptComponent>(entity))
        return;

    registry.remove<ScriptComponent>(entity);
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
    mono_add_internal_call("Engine.EditorBridge::HasScript", (const void*)&EditorBridge_HasScript);
    mono_add_internal_call("Engine.EditorBridge::AddScript", (const void*)&EditorBridge_AddScript);
    mono_add_internal_call("Engine.EditorBridge::RemoveScript", (const void*)&EditorBridge_RemoveScript);
    mono_add_internal_call("Engine.EditorBridge::SetScriptEnabled", (const void*)&EditorBridge_SetScriptEnabled);
    mono_add_internal_call("Engine.EditorBridge::GetScriptEnabled", (const void*)&EditorBridge_GetScriptEnabled);

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

    m_impl->shadowCopyDirectory = std::filesystem::current_path() / ".mono_cache";
    std::error_code shadowError;
    std::filesystem::create_directories(m_impl->shadowCopyDirectory, shadowError);

    const auto assemblyPath = FindScriptAssemblyPath(m_impl->preferredScriptAssemblyPath);
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

void MonoRuntime::Update(float deltaTime, Scene* scene)
{
#if !ENGINE_MONO_RUNTIME_AVAILABLE
    (void)deltaTime;
    (void)scene;
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
