#pragma once

#include <memory>
#include <string>

class SDLWindow;
class Renderer;
class Scene;
class MonoRuntime;
class ProjectContext;
class AssetDatabase;

class Engine
{
public:
    Engine();
    ~Engine();

    bool Initialize(const std::string& title, int width, int height);
    void Run();
    void Shutdown();

private:
    bool InitializeImGui();
    void ShutdownImGui();

private:
    std::unique_ptr<SDLWindow> m_window;
    std::unique_ptr<Renderer> m_renderer;
    std::unique_ptr<Scene> m_scene;
    std::unique_ptr<MonoRuntime> m_mono;
    std::unique_ptr<ProjectContext> m_projectContext;
    std::unique_ptr<AssetDatabase> m_assetDatabase;

    bool m_running = false;
};
