#include "AssetDatabase.h"

#include "ProjectContext.h"

#include <algorithm>
#include <cctype>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <optional>
#include <sstream>

namespace
{
    std::string EscapeJson(const std::string& value)
    {
        std::string escaped;
        escaped.reserve(value.size() + 8);

        for (char c : value)
        {
            switch (c)
            {
            case '\\':
                escaped += "\\\\";
                break;
            case '"':
                escaped += "\\\"";
                break;
            case '\n':
                escaped += "\\n";
                break;
            case '\r':
                escaped += "\\r";
                break;
            case '\t':
                escaped += "\\t";
                break;
            default:
                escaped += c;
                break;
            }
        }

        return escaped;
    }

    std::string Trim(const std::string& value)
    {
        const std::string whitespace = " \t\r\n";
        const std::size_t start = value.find_first_not_of(whitespace);
        if (start == std::string::npos)
            return {};

        const std::size_t end = value.find_last_not_of(whitespace);
        return value.substr(start, end - start + 1);
    }

    std::optional<std::string> ExtractJsonStringFromLine(const std::string& line, const std::string& key)
    {
        const std::string needle = "\"" + key + "\"";
        const std::size_t keyPos = line.find(needle);
        if (keyPos == std::string::npos)
            return std::nullopt;

        const std::size_t colonPos = line.find(':', keyPos + needle.size());
        if (colonPos == std::string::npos)
            return std::nullopt;

        const std::size_t firstQuotePos = line.find('"', colonPos + 1);
        if (firstQuotePos == std::string::npos)
            return std::nullopt;

        std::string value;
        bool escaped = false;

        for (std::size_t i = firstQuotePos + 1; i < line.size(); ++i)
        {
            const char c = line[i];
            if (escaped)
            {
                value.push_back(c);
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                continue;
            }

            if (c == '"')
                return value;

            value.push_back(c);
        }

        return std::nullopt;
    }

    std::optional<std::uint64_t> ExtractJsonUInt64FromLine(const std::string& line, const std::string& key)
    {
        const std::string needle = "\"" + key + "\"";
        const std::size_t keyPos = line.find(needle);
        if (keyPos == std::string::npos)
            return std::nullopt;

        const std::size_t colonPos = line.find(':', keyPos + needle.size());
        if (colonPos == std::string::npos)
            return std::nullopt;

        std::size_t valueStart = colonPos + 1;
        while (valueStart < line.size() && std::isspace(static_cast<unsigned char>(line[valueStart])) != 0)
            ++valueStart;

        std::size_t valueEnd = valueStart;
        while (valueEnd < line.size() && std::isdigit(static_cast<unsigned char>(line[valueEnd])) != 0)
            ++valueEnd;

        if (valueStart == valueEnd)
            return std::nullopt;

        try
        {
            return static_cast<std::uint64_t>(std::stoull(line.substr(valueStart, valueEnd - valueStart)));
        }
        catch (...)
        {
            return std::nullopt;
        }
    }

    std::string NormalizeKeyPath(const std::filesystem::path& path)
    {
        return path.generic_string();
    }
}

bool AssetDatabase::LoadOrCreate(const std::filesystem::path& databasePath)
{
    m_databasePath = databasePath;
    m_previousEntries.clear();

    if (!std::filesystem::exists(m_databasePath))
        return true;

    std::ifstream input(m_databasePath);
    if (!input.is_open())
        return false;

    std::string line;
    while (std::getline(input, line))
    {
        line = Trim(line);
        if (line.find("\"path\"") == std::string::npos)
            continue;

        const auto pathValue = ExtractJsonStringFromLine(line, "path");
        const auto typeValue = ExtractJsonStringFromLine(line, "type");
        const auto sizeValue = ExtractJsonUInt64FromLine(line, "size");
        const auto ticksValue = ExtractJsonUInt64FromLine(line, "ticks");
        const auto handleValue = ExtractJsonUInt64FromLine(line, "handle");

        if (!pathValue.has_value() || !typeValue.has_value() || !sizeValue.has_value() || !ticksValue.has_value() || !handleValue.has_value())
            continue;

        PersistedEntry entry;
        entry.handle = *handleValue;
        entry.type = StringToAssetType(*typeValue);
        entry.fileSize = *sizeValue;
        entry.lastWriteTicks = *ticksValue;

        if (const auto hashValue = ExtractJsonStringFromLine(line, "hash"))
            entry.hash = *hashValue;

        m_previousEntries[*pathValue] = entry;
    }

    return true;
}

void AssetDatabase::ScanProject(const ProjectContext& projectContext)
{
    m_records.clear();

    if (!projectContext.IsOpen())
        return;

    const std::filesystem::path projectRoot = projectContext.ProjectRoot();

    IndexDirectory(projectRoot, projectContext.AssetsRoot());
    IndexDirectory(projectRoot, projectContext.ScriptsRoot());

    const std::filesystem::path projectFile = projectRoot / "project.json";
    if (std::filesystem::exists(projectFile))
        IndexFile(projectRoot, projectFile);

    const std::filesystem::path solutionPath = projectContext.ScriptSolutionPath();
    if (!solutionPath.empty() && std::filesystem::exists(solutionPath))
        IndexFile(projectRoot, solutionPath);

    const std::filesystem::path scriptProjectPath = projectContext.ScriptProjectPath();
    if (!scriptProjectPath.empty() && std::filesystem::exists(scriptProjectPath))
        IndexFile(projectRoot, scriptProjectPath);

    const std::filesystem::path assemblyPath = projectContext.ScriptAssemblyAbsolutePath();
    if (!assemblyPath.empty() && std::filesystem::exists(assemblyPath))
        IndexFile(projectRoot, assemblyPath);

    std::sort(m_records.begin(), m_records.end(), [](const AssetRecord& lhs, const AssetRecord& rhs)
    {
        return lhs.projectRelativePath.generic_string() < rhs.projectRelativePath.generic_string();
    });
}

std::vector<AssetRecord> AssetDatabase::DetectDirty() const
{
    std::vector<AssetRecord> dirtyRecords;
    const auto changes = DetectChanges();
    dirtyRecords.reserve(changes.size());

    for (const auto& change : changes)
    {
        if (change.kind == AssetChangeKind::Deleted)
            continue;

        dirtyRecords.push_back(change.record);
    }

    return dirtyRecords;
}

std::vector<AssetDatabase::AssetChange> AssetDatabase::DetectChanges() const
{
    std::vector<AssetChange> changes;
    std::unordered_map<std::string, const AssetRecord*> currentEntries;
    currentEntries.reserve(m_records.size());

    for (const auto& record : m_records)
    {
        const std::string pathKey = NormalizeKeyPath(record.projectRelativePath);
        currentEntries[pathKey] = &record;

        const auto previousIt = m_previousEntries.find(pathKey);
        if (previousIt == m_previousEntries.end())
        {
            changes.push_back(AssetChange{ AssetChangeKind::Added, record });
            continue;
        }

        const PersistedEntry& previousEntry = previousIt->second;
        if (previousEntry.type != record.type ||
            previousEntry.fileSize != record.fileSize ||
            previousEntry.lastWriteTicks != record.lastWriteTicks ||
            previousEntry.hash != record.hash)
        {
            changes.push_back(AssetChange{ AssetChangeKind::Modified, record });
        }
    }

    for (const auto& [pathKey, previousEntry] : m_previousEntries)
    {
        if (currentEntries.find(pathKey) != currentEntries.end())
            continue;

        AssetRecord deletedRecord;
        deletedRecord.handle = previousEntry.handle;
        deletedRecord.type = previousEntry.type;
        deletedRecord.projectRelativePath = std::filesystem::path(pathKey);
        deletedRecord.sourcePath = deletedRecord.projectRelativePath;
        deletedRecord.fileSize = previousEntry.fileSize;
        deletedRecord.lastWriteTicks = previousEntry.lastWriteTicks;
        deletedRecord.hash = previousEntry.hash;

        changes.push_back(AssetChange{ AssetChangeKind::Deleted, std::move(deletedRecord) });
    }

    std::sort(changes.begin(), changes.end(), [](const AssetChange& lhs, const AssetChange& rhs)
    {
        const std::string lhsPath = lhs.record.projectRelativePath.generic_string();
        const std::string rhsPath = rhs.record.projectRelativePath.generic_string();
        if (lhsPath != rhsPath)
            return lhsPath < rhsPath;

        return static_cast<int>(lhs.kind) < static_cast<int>(rhs.kind);
    });

    return changes;
}

bool AssetDatabase::Save() const
{
    if (m_databasePath.empty())
        return false;

    std::filesystem::create_directories(m_databasePath.parent_path());

    std::ofstream output(m_databasePath, std::ios::trunc);
    if (!output.is_open())
        return false;

    output << "{\n";
    output << "  \"version\": 1,\n";
    output << "  \"assets\": [\n";

    for (std::size_t index = 0; index < m_records.size(); ++index)
    {
        const AssetRecord& record = m_records[index];
        output << "    {"
               << "\"path\": \"" << EscapeJson(record.projectRelativePath.generic_string()) << "\", "
               << "\"type\": \"" << AssetTypeToString(record.type) << "\", "
               << "\"handle\": " << record.handle << ", "
               << "\"size\": " << record.fileSize << ", "
               << "\"ticks\": " << record.lastWriteTicks << ", "
               << "\"hash\": \"" << EscapeJson(record.hash) << "\""
               << "}";

        if (index + 1 < m_records.size())
            output << ",";

        output << "\n";
    }

    output << "  ]\n";
    output << "}\n";

    return true;
}

void AssetDatabase::IndexDirectory(const std::filesystem::path& projectRoot, const std::filesystem::path& directoryPath)
{
    if (directoryPath.empty() || !std::filesystem::exists(directoryPath))
        return;

    for (const auto& entry : std::filesystem::recursive_directory_iterator(directoryPath))
    {
        if (!entry.is_regular_file())
            continue;

        IndexFile(projectRoot, entry.path());
    }
}

void AssetDatabase::IndexFile(const std::filesystem::path& projectRoot, const std::filesystem::path& filePath)
{
    const AssetType type = ResolveAssetType(filePath);
    if (type == AssetType::Unknown)
        return;

    AssetRecord record;
    record.type = type;
    record.sourcePath = filePath.lexically_normal();

    std::error_code relativeError;
    const std::filesystem::path relativePath = std::filesystem::relative(record.sourcePath, projectRoot, relativeError);
    record.projectRelativePath = relativeError ? record.sourcePath.filename() : relativePath.lexically_normal();

    std::error_code sizeError;
    record.fileSize = std::filesystem::file_size(record.sourcePath, sizeError);
    if (sizeError)
        record.fileSize = 0;

    record.lastWriteTicks = GetLastWriteTicks(record.sourcePath);
    record.handle = ComputeHandle(record.projectRelativePath, type);

    m_records.push_back(std::move(record));
}

AssetType AssetDatabase::ResolveAssetType(const std::filesystem::path& filePath)
{
    const std::string fileName = filePath.filename().generic_string();
    if (fileName == "project.json")
        return AssetType::ProjectFile;

    std::string extension = filePath.extension().generic_string();
    std::transform(extension.begin(), extension.end(), extension.begin(), [](unsigned char c) { return static_cast<char>(std::tolower(c)); });

    if (extension == ".fbx" || extension == ".obj" || extension == ".gltf" || extension == ".glb" || extension == ".dae" || extension == ".3ds")
        return AssetType::ModelSource;

    if (extension == ".bmp" || extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".tga" || extension == ".dds")
        return AssetType::ImageSource;

    if (extension == ".wav" || extension == ".ogg" || extension == ".mp3" || extension == ".flac")
        return AssetType::AudioSource;

    if (extension == ".cs")
        return AssetType::ScriptSource;

    if (extension == ".dll")
        return AssetType::ManagedAssembly;

    if (extension == ".sln")
        return AssetType::SolutionFile;

    if (extension == ".csproj")
        return AssetType::ScriptProjectFile;

    return AssetType::Unknown;
}

AssetHandle AssetDatabase::ComputeHandle(const std::filesystem::path& relativePath, AssetType type)
{
    // FNV-1a 64-bit for deterministic project-local handles.
    constexpr std::uint64_t offset = 1469598103934665603ull;
    constexpr std::uint64_t prime = 1099511628211ull;

    std::uint64_t hash = offset;
    const std::string key = relativePath.generic_string() + "#" + AssetTypeToString(type);

    for (const unsigned char c : key)
    {
        hash ^= c;
        hash *= prime;
    }

    if (hash == InvalidAssetHandle)
        hash = 1;

    return hash;
}

std::uint64_t AssetDatabase::GetLastWriteTicks(const std::filesystem::path& filePath)
{
    std::error_code error;
    const auto writeTime = std::filesystem::last_write_time(filePath, error);
    if (error)
        return 0;

    return static_cast<std::uint64_t>(writeTime.time_since_epoch().count());
}

std::string AssetDatabase::AssetTypeToString(AssetType type)
{
    switch (type)
    {
    case AssetType::ModelSource:
        return "ModelSource";
    case AssetType::ImageSource:
        return "ImageSource";
    case AssetType::AudioSource:
        return "AudioSource";
    case AssetType::ScriptSource:
        return "ScriptSource";
    case AssetType::ManagedAssembly:
        return "ManagedAssembly";
    case AssetType::ProjectFile:
        return "ProjectFile";
    case AssetType::SolutionFile:
        return "SolutionFile";
    case AssetType::ScriptProjectFile:
        return "ScriptProjectFile";
    case AssetType::Unknown:
    default:
        return "Unknown";
    }
}

AssetType AssetDatabase::StringToAssetType(const std::string& value)
{
    if (value == "ModelSource")
        return AssetType::ModelSource;
    if (value == "ImageSource")
        return AssetType::ImageSource;
    if (value == "AudioSource")
        return AssetType::AudioSource;
    if (value == "ScriptSource")
        return AssetType::ScriptSource;
    if (value == "ManagedAssembly")
        return AssetType::ManagedAssembly;
    if (value == "ProjectFile")
        return AssetType::ProjectFile;
    if (value == "SolutionFile")
        return AssetType::SolutionFile;
    if (value == "ScriptProjectFile")
        return AssetType::ScriptProjectFile;

    return AssetType::Unknown;
}
