#pragma once

#include <filesystem>
#include <string>

class ProjectContext
{
public:
    struct Metadata
    {
        int version = 1;
        std::string name;
        std::string templateName;
        std::string assetsRoot = "Assets";
        std::string scriptsRoot = "Scripts";
        std::string scenesRoot = "Scenes";
        std::string libraryRoot = "Library";
        std::string scriptProject;
        std::string scriptSolution;
        std::string assemblyPath;
        std::string targetFramework = "net472";
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

private:
    bool LoadProjectMetadata();
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
