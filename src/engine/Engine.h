#pragma once

#include <filesystem>
#include <cstdint>
#include <memory>
#include <cstddef>
#include <string>
#include <unordered_map>

class SDLWindow;
class Renderer;
class Scene;
class MonoRuntime;
class ProjectContext;
class AssetDatabase;
class Texture;
class AuxiliaryWindowManager;

class Engine
{
public:
    enum class SceneStorageFormat
    {
        Json,
        Binary,
    };

    Engine();
    ~Engine();

    using AuxiliaryWindowId = std::uint32_t;

    struct AuxiliaryWindowDesc
    {
        std::string title = "Auxiliary Window";
        int width = 640;
        int height = 360;
        bool resizable = true;
        bool borderless = false;
        bool alwaysOnTop = false;
        bool startHidden = false;
    };

    void SetEditorMode(bool enabled);
    bool IsEditorMode() const { return m_editorMode; }

    bool Initialize(const std::string& title, int width, int height);
    void Run();
    void Shutdown();

    Scene* GetScene() { return m_scene.get(); }
    const Scene* GetScene() const { return m_scene.get(); }

    bool SaveScene(const std::filesystem::path& scenePath, SceneStorageFormat format);
    bool LoadScene(const std::filesystem::path& scenePath, SceneStorageFormat format);
    const std::string& GetLastSceneIoError() const { return m_lastSceneIoError; }

    AuxiliaryWindowId CreateAuxiliaryWindow(const AuxiliaryWindowDesc& desc);
    bool DestroyAuxiliaryWindow(AuxiliaryWindowId id);
    void DestroyAllAuxiliaryWindows();
    bool ShowAuxiliaryWindow(AuxiliaryWindowId id);
    bool HideAuxiliaryWindow(AuxiliaryWindowId id);
    bool SetAuxiliaryWindowTitle(AuxiliaryWindowId id, const std::string& title);
    bool SetAuxiliaryWindowSize(AuxiliaryWindowId id, int width, int height);
    bool CenterAuxiliaryWindow(AuxiliaryWindowId id);
    std::size_t GetAuxiliaryWindowCount() const;
    void SetEditorPreviewCamera(float x, float y, float zoom, bool enabled);

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
    std::unique_ptr<AuxiliaryWindowManager> m_auxiliaryWindows;
    std::unordered_map<std::string, std::unique_ptr<Texture>> m_spriteTextureCache;

    bool m_editorMode = false;
    bool m_running = false;
    bool m_editorPreviewCameraEnabled = false;
    float m_editorPreviewCameraX = 0.0f;
    float m_editorPreviewCameraY = 0.0f;
    float m_editorPreviewCameraZoom = 1.0f;
    std::string m_lastSceneIoError;
};
