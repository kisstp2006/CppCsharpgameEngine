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
#include <cctype>
#include <ctime>
#include <iomanip>
#include <iostream>
#include <limits>
#include <map>
#include <optional>
#include <sstream>
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
        MonoMethod* onEnable = nullptr;
        MonoMethod* onDisable = nullptr;
        MonoMethod* onDestroy = nullptr;
        std::string classNamespace;
        std::string className;
        bool created = false;
        bool active = false;
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
    bool editorMode = false;
    bool scriptLoaded = false;
    bool editorLoaded = false;
};

#if ENGINE_MONO_RUNTIME_AVAILABLE
static MonoRuntime::Impl* g_monoRuntimeImplForEditorBridge = nullptr;
#endif

#include "editor/Mono/EditorMonoBridge.inl"

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

#include "MonoRuntime/MonoRuntime.Assembly.inl"
#include "editor/Mono/EditorMonoAssembly.inl"

MonoRuntime::MonoRuntime() = default;
MonoRuntime::~MonoRuntime()
{
    Shutdown();
}

void MonoRuntime::SetEditorMode(bool enabled)
{
    if (!m_impl)
        m_impl = std::make_unique<Impl>();

#if ENGINE_MONO_RUNTIME_AVAILABLE
    g_monoRuntimeImplForEditorBridge = m_impl.get();
#endif

    m_impl->editorMode = enabled;
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

    if (m_impl->editorMode)
    {
        mono_add_internal_call("Engine.EditorBridge::GetEntityCount", (const void*)&EditorBridge_GetEntityCount);
        mono_add_internal_call("Engine.EditorBridge::GetEntityIdAtIndex", (const void*)&EditorBridge_GetEntityIdAtIndex);
        mono_add_internal_call("Engine.EditorBridge::IsEntityValid", (const void*)&EditorBridge_IsEntityValid);
        mono_add_internal_call("Engine.EditorBridge::CreateEntity", (const void*)&EditorBridge_CreateEntity);
        mono_add_internal_call("Engine.EditorBridge::DestroyEntity", (const void*)&EditorBridge_DestroyEntity);
        mono_add_internal_call("Engine.EditorBridge::NewScene", (const void*)&EditorBridge_NewScene);
        mono_add_internal_call("Engine.EditorBridge::SaveScene", (const void*)&EditorBridge_SaveScene);
        mono_add_internal_call("Engine.EditorBridge::LoadScene", (const void*)&EditorBridge_LoadScene);
        mono_add_internal_call("Engine.EditorBridge::GetLastSceneIoStatus", (const void*)&EditorBridge_GetLastSceneIoStatus);
        mono_add_internal_call("Engine.EditorBridge::GetScriptedEntityCount", (const void*)&EditorBridge_GetScriptedEntityCount);
        mono_add_internal_call("Engine.EditorBridge::HasComponent", (const void*)&EditorBridge_HasComponent);
        mono_add_internal_call("Engine.EditorBridge::AddComponent", (const void*)&EditorBridge_AddComponent);
        mono_add_internal_call("Engine.EditorBridge::RemoveComponent", (const void*)&EditorBridge_RemoveComponent);
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
        mono_add_internal_call("Engine.EditorBridge::GetScriptTypeName", (const void*)&EditorBridge_GetScriptTypeName);
        mono_add_internal_call("Engine.EditorBridge::SetScriptTypeName", (const void*)&EditorBridge_SetScriptTypeName);
        mono_add_internal_call("Engine.EditorBridge::GetScriptFieldValue", (const void*)&EditorBridge_GetScriptFieldValue);
        mono_add_internal_call("Engine.EditorBridge::SetScriptFieldValue", (const void*)&EditorBridge_SetScriptFieldValue);
        mono_add_internal_call("Engine.EditorBridge::SetGameViewSize", (const void*)&EditorBridge_SetGameViewSize);
        mono_add_internal_call("Engine.EditorBridge::GetGameViewTextureHandle", (const void*)&EditorBridge_GetGameViewTextureHandle);
    }

    mono_add_internal_call("Engine.DebugDraw::LineInternal", (const void*)&EditorDebugDraw_Line);
    mono_add_internal_call("Engine.DebugDraw::CircleInternal", (const void*)&EditorDebugDraw_Circle);
    mono_add_internal_call("Engine.DebugDraw::FilledCircleInternal", (const void*)&EditorDebugDraw_FilledCircle);
    mono_add_internal_call("Engine.DebugDraw::RectInternal", (const void*)&EditorDebugDraw_Rect);
    mono_add_internal_call("Engine.DebugDraw::FilledRectInternal", (const void*)&EditorDebugDraw_FilledRect);
    mono_add_internal_call("Engine.DebugDraw::ClearInternal", (const void*)&EditorDebugDraw_Clear);
    mono_add_internal_call("Engine.Debug::LogInternal", (const void*)&EngineDebug_Log);
    mono_add_internal_call("Engine.Debug::LogWarningInternal", (const void*)&EngineDebug_LogWarning);
    mono_add_internal_call("Engine.Debug::LogErrorInternal", (const void*)&EngineDebug_LogError);
    mono_add_internal_call("Engine.Debug::GetLogCountInternal", (const void*)&EngineDebug_GetLogCount);
    mono_add_internal_call("Engine.Debug::GetLogMessageInternal", (const void*)&EngineDebug_GetLogMessage);
    mono_add_internal_call("Engine.Debug::GetLogLevelInternal", (const void*)&EngineDebug_GetLogLevel);
    mono_add_internal_call("Engine.Debug::ClearLogsInternal", (const void*)&EngineDebug_ClearLogs);

    if (m_impl->editorMode)
    {
        mono_add_internal_call("Engine.Explorer::PickFolderInternal", (const void*)&EditorExplorer_PickFolder);
        mono_add_internal_call("Engine.Explorer::PickFileInternal", (const void*)&EditorExplorer_PickFile);
        mono_add_internal_call("Engine.Explorer::PickFilesInternal", (const void*)&EditorExplorer_PickFiles);

        mono_add_internal_call("Engine.ImGui::Begin", (const void*)&EditorImGui_Begin);
        mono_add_internal_call("Engine.ImGui::BeginTopBar", (const void*)&EditorImGui_BeginTopBar);
        mono_add_internal_call("Engine.ImGui::EndTopBar", (const void*)&EditorImGui_EndTopBar);
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
    }

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

    if (m_impl->editorMode)
    {
        const std::filesystem::path editorProjectPath = FindEditorProjectPath();
        if (!editorProjectPath.empty())
            TryBuildDotnetProject(editorProjectPath, "editor project");
        else
            std::cout << "[Mono] Editor project file not found; skipping editor build step." << std::endl;
    }

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

    if (m_impl->editorMode)
    {
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
    }
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
                            if (instance.created && registry.valid(entity))
                            {
                                std::uint32_t lifecycleEntityId = entityId;

                                if (instance.active && instance.onDisable)
                                {
                                    void* disableArgs[1] = { (void*)&lifecycleEntityId };
                                    mono_runtime_invoke(instance.onDisable, instance.instance, disableArgs, nullptr);
                                    instance.active = false;
                                }

                                if (instance.onDestroy)
                                {
                                    void* destroyArgs[1] = { (void*)&lifecycleEntityId };
                                    mono_runtime_invoke(instance.onDestroy, instance.instance, destroyArgs, nullptr);
                                }
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

        if (m_impl->editorMode)
        {
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
    }

    if (m_impl->editorMode)
    {
        g_editorSceneContext = scene;
        g_editorRendererContext = renderer;

        if (m_impl->editorLoaded && m_impl->editorOnUpdate)
        {
            void* editorArgs[1] = { &deltaTime };
            mono_runtime_invoke(m_impl->editorOnUpdate, nullptr, editorArgs, nullptr);
        }
    }
    else
    {
        g_editorSceneContext = nullptr;
        g_editorRendererContext = nullptr;
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

    auto invokeScriptMethod = [](MonoMethod* method, MonoObject* instanceObject, std::uint32_t entityId)
    {
        if (!method)
            return;

        void* args[1] = { (void*)&entityId };
        mono_runtime_invoke(method, instanceObject, args, nullptr);
    };

    auto teardownScriptInstance = [&](std::uint32_t entityId, MonoRuntime::Impl::ScriptInstance& instance)
    {
        if (!instance.created)
            return;

        if (instance.active)
        {
            invokeScriptMethod(instance.onDisable, instance.instance, entityId);
            instance.active = false;
        }

        invokeScriptMethod(instance.onDestroy, instance.instance, entityId);
        instance.created = false;
    };

    for (const auto entity : view)
    {
        auto& script = view.get<ScriptComponent>(entity);
        const std::uint32_t entityId = static_cast<std::uint32_t>(entt::to_integral(entity));

        auto instanceIt = m_impl->entityScripts.find(entityId);
        if (instanceIt != m_impl->entityScripts.end())
        {
            auto& existing = instanceIt->second;
            const bool classChanged = existing.classNamespace != script.classNamespace
                                   || existing.className != script.className;
            if (classChanged)
            {
                teardownScriptInstance(entityId, existing);
                m_impl->entityScripts.erase(instanceIt);
                instanceIt = m_impl->entityScripts.end();
            }
        }

        if (!script.enabled)
        {
            if (instanceIt != m_impl->entityScripts.end())
            {
                auto& instance = instanceIt->second;
                if (instance.active)
                {
                    invokeScriptMethod(instance.onDisable, instance.instance, entityId);
                    instance.active = false;
                }
            }

            continue;
        }

        if (instanceIt == m_impl->entityScripts.end())
        {
            auto [insertedIt, inserted] = m_impl->entityScripts.try_emplace(entityId);
            if (!inserted)
                continue;

            instanceIt = insertedIt;
            auto& instance = instanceIt->second;

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
                m_impl->entityScripts.erase(insertedIt);
                continue;
            }

            mono_runtime_object_init(instance.instance);
            instance.onCreate = mono_class_get_method_from_name(klass, "OnCreate", 1);
            instance.onUpdate = mono_class_get_method_from_name(klass, "OnUpdate", 2);
            instance.onEnable = mono_class_get_method_from_name(klass, "OnEnable", 1);
            instance.onDisable = mono_class_get_method_from_name(klass, "OnDisable", 1);
            instance.onDestroy = mono_class_get_method_from_name(klass, "OnDestroy", 1);
            instance.classNamespace = script.classNamespace;
            instance.className = script.className;

            EditorBridge_ApplyPersistedScriptFields(entityId,
                                                    script,
                                                    instance.instance,
                                                    klass,
                                                    m_impl->domain);
        }

        auto& instance = instanceIt->second;

        if (!instance.created)
        {
            invokeScriptMethod(instance.onCreate, instance.instance, entityId);
            instance.created = true;
        }

        if (!instance.active)
        {
            invokeScriptMethod(instance.onEnable, instance.instance, entityId);
            instance.active = true;
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

        if (!hasComponent)
        {
            teardownScriptInstance(it->first, it->second);
            it = m_impl->entityScripts.erase(it);
        }
        else
        {
            ++it;
        }
    }

    g_editorSceneContext = nullptr;
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

#if ENGINE_MONO_RUNTIME_AVAILABLE
    g_monoRuntimeImplForEditorBridge = nullptr;
#endif

    if (m_impl->editorMode)
    {
        g_editorSceneContext = scene;
        g_editorRendererContext = nullptr;

        if (m_impl->editorLoaded && m_impl->editorOnShutdown)
            mono_runtime_invoke(m_impl->editorOnShutdown, nullptr, nullptr, nullptr);

        g_editorSceneContext = nullptr;
        g_editorRendererContext = nullptr;
    }

    if (scene)
    {
        auto& registry = scene->Registry();
        for (auto& [entityId, instance] : m_impl->entityScripts)
        {
            const auto entity = static_cast<entt::entity>(entityId);
            if (instance.created && registry.valid(entity))
            {
                std::uint32_t lifecycleEntityId = entityId;

                if (instance.active && instance.onDisable)
                {
                    void* disableArgs[1] = { (void*)&lifecycleEntityId };
                    mono_runtime_invoke(instance.onDisable, instance.instance, disableArgs, nullptr);
                    instance.active = false;
                }

                if (instance.onDestroy)
                {
                    void* destroyArgs[1] = { (void*)&lifecycleEntityId };
                    mono_runtime_invoke(instance.onDestroy, instance.instance, destroyArgs, nullptr);
                }
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
