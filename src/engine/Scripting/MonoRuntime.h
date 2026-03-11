#pragma once

#include <memory>
#include <string>

class Scene;
class Renderer;

class MonoRuntime
{
public:
    MonoRuntime();
    ~MonoRuntime();

    bool Initialize();
    void SetPreferredScriptAssemblyPath(const std::string& assemblyPath);
    void SetPreferredScriptProjectPath(const std::string& projectPath);
    void Update(float deltaTime, Scene* scene, Renderer* renderer);
    void Shutdown(Scene* scene = nullptr);

    bool IsScriptLoaded() const;
    bool IsEditorLoaded() const;

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};
