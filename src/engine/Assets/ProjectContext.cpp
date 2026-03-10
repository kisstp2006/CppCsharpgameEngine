#include "ProjectContext.h"

#include <fstream>
#include <iostream>
#include <optional>
#include <sstream>

namespace
{
    std::string Trim(const std::string& value)
    {
        const std::string whitespace = " \t\r\n";
        const std::size_t start = value.find_first_not_of(whitespace);
        if (start == std::string::npos)
            return {};

        const std::size_t end = value.find_last_not_of(whitespace);
        return value.substr(start, end - start + 1);
    }

    std::optional<std::string> ExtractJsonString(const std::string& json, const std::string& key)
    {
        const std::string needle = "\"" + key + "\"";
        const std::size_t keyPos = json.find(needle);
        if (keyPos == std::string::npos)
            return std::nullopt;

        const std::size_t colonPos = json.find(':', keyPos + needle.size());
        if (colonPos == std::string::npos)
            return std::nullopt;

        const std::size_t firstQuotePos = json.find('"', colonPos + 1);
        if (firstQuotePos == std::string::npos)
            return std::nullopt;

        std::string value;
        bool escaped = false;
        for (std::size_t i = firstQuotePos + 1; i < json.size(); ++i)
        {
            const char c = json[i];
            if (escaped)
            {
                switch (c)
                {
                case 'n':
                    value.push_back('\n');
                    break;
                case 'r':
                    value.push_back('\r');
                    break;
                case 't':
                    value.push_back('\t');
                    break;
                default:
                    value.push_back(c);
                    break;
                }
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

    std::optional<int> ExtractJsonInt(const std::string& json, const std::string& key)
    {
        const std::string needle = "\"" + key + "\"";
        const std::size_t keyPos = json.find(needle);
        if (keyPos == std::string::npos)
            return std::nullopt;

        const std::size_t colonPos = json.find(':', keyPos + needle.size());
        if (colonPos == std::string::npos)
            return std::nullopt;

        std::size_t valueStart = colonPos + 1;
        while (valueStart < json.size() && (json[valueStart] == ' ' || json[valueStart] == '\t'))
            ++valueStart;

        std::size_t valueEnd = valueStart;
        while (valueEnd < json.size() && json[valueEnd] >= '0' && json[valueEnd] <= '9')
            ++valueEnd;

        if (valueEnd == valueStart)
            return std::nullopt;

        try
        {
            return std::stoi(json.substr(valueStart, valueEnd - valueStart));
        }
        catch (...)
        {
            return std::nullopt;
        }
    }

    std::filesystem::path ReadLastProjectPath(const std::filesystem::path& workspaceRoot)
    {
        const std::filesystem::path lastProjectPath = workspaceRoot / ".editor" / "last_project.txt";
        if (!std::filesystem::exists(lastProjectPath))
            return {};

        std::ifstream input(lastProjectPath);
        if (!input.is_open())
            return {};

        std::string line;
        std::getline(input, line);
        line = Trim(line);

        return line.empty() ? std::filesystem::path{} : std::filesystem::path(line);
    }
}

bool ProjectContext::OpenWorkspace(const std::filesystem::path& workspaceRoot)
{
    m_workspaceRoot = workspaceRoot;

    const std::filesystem::path projectRootCandidate = workspaceRoot / "project.json";
    if (std::filesystem::exists(projectRootCandidate))
        return OpenProject(workspaceRoot);

    const std::filesystem::path restoredProjectPath = ReadLastProjectPath(workspaceRoot);
    if (!restoredProjectPath.empty() && std::filesystem::exists(restoredProjectPath))
        return OpenProject(restoredProjectPath);

    m_isOpen = false;
    return false;
}

bool ProjectContext::OpenProject(const std::filesystem::path& projectRoot)
{
    m_projectRoot = std::filesystem::absolute(projectRoot).lexically_normal();
    m_projectFilePath = m_projectRoot / "project.json";

    if (!LoadProjectMetadata())
    {
        std::cerr << "[ProjectContext] Failed to load metadata from: " << m_projectFilePath << std::endl;
        m_isOpen = false;
        return false;
    }

    ApplyMetadataDefaults();

    m_assetsRoot = (m_projectRoot / m_metadata.assetsRoot).lexically_normal();
    m_scriptsRoot = (m_projectRoot / m_metadata.scriptsRoot).lexically_normal();
    m_scenesRoot = (m_projectRoot / m_metadata.scenesRoot).lexically_normal();
    m_libraryRoot = (m_projectRoot / m_metadata.libraryRoot).lexically_normal();

    std::filesystem::create_directories(m_assetsRoot);
    std::filesystem::create_directories(m_scriptsRoot);
    std::filesystem::create_directories(m_scenesRoot);
    std::filesystem::create_directories(m_libraryRoot);

    m_isOpen = true;
    return true;
}

std::filesystem::path ProjectContext::ScriptProjectPath() const
{
    if (m_metadata.scriptProject.empty())
        return {};

    return (m_projectRoot / m_metadata.scriptProject).lexically_normal();
}

std::filesystem::path ProjectContext::ScriptSolutionPath() const
{
    if (m_metadata.scriptSolution.empty())
        return {};

    return (m_projectRoot / m_metadata.scriptSolution).lexically_normal();
}

std::filesystem::path ProjectContext::ScriptAssemblyAbsolutePath() const
{
    if (m_metadata.assemblyPath.empty())
        return {};

    const std::filesystem::path configuredPath(m_metadata.assemblyPath);
    if (configuredPath.is_absolute())
        return configuredPath.lexically_normal();

    return (m_projectRoot / configuredPath).lexically_normal();
}

bool ProjectContext::LoadProjectMetadata()
{
    m_metadata = Metadata{};

    if (!std::filesystem::exists(m_projectFilePath))
    {
        std::cerr << "[ProjectContext] project.json not found: " << m_projectFilePath << std::endl;
        return false;
    }

    std::ifstream input(m_projectFilePath);
    if (!input.is_open())
        return false;

    std::stringstream buffer;
    buffer << input.rdbuf();
    const std::string json = buffer.str();

    if (const auto version = ExtractJsonInt(json, "version"))
        m_metadata.version = *version;

    if (const auto name = ExtractJsonString(json, "name"))
        m_metadata.name = *name;

    if (const auto templateName = ExtractJsonString(json, "template"))
        m_metadata.templateName = *templateName;

    if (const auto assetsRoot = ExtractJsonString(json, "assetsRoot"))
        m_metadata.assetsRoot = *assetsRoot;

    if (const auto scriptsRoot = ExtractJsonString(json, "scriptsRoot"))
        m_metadata.scriptsRoot = *scriptsRoot;

    if (const auto scenesRoot = ExtractJsonString(json, "scenesRoot"))
        m_metadata.scenesRoot = *scenesRoot;

    if (const auto libraryRoot = ExtractJsonString(json, "libraryRoot"))
        m_metadata.libraryRoot = *libraryRoot;

    if (const auto scriptProject = ExtractJsonString(json, "scriptProject"))
        m_metadata.scriptProject = *scriptProject;

    if (const auto scriptSolution = ExtractJsonString(json, "scriptSolution"))
        m_metadata.scriptSolution = *scriptSolution;

    if (const auto assemblyPath = ExtractJsonString(json, "assemblyPath"))
        m_metadata.assemblyPath = *assemblyPath;

    if (const auto targetFramework = ExtractJsonString(json, "targetFramework"))
        m_metadata.targetFramework = *targetFramework;

    return true;
}

void ProjectContext::ApplyMetadataDefaults()
{
    if (m_metadata.assetsRoot.empty())
        m_metadata.assetsRoot = "Assets";

    if (m_metadata.scriptsRoot.empty())
        m_metadata.scriptsRoot = "Scripts";

    if (m_metadata.scenesRoot.empty())
        m_metadata.scenesRoot = "Scenes";

    if (m_metadata.libraryRoot.empty())
        m_metadata.libraryRoot = "Library";

    if (m_metadata.targetFramework.empty())
        m_metadata.targetFramework = "net472";

    if (m_metadata.scriptProject.empty() && !m_metadata.name.empty())
        m_metadata.scriptProject = m_metadata.name + ".csproj";

    if (m_metadata.scriptSolution.empty() && !m_metadata.name.empty())
        m_metadata.scriptSolution = m_metadata.name + ".sln";

    if (m_metadata.assemblyPath.empty())
    {
        if (!m_metadata.name.empty())
            m_metadata.assemblyPath = "bin/Debug/" + m_metadata.targetFramework + "/" + m_metadata.name + ".dll";
        else
            m_metadata.assemblyPath = "scripts/bin/Debug/net472/GameScripts.dll";

        std::cout << "[ProjectContext] Missing assemblyPath in project.json, using default: " << m_metadata.assemblyPath << std::endl;
    }
}
