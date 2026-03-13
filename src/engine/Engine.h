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

    struct WindowCreateDesc
    {
        std::string title = "Engine Window";
        int width = 1280;
        int height = 720;
        bool resizable = true;
        bool borderless = false;
        bool maximized = false;
        bool dockspaceEnabled = false;
        const char* imguiIniPath = nullptr; // nullptr = no persistence
    };

    void SetEditorMode(bool enabled);
    bool IsEditorMode() const { return m_editorMode; }

    // ---- Lifecycle: core systems (called once per process) ----
    bool InitializeCoreSystems();
    void ShutdownCoreSystems();

    // ---- Lifecycle: per-window phase ----
    bool CreateWindow(const WindowCreateDesc& desc);
    void DestroyWindow();
    bool HasWindow() const;

    // Legacy single-window API (delegates to core + window)
    bool Initialize(const std::string& title, int width, int height);
    void Run();
    void Shutdown();

    // ---- Per-frame rendering (for custom boot loops) ----
    bool BeginFrame(float& outDeltaTime);
    void EndFrame();
    void RequestQuit() { m_running = false; }
    bool IsRunning() const { return m_running; }

    Scene* GetScene() { return m_scene.get(); }
    const Scene* GetScene() const { return m_scene.get(); }
    ProjectContext* GetProjectContext() { return m_projectContext.get(); }
    const ProjectContext* GetProjectContext() const { return m_projectContext.get(); }
    MonoRuntime* GetMonoRuntime() { return m_mono.get(); }

    bool SaveScene(const std::filesystem::path& scenePath, SceneStorageFormat format);
    bool LoadScene(const std::filesystem::path& scenePath, SceneStorageFormat format);
    const std::string& GetLastSceneIoError() const { return m_lastSceneIoError; }

    // ---- Project loading ----
    bool OpenProject(const std::filesystem::path& projectPath);

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

    void SetMainWindowSize(int width, int height);
    void SetMainWindowTitle(const std::string& title);
    void CenterMainWindow();
    void MaximizeMainWindow();
    void SetMainWindowResizable(bool enabled);
    void SetMainWindowBorderless(bool enabled);
    void SetDockspaceEnabled(bool enabled);
    bool IsDockspaceEnabled() const { return m_dockspaceEnabled; }
    bool SetWindowBackgroundImage(const std::string& imagePath);
    void ClearWindowBackgroundImage();
    void SetWindowBackgroundVisible(bool visible);

    // ---- Loading progress (for splash screen) ----
    void SetLoadingMessage(const std::string& message);
    const std::string& GetLoadingMessage() const { return m_loadingMessage; }

    // ---- Project selection signaling (C# -> C++) ----
    void SetSelectedProjectPath(const std::string& path);
    const std::string& GetSelectedProjectPath() const { return m_selectedProjectPath; }
    void ClearSelectedProjectPath();

    // ---- Boot phase (0=ProjectSelector, 1=Loading, 2=Editor) ----
    void SetBootPhase(int phase) { m_bootPhase = phase; }
    int GetBootPhase() const { return m_bootPhase; }

    // ---- Native project path (for C# to query after native OpenProject) ----
    std::string GetNativeProjectPath() const;

private:
    bool InitializeImGui(const char* iniPath);
    void ShutdownImGui();

    void RunMainLoop();
    void RunFrameInternal(float deltaTime);

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
    bool m_coreInitialized = false;
    bool m_dockspaceEnabled = true;
    bool m_windowBackgroundVisible = false;
    bool m_editorPreviewCameraEnabled = false;
    float m_editorPreviewCameraX = 0.0f;
    float m_editorPreviewCameraY = 0.0f;
    float m_editorPreviewCameraZoom = 1.0f;
    std::string m_lastSceneIoError;
    std::unique_ptr<Texture> m_windowBackgroundTexture;
    std::string m_loadingMessage;
    std::string m_selectedProjectPath;

    int m_bootPhase = 0;
    std::uint64_t m_perfFrequency = 0;
    std::uint64_t m_previousCounter = 0;
};
