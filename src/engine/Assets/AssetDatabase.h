#pragma once

#include "AssetRecord.h"

#include <filesystem>
#include <string>
#include <unordered_map>
#include <vector>

class ProjectContext;

class AssetDatabase
{
public:
    enum class AssetChangeKind
    {
        Added,
        Modified,
        Deleted,
    };

    struct AssetChange
    {
        AssetChangeKind kind = AssetChangeKind::Added;
        AssetRecord record;
    };

    bool LoadOrCreate(const std::filesystem::path& databasePath);
    void ScanProject(const ProjectContext& projectContext);
    std::vector<AssetChange> DetectChanges() const;
    std::vector<AssetRecord> DetectDirty() const;
    bool Save() const;

    const std::vector<AssetRecord>& Records() const { return m_records; }

private:
    struct PersistedEntry
    {
        AssetHandle handle = InvalidAssetHandle;
        AssetType type = AssetType::Unknown;
        std::uint64_t fileSize = 0;
        std::uint64_t lastWriteTicks = 0;
        std::string hash;
    };

private:
    void IndexDirectory(const std::filesystem::path& projectRoot, const std::filesystem::path& directoryPath);
    void IndexFile(const std::filesystem::path& projectRoot, const std::filesystem::path& filePath);

    static AssetType ResolveAssetType(const std::filesystem::path& filePath);
    static AssetHandle ComputeHandle(const std::filesystem::path& relativePath, AssetType type);
    static std::uint64_t GetLastWriteTicks(const std::filesystem::path& filePath);
    static std::string AssetTypeToString(AssetType type);
    static AssetType StringToAssetType(const std::string& value);

private:
    std::filesystem::path m_databasePath;
    std::vector<AssetRecord> m_records;
    std::unordered_map<std::string, PersistedEntry> m_previousEntries;
};
