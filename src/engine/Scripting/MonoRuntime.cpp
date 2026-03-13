#include "MonoRuntime.h"

#include "engine/Engine.h"
#include "engine/Assets/ProjectContext.h"
#include "engine/ECS/Components.h"
#include "engine/ECS/Scene.h"
#include "engine/Core/Logger.h"
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
#include <cstring>
#include <ctime>
#include <fstream>
#include <iomanip>
#include <limits>
#include <map>
#include <optional>
#include <sstream>
#include <string>
#include <thread>
#include <unordered_map>
#include <vector>

#include <entt/entt.hpp>
#include <glm/glm.hpp>
#include <imgui.h>
#include <ImGuizmo.h>

#if !defined(__INTELLISENSE__) && !defined(ENGINE_MONO_DISABLED) && __has_include(<mono/jit/jit.h>) && __has_include(<mono/metadata/appdomain.h>) && __has_include(<mono/metadata/assembly.h>) && __has_include(<mono/metadata/class.h>) && __has_include(<mono/metadata/debug-helpers.h>) && __has_include(<mono/metadata/mono-config.h>) && __has_include(<mono/metadata/object.h>)
#define ENGINE_MONO_RUNTIME_AVAILABLE 1
#include <mono/jit/jit.h>
#include <mono/metadata/appdomain.h>
#include <mono/metadata/assembly.h>
#include <mono/metadata/class.h>
#include <mono/metadata/debug-helpers.h>
#include <mono/metadata/mono-config.h>
#include <mono/metadata/object.h>
#else
#define ENGINE_MONO_RUNTIME_AVAILABLE 0
#endif

struct MonoRuntime::Impl
{
#if ENGINE_MONO_RUNTIME_AVAILABLE
    MonoDomain* rootDomain = nullptr;
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
        uint32_t gcHandle = 0;
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
    std::uintmax_t scriptAssemblyFileSize = 0;
    bool hasScriptAssemblyWriteTime = false;
    bool hasEditorAssemblyWriteTime = false;
    bool hasScriptAssemblyFileSize = false;
    bool autoScriptReloadEnabled = true;
    bool scriptReloadRequested = false;
    bool scriptReloadDeferredUntilEdit = false;
    bool isReloadingScripts = false;
    bool playModeRequested = false;
    bool stopModeRequested = false;

    float hotReloadPollAccumulator = 0.0f;
#endif
    std::filesystem::path preferredScriptAssemblyPath;
    std::filesystem::path preferredScriptProjectPath;
    bool editorMode = false;
    MonoRuntime::SimulationState simulationState = MonoRuntime::SimulationState::Edit;
    bool gameplaySessionActive = false;
    bool scriptLoaded = false;
    bool editorLoaded = false;
};

#if ENGINE_MONO_RUNTIME_AVAILABLE
static MonoRuntime::Impl* g_monoRuntimeImplForEditorBridge = nullptr;
static bool MonoRuntime_ShouldRunGameplay(const MonoRuntime::Impl* impl);
static void MonoRuntime_StopActiveScriptInstances(MonoRuntime::Impl* impl, Scene* scene);
static bool MonoRuntime_CreateManagedAppDomain(MonoRuntime::Impl* impl);
static bool MonoRuntime_RecreateManagedAppDomain(MonoRuntime::Impl* impl, Scene* scene);
static bool MonoRuntime_LoadEditorAssemblyInCurrentDomain(MonoRuntime::Impl* impl, bool invokeOnStart);
static void MonoRuntime_StartPlaySession(MonoRuntime::Impl* impl, Scene* scene);
static void MonoRuntime_StopPlaySession(MonoRuntime::Impl* impl, Scene* scene);
static bool MonoRuntime_ReloadScriptAssemblyIfNeeded(MonoRuntime::Impl* impl, Scene* scene, bool forceReload);
#endif

#include "editor/Mono/EditorMonoBridge.inl"

#if ENGINE_MONO_RUNTIME_AVAILABLE
static bool MonoRuntime_ShouldRunGameplay(const MonoRuntime::Impl* impl)
{
    if (!impl)
        return false;

    if (!impl->editorMode)
        return impl->gameplaySessionActive;

    return impl->gameplaySessionActive && impl->simulationState == MonoRuntime::SimulationState::Play;
}

static bool MonoRuntime_SafeInvoke(MonoMethod* method, MonoObject* instance, void** args, const char* callContext)
{
    if (!method)
    {
        EngineLogger::Warningf("Mono", "SafeInvoke skipped: method is null (", callContext, ").");
        return false;
    }

    MonoObject* exception = nullptr;
    mono_runtime_invoke(method, instance, args, &exception);

    if (exception)
    {
        MonoString* exStr = mono_object_to_string(exception, nullptr);
        if (exStr)
        {
            char* utf8 = mono_string_to_utf8(exStr);
            EngineLogger::Errorf("Mono", "Exception in ", callContext, ": ", utf8 ? utf8 : "<unknown>");
            if (utf8)
                mono_free(utf8);
        }
        else
        {
            EngineLogger::Errorf("Mono", "Unhandled exception in ", callContext);
        }
        return false;
    }

    return true;
}

static void MonoRuntime_InvokeEntityLifecycle(MonoMethod* method, MonoObject* instanceObject, std::uint32_t entityId)
{
    if (!method)
        return;

    if (!instanceObject)
    {
        EngineLogger::Error("Mono", "InvokeEntityLifecycle skipped: instance is null.");
        return;
    }

    void* args[1] = { (void*)&entityId };
    MonoRuntime_SafeInvoke(method, instanceObject, args, mono_method_get_name(method));
}

static MonoClass* MonoRuntime_FindMonoBehaviourBaseClass(MonoClass* klass)
{
    MonoClass* current = klass;
    while (current)
    {
        const char* classNamespace = mono_class_get_namespace(current);
        const char* className = mono_class_get_name(current);
        if (classNamespace && className
            && std::strcmp(classNamespace, "Engine") == 0
            && std::strcmp(className, "MonoBehaviour") == 0)
            return current;

        current = mono_class_get_parent(current);
    }

    return nullptr;
}

static void MonoRuntime_TeardownScriptInstance(std::uint32_t entityId, MonoRuntime::Impl::ScriptInstance& instance)
{
    if (instance.created)
    {
        if (instance.active)
        {
            MonoRuntime_InvokeEntityLifecycle(instance.onDisable, instance.instance, entityId);
            instance.active = false;
        }

        MonoRuntime_InvokeEntityLifecycle(instance.onDestroy, instance.instance, entityId);
        instance.created = false;
    }

    if (instance.gcHandle != 0)
    {
        mono_gchandle_free(instance.gcHandle);
        instance.gcHandle = 0;
    }
}

static void MonoRuntime_StopActiveScriptInstances(MonoRuntime::Impl* impl, Scene* scene)
{
    if (!impl)
        return;

    if (!scene)
    {
        for (auto& pair : impl->entityScripts)
        {
            if (pair.second.gcHandle != 0)
            {
                mono_gchandle_free(pair.second.gcHandle);
                pair.second.gcHandle = 0;
            }
        }
        impl->entityScripts.clear();
        return;
    }

    auto& registry = scene->Registry();
    for (auto& [entityId, instance] : impl->entityScripts)
    {
        MonoRuntime_TeardownScriptInstance(entityId, instance);
    }

    impl->entityScripts.clear();
}

static void MonoRuntime_ClearManagedAssemblyState(MonoRuntime::Impl* impl)
{
    if (!impl)
        return;

    ClearDefaultValueProbeCache();

    impl->assembly = nullptr;
    impl->image = nullptr;
    impl->scriptClass = nullptr;
    impl->onStart = nullptr;
    impl->onUpdate = nullptr;
    impl->onShutdown = nullptr;
    impl->scriptLoaded = false;

    impl->editorAssembly = nullptr;
    impl->editorImage = nullptr;
    impl->editorClass = nullptr;
    impl->editorOnStart = nullptr;
    impl->editorOnUpdate = nullptr;
    impl->editorOnShutdown = nullptr;
    impl->editorLoaded = false;

    impl->resolvedScriptAssemblyPath.clear();
    impl->resolvedEditorAssemblyPath.clear();

    impl->scriptAssemblyWriteTime = {};
    impl->editorAssemblyWriteTime = {};
    impl->scriptAssemblyFileSize = 0;

    impl->hasScriptAssemblyWriteTime = false;
    impl->hasEditorAssemblyWriteTime = false;
    impl->hasScriptAssemblyFileSize = false;

    impl->gameplaySessionActive = false;
}

static bool MonoRuntime_CreateManagedAppDomain(MonoRuntime::Impl* impl)
{
    if (!impl || !impl->rootDomain)
        return false;

    static std::uint64_t domainCounter = 0;
    ++domainCounter;

    const std::string domainName = "CppCSharpGameEngine.ScriptDomain." + std::to_string(domainCounter);
    MonoDomain* appDomain = mono_domain_create_appdomain(const_cast<char*>(domainName.c_str()), nullptr);
    if (!appDomain)
    {
        EngineLogger::Error("Mono", "Failed to create managed script AppDomain.");
        return false;
    }

    if (mono_domain_set(appDomain, false) == 0)
    {
        EngineLogger::Error("Mono", "Failed to switch to managed script AppDomain.");
        mono_domain_unload(appDomain);
        return false;
    }

    impl->domain = appDomain;
    return true;
}

static bool MonoRuntime_RecreateManagedAppDomain(MonoRuntime::Impl* impl, Scene* scene)
{
    if (!impl)
        return false;

    MonoRuntime_StopActiveScriptInstances(impl, scene);

    if (impl->gameplaySessionActive && impl->scriptLoaded && impl->onShutdown)
        mono_runtime_invoke(impl->onShutdown, nullptr, nullptr, nullptr);

    if (impl->editorMode && impl->editorLoaded && impl->editorOnShutdown)
    {
        Scene* previousScene = g_editorSceneContext;
        g_editorSceneContext = scene;
        mono_runtime_invoke(impl->editorOnShutdown, nullptr, nullptr, nullptr);
        g_editorSceneContext = previousScene;
    }

    MonoRuntime_ClearManagedAssemblyState(impl);

    if (impl->domain)
    {
        MonoDomain* domainToUnload = impl->domain;
        impl->domain = nullptr;

        if (impl->rootDomain)
            mono_domain_set(impl->rootDomain, false);

        mono_domain_unload(domainToUnload);
    }

    return MonoRuntime_CreateManagedAppDomain(impl);
}

static void MonoRuntime_StartPlaySession(MonoRuntime::Impl* impl, Scene* scene)
{
    if (!impl || !impl->editorMode)
        return;

    if (impl->simulationState == MonoRuntime::SimulationState::Play)
        return;

    if (impl->simulationState == MonoRuntime::SimulationState::Pause)
    {
        impl->simulationState = MonoRuntime::SimulationState::Play;
        return;
    }

    MonoRuntime_StopActiveScriptInstances(impl, scene);

    if (impl->scriptLoaded && impl->onStart)
        mono_runtime_invoke(impl->onStart, nullptr, nullptr, nullptr);

    impl->gameplaySessionActive = impl->scriptLoaded;

    impl->simulationState = MonoRuntime::SimulationState::Play;
}

static void MonoRuntime_StopPlaySession(MonoRuntime::Impl* impl, Scene* scene)
{
    if (!impl || !impl->editorMode)
        return;

    if (impl->simulationState == MonoRuntime::SimulationState::Edit)
        return;

    MonoRuntime_StopActiveScriptInstances(impl, scene);

    if (impl->gameplaySessionActive && impl->scriptLoaded && impl->onShutdown)
        mono_runtime_invoke(impl->onShutdown, nullptr, nullptr, nullptr);

    impl->gameplaySessionActive = false;
    impl->simulationState = MonoRuntime::SimulationState::Edit;
}
#endif

static std::filesystem::path FindScriptAssemblyPath(const std::filesystem::path& preferredPath, bool verbose = true)
{
    if (!preferredPath.empty())
    {
        if (std::filesystem::exists(preferredPath))
        {
            if (verbose)
                EngineLogger::Infof("Mono", "Using project metadata script assembly: ", preferredPath);
            return preferredPath;
        }

        if (verbose)
        {
            EngineLogger::Infof("Mono", "Preferred script assembly not found: ", preferredPath);
            EngineLogger::Info("Mono", "Falling back to legacy script assembly candidates...");
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
                EngineLogger::Infof("Mono", "Using legacy fallback script assembly: ", candidate);
            return candidate;
        }
    }

    if (verbose && !preferredPath.empty())
        EngineLogger::Info("Mono", "Legacy fallback candidates also failed.");

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
        EngineLogger::Infof("Mono", "Skip build (project missing): ", projectLabel);
        return false;
    }

    const std::string command = "dotnet build \"" + projectPath.string() + "\" -c Debug -nologo -t:Rebuild";
    EngineLogger::Infof("Mono", "Building ", projectLabel, ": ", projectPath);

    const int exitCode = std::system(command.c_str());
    if (exitCode == 0)
    {
        EngineLogger::Infof("Mono", "Build succeeded for ", projectLabel, ".");
        return true;
    }

    EngineLogger::Errorf("Mono", "Build failed for ", projectLabel, " (exit code: ", exitCode, ").");
    return false;
}

static void ReportAssemblyAvailability(const std::filesystem::path& assemblyPath, const char* assemblyLabel)
{
    if (assemblyPath.empty())
    {
        EngineLogger::Errorf("Mono", assemblyLabel, " path could not be resolved.");
        return;
    }

    if (!std::filesystem::exists(assemblyPath))
    {
        EngineLogger::Errorf("Mono", "Missing ", assemblyLabel, ": ", assemblyPath);
        return;
    }

    EngineLogger::Infof("Mono", "Verified ", assemblyLabel, ": ", assemblyPath);
}

#include "MonoRuntime/MonoRuntime.Assembly.inl"
#include "editor/Mono/EditorMonoAssembly.inl"

#if ENGINE_MONO_RUNTIME_AVAILABLE
static bool MonoRuntime_LoadEditorAssemblyInCurrentDomain(MonoRuntime::Impl* impl, bool invokeOnStart)
{
    if (!impl || !impl->editorMode)
        return true;

    const auto editorPath = FindEditorAssemblyPath();
    if (editorPath.empty())
    {
        impl->editorAssembly = nullptr;
        impl->editorImage = nullptr;
        impl->editorClass = nullptr;
        impl->editorOnStart = nullptr;
        impl->editorOnUpdate = nullptr;
        impl->editorOnShutdown = nullptr;
        impl->editorLoaded = false;
        impl->resolvedEditorAssemblyPath.clear();
        impl->editorAssemblyWriteTime = {};
        impl->hasEditorAssemblyWriteTime = false;
        return true;
    }

    EditorAssemblyBindings editorBindings;
    if (!TryLoadEditorAssemblyBindings(impl->domain, impl->shadowCopyDirectory, editorPath, editorBindings))
        return false;

    impl->editorAssembly = editorBindings.assembly;
    impl->editorImage = editorBindings.image;
    impl->editorClass = editorBindings.editorClass;
    impl->editorOnStart = editorBindings.onStart;
    impl->editorOnUpdate = editorBindings.onUpdate;
    impl->editorOnShutdown = editorBindings.onShutdown;
    impl->editorLoaded = true;

    impl->resolvedEditorAssemblyPath = editorPath;
    impl->hasEditorAssemblyWriteTime = TryGetFileWriteTime(editorPath, impl->editorAssemblyWriteTime);

    if (invokeOnStart && impl->editorOnStart)
        mono_runtime_invoke(impl->editorOnStart, nullptr, nullptr, nullptr);

    return true;
}

static bool MonoRuntime_ReloadScriptAssemblyIfNeeded(MonoRuntime::Impl* impl, Scene* scene, bool forceReload)
{
    if (!impl)
        return false;

    if (impl->isReloadingScripts)
        return false;

    if (!impl->domain)
    {
        if (!MonoRuntime_CreateManagedAppDomain(impl))
            return false;
    }

    const auto scriptPath = FindScriptAssemblyPath(impl->preferredScriptAssemblyPath, false);
    if (scriptPath.empty())
        return false;

    std::filesystem::file_time_type scriptWriteTime{};
    std::uintmax_t scriptFileSize = 0;
    const bool hasScriptWriteTime = TryGetFileWriteTime(scriptPath, scriptWriteTime);
    const bool hasScriptFileSize = TryGetFileSize(scriptPath, scriptFileSize);

    const bool scriptPathChanged = !impl->resolvedScriptAssemblyPath.empty() &&
                                   (scriptPath != impl->resolvedScriptAssemblyPath);
    const bool scriptTimeChanged = hasScriptWriteTime &&
                                   impl->hasScriptAssemblyWriteTime &&
                                   (scriptWriteTime != impl->scriptAssemblyWriteTime);
    const bool scriptSizeChanged = hasScriptFileSize &&
                                   impl->hasScriptAssemblyFileSize &&
                                   (scriptFileSize != impl->scriptAssemblyFileSize);
    const bool scriptNeedsLoad = !impl->scriptLoaded;
    const bool scriptChangedOnDisk = scriptNeedsLoad || scriptPathChanged || scriptTimeChanged || scriptSizeChanged;
    const bool scriptDeferredReload = impl->scriptReloadDeferredUntilEdit;
    const bool scriptForceReload = forceReload || impl->scriptReloadRequested || scriptDeferredReload;
    const bool autoReloadTriggered = impl->autoScriptReloadEnabled && scriptChangedOnDisk;

    if (!(autoReloadTriggered || scriptForceReload))
        return false;

    // Safety guard: if the assembly file on disk hasn't actually changed
    // (same path, write time, and size), reject the reload request.
    // This prevents infinite reload loops where domain recreation triggers
    // a recompile that requests another reload even though nothing changed.
    if (!scriptChangedOnDisk && !forceReload)
    {
        impl->scriptReloadRequested = false;
        impl->scriptReloadDeferredUntilEdit = false;
        return false;
    }

    const bool inPlayOrPause = impl->editorMode && impl->simulationState != MonoRuntime::SimulationState::Edit;
    if (!forceReload && inPlayOrPause)
    {
        impl->scriptReloadDeferredUntilEdit = true;
        impl->scriptReloadRequested = false;
        return false;
    }

    const bool wasGameplaySessionActive = impl->gameplaySessionActive;

    impl->isReloadingScripts = true;

    if (!MonoRuntime_RecreateManagedAppDomain(impl, scene))
    {
        impl->isReloadingScripts = false;
        impl->scriptReloadRequested = true;
        return false;
    }

    if (impl->editorMode)
    {
        if (!MonoRuntime_LoadEditorAssemblyInCurrentDomain(impl, true))
        {
            impl->isReloadingScripts = false;
            impl->scriptReloadRequested = true;
            EngineLogger::Error("Mono", "Reload failed: editor assembly could not be loaded into recreated AppDomain.");
            return false;
        }
    }

    ScriptAssemblyBindings newScriptBindings;
    if (!TryLoadScriptAssemblyBindings(impl->domain, impl->shadowCopyDirectory, scriptPath, newScriptBindings))
    {
        impl->isReloadingScripts = false;
        impl->scriptReloadRequested = true;
        return false;
    }

    impl->assembly = newScriptBindings.assembly;
    impl->image = newScriptBindings.image;
    impl->scriptClass = newScriptBindings.scriptClass;
    impl->onStart = newScriptBindings.onStart;
    impl->onUpdate = newScriptBindings.onUpdate;
    impl->onShutdown = newScriptBindings.onShutdown;
    impl->scriptLoaded = true;

    impl->resolvedScriptAssemblyPath = scriptPath;
    impl->hasScriptAssemblyWriteTime = hasScriptWriteTime;
    impl->hasScriptAssemblyFileSize = hasScriptFileSize;
    if (hasScriptWriteTime)
        impl->scriptAssemblyWriteTime = scriptWriteTime;
    if (hasScriptFileSize)
        impl->scriptAssemblyFileSize = scriptFileSize;
    impl->scriptReloadRequested = false;
    impl->scriptReloadDeferredUntilEdit = false;
    impl->isReloadingScripts = false;
    impl->gameplaySessionActive = false;

    if (wasGameplaySessionActive)
    {
        if (impl->onStart)
            mono_runtime_invoke(impl->onStart, nullptr, nullptr, nullptr);
        impl->gameplaySessionActive = true;
    }
    else
    {
        impl->gameplaySessionActive = false;
    }

    if (scriptNeedsLoad)
        EngineLogger::Infof("Mono", "Script assembly became available: ", scriptPath);
    else if (scriptDeferredReload)
        EngineLogger::Infof("Mono", "Applied deferred script assembly reload: ", scriptPath);
    else if (scriptForceReload)
        EngineLogger::Infof("Mono", "Reloaded script AppDomain and assembly from explicit request: ", scriptPath);
    else
        EngineLogger::Infof("Mono", "Hot-reloaded script AppDomain and assembly: ", scriptPath);

    return true;
}
#endif

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
    m_impl->simulationState = enabled ? SimulationState::Edit : SimulationState::Play;
    m_impl->gameplaySessionActive = false;
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
    EngineLogger::Warning("Mono", "Scripting disabled at compile time.");
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
    m_impl->rootDomain = mono_jit_init_version("CppCSharpGameEngine", "v4.0.30319");
    if (!m_impl->rootDomain)
    {
        EngineLogger::Error("Mono", "Failed to initialize Mono JIT domain.");
        return false;
    }

    if (!MonoRuntime_CreateManagedAppDomain(m_impl.get()))
    {
        mono_jit_cleanup(m_impl->rootDomain);
        m_impl->rootDomain = nullptr;
        EngineLogger::Error("Mono", "Failed to initialize managed script AppDomain.");
        return false;
    }

    if (m_impl->editorMode)
    {
        mono_add_internal_call("Engine.EditorBridge::GetEntityCount", (const void*)&EditorBridge_GetEntityCount);
        mono_add_internal_call("Engine.EditorBridge::GetEntityIdAtIndex", (const void*)&EditorBridge_GetEntityIdAtIndex);
        mono_add_internal_call("Engine.EditorBridge::IsEntityValid", (const void*)&EditorBridge_IsEntityValid);
        mono_add_internal_call("Engine.EditorBridge::GetEntityName", (const void*)&EditorBridge_GetEntityName);
        mono_add_internal_call("Engine.EditorBridge::SetEntityName", (const void*)&EditorBridge_SetEntityName);
        mono_add_internal_call("Engine.EditorBridge::GetEntityTag", (const void*)&EditorBridge_GetEntityTag);
        mono_add_internal_call("Engine.EditorBridge::SetEntityTag", (const void*)&EditorBridge_SetEntityTag);
        mono_add_internal_call("Engine.EditorBridge::GetEntityLayer", (const void*)&EditorBridge_GetEntityLayer);
        mono_add_internal_call("Engine.EditorBridge::SetEntityLayer", (const void*)&EditorBridge_SetEntityLayer);
        mono_add_internal_call("Engine.EditorBridge::GetEntityStatic", (const void*)&EditorBridge_GetEntityStatic);
        mono_add_internal_call("Engine.EditorBridge::SetEntityStatic", (const void*)&EditorBridge_SetEntityStatic);
        mono_add_internal_call("Engine.EditorBridge::GetEntityActive", (const void*)&EditorBridge_GetEntityActive);
        mono_add_internal_call("Engine.EditorBridge::SetEntityActive", (const void*)&EditorBridge_SetEntityActive);
        mono_add_internal_call("Engine.EditorBridge::CreateEntity", (const void*)&EditorBridge_CreateEntity);
        mono_add_internal_call("Engine.EditorBridge::DestroyEntity", (const void*)&EditorBridge_DestroyEntity);
        mono_add_internal_call("Engine.EditorBridge::NewScene", (const void*)&EditorBridge_NewScene);
        mono_add_internal_call("Engine.EditorBridge::SaveScene", (const void*)&EditorBridge_SaveScene);
        mono_add_internal_call("Engine.EditorBridge::LoadScene", (const void*)&EditorBridge_LoadScene);
        mono_add_internal_call("Engine.EditorBridge::GetLastSceneIoStatus", (const void*)&EditorBridge_GetLastSceneIoStatus);
        mono_add_internal_call("Engine.EditorBridge::GetSimulationState", (const void*)&EditorBridge_GetSimulationState);
        mono_add_internal_call("Engine.EditorBridge::StartPlayMode", (const void*)&EditorBridge_StartPlayMode);
        mono_add_internal_call("Engine.EditorBridge::StopPlayMode", (const void*)&EditorBridge_StopPlayMode);
        mono_add_internal_call("Engine.EditorBridge::SetSimulationPaused", (const void*)&EditorBridge_SetSimulationPaused);
        mono_add_internal_call("Engine.EditorBridge::RequestScriptAssemblyReload", (const void*)&EditorBridge_RequestScriptAssemblyReload);
        mono_add_internal_call("Engine.EditorBridge::SetPreferredScriptAssemblyPath", (const void*)&EditorBridge_SetPreferredScriptAssemblyPath);
        mono_add_internal_call("Engine.EditorBridge::SetPreferredScriptProjectPath", (const void*)&EditorBridge_SetPreferredScriptProjectPath);
        mono_add_internal_call("Engine.EditorBridge::SetScriptAutoReloadEnabled", (const void*)&EditorBridge_SetScriptAutoReloadEnabled);
        mono_add_internal_call("Engine.EditorBridge::GetProjectSetting", (const void*)&EditorBridge_GetProjectSetting);
        mono_add_internal_call("Engine.EditorBridge::SetProjectSetting", (const void*)&EditorBridge_SetProjectSetting);
        mono_add_internal_call("Engine.EditorBridge::CreateAuxiliaryWindow", (const void*)&EditorBridge_CreateAuxiliaryWindow);
        mono_add_internal_call("Engine.EditorBridge::DestroyAuxiliaryWindow", (const void*)&EditorBridge_DestroyAuxiliaryWindow);
        mono_add_internal_call("Engine.EditorBridge::DestroyAllAuxiliaryWindows", (const void*)&EditorBridge_DestroyAllAuxiliaryWindows);
        mono_add_internal_call("Engine.EditorBridge::ShowAuxiliaryWindow", (const void*)&EditorBridge_ShowAuxiliaryWindow);
        mono_add_internal_call("Engine.EditorBridge::HideAuxiliaryWindow", (const void*)&EditorBridge_HideAuxiliaryWindow);
        mono_add_internal_call("Engine.EditorBridge::SetAuxiliaryWindowTitle", (const void*)&EditorBridge_SetAuxiliaryWindowTitle);
        mono_add_internal_call("Engine.EditorBridge::SetAuxiliaryWindowSize", (const void*)&EditorBridge_SetAuxiliaryWindowSize);
        mono_add_internal_call("Engine.EditorBridge::CenterAuxiliaryWindow", (const void*)&EditorBridge_CenterAuxiliaryWindow);
        mono_add_internal_call("Engine.EditorBridge::GetAuxiliaryWindowCount", (const void*)&EditorBridge_GetAuxiliaryWindowCount);
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
        mono_add_internal_call("Engine.EditorBridge::GetCameraSettings", (const void*)&EditorBridge_GetCameraSettings);
        mono_add_internal_call("Engine.EditorBridge::SetCameraSettings", (const void*)&EditorBridge_SetCameraSettings);
        mono_add_internal_call("Engine.EditorBridge::GetCameraSettingsV2", (const void*)&EditorBridge_GetCameraSettingsV2);
        mono_add_internal_call("Engine.EditorBridge::SetCameraSettingsV2", (const void*)&EditorBridge_SetCameraSettingsV2);
        mono_add_internal_call("Engine.EditorBridge::RemoveCamera", (const void*)&EditorBridge_RemoveCamera);
        mono_add_internal_call("Engine.EditorBridge::HasSprite", (const void*)&EditorBridge_HasSprite);
        mono_add_internal_call("Engine.EditorBridge::AddSprite", (const void*)&EditorBridge_AddSprite);
        mono_add_internal_call("Engine.EditorBridge::RemoveSprite", (const void*)&EditorBridge_RemoveSprite);
        mono_add_internal_call("Engine.EditorBridge::GetSpriteTexturePath", (const void*)&EditorBridge_GetSpriteTexturePath);
        mono_add_internal_call("Engine.EditorBridge::SetSpriteTexturePath", (const void*)&EditorBridge_SetSpriteTexturePath);
        mono_add_internal_call("Engine.EditorBridge::GetSpriteFallbackColor", (const void*)&EditorBridge_GetSpriteFallbackColor);
        mono_add_internal_call("Engine.EditorBridge::SetSpriteFallbackColor", (const void*)&EditorBridge_SetSpriteFallbackColor);
        mono_add_internal_call("Engine.EditorBridge::GetSpriteSettings", (const void*)&EditorBridge_GetSpriteSettings);
        mono_add_internal_call("Engine.EditorBridge::SetSpriteSettings", (const void*)&EditorBridge_SetSpriteSettings);
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
        mono_add_internal_call("Engine.EditorBridge::SetEditorPreviewCamera", (const void*)&EditorBridge_SetEditorPreviewCamera);
        mono_add_internal_call("Engine.EditorBridge::GetGameViewTextureHandle", (const void*)&EditorBridge_GetGameViewTextureHandle);
        mono_add_internal_call("Engine.EditorBridge::SetMainWindowSize", (const void*)&EditorBridge_SetMainWindowSize);
        mono_add_internal_call("Engine.EditorBridge::SetMainWindowTitle", (const void*)&EditorBridge_SetMainWindowTitle);
        mono_add_internal_call("Engine.EditorBridge::CenterMainWindow", (const void*)&EditorBridge_CenterMainWindow);
        mono_add_internal_call("Engine.EditorBridge::MaximizeMainWindow", (const void*)&EditorBridge_MaximizeMainWindow);
        mono_add_internal_call("Engine.EditorBridge::SetDockspaceEnabled", (const void*)&EditorBridge_SetDockspaceEnabled);
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
    mono_add_internal_call("Engine.Sprite::Has", (const void*)&RuntimeSprite_Has);
    mono_add_internal_call("Engine.Sprite::Add", (const void*)&RuntimeSprite_Add);
    mono_add_internal_call("Engine.Sprite::Remove", (const void*)&RuntimeSprite_Remove);
    mono_add_internal_call("Engine.Sprite::GetTexturePath", (const void*)&RuntimeSprite_GetTexturePath);
    mono_add_internal_call("Engine.Sprite::SetTexturePath", (const void*)&RuntimeSprite_SetTexturePath);
    mono_add_internal_call("Engine.Sprite::GetFallbackColor", (const void*)&RuntimeSprite_GetFallbackColor);
    mono_add_internal_call("Engine.Sprite::SetFallbackColor", (const void*)&RuntimeSprite_SetFallbackColor);
    mono_add_internal_call("Engine.Sprite::GetSettings", (const void*)&RuntimeSprite_GetSettings);
    mono_add_internal_call("Engine.Sprite::SetSettings", (const void*)&RuntimeSprite_SetSettings);
    mono_add_internal_call("Engine.EntityManager::CreateEntityInternal", (const void*)&EntityManager_CreateEntityInternal);
    mono_add_internal_call("Engine.EntityManager::DestroyEntityInternal", (const void*)&EntityManager_DestroyEntityInternal);
    mono_add_internal_call("Engine.EntityManager::IsEntityValidInternal", (const void*)&EntityManager_IsEntityValidInternal);
    mono_add_internal_call("Engine.EntityManager::GetEntityCountInternal", (const void*)&EntityManager_GetEntityCountInternal);
    mono_add_internal_call("Engine.EntityManager::GetEntityIdAtIndexInternal", (const void*)&EntityManager_GetEntityIdAtIndexInternal);
    mono_add_internal_call("Engine.EntityManager::GetEntityNameInternal", (const void*)&EntityManager_GetEntityNameInternal);
    mono_add_internal_call("Engine.EntityManager::SetEntityNameInternal", (const void*)&EntityManager_SetEntityNameInternal);
    mono_add_internal_call("Engine.EntityManager::GetEntityActiveInternal", (const void*)&EntityManager_GetEntityActiveInternal);
    mono_add_internal_call("Engine.EntityManager::SetEntityActiveInternal", (const void*)&EntityManager_SetEntityActiveInternal);
    mono_add_internal_call("Engine.EntityManager::HasComponentInternal", (const void*)&EntityManager_HasComponentInternal);
    mono_add_internal_call("Engine.EntityManager::AddComponentInternal", (const void*)&EntityManager_AddComponentInternal);
    mono_add_internal_call("Engine.EntityManager::RemoveComponentInternal", (const void*)&EntityManager_RemoveComponentInternal);
    mono_add_internal_call("Engine.EntityManager::HasTransformInternal", (const void*)&EntityManager_HasTransformInternal);
    mono_add_internal_call("Engine.EntityManager::AddTransformInternal", (const void*)&EntityManager_AddTransformInternal);
    mono_add_internal_call("Engine.EntityManager::GetTransformInternal", (const void*)&EntityManager_GetTransformInternal);
    mono_add_internal_call("Engine.EntityManager::SetTransformInternal", (const void*)&EntityManager_SetTransformInternal);
    mono_add_internal_call("Engine.EntityManager::HasCameraInternal", (const void*)&EntityManager_HasCameraInternal);
    mono_add_internal_call("Engine.EntityManager::AddCameraInternal", (const void*)&EntityManager_AddCameraInternal);
    mono_add_internal_call("Engine.EntityManager::GetCameraInternal", (const void*)&EntityManager_GetCameraInternal);
    mono_add_internal_call("Engine.EntityManager::SetCameraInternal", (const void*)&EntityManager_SetCameraInternal);
    mono_add_internal_call("Engine.EntityManager::GetCameraSettingsInternal", (const void*)&EntityManager_GetCameraSettingsInternal);
    mono_add_internal_call("Engine.EntityManager::SetCameraSettingsInternal", (const void*)&EntityManager_SetCameraSettingsInternal);
    mono_add_internal_call("Engine.EntityManager::GetCameraSettingsV2Internal", (const void*)&EntityManager_GetCameraSettingsV2Internal);
    mono_add_internal_call("Engine.EntityManager::SetCameraSettingsV2Internal", (const void*)&EntityManager_SetCameraSettingsV2Internal);
    mono_add_internal_call("Engine.EntityManager::RemoveCameraInternal", (const void*)&EntityManager_RemoveCameraInternal);
    mono_add_internal_call("Engine.EntityManager::HasSpriteInternal", (const void*)&EntityManager_HasSpriteInternal);
    mono_add_internal_call("Engine.EntityManager::AddSpriteInternal", (const void*)&EntityManager_AddSpriteInternal);
    mono_add_internal_call("Engine.EntityManager::RemoveSpriteInternal", (const void*)&EntityManager_RemoveSpriteInternal);
    mono_add_internal_call("Engine.EntityManager::HasScriptInternal", (const void*)&EntityManager_HasScriptInternal);
    mono_add_internal_call("Engine.EntityManager::AddScriptInternal", (const void*)&EntityManager_AddScriptInternal);
    mono_add_internal_call("Engine.EntityManager::RemoveScriptInternal", (const void*)&EntityManager_RemoveScriptInternal);
    mono_add_internal_call("Engine.Glm::Vec2AddInternal", (const void*)&RuntimeGlm_Vec2Add);
    mono_add_internal_call("Engine.Glm::Vec2SubInternal", (const void*)&RuntimeGlm_Vec2Sub);
    mono_add_internal_call("Engine.Glm::Vec2ScaleInternal", (const void*)&RuntimeGlm_Vec2Scale);
    mono_add_internal_call("Engine.Glm::Vec2LengthInternal", (const void*)&RuntimeGlm_Vec2Length);
    mono_add_internal_call("Engine.Glm::Vec2DotInternal", (const void*)&RuntimeGlm_Vec2Dot);
    mono_add_internal_call("Engine.Glm::Vec2NormalizeInternal", (const void*)&RuntimeGlm_Vec2Normalize);
    mono_add_internal_call("Engine.Glm::Vec3AddInternal", (const void*)&RuntimeGlm_Vec3Add);
    mono_add_internal_call("Engine.Glm::Vec3SubInternal", (const void*)&RuntimeGlm_Vec3Sub);
    mono_add_internal_call("Engine.Glm::Vec3ScaleInternal", (const void*)&RuntimeGlm_Vec3Scale);
    mono_add_internal_call("Engine.Glm::Vec3LengthInternal", (const void*)&RuntimeGlm_Vec3Length);
    mono_add_internal_call("Engine.Glm::Vec3DotInternal", (const void*)&RuntimeGlm_Vec3Dot);
    mono_add_internal_call("Engine.Glm::Vec3CrossInternal", (const void*)&RuntimeGlm_Vec3Cross);
    mono_add_internal_call("Engine.Glm::Vec3NormalizeInternal", (const void*)&RuntimeGlm_Vec3Normalize);

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
        mono_add_internal_call("Engine.ImGui::SetTooltip", (const void*)&EditorImGui_SetTooltip);
        mono_add_internal_call("Engine.ImGui::Button", (const void*)&EditorImGui_Button);
        mono_add_internal_call("Engine.ImGui::OpenPopup", (const void*)&EditorImGui_OpenPopup);
        mono_add_internal_call("Engine.ImGui::BeginPopupModal", (const void*)&EditorImGui_BeginPopupModal);
        mono_add_internal_call("Engine.ImGui::BeginPopup", (const void*)&EditorImGui_BeginPopup);
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
        mono_add_internal_call("Engine.ImGui::ColorButton", (const void*)&EditorImGui_ColorButton);
        mono_add_internal_call("Engine.ImGui::ColorPicker4", (const void*)&EditorImGui_ColorPicker4);
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
    {
        if (!m_impl->preferredScriptProjectPath.empty())
            EngineLogger::Info("Mono", "Script project build already handled by ProjectContext during project open.");
        else
            TryBuildDotnetProject(scriptProjectPath, "script project");
    }
    else
        EngineLogger::Info("Mono", "Script project file not found; skipping script build step.");

    if (m_impl->editorMode)
    {
        const std::filesystem::path editorProjectPath = FindEditorProjectPath();
        if (!editorProjectPath.empty())
            TryBuildDotnetProject(editorProjectPath, "editor project");
        else
            EngineLogger::Info("Mono", "Editor project file not found; skipping editor build step.");
    }

    const auto assemblyPath = FindScriptAssemblyPath(m_impl->preferredScriptAssemblyPath);
    ReportAssemblyAvailability(assemblyPath, "script assembly");
    if (assemblyPath.empty())
    {
        EngineLogger::Warning("Mono", "No script assembly found (metadata path + legacy candidates checked).");
    }
    else
    {
        ScriptAssemblyBindings scriptBindings;
        if (!TryLoadScriptAssemblyBindings(m_impl->domain, m_impl->shadowCopyDirectory, assemblyPath, scriptBindings))
        {
            EngineLogger::Errorf("Mono", "Failed to load assembly: ", assemblyPath);
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
            m_impl->hasScriptAssemblyFileSize = TryGetFileSize(assemblyPath, m_impl->scriptAssemblyFileSize);
            m_impl->scriptReloadRequested = false;

            if (!m_impl->editorMode)
            {
                if (m_impl->onStart)
                    mono_runtime_invoke(m_impl->onStart, nullptr, nullptr, nullptr);
                m_impl->gameplaySessionActive = true;
            }
            else
            {
                m_impl->gameplaySessionActive = false;
            }

            EngineLogger::Infof("Mono", "Loaded script assembly: ", assemblyPath);
        }
    }

    if (m_impl->editorMode)
    {
        const auto editorPath = FindEditorAssemblyPath();
        ReportAssemblyAvailability(editorPath, "editor assembly");
        if (editorPath.empty())
        {
            EngineLogger::Info("Mono", "No editor assembly found (expected editor/bin/*/net472/EngineEditor.dll).");
            return true;
        }

        if (!MonoRuntime_LoadEditorAssemblyInCurrentDomain(m_impl.get(), true))
        {
            EngineLogger::Errorf("Mono", "Failed to load editor assembly: ", editorPath);
            return true;
        }

        EngineLogger::Infof("Mono", "Loaded editor assembly: ", editorPath);
    }
    return true;
#endif
}

void MonoRuntime::Update(float deltaTime, Scene* scene, Renderer* renderer, Engine* engineContext)
{
#if !ENGINE_MONO_RUNTIME_AVAILABLE
    (void)deltaTime;
    (void)scene;
    (void)renderer;
    (void)engineContext;
#else
    if (!m_impl)
    {
        g_runtimeSceneForScriptApi = nullptr;
        return;
    }

    g_runtimeSceneForScriptApi = scene;

    // Process deferred play/stop requests from C++ side, BEFORE any managed
    // code runs, so that domain recreation does not destroy a live C# stack.
    if (m_impl->playModeRequested)
    {
        m_impl->playModeRequested = false;

        MonoRuntime_ReloadScriptAssemblyIfNeeded(m_impl.get(), scene, true);

        if (m_impl->scriptLoaded)
        {
            MonoRuntime_StartPlaySession(m_impl.get(), scene);
            EngineLogger::Info("Mono", "Entered play mode (deferred from editor request).");
            g_editorSceneIoStatus = "Entered play mode.";
        }
        else
        {
            EngineLogger::Warning("Mono", "Play mode deferred request failed: script assembly not loaded.");
            g_editorSceneIoStatus = "Play failed: script assembly is not loaded.";
        }
    }

    if (m_impl->stopModeRequested)
    {
        m_impl->stopModeRequested = false;
        MonoRuntime_StopPlaySession(m_impl.get(), scene);
        EngineLogger::Info("Mono", "Stopped play mode (deferred from editor request).");
        g_editorSceneIoStatus = "Stopped play mode.";
    }

    m_impl->hotReloadPollAccumulator += deltaTime;
    const bool shouldPollHotReload = m_impl->hotReloadPollAccumulator >= 0.5f;
    const bool shouldProcessReloadRequestNow = m_impl->scriptReloadRequested ||
        (m_impl->scriptReloadDeferredUntilEdit &&
         (!m_impl->editorMode || m_impl->simulationState == MonoRuntime::SimulationState::Edit));

    if (shouldPollHotReload || shouldProcessReloadRequestNow)
    {
        if (shouldPollHotReload)
            m_impl->hotReloadPollAccumulator = 0.0f;

        MonoRuntime_ReloadScriptAssemblyIfNeeded(m_impl.get(), scene, false);

        if (shouldPollHotReload && m_impl->editorMode)
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
                            EngineLogger::Infof("Mono", "Editor assembly became available: ", editorPath);
                        else
                            EngineLogger::Infof("Mono", "Hot-reloaded editor assembly: ", editorPath);
                    }
                }
            }
        }
    }

    if (m_impl->editorMode)
    {
        g_editorSceneContext = scene;
        g_editorRendererContext = renderer;
        g_editorEngineContext = engineContext;

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
        g_editorEngineContext = nullptr;
    }

    const bool runGameplay = MonoRuntime_ShouldRunGameplay(m_impl.get());

    if (runGameplay && m_impl->onUpdate)
    {
        void* args[1] = { &deltaTime };
        mono_runtime_invoke(m_impl->onUpdate, nullptr, args, nullptr);
    }

    if (!runGameplay || !scene || !m_impl->scriptLoaded || !m_impl->image)
    {
        g_editorSceneContext = nullptr;
        g_editorRendererContext = nullptr;
        g_editorEngineContext = nullptr;
        g_runtimeSceneForScriptApi = nullptr;
        return;
    }

    auto& registry = scene->Registry();
    auto view = registry.view<ScriptComponent>();

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
                MonoRuntime_TeardownScriptInstance(entityId, existing);
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
                    MonoRuntime_InvokeEntityLifecycle(instance.onDisable, instance.instance, entityId);
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
                EngineLogger::Errorf("Mono",
                                     "Script class not found: ",
                                     script.classNamespace,
                                     ".",
                                     script.className);
                m_impl->entityScripts.erase(instanceIt);
                continue;
            }

            MonoClass* monoBehaviourBaseClass = MonoRuntime_FindMonoBehaviourBaseClass(klass);
            if (!monoBehaviourBaseClass)
            {
                EngineLogger::Errorf("Mono",
                                     "Script class does not inherit Engine.MonoBehaviour (legacy OnCreate/OnUpdate scripts are unsupported): ",
                                     script.classNamespace,
                                     ".",
                                     script.className);
                m_impl->entityScripts.erase(instanceIt);
                continue;
            }

            instance.instance = mono_object_new(m_impl->domain, klass);
            if (!instance.instance)
            {
                EngineLogger::Error("Mono", "Failed to create script instance for entity.");
                m_impl->entityScripts.erase(insertedIt);
                continue;
            }

            mono_runtime_object_init(instance.instance);
            instance.gcHandle = mono_gchandle_new(instance.instance, true);
            instance.onCreate = mono_class_get_method_from_name(monoBehaviourBaseClass, "OnCreate", 1);
            instance.onUpdate = mono_class_get_method_from_name(monoBehaviourBaseClass, "OnUpdate", 2);
            instance.onEnable = mono_class_get_method_from_name(monoBehaviourBaseClass, "OnEnable", 1);
            instance.onDisable = mono_class_get_method_from_name(monoBehaviourBaseClass, "OnDisable", 1);
            instance.onDestroy = mono_class_get_method_from_name(monoBehaviourBaseClass, "OnDestroy", 1);

            if (!instance.onCreate || !instance.onUpdate)
            {
                EngineLogger::Error("Mono", "Engine.MonoBehaviour bridge methods are missing (OnCreate/OnUpdate). Check EngineManagedApi version.");
                MonoRuntime_TeardownScriptInstance(entityId, instance);
                m_impl->entityScripts.erase(instanceIt);
                continue;
            }

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
            MonoRuntime_InvokeEntityLifecycle(instance.onCreate, instance.instance, entityId);
            instance.created = true;
        }

        if (!instance.active)
        {
            MonoRuntime_InvokeEntityLifecycle(instance.onEnable, instance.instance, entityId);
            instance.active = true;
        }

        if (instance.onUpdate && instance.instance)
        {
            void* updateArgs[2] = { (void*)&entityId, (void*)&deltaTime };
            if (!MonoRuntime_SafeInvoke(instance.onUpdate, instance.instance, updateArgs, "OnUpdate"))
            {
                EngineLogger::Errorf("Mono", "Script OnUpdate failed for entity ", entityId,
                                     " (", instance.classNamespace, ".", instance.className, ")");
            }
        }
    }

    for (auto it = m_impl->entityScripts.begin(); it != m_impl->entityScripts.end();)
    {
        const auto entity = static_cast<entt::entity>(it->first);
        const bool hasComponent = registry.valid(entity) && registry.all_of<ScriptComponent>(entity);

        if (!hasComponent)
        {
            MonoRuntime_TeardownScriptInstance(it->first, it->second);
            it = m_impl->entityScripts.erase(it);
        }
        else
        {
            ++it;
        }
    }

    g_editorSceneContext = nullptr;
    g_editorRendererContext = nullptr;
    g_editorEngineContext = nullptr;
    g_runtimeSceneForScriptApi = nullptr;
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

    if (m_impl->editorMode)
        MonoRuntime_StopPlaySession(m_impl.get(), scene);

#if ENGINE_MONO_RUNTIME_AVAILABLE
    g_monoRuntimeImplForEditorBridge = nullptr;
#endif

    if (m_impl->editorMode)
    {
        g_editorSceneContext = scene;
        g_editorRendererContext = nullptr;
        g_editorEngineContext = nullptr;

        if (m_impl->editorLoaded && m_impl->editorOnShutdown)
            mono_runtime_invoke(m_impl->editorOnShutdown, nullptr, nullptr, nullptr);

        g_editorSceneContext = nullptr;
        g_editorRendererContext = nullptr;
        g_editorEngineContext = nullptr;
    }

    MonoRuntime_StopActiveScriptInstances(m_impl.get(), scene);

    if (m_impl->gameplaySessionActive && m_impl->scriptLoaded && m_impl->onShutdown)
        mono_runtime_invoke(m_impl->onShutdown, nullptr, nullptr, nullptr);

    m_impl->gameplaySessionActive = false;

    if (m_impl->domain)
    {
        MonoDomain* domainToUnload = m_impl->domain;
        m_impl->domain = nullptr;

        if (m_impl->rootDomain && domainToUnload != m_impl->rootDomain)
        {
            mono_domain_set(m_impl->rootDomain, false);
            mono_domain_unload(domainToUnload);
        }
    }

    if (m_impl->rootDomain)
    {
        mono_jit_cleanup(m_impl->rootDomain);
        m_impl->rootDomain = nullptr;
    }

    m_impl.reset();
#endif
}

MonoRuntime::SimulationState MonoRuntime::GetSimulationState() const
{
    if (!m_impl)
        return SimulationState::Edit;

    if (!m_impl->editorMode)
        return SimulationState::Play;

    return m_impl->simulationState;
}

bool MonoRuntime::StartPlayMode(Scene* scene)
{
#if !ENGINE_MONO_RUNTIME_AVAILABLE
    (void)scene;
    return false;
#else
    if (!m_impl || !m_impl->editorMode)
        return false;

    MonoRuntime_StartPlaySession(m_impl.get(), scene);
    return m_impl->simulationState == SimulationState::Play;
#endif
}

void MonoRuntime::StopPlayMode(Scene* scene)
{
#if !ENGINE_MONO_RUNTIME_AVAILABLE
    (void)scene;
#else
    if (!m_impl || !m_impl->editorMode)
        return;

    MonoRuntime_StopPlaySession(m_impl.get(), scene);
#endif
}

void MonoRuntime::SetSimulationPaused(bool paused)
{
    if (!m_impl || !m_impl->editorMode)
        return;

    if (paused)
    {
        if (m_impl->simulationState == SimulationState::Play)
            m_impl->simulationState = SimulationState::Pause;
        return;
    }

    if (m_impl->simulationState == SimulationState::Pause)
        m_impl->simulationState = SimulationState::Play;
}

bool MonoRuntime::IsScriptLoaded() const
{
    return m_impl && m_impl->scriptLoaded;
}

bool MonoRuntime::IsEditorLoaded() const
{
    return m_impl && m_impl->editorLoaded;
}
