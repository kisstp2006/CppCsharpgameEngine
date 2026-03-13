#pragma once

#include <memory>
#include <string>

class Scene;
class Renderer;
class Engine;

class MonoRuntime
{
public:
    struct Impl;

    enum class SimulationState
    {
        Edit = 0,
        Play = 1,
        Pause = 2
    };

    MonoRuntime();
    ~MonoRuntime();

    void SetEditorMode(bool enabled);
    bool Initialize();
    void SetPreferredScriptAssemblyPath(const std::string& assemblyPath);
    void SetPreferredScriptProjectPath(const std::string& projectPath);
    void Update(float deltaTime, Scene* scene, Renderer* renderer, Engine* engineContext = nullptr);
    SimulationState GetSimulationState() const;
    bool StartPlayMode(Scene* scene);
    void StopPlayMode(Scene* scene);
    void SetSimulationPaused(bool paused);
    void Shutdown(Scene* scene = nullptr);

    bool IsScriptLoaded() const;
    bool IsEditorLoaded() const;
    void ReInvokeEditorStart(Scene* scene = nullptr, Renderer* renderer = nullptr, Engine* engineContext = nullptr);

private:
    std::unique_ptr<Impl> m_impl;
};
