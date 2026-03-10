#include "Engine.h"
#include "Assets/AssetDatabase.h"
#include "Assets/AssetImportPipeline.h"
#include "Assets/ProjectContext.h"
#include "ECS/Components.h"
#include "ECS/Scene.h"
#include "Platform/SDLWindow.h"
#include "Render/Renderer.h"
#include "Scripting/MonoRuntime.h"

#include <imgui.h>
#include <backends/imgui_impl_sdl2.h>
#include <backends/imgui_impl_opengl3.h>

#include <SDL.h>

#include <cstdint>
#include <filesystem>
#include <iostream>

Engine::Engine() = default;
Engine::~Engine() = default;

bool Engine::Initialize(const std::string& title, int width, int height)
{
    m_window = std::make_unique<SDLWindow>();
    if (!m_window->Initialize(title, width, height))
        return false;

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

    if (m_projectContext && m_projectContext->IsOpen())
    {
        const std::filesystem::path assemblyPath = m_projectContext->ScriptAssemblyAbsolutePath();
        if (!assemblyPath.empty())
            m_mono->SetPreferredScriptAssemblyPath(assemblyPath.string());
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

        SDL_Event event;
        while (SDL_PollEvent(&event))
        {
            ImGui_ImplSDL2_ProcessEvent(&event);
            if (event.type == SDL_QUIT)
                m_running = false;

            if (event.type == SDL_KEYDOWN && event.key.keysym.sym == SDLK_ESCAPE)
                m_running = false;
        }

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
            m_mono->Update(deltaTime, m_scene.get());
#endif

        if (m_scene)
        {
            auto view = m_scene->Registry().view<const TransformComponent, const SpriteComponent>();
            for (const auto entity : view)
            {
                const auto& transform = view.get<const TransformComponent>(entity);
                const auto& sprite = view.get<const SpriteComponent>(entity);

                if (sprite.texture)
                {
                    m_renderer->DrawSprite(*sprite.texture, transform.x, transform.y, transform.width, transform.height);
                }
            }
        }

        ImGui::Render();
        ImGui_ImplOpenGL3_RenderDrawData(ImGui::GetDrawData());

        m_window->SwapBuffers();
    }
}

void Engine::Shutdown()
{
    ShutdownImGui();

#ifndef ENGINE_MONO_DISABLED
    if (m_mono)
        m_mono->Shutdown(m_scene.get());
#endif

    m_scene.reset();

    if (m_assetDatabase)
        m_assetDatabase->Save();

    if (m_renderer)
        m_renderer->Shutdown();

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
