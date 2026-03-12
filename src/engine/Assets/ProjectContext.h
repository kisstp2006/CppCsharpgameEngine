#pragma once

#include <filesystem>
#include <string>
#include <unordered_map>

class ProjectContext
{
public:
    static constexpr int CurrentProjectVersion = 3;
    static constexpr const char* ProjectEngineVersion = "2026.03";

    struct Metadata
    {
        int version = 1;
        std::string engineVersion;
        std::string name;
        std::string templateName;
        std::string assetsRoot = "Assets";
        std::string scriptsRoot = "Scripts";
        std::string scenesRoot = "Scenes";
        std::string libraryRoot = "Library";
        std::string scriptProject;
        std::string scriptSolution;
        std::string assemblyPath;
        std::string engineApiProject;
        std::string targetFramework = "net472";
        std::unordered_map<std::string, std::string> projectSettings;
    };

public:
    bool OpenWorkspace(const std::filesystem::path& workspaceRoot);
    bool OpenProject(const std::filesystem::path& projectRoot);

    bool IsOpen() const { return m_isOpen; }

    const std::filesystem::path& WorkspaceRoot() const { return m_workspaceRoot; }
    const std::filesystem::path& ProjectRoot() const { return m_projectRoot; }
    const std::filesystem::path& AssetsRoot() const { return m_assetsRoot; }
    const std::filesystem::path& ScriptsRoot() const { return m_scriptsRoot; }
    const std::filesystem::path& ScenesRoot() const { return m_scenesRoot; }
    const std::filesystem::path& LibraryRoot() const { return m_libraryRoot; }

    const Metadata& GetMetadata() const { return m_metadata; }

    std::filesystem::path ScriptProjectPath() const;
    std::filesystem::path ScriptSolutionPath() const;
    std::filesystem::path ScriptAssemblyAbsolutePath() const;

    bool GetProjectSetting(const std::string& key, std::string& outValue) const;
    bool SetProjectSetting(const std::string& key, const std::string& value);
    bool RemoveProjectSetting(const std::string& key);

private:
    bool LoadProjectMetadata();
    bool UpgradeProjectMetadataIfNeeded();
    bool SaveProjectMetadata() const;
    void PrepareManagedScriptProject() const;
    void ApplyMetadataDefaults();

private:
    bool m_isOpen = false;
    std::filesystem::path m_workspaceRoot;
    std::filesystem::path m_projectRoot;
    std::filesystem::path m_projectFilePath;
    std::filesystem::path m_assetsRoot;
    std::filesystem::path m_scriptsRoot;
    std::filesystem::path m_scenesRoot;
    std::filesystem::path m_libraryRoot;
    Metadata m_metadata;
};
