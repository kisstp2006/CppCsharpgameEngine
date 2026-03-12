#include "Engine.h"
#include "Assets/AssetDatabase.h"
#include "Assets/AssetImportPipeline.h"
#include "Assets/ProjectContext.h"
#include "ECS/Components.h"
#include "ECS/Scene.h"
#include "Platform/SDLInputState.h"
#include "Platform/SDLWindow.h"
#include "Platform/AuxiliaryWindowManager.h"
#include "Render/DebugDraw.h"
#include "Render/Renderer.h"
#include "Render/Texture.h"
#include "Scripting/MonoRuntime.h"

#include <imgui.h>
#include <backends/imgui_impl_sdl2.h>
#include <backends/imgui_impl_opengl3.h>

#include <SDL.h>

#include <cstdint>
#include <filesystem>
#include <iostream>

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
}

Engine::Engine() = default;
Engine::~Engine() = default;

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

void Engine::SetEditorMode(bool enabled)
{
    m_editorMode = enabled;

#ifndef ENGINE_MONO_DISABLED
    if (m_mono)
        m_mono->SetEditorMode(enabled);
#endif
}

bool Engine::Initialize(const std::string& title, int width, int height)
{
    m_window = std::make_unique<SDLWindow>();
    if (!m_window->Initialize(title, width, height))
        return false;

    m_auxiliaryWindows = std::make_unique<AuxiliaryWindowManager>();

    m_renderer = std::make_unique<Renderer>();
    if (!m_renderer->Initialize(width, height))
        return false;

    m_scene = std::make_unique<Scene>();

    m_projectContext = std::make_unique<ProjectContext>();
    if (m_projectContext->OpenWorkspace(std::filesystem::current_path()))
    {
        m_assetDatabase = std::make_unique<AssetDatabase>();
        const std::filesystem::path databasePath = m_projectContext->LibraryRoot() / "AssetDatabase.json";

        if (!m_assetDatabase->LoadOrCreate(databasePath))
            std::cerr << "[Assets] Failed to load asset database: " << databasePath << std::endl;

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

            std::cout << "[Assets] Changes detected: added=" << addedCount
                      << ", modified=" << modifiedCount
                      << ", deleted=" << deletedCount << std::endl;

            AssetImportPipeline importPipeline;
            const AssetImportPipeline::Result importResult = importPipeline.Run(*m_projectContext, assetChanges);
            if (importResult.imported > 0 || importResult.removed > 0 || importResult.failed > 0)
            {
                std::cout << "[Assets] Import pass: imported=" << importResult.imported
                          << ", removed=" << importResult.removed
                          << ", failed=" << importResult.failed << std::endl;
            }
        }

        if (!m_assetDatabase->Save())
            std::cerr << "[Assets] Failed to save asset database: " << databasePath << std::endl;
    }
    else
    {
        m_projectContext.reset();
    }

#ifndef ENGINE_MONO_DISABLED
    m_mono = std::make_unique<MonoRuntime>();
    m_mono->SetEditorMode(m_editorMode);

    if (m_projectContext && m_projectContext->IsOpen())
    {
        const std::filesystem::path assemblyPath = m_projectContext->ScriptAssemblyAbsolutePath();
        if (!assemblyPath.empty())
            m_mono->SetPreferredScriptAssemblyPath(assemblyPath.string());

        const std::filesystem::path scriptProjectPath = m_projectContext->ScriptProjectPath();
        if (!scriptProjectPath.empty())
            m_mono->SetPreferredScriptProjectPath(scriptProjectPath.string());
    }

    if (!m_mono->Initialize())
        return false;
#endif

    if (!InitializeImGui())
        return false;

    m_running = true;
    return true;
}

void Engine::Run()
{
    const std::uint64_t perfFrequency = SDL_GetPerformanceFrequency();
    std::uint64_t previousCounter = SDL_GetPerformanceCounter();

    while (m_running)
    {
        const std::uint64_t currentCounter = SDL_GetPerformanceCounter();
        const float deltaTime = static_cast<float>(currentCounter - previousCounter) / static_cast<float>(perfFrequency);
        previousCounter = currentCounter;

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

            if (event.type == SDL_KEYDOWN && event.key.keysym.sym == SDLK_ESCAPE)
                m_running = false;
        }

        SDLInputState::EndFrame();

        m_renderer->BeginFrame();

        // Keep ImGui frame active so C# editor code can draw managed panels.
        ImGui_ImplOpenGL3_NewFrame();
        ImGui_ImplSDL2_NewFrame(m_window->GetSDL_Window());
        ImGui::NewFrame();

        // Root dockspace so C# editor windows can be docked.
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

#ifndef ENGINE_MONO_DISABLED
        if (m_mono)
            m_mono->Update(deltaTime, m_scene.get(), m_renderer.get(), this);
#endif

        if (m_scene && m_renderer)
        {
            float cameraX = 0.0f;
            float cameraY = 0.0f;
            float cameraZoom = 1.0f;
            float cameraViewportX = 0.0f;
            float cameraViewportY = 0.0f;
            float cameraViewportWidth = 1.0f;
            float cameraViewportHeight = 1.0f;
            std::uint32_t cameraCullingMask = 0xFFFFFFFFu;
            std::uint32_t cameraBackgroundColor = 0x14141AFFu;
            bool clearCameraViewport = false;
            bool hasActiveSceneCamera = false;
            bool useEditorPreviewCamera = false;

            if (m_editorMode && m_editorPreviewCameraEnabled)
            {
#ifndef ENGINE_MONO_DISABLED
                useEditorPreviewCamera = (!m_mono) ||
                    (m_mono->GetSimulationState() == MonoRuntime::SimulationState::Edit);
#else
                useEditorPreviewCamera = true;
#endif
            }

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
                        cameraX = activeCamera->x;
                        cameraY = activeCamera->y;
                        cameraZoom = activeCamera->zoom;
                        cameraViewportX = activeCamera->viewportX;
                        cameraViewportY = activeCamera->viewportY;
                        cameraViewportWidth = activeCamera->viewportWidth;
                        cameraViewportHeight = activeCamera->viewportHeight;
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
            DebugDraw::Render(deltaTime, m_renderer->GetViewHeight());

        ImGui::Render();
        ImGui_ImplOpenGL3_RenderDrawData(ImGui::GetDrawData());

        m_window->SwapBuffers();
    }
}

void Engine::Shutdown()
{
    DestroyAllAuxiliaryWindows();
    m_auxiliaryWindows.reset();

    ShutdownImGui();

#ifndef ENGINE_MONO_DISABLED
    if (m_mono)
        m_mono->Shutdown(m_scene.get());
#endif

    m_scene.reset();
    m_spriteTextureCache.clear();

    if (m_assetDatabase)
        m_assetDatabase->Save();

    if (m_renderer)
        m_renderer->Shutdown();

    DebugDraw::Clear();

    if (m_window)
        m_window->Shutdown();
}

bool Engine::InitializeImGui()
{
    IMGUI_CHECKVERSION();
    ImGui::CreateContext();
    ImGuiIO& io = ImGui::GetIO();
    io.ConfigFlags |= ImGuiConfigFlags_DockingEnable;
    ImGui::StyleColorsDark();

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
