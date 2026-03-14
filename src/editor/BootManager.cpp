#include "BootManager.h"
#include "engine/Engine.h"
#include "engine/BootStage.h"
#include "engine/Scripting/MonoRuntime.h"
#include "engine/Core/Logger.h"

#include <imgui.h>
#include <filesystem>
#include <string>

int BootManager::Run()
{
    Engine engine;
    engine.SetEditorMode(true);

    if (!engine.InitializeCoreSystems())
    {
        EngineLogger::Error("BootManager", "Failed to initialize core systems.");
        return -1;
    }

    // ================================================================
    // Phase 1: Project Selector
    // ================================================================
    engine.SetBootPhase(0);

    {
        Engine::WindowCreateDesc desc;
        desc.title = "CppCSharp Editor \u2014 Project Selector";
        desc.width = 960;
        desc.height = 640;
        desc.resizable = true;
        desc.dockspaceEnabled = false;

        if (!engine.CreateWindow(desc))
        {
            EngineLogger::Error("BootManager", "Failed to create project selector window.");
            engine.ShutdownCoreSystems();
            return -1;
        }

        while (engine.IsRunning())
        {
            float dt = 0.0f;
            if (!engine.BeginFrame(dt))
                break;

#ifndef ENGINE_MONO_DISABLED
            if (engine.GetMonoRuntime())
                engine.GetMonoRuntime()->Update(dt, engine.GetScene(), nullptr, &engine);
#endif

            engine.EndFrame();

            if (!engine.GetSelectedProjectPath().empty())
                break;
        }

        engine.DestroyWindow();
    }

    const std::string selectedProjectPath = engine.GetSelectedProjectPath();

    if (selectedProjectPath.empty())
    {
        engine.ShutdownCoreSystems();
        return 0;
    }

    // ================================================================
    // Phase 2: Loading / Splash screen
    // ================================================================
    engine.SetBootPhase(1);

    {
        Engine::WindowCreateDesc desc;
        desc.title = "Loading...";
        desc.width = 560;
        desc.height = 270;
        desc.borderless = true;
        desc.resizable = false;
        desc.dockspaceEnabled = false;

        if (!engine.CreateWindow(desc))
        {
            EngineLogger::Error("BootManager", "Failed to create loading window.");
            engine.ShutdownCoreSystems();
            return -1;
        }

        engine.SetWindowBackgroundImage("assets/editor/splash_logo.png");
        engine.SetWindowBackgroundVisible(true);

        // Open native project (ProjectContext + AssetDatabase + Mono script paths).
        engine.SetLoadingMessage("Loading project...");
        engine.OpenProject(std::filesystem::path(selectedProjectPath));

        // Render a few loading frames so the splash is visible.
        const int kMinLoadingFrames = 60;
        for (int frame = 0; frame < kMinLoadingFrames && engine.IsRunning(); ++frame)
        {
            float dt = 0.0f;
            if (!engine.BeginFrame(dt))
                break;

#ifndef ENGINE_MONO_DISABLED
            if (engine.GetMonoRuntime())
                engine.GetMonoRuntime()->Update(dt, engine.GetScene(), nullptr, &engine);
#else
            // Fallback loading overlay when managed editor is disabled.
            {
                const ImGuiViewport* viewport = ImGui::GetMainViewport();
                ImGui::SetNextWindowPos(ImVec2(viewport->WorkPos.x, viewport->WorkPos.y + viewport->WorkSize.y * 0.70f));
                ImGui::SetNextWindowSize(ImVec2(viewport->WorkSize.x, viewport->WorkSize.y * 0.30f));

                ImGuiWindowFlags flags = ImGuiWindowFlags_NoTitleBar
                    | ImGuiWindowFlags_NoResize
                    | ImGuiWindowFlags_NoMove
                    | ImGuiWindowFlags_NoScrollbar
                    | ImGuiWindowFlags_NoCollapse
                    | ImGuiWindowFlags_NoBackground;

                ImGui::Begin("##LoadingOverlay", nullptr, flags);
                ImGui::Text("  %s", engine.GetLoadingMessage().c_str());
                ImGui::End();
            }
#endif

            engine.EndFrame();
        }

        engine.DestroyWindow();
    }

    // ================================================================
    // Phase 3: Main Editor
    // ================================================================
    engine.SetBootPhase(2);

    {
        std::string projectName = std::filesystem::path(selectedProjectPath).filename().string();
        if (projectName.empty())
            projectName = "Untitled";

        Engine::WindowCreateDesc desc;
        desc.title = "CppCSharp Editor \u2014 " + projectName;
        desc.width = 1280;
        desc.height = 720;
        desc.resizable = true;
        desc.maximized = true;
        desc.dockspaceEnabled = true;
        desc.imguiIniPath = "imgui.ini";

        if (!engine.CreateWindow(desc))
        {
            EngineLogger::Error("BootManager", "Failed to create editor window.");
            engine.ShutdownCoreSystems();
            return -1;
        }

        // Re-invoke C# OnEditorStart so it can set up for the Editor phase.
#ifndef ENGINE_MONO_DISABLED
        if (engine.GetMonoRuntime())
            engine.GetMonoRuntime()->ReInvokeEditorStart(engine.GetScene(), nullptr, &engine);
#endif

        while (engine.IsRunning())
        {
            float dt = 0.0f;
            if (!engine.BeginFrame(dt))
                break;

#ifndef ENGINE_MONO_DISABLED
            if (engine.GetMonoRuntime())
                engine.GetMonoRuntime()->Update(dt, engine.GetScene(), nullptr, &engine);
#endif

            engine.EndFrame();
        }

        engine.DestroyWindow();
    }

    engine.ShutdownCoreSystems();
    return 0;
}
