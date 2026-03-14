#include "Engine.h"
#include "Assets/AssetDatabase.h"
#include "Assets/AssetImportPipeline.h"
#include "Assets/ProjectContext.h"
#include "ECS/Components.h"
#include "ECS/AnimatorSystem.h"
#include "ECS/Scene.h"
#include "Platform/SDLInputState.h"
#include "Platform/SDLWindow.h"
#include "Platform/AuxiliaryWindowManager.h"
#include "Render/DebugDraw.h"
#include "Render/Renderer.h"
#include "Render/Texture.h"
#include "Scripting/MonoRuntime.h"
#include "engine/Core/Logger.h"

#include <imgui.h>
#include <backends/imgui_impl_sdl2.h>
#include <backends/imgui_impl_opengl3.h>

#include <SDL.h>

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <filesystem>

namespace
{
    static std::filesystem::path ResolveScenePath(const std::filesystem::path& requestedPath,
                                                  const ProjectContext* projectContext,
                                                  Engine::SceneStorageFormat format)
    {
        std::filesystem::path resolved;
        if (requestedPath.is_absolute())
            resolved = requestedPath;
        else if (projectContext && projectContext->IsOpen())
            resolved = projectContext->ScenesRoot() / requestedPath;
        else
            resolved = std::filesystem::current_path() / requestedPath;

        if (!resolved.has_extension())
        {
            resolved += (format == Engine::SceneStorageFormat::Binary)
                ? ".scene.bin"
                : ".scene.json";
        }

        return resolved.lexically_normal();
    }

    static std::filesystem::path ResolveSpriteTexturePath(const std::string& texturePath,
                                                          const ProjectContext* projectContext)
    {
        if (texturePath.empty())
            return {};

        std::filesystem::path candidate(texturePath);
        if (candidate.is_absolute())
            return candidate.lexically_normal();

        if (projectContext && projectContext->IsOpen())
            return (projectContext->ProjectRoot() / candidate).lexically_normal();

        return (std::filesystem::current_path() / candidate).lexically_normal();
    }

    static Texture* ResolveSpriteTexture(SpriteComponent& sprite,
                                         std::unordered_map<std::string, std::unique_ptr<Texture>>& cache,
                                         const ProjectContext* projectContext)
    {
        if (sprite.texture)
            return sprite.texture;

        if (sprite.textureAssetPath.empty())
            return nullptr;

        const std::filesystem::path resolvedPath = ResolveSpriteTexturePath(sprite.textureAssetPath, projectContext);
        if (resolvedPath.empty())
            return nullptr;

        std::error_code existsError;
        if (!std::filesystem::exists(resolvedPath, existsError) || existsError)
            return nullptr;

        const std::string cacheKey = resolvedPath.string();
        auto found = cache.find(cacheKey);
        if (found != cache.end())
        {
            sprite.texture = found->second.get();
            return sprite.texture;
        }

        auto texture = std::make_unique<Texture>();
        if (!texture->CreateFromFile(cacheKey))
            return nullptr;

        Texture* texturePtr = texture.get();
        cache.emplace(cacheKey, std::move(texture));
        sprite.texture = texturePtr;
        return texturePtr;
    }

    static float Clamp01(float value)
    {
        if (value < 0.0f)
            return 0.0f;
        if (value > 1.0f)
            return 1.0f;
        return value;
    }

    static float ByteToUnit(std::uint32_t value)
    {
        return static_cast<float>(value) / 255.0f;
    }

    static void UnpackColorRgba32(std::uint32_t rgba,
                                  float& outR,
                                  float& outG,
                                  float& outB,
                                  float& outA)
    {
        outR = ByteToUnit((rgba >> 24) & 0xFFu);
        outG = ByteToUnit((rgba >> 16) & 0xFFu);
        outB = ByteToUnit((rgba >> 8) & 0xFFu);
        outA = ByteToUnit(rgba & 0xFFu);
    }

    static void SetupEditorImGuiStyle()
    {
        ImGuiStyle& style = ImGui::GetStyle();
        style.Alpha = 1.0f;
        style.DisabledAlpha = 0.6f;

        // Flat, editor-focused geometry similar to professional game tooling UIs.
        style.WindowRounding = 3.0f;
        style.FrameRounding = 3.0f;
        style.ScrollbarRounding = 2.0f;
        style.GrabRounding = 2.0f;
        style.TabRounding = 3.0f;

        style.WindowPadding = ImVec2(10.0f, 10.0f);
        style.FramePadding = ImVec2(8.0f, 4.0f);
        style.ItemSpacing = ImVec2(8.0f, 6.0f);
        style.ItemInnerSpacing = ImVec2(6.0f, 4.0f);
        style.CellPadding = ImVec2(6.0f, 4.0f);

        style.WindowBorderSize = 1.0f;
        style.FrameBorderSize = 1.0f;
        style.PopupBorderSize = 1.0f;
        style.ChildBorderSize = 1.0f;
        style.TabBorderSize = 0.0f;

        style.WindowTitleAlign = ImVec2(0.0f, 0.5f);
        style.WindowMenuButtonPosition = ImGuiDir_Left;
        style.IndentSpacing = 20.0f;
        style.ScrollbarSize = 14.0f;
        style.GrabMinSize = 10.0f;

        ImVec4* colors = style.Colors;
        colors[ImGuiCol_Text] = ImVec4(0.90f, 0.90f, 0.90f, 1.00f);
        colors[ImGuiCol_TextDisabled] = ImVec4(0.58f, 0.58f, 0.58f, 1.00f);

        colors[ImGuiCol_WindowBg] = ImVec4(0.17f, 0.17f, 0.18f, 1.00f);
        colors[ImGuiCol_ChildBg] = ImVec4(0.15f, 0.15f, 0.16f, 1.00f);
        colors[ImGuiCol_PopupBg] = ImVec4(0.13f, 0.13f, 0.14f, 1.00f);

        colors[ImGuiCol_FrameBg] = ImVec4(0.23f, 0.23f, 0.24f, 1.00f);
        colors[ImGuiCol_FrameBgHovered] = ImVec4(0.29f, 0.29f, 0.30f, 1.00f);
        colors[ImGuiCol_FrameBgActive] = ImVec4(0.34f, 0.34f, 0.35f, 1.00f);

        colors[ImGuiCol_TitleBg] = ImVec4(0.19f, 0.19f, 0.20f, 1.00f);
        colors[ImGuiCol_TitleBgActive] = ImVec4(0.24f, 0.24f, 0.25f, 1.00f);
        colors[ImGuiCol_TitleBgCollapsed] = ImVec4(0.19f, 0.19f, 0.20f, 1.00f);

        colors[ImGuiCol_Button] = ImVec4(0.24f, 0.24f, 0.25f, 1.00f);
        colors[ImGuiCol_ButtonHovered] = ImVec4(0.31f, 0.31f, 0.32f, 1.00f);
        colors[ImGuiCol_ButtonActive] = ImVec4(0.37f, 0.37f, 0.38f, 1.00f);

        colors[ImGuiCol_Tab] = ImVec4(0.20f, 0.20f, 0.21f, 1.00f);
        colors[ImGuiCol_TabHovered] = ImVec4(0.30f, 0.30f, 0.31f, 1.00f);
        colors[ImGuiCol_TabActive] = ImVec4(0.26f, 0.26f, 0.27f, 1.00f);
        colors[ImGuiCol_TabUnfocused] = ImVec4(0.18f, 0.18f, 0.19f, 1.00f);
        colors[ImGuiCol_TabUnfocusedActive] = ImVec4(0.22f, 0.22f, 0.23f, 1.00f);

        colors[ImGuiCol_Header] = ImVec4(0.23f, 0.23f, 0.24f, 1.00f);
        colors[ImGuiCol_HeaderHovered] = ImVec4(0.30f, 0.30f, 0.31f, 1.00f);
        colors[ImGuiCol_HeaderActive] = ImVec4(0.35f, 0.35f, 0.36f, 1.00f);

        colors[ImGuiCol_Border] = ImVec4(0.31f, 0.31f, 0.33f, 1.00f);
        colors[ImGuiCol_BorderShadow] = ImVec4(0.00f, 0.00f, 0.00f, 0.00f);

        // Docking and menu polish for clear panel separation.
        colors[ImGuiCol_MenuBarBg] = ImVec4(0.16f, 0.16f, 0.17f, 1.00f);
        colors[ImGuiCol_Separator] = ImVec4(0.31f, 0.31f, 0.33f, 1.00f);
        colors[ImGuiCol_SeparatorHovered] = ImVec4(0.42f, 0.42f, 0.44f, 1.00f);
        colors[ImGuiCol_SeparatorActive] = ImVec4(0.50f, 0.50f, 0.52f, 1.00f);
        colors[ImGuiCol_ScrollbarBg] = ImVec4(0.15f, 0.15f, 0.16f, 1.00f);
        colors[ImGuiCol_ScrollbarGrab] = ImVec4(0.29f, 0.29f, 0.30f, 1.00f);
        colors[ImGuiCol_ScrollbarGrabHovered] = ImVec4(0.37f, 0.37f, 0.38f, 1.00f);
        colors[ImGuiCol_ScrollbarGrabActive] = ImVec4(0.44f, 0.44f, 0.46f, 1.00f);
        colors[ImGuiCol_CheckMark] = ImVec4(0.76f, 0.76f, 0.76f, 1.00f);
        colors[ImGuiCol_SliderGrab] = ImVec4(0.55f, 0.55f, 0.56f, 1.00f);
        colors[ImGuiCol_SliderGrabActive] = ImVec4(0.68f, 0.68f, 0.70f, 1.00f);
        colors[ImGuiCol_ResizeGrip] = ImVec4(0.33f, 0.33f, 0.34f, 1.00f);
        colors[ImGuiCol_ResizeGripHovered] = ImVec4(0.46f, 0.46f, 0.48f, 1.00f);
        colors[ImGuiCol_ResizeGripActive] = ImVec4(0.56f, 0.56f, 0.58f, 1.00f);
        colors[ImGuiCol_DockingEmptyBg] = ImVec4(0.12f, 0.12f, 0.13f, 1.00f);
        colors[ImGuiCol_DockingPreview] = ImVec4(0.24f, 0.38f, 0.58f, 1.00f);
    }
}

Engine::Engine() = default;
Engine::~Engine() = default;

void Engine::SetEditorMode(bool enabled)
{
    m_editorMode = enabled;

#ifndef ENGINE_MONO_DISABLED
    if (m_mono)
        m_mono->SetEditorMode(enabled);
#endif
}

// ---- Core systems: SDL_Init + Mono JIT (once per process) ----

bool Engine::InitializeCoreSystems()
{
    if (m_coreInitialized)
        return true;

    // SDL_Init is idempotent but we only want to do it once explicitly.
    if (SDL_Init(SDL_INIT_VIDEO | SDL_INIT_EVENTS | SDL_INIT_TIMER) != 0)
    {
        EngineLogger::Error("SDL", std::string("SDL_Init failed: ") + SDL_GetError());
        return false;
    }

    m_scene = std::make_unique<Scene>();

#ifndef ENGINE_MONO_DISABLED
    m_mono = std::make_unique<MonoRuntime>();
    m_mono->SetEditorMode(m_editorMode);

    if (!m_mono->Initialize())
        return false;
#endif

    m_coreInitialized = true;
    return true;
}

void Engine::ShutdownCoreSystems()
{
    if (!m_coreInitialized)
        return;

#ifndef ENGINE_MONO_DISABLED
    if (m_mono)
        m_mono->Shutdown(m_scene.get());
#endif

    m_scene.reset();
    m_spriteTextureCache.clear();

    if (m_assetDatabase)
        m_assetDatabase->Save();

    m_assetDatabase.reset();
    m_projectContext.reset();

    SDL_Quit();
    m_coreInitialized = false;
}

// ---- Per-window lifecycle ----

bool Engine::CreateWindow(const WindowCreateDesc& desc)
{
    m_window = std::make_unique<SDLWindow>();

    // SDLWindow::Initialize calls SDL_Init internally, which is safe because
    // SDL_Init is idempotent.  We rely on the InitializeCoreSystems() call above
    // to have already initialised the SDL subsystems.
    if (!m_window->Initialize(desc.title, desc.width, desc.height))
        return false;

    if (desc.borderless)
        m_window->SetBorderless(true);

    if (!desc.resizable)
        m_window->SetResizable(false);

    m_auxiliaryWindows = std::make_unique<AuxiliaryWindowManager>();

    m_renderer = std::make_unique<Renderer>();
    if (!m_renderer->Initialize(desc.width, desc.height))
        return false;

    m_dockspaceEnabled = desc.dockspaceEnabled;

    if (!InitializeImGui(desc.imguiIniPath))
        return false;

    if (desc.maximized)
        m_window->Maximize();

    m_running = true;
    m_perfFrequency = SDL_GetPerformanceFrequency();
    m_previousCounter = SDL_GetPerformanceCounter();

    return true;
}

void Engine::DestroyWindow()
{
    DestroyAllAuxiliaryWindows();
    m_auxiliaryWindows.reset();

    ShutdownImGui();

    m_windowBackgroundTexture.reset();

    if (m_renderer)
        m_renderer->Shutdown();
    m_renderer.reset();

    DebugDraw::Clear();

    if (m_window)
        m_window->Shutdown();
    m_window.reset();

    m_running = false;
}

bool Engine::HasWindow() const
{
    return m_window != nullptr;
}

bool Engine::SaveScene(const std::filesystem::path& scenePath, SceneStorageFormat format)
{
    m_lastSceneIoError.clear();

    if (!m_scene)
    {
        m_lastSceneIoError = "Cannot save scene: scene is not initialized.";
        return false;
    }

    const std::filesystem::path resolvedPath = ResolveScenePath(scenePath, m_projectContext.get(), format);
    const Scene::SceneFileFormat sceneFormat = (format == SceneStorageFormat::Binary)
        ? Scene::SceneFileFormat::Binary
        : Scene::SceneFileFormat::Json;

    if (!m_scene->SaveToFile(resolvedPath, sceneFormat))
    {
        m_lastSceneIoError = m_scene->GetLastIoError();
        if (m_lastSceneIoError.empty())
            m_lastSceneIoError = "Scene save failed: " + resolvedPath.string();
        return false;
    }

    return true;
}

bool Engine::LoadScene(const std::filesystem::path& scenePath, SceneStorageFormat format)
{
    m_lastSceneIoError.clear();

    if (!m_scene)
    {
        m_lastSceneIoError = "Cannot load scene: scene is not initialized.";
        return false;
    }

    const std::filesystem::path resolvedPath = ResolveScenePath(scenePath, m_projectContext.get(), format);
    const Scene::SceneFileFormat sceneFormat = (format == SceneStorageFormat::Binary)
        ? Scene::SceneFileFormat::Binary
        : Scene::SceneFileFormat::Json;

    if (!m_scene->LoadFromFile(resolvedPath, sceneFormat))
    {
        m_lastSceneIoError = m_scene->GetLastIoError();
        if (m_lastSceneIoError.empty())
            m_lastSceneIoError = "Scene load failed: " + resolvedPath.string();
        return false;
    }

    return true;
}

Engine::AuxiliaryWindowId Engine::CreateAuxiliaryWindow(const AuxiliaryWindowDesc& desc)
{
    if (!m_auxiliaryWindows)
        return 0;

    AuxiliaryWindowManager::CreateDesc nativeDesc;
    nativeDesc.title = desc.title;
    nativeDesc.width = desc.width;
    nativeDesc.height = desc.height;
    nativeDesc.resizable = desc.resizable;
    nativeDesc.borderless = desc.borderless;
    nativeDesc.alwaysOnTop = desc.alwaysOnTop;
    nativeDesc.startHidden = desc.startHidden;
    return m_auxiliaryWindows->CreateWindow(nativeDesc);
}

bool Engine::DestroyAuxiliaryWindow(AuxiliaryWindowId id)
{
    return m_auxiliaryWindows && m_auxiliaryWindows->DestroyWindow(id);
}

void Engine::DestroyAllAuxiliaryWindows()
{
    if (m_auxiliaryWindows)
        m_auxiliaryWindows->DestroyAllWindows();
}

bool Engine::ShowAuxiliaryWindow(AuxiliaryWindowId id)
{
    return m_auxiliaryWindows && m_auxiliaryWindows->ShowWindow(id);
}

bool Engine::HideAuxiliaryWindow(AuxiliaryWindowId id)
{
    return m_auxiliaryWindows && m_auxiliaryWindows->HideWindow(id);
}

bool Engine::SetAuxiliaryWindowTitle(AuxiliaryWindowId id, const std::string& title)
{
    return m_auxiliaryWindows && m_auxiliaryWindows->SetWindowTitle(id, title);
}

bool Engine::SetAuxiliaryWindowSize(AuxiliaryWindowId id, int width, int height)
{
    return m_auxiliaryWindows && m_auxiliaryWindows->SetWindowSize(id, width, height);
}

bool Engine::CenterAuxiliaryWindow(AuxiliaryWindowId id)
{
    return m_auxiliaryWindows && m_auxiliaryWindows->CenterWindow(id);
}

void Engine::SetEditorPreviewCamera(float x, float y, float zoom, bool enabled)
{
    m_editorPreviewCameraX = x;
    m_editorPreviewCameraY = y;
    m_editorPreviewCameraZoom = zoom;
    m_editorPreviewCameraEnabled = enabled;
}

std::size_t Engine::GetAuxiliaryWindowCount() const
{
    return m_auxiliaryWindows ? m_auxiliaryWindows->GetWindowCount() : 0;
}

void Engine::SetMainWindowSize(int width, int height)
{
    if (m_window)
    {
        m_window->SetSize(width, height);
        if (m_renderer)
            m_renderer->Initialize(width, height);
    }
}

void Engine::SetMainWindowTitle(const std::string& title)
{
    if (m_window)
        m_window->SetTitle(title);
}

void Engine::CenterMainWindow()
{
    if (m_window)
        m_window->Center();
}

void Engine::MaximizeMainWindow()
{
    if (m_window)
        m_window->Maximize();
}

void Engine::SetMainWindowResizable(bool enabled)
{
    if (m_window)
        m_window->SetResizable(enabled);
}

void Engine::SetMainWindowBorderless(bool enabled)
{
    if (m_window)
        m_window->SetBorderless(enabled);
}

void Engine::SetDockspaceEnabled(bool enabled)
{
    m_dockspaceEnabled = enabled;
}

bool Engine::SetWindowBackgroundImage(const std::string& imagePath)
{
    if (imagePath.empty())
        return false;

    std::filesystem::path resolvedPath(imagePath);
    if (resolvedPath.is_relative())
        resolvedPath = std::filesystem::current_path() / resolvedPath;

    resolvedPath = resolvedPath.lexically_normal();
    if (!std::filesystem::exists(resolvedPath))
    {
        EngineLogger::Warningf("Editor", "Window background image not found: ", resolvedPath.string());
        return false;
    }

    auto texture = std::make_unique<Texture>();
    if (!texture->CreateFromFile(resolvedPath.string()))
    {
        EngineLogger::Warningf("Editor", "Failed to load window background image: ", resolvedPath.string());
        return false;
    }

    m_windowBackgroundTexture = std::move(texture);
    return true;
}

void Engine::ClearWindowBackgroundImage()
{
    m_windowBackgroundTexture.reset();
}

void Engine::SetWindowBackgroundVisible(bool visible)
{
    m_windowBackgroundVisible = visible;
}

bool Engine::Initialize(const std::string& title, int width, int height)
{
    if (!InitializeCoreSystems())
        return false;

    WindowCreateDesc desc;
    desc.title = title;
    desc.width = width;
    desc.height = height;
    desc.resizable = true;
    desc.dockspaceEnabled = m_editorMode;
    if (!CreateWindow(desc))
        return false;

    // Legacy path: open project from current working directory.
    OpenProject(std::filesystem::current_path());

    return true;
}

// ---- Project loading ----

bool Engine::OpenProject(const std::filesystem::path& projectPath)
{
    m_projectContext = std::make_unique<ProjectContext>();
    if (!m_projectContext->OpenProject(projectPath))
    {
        m_projectContext.reset();
        return false;
    }

    m_assetDatabase = std::make_unique<AssetDatabase>();
    const std::filesystem::path databasePath = m_projectContext->LibraryRoot() / "AssetDatabase.json";

    if (!m_assetDatabase->LoadOrCreate(databasePath))
        EngineLogger::Errorf("Assets", "Failed to load asset database: ", databasePath);

    m_assetDatabase->ScanProject(*m_projectContext);
    const auto assetChanges = m_assetDatabase->DetectChanges();
    if (!assetChanges.empty())
    {
        std::size_t addedCount = 0;
        std::size_t modifiedCount = 0;
        std::size_t deletedCount = 0;

        for (const auto& change : assetChanges)
        {
            switch (change.kind)
            {
            case AssetDatabase::AssetChangeKind::Added:
                ++addedCount;
                break;
            case AssetDatabase::AssetChangeKind::Modified:
                ++modifiedCount;
                break;
            case AssetDatabase::AssetChangeKind::Deleted:
                ++deletedCount;
                break;
            }
        }

        EngineLogger::Infof("Assets",
                            "Changes detected: added=", addedCount,
                            ", modified=", modifiedCount,
                            ", deleted=", deletedCount);

        AssetImportPipeline importPipeline;
        const AssetImportPipeline::Result importResult = importPipeline.Run(*m_projectContext, assetChanges);
        if (importResult.imported > 0 || importResult.removed > 0 || importResult.failed > 0)
        {
            EngineLogger::Infof("Assets",
                                "Import pass: imported=", importResult.imported,
                                ", removed=", importResult.removed,
                                ", failed=", importResult.failed);
        }
    }

    if (!m_assetDatabase->Save())
        EngineLogger::Errorf("Assets", "Failed to save asset database: ", databasePath);

#ifndef ENGINE_MONO_DISABLED
    if (m_mono && m_projectContext && m_projectContext->IsOpen())
    {
        const std::filesystem::path assemblyPath = m_projectContext->ScriptAssemblyAbsolutePath();
        if (!assemblyPath.empty())
            m_mono->SetPreferredScriptAssemblyPath(assemblyPath.string());

        const std::filesystem::path scriptProjectPath = m_projectContext->ScriptProjectPath();
        if (!scriptProjectPath.empty())
            m_mono->SetPreferredScriptProjectPath(scriptProjectPath.string());
    }
#endif

    return true;
}

void Engine::Run()
{
    RunMainLoop();
}

void Engine::Shutdown()
{
    DestroyWindow();
    ShutdownCoreSystems();
}

// ---- Per-frame API for custom boot loops ----

bool Engine::BeginFrame(float& outDeltaTime)
{
    if (!m_running || !m_window)
    {
        outDeltaTime = 0.0f;
        return false;
    }

    const std::uint64_t currentCounter = SDL_GetPerformanceCounter();
    outDeltaTime = static_cast<float>(currentCounter - m_previousCounter) / static_cast<float>(m_perfFrequency);
    m_previousCounter = currentCounter;

    SDLInputState::BeginFrame();

    SDL_Event event;
    while (SDL_PollEvent(&event))
    {
        if (m_auxiliaryWindows && m_auxiliaryWindows->HandleWindowCloseEvent(event))
            continue;

        SDLInputState::ProcessEvent(event);
        ImGui_ImplSDL2_ProcessEvent(&event);
        if (event.type == SDL_QUIT)
            m_running = false;
    }

    SDLInputState::EndFrame();

    if (!m_running)
    {
        outDeltaTime = 0.0f;
        return false;
    }

    m_renderer->BeginFrame();

    if (m_windowBackgroundVisible && m_windowBackgroundTexture)
    {
        const float viewWidth = static_cast<float>(m_renderer->GetViewWidth());
        const float viewHeight = static_cast<float>(m_renderer->GetViewHeight());
        const float textureWidth = static_cast<float>(m_windowBackgroundTexture->GetWidth());
        const float textureHeight = static_cast<float>(m_windowBackgroundTexture->GetHeight());

        if (viewWidth > 1.0f && viewHeight > 1.0f && textureWidth > 1.0f && textureHeight > 1.0f)
        {
            const float fitScale = std::min(viewWidth / textureWidth, viewHeight / textureHeight);
            const float drawWidth = textureWidth * fitScale;
            const float drawHeight = textureHeight * fitScale;
            const float drawX = (viewWidth - drawWidth) * 0.5f;
            const float drawY = (viewHeight - drawHeight) * 0.5f;

            m_renderer->SetCameraProjection(viewWidth * 0.5f, viewHeight * 0.5f, 1.0f);
            m_renderer->DrawSprite(*m_windowBackgroundTexture, drawX, drawY, drawWidth, drawHeight);
        }
    }

    ImGui_ImplOpenGL3_NewFrame();
    ImGui_ImplSDL2_NewFrame(m_window->GetSDL_Window());
    ImGui::NewFrame();

    if (m_dockspaceEnabled)
    {
        const ImGuiDockNodeFlags dockspaceFlags = ImGuiDockNodeFlags_PassthruCentralNode;

        ImGuiWindowFlags windowFlags = ImGuiWindowFlags_NoDocking;
        const ImGuiViewport* viewport = ImGui::GetMainViewport();
        ImGui::SetNextWindowPos(viewport->WorkPos);
        ImGui::SetNextWindowSize(viewport->WorkSize);
        ImGui::SetNextWindowViewport(viewport->ID);

        windowFlags |= ImGuiWindowFlags_NoTitleBar;
        windowFlags |= ImGuiWindowFlags_NoCollapse;
        windowFlags |= ImGuiWindowFlags_NoResize;
        windowFlags |= ImGuiWindowFlags_NoMove;
        windowFlags |= ImGuiWindowFlags_NoBringToFrontOnFocus;
        windowFlags |= ImGuiWindowFlags_NoNavFocus;
        if ((dockspaceFlags & ImGuiDockNodeFlags_PassthruCentralNode) != 0)
            windowFlags |= ImGuiWindowFlags_NoBackground;

        ImGui::PushStyleVar(ImGuiStyleVar_WindowRounding, 0.0f);
        ImGui::PushStyleVar(ImGuiStyleVar_WindowBorderSize, 0.0f);
        ImGui::Begin("##MainDockSpaceHost", nullptr, windowFlags);
        ImGui::PopStyleVar(2);

        const ImGuiID dockspaceId = ImGui::GetID("MainDockSpace");
        ImGui::DockSpace(dockspaceId, ImVec2(0.0f, 0.0f), dockspaceFlags);
        ImGui::End();
    }

    return true;
}

void Engine::EndFrame()
{
    if (m_dockspaceEnabled && m_scene && m_renderer)
    {
        float cameraX = 0.0f;
        float cameraY = 0.0f;
        float cameraZoom = 1.0f;
        float cameraViewportX = 0.0f;
        float cameraViewportY = 0.0f;
        float cameraViewportWidth = 1.0f;
        float cameraViewportHeight = 1.0f;
        float cameraOrthographicSize = 0.0f;
        std::uint32_t cameraCullingMask = 0xFFFFFFFFu;
        std::uint32_t cameraBackgroundColor = 0x14141AFFu;
        bool clearCameraViewport = false;
        bool hasActiveSceneCamera = false;
        const bool useEditorPreviewCamera = m_editorMode && m_editorPreviewCameraEnabled;

        if (useEditorPreviewCamera)
        {
            cameraX = m_editorPreviewCameraX;
            cameraY = m_editorPreviewCameraY;
            cameraZoom = m_editorPreviewCameraZoom;
        }
        else
        {
            const Scene::Entity cameraEntity = m_scene->FindFirstCamera();
            if (m_scene->IsValid(cameraEntity))
            {
                const CameraComponent* activeCamera = m_scene->TryGetCamera(cameraEntity);
                if (activeCamera && activeCamera->enabled)
                {
                    hasActiveSceneCamera = true;
                    if (const TransformComponent* cameraTransform = m_scene->TryGetTransform(cameraEntity))
                    {
                        cameraX = cameraTransform->x;
                        cameraY = cameraTransform->y;
                    }
                    else
                    {
                        cameraX = activeCamera->x;
                        cameraY = activeCamera->y;
                    }
                    cameraZoom = activeCamera->zoom;
                    cameraViewportX = activeCamera->viewportX;
                    cameraViewportY = activeCamera->viewportY;
                    cameraViewportWidth = activeCamera->viewportWidth;
                    cameraViewportHeight = activeCamera->viewportHeight;
                    cameraOrthographicSize = activeCamera->orthographicSize;
                    cameraCullingMask = activeCamera->cullingMask;
                    cameraBackgroundColor = activeCamera->backgroundColor;
                    clearCameraViewport = activeCamera->clearColor;
                }
            }
        }

        m_renderer->BeginGameView();
        m_renderer->SetCameraViewportNormalized(cameraViewportX,
                                               cameraViewportY,
                                               cameraViewportWidth,
                                               cameraViewportHeight);

        if (hasActiveSceneCamera && cameraOrthographicSize > 0.0001f)
        {
            const int viewportPixelHeight = m_renderer->GetCameraViewportHeight();
            const float derivedZoom = (static_cast<float>(viewportPixelHeight) * 0.5f) / cameraOrthographicSize;
            if (std::isfinite(derivedZoom) && derivedZoom > 0.0001f)
                cameraZoom = derivedZoom;
        }

        if (clearCameraViewport)
        {
            float clearR = 0.0f;
            float clearG = 0.0f;
            float clearB = 0.0f;
            float clearA = 1.0f;
            UnpackColorRgba32(cameraBackgroundColor, clearR, clearG, clearB, clearA);
            m_renderer->ClearCameraViewport(clearR, clearG, clearB, clearA);
        }

        m_renderer->SetCameraProjection(cameraX, cameraY, cameraZoom);

        auto view = m_scene->Registry().view<const TransformComponent, const SpriteComponent>();
        for (const auto entity : view)
        {
            if (hasActiveSceneCamera)
            {
                std::uint32_t entityLayer = 0;
                const EntityMetadataComponent* metadata = m_scene->TryGetMetadata(entity);
                if (metadata)
                    entityLayer = metadata->layer > 31 ? 31u : metadata->layer;

                const std::uint32_t entityLayerMask = (1u << entityLayer);
                if ((cameraCullingMask & entityLayerMask) == 0u)
                    continue;
            }

            const auto& transform = view.get<const TransformComponent>(entity);
            auto& sprite = m_scene->Registry().get<SpriteComponent>(entity);
            if (!sprite.enabled)
                continue;

            float drawX = transform.x + sprite.offsetX;
            float drawY = transform.y + sprite.offsetY;
            if (sprite.centered)
            {
                drawX -= transform.width * 0.5f;
                drawY -= transform.height * 0.5f;
            }

            Texture* texture = ResolveSpriteTexture(sprite, m_spriteTextureCache, m_projectContext.get());
            if (!texture)
            {
                float r = 1.0f;
                float g = 1.0f;
                float b = 1.0f;
                float a = 1.0f;
                UnpackColorRgba32(sprite.fallbackColor, r, g, b, a);
                m_renderer->DrawSolidSprite(drawX,
                                            drawY,
                                            transform.width,
                                            transform.height,
                                            r,
                                            g,
                                            b,
                                            a);
                continue;
            }

            const int textureWidth = texture->GetWidth();
            const int textureHeight = texture->GetHeight();
            if (textureWidth <= 0 || textureHeight <= 0)
                continue;

            float sourceX = 0.0f;
            float sourceY = 0.0f;
            float sourceWidth = static_cast<float>(textureWidth);
            float sourceHeight = static_cast<float>(textureHeight);

            if (sprite.regionEnabled)
            {
                sourceX = sprite.regionX;
                sourceY = sprite.regionY;
                sourceWidth = sprite.regionWidth > 0.0f ? sprite.regionWidth : sourceWidth;
                sourceHeight = sprite.regionHeight > 0.0f ? sprite.regionHeight : sourceHeight;
            }
            else
            {
                const std::uint32_t hframes = sprite.hframes < 1 ? 1 : sprite.hframes;
                const std::uint32_t vframes = sprite.vframes < 1 ? 1 : sprite.vframes;
                const std::uint64_t frameCount = static_cast<std::uint64_t>(hframes) * static_cast<std::uint64_t>(vframes);
                std::uint32_t frame = sprite.frame;
                if (frameCount == 0)
                    frame = 0;
                else if (frame >= frameCount)
                    frame = static_cast<std::uint32_t>(frameCount - 1);

                sourceWidth = static_cast<float>(textureWidth) / static_cast<float>(hframes);
                sourceHeight = static_cast<float>(textureHeight) / static_cast<float>(vframes);

                const std::uint32_t frameX = frame % hframes;
                const std::uint32_t frameY = frame / hframes;
                sourceX = static_cast<float>(frameX) * sourceWidth;
                sourceY = static_cast<float>(frameY) * sourceHeight;
            }

            if (sourceWidth <= 0.0f || sourceHeight <= 0.0f)
                continue;

            float uvMinX = Clamp01(sourceX / static_cast<float>(textureWidth));
            float uvMinY = Clamp01(sourceY / static_cast<float>(textureHeight));
            float uvMaxX = Clamp01((sourceX + sourceWidth) / static_cast<float>(textureWidth));
            float uvMaxY = Clamp01((sourceY + sourceHeight) / static_cast<float>(textureHeight));

            if (sprite.flipH)
            {
                const float swap = uvMinX;
                uvMinX = uvMaxX;
                uvMaxX = swap;
            }

            if (sprite.flipV)
            {
                const float swap = uvMinY;
                uvMinY = uvMaxY;
                uvMaxY = swap;
            }

            m_renderer->DrawSprite(*texture,
                                   drawX,
                                   drawY,
                                   transform.width,
                                   transform.height,
                                   uvMinX,
                                   uvMinY,
                                   uvMaxX,
                                   uvMaxY);
        }

        m_renderer->EndGameView();
    }

    if (m_renderer)
        DebugDraw::Render(0.0f, m_renderer->GetViewHeight());

    ImGui::Render();
    ImGui_ImplOpenGL3_RenderDrawData(ImGui::GetDrawData());

    m_window->SwapBuffers();
}

// ---- Loading/selection signaling ----

void Engine::SetLoadingMessage(const std::string& message)
{
    m_loadingMessage = message;
}

void Engine::SetSelectedProjectPath(const std::string& path)
{
    m_selectedProjectPath = path;
}

void Engine::ClearSelectedProjectPath()
{
    m_selectedProjectPath.clear();
}

std::string Engine::GetNativeProjectPath() const
{
    if (m_projectContext && m_projectContext->IsOpen())
        return m_projectContext->ProjectRoot().string();
    return {};
}

// ---- Internal main loop ----

void Engine::RunMainLoop()
{
    while (m_running)
    {
        float deltaTime = 0.0f;
        if (!BeginFrame(deltaTime))
            break;

#ifndef ENGINE_MONO_DISABLED
        if (m_mono)
            m_mono->Update(deltaTime, m_scene.get(), m_renderer.get(), this);
#endif

        if (m_scene)
            AnimatorSystem::Update(*m_scene, deltaTime, m_projectContext.get());

        EndFrame();
    }
}

void Engine::RunFrameInternal(float deltaTime)
{
    // Reserved for future per-frame logic.
    (void)deltaTime;
}

bool Engine::InitializeImGui(const char* iniPath)
{
    IMGUI_CHECKVERSION();
    ImGui::CreateContext();
    ImGuiIO& io = ImGui::GetIO();
    io.ConfigFlags |= ImGuiConfigFlags_DockingEnable;
    io.IniFilename = iniPath; // nullptr disables persistence
    SetupEditorImGuiStyle();

    const std::filesystem::path fontRoot = std::filesystem::current_path() / "assets" / "font";
    const std::filesystem::path regularFontPath = fontRoot / "Inconsolata-LGC.otf";
    const std::filesystem::path boldFontPath = fontRoot / "Inconsolata-LGC-Bold.otf";

    ImFont* defaultFont = nullptr;
    std::error_code fontExistsError;
    if (std::filesystem::exists(regularFontPath, fontExistsError) && !fontExistsError)
    {
        defaultFont = io.Fonts->AddFontFromFileTTF(regularFontPath.string().c_str(), 14.0f);
        io.Fonts->AddFontFromFileTTF(regularFontPath.string().c_str(), 16.0f);
    }

    fontExistsError.clear();
    if (std::filesystem::exists(boldFontPath, fontExistsError) && !fontExistsError)
    {
        io.Fonts->AddFontFromFileTTF(boldFontPath.string().c_str(), 16.0f);
        io.Fonts->AddFontFromFileTTF(boldFontPath.string().c_str(), 18.0f);
    }

    if (defaultFont)
        io.FontDefault = defaultFont;

    if (!ImGui_ImplSDL2_InitForOpenGL(m_window->GetSDL_Window(), m_window->GetGLContext()))
        return false;

    if (!ImGui_ImplOpenGL3_Init("#version 330"))
        return false;

    return true;
}

void Engine::ShutdownImGui()
{
    ImGui_ImplOpenGL3_Shutdown();
    ImGui_ImplSDL2_Shutdown();
    ImGui::DestroyContext();
}
