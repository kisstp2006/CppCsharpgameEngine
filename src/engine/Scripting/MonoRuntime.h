#pragma once

#include <memory>
#include <string>

class Scene;

class MonoRuntime
{
public:
    MonoRuntime();
    ~MonoRuntime();

    bool Initialize();
    void SetPreferredScriptAssemblyPath(const std::string& assemblyPath);
    void Update(float deltaTime, Scene* scene);
    void Shutdown(Scene* scene = nullptr);

    bool IsScriptLoaded() const;
    bool IsEditorLoaded() const;

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};
