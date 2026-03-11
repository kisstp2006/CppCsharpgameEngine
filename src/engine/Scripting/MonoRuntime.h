#pragma once

#include <memory>
#include <string>

class Scene;
class Renderer;

class MonoRuntime
{
public:
    struct Impl;

    MonoRuntime();
    ~MonoRuntime();

    void SetEditorMode(bool enabled);
    bool Initialize();
    void SetPreferredScriptAssemblyPath(const std::string& assemblyPath);
    void SetPreferredScriptProjectPath(const std::string& projectPath);
    void Update(float deltaTime, Scene* scene, Renderer* renderer);
    void Shutdown(Scene* scene = nullptr);

    bool IsScriptLoaded() const;
    bool IsEditorLoaded() const;

private:
    std::unique_ptr<Impl> m_impl;
};
