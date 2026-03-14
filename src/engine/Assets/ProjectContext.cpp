#include "ProjectContext.h"

#include "engine/Core/Logger.h"

#include <algorithm>
#include <array>
#include <cctype>
#include <cstdio>
#include <fstream>
#include <optional>
#include <sstream>
#include <vector>

#include <rapidjson/document.h>

namespace
{
    std::string ToLowerAscii(std::string value)
    {
        for (char& c : value)
            c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
        return value;
    }

    bool ContainsCaseInsensitive(const std::string& text, const std::string& token)
    {
        if (token.empty())
            return true;

        const std::string lowerText = ToLowerAscii(text);
        const std::string lowerToken = ToLowerAscii(token);
        return lowerText.find(lowerToken) != std::string::npos;
    }

    std::string EscapeXmlAttribute(const std::string& value)
    {
        std::string escaped;
        escaped.reserve(value.size());

        for (const char c : value)
        {
            switch (c)
            {
            case '&':
                escaped += "&amp;";
                break;
            case '<':
                escaped += "&lt;";
                break;
            case '>':
                escaped += "&gt;";
                break;
            case '"':
                escaped += "&quot;";
                break;
            case '\'':
                escaped += "&apos;";
                break;
            default:
                escaped.push_back(c);
                break;
            }
        }

        return escaped;
    }

    std::string EscapeJsonString(const std::string& value)
    {
        std::string escaped;
        escaped.reserve(value.size());

        for (const char c : value)
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
                escaped.push_back(c);
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

    bool EnsureEngineApiReferencesInCsproj(const std::filesystem::path& scriptProjectPath,
                                           const std::filesystem::path& workspaceRoot,
                                           const std::string& configuredEngineApiProject)
    {
        if (scriptProjectPath.empty() || !std::filesystem::exists(scriptProjectPath))
            return false;

        if (!ContainsCaseInsensitive(scriptProjectPath.extension().string(), ".csproj"))
            return false;

        std::ifstream input(scriptProjectPath);
        if (!input.is_open())
        {
            EngineLogger::Errorf("ProjectContext",
                                 "Failed to open script project for reference check: ",
                                 scriptProjectPath);
            return false;
        }

        std::stringstream buffer;
        buffer << input.rdbuf();
        std::string content = buffer.str();

        const std::vector<std::string> wrapperNames = {
            "Debug.cs",
            "Input.cs",
            "DebugDraw.cs",
            "Camera2D.cs",
            "ImGuizmo.cs",
            "UI.cs",
        };

        const bool hasProjectReference = ContainsCaseInsensitive(content, "<ProjectReference") &&
                                         ContainsCaseInsensitive(content, "EngineManagedApi.csproj");
        if (hasProjectReference)
            return true;

        bool hasEngineReference = ContainsCaseInsensitive(content, "EngineManagedApiReferences");
        if (!hasEngineReference)
        {
            int foundWrapperTokens = 0;
            for (const auto& wrapper : wrapperNames)
            {
                if (ContainsCaseInsensitive(content, wrapper))
                    ++foundWrapperTokens;
            }

            hasEngineReference = foundWrapperTokens >= 2;
        }

        if (hasEngineReference)
            return true;

        const std::filesystem::path scriptProjectDirectory = scriptProjectPath.parent_path();
        const std::filesystem::path engineRoot = workspaceRoot.empty()
            ? std::filesystem::current_path()
            : workspaceRoot;

        std::filesystem::path engineApiProjectPath;
        if (!configuredEngineApiProject.empty())
        {
            const std::filesystem::path configuredPathCandidate(configuredEngineApiProject);
            if (configuredPathCandidate.is_absolute())
                engineApiProjectPath = configuredPathCandidate;
            else
                engineApiProjectPath = (scriptProjectDirectory / configuredPathCandidate).lexically_normal();
        }

        if ((engineApiProjectPath.empty() || !std::filesystem::exists(engineApiProjectPath)) && !engineRoot.empty())
        {
            const std::filesystem::path workspaceCandidate = (engineRoot / "scripts" / "EngineManagedApi.csproj").lexically_normal();
            if (std::filesystem::exists(workspaceCandidate))
                engineApiProjectPath = workspaceCandidate;
        }

        const std::size_t projectTagPos = content.rfind("</Project>");
        if (projectTagPos == std::string::npos)
        {
            EngineLogger::Errorf("ProjectContext",
                                 "Could not patch script project (missing </Project>): ",
                                 scriptProjectPath);
            return false;
        }

        if (!engineApiProjectPath.empty() && std::filesystem::exists(engineApiProjectPath))
        {
            std::error_code relativeError;
            std::filesystem::path referencePath = std::filesystem::relative(engineApiProjectPath, scriptProjectDirectory, relativeError);
            if (relativeError || referencePath.empty())
                referencePath = engineApiProjectPath;

            std::string itemGroup = "  <ItemGroup Label=\"EngineManagedApiProjectReference\">\n";
            itemGroup += "    <ProjectReference Include=\"" + EscapeXmlAttribute(referencePath.generic_string()) + "\" />\n";
            itemGroup += "  </ItemGroup>\n";

            content.insert(projectTagPos, itemGroup);

            std::ofstream output(scriptProjectPath, std::ios::trunc);
            if (!output.is_open())
            {
                EngineLogger::Errorf("ProjectContext",
                                     "Failed to write patched script project: ",
                                     scriptProjectPath);
                return false;
            }

            output << content;
            if (!output.good())
            {
                EngineLogger::Errorf("ProjectContext",
                                     "Failed while saving patched script project: ",
                                     scriptProjectPath);
                return false;
            }

            EngineLogger::Infof("ProjectContext",
                                "[Migration] Added EngineManagedApi ProjectReference to legacy script project: ",
                                scriptProjectPath);
            return true;
        }

        std::vector<std::filesystem::path> wrapperPaths;
        for (const auto& wrapperName : wrapperNames)
        {
            const std::filesystem::path wrapperPath = engineRoot / "scripts" / wrapperName;
            if (std::filesystem::exists(wrapperPath))
                wrapperPaths.push_back(wrapperPath.lexically_normal());
        }

        if (wrapperPaths.empty())
        {
            EngineLogger::Errorf("ProjectContext",
                                 "Engine API wrappers not found under: ",
                                 (engineRoot / "scripts"));
            return false;
        }

        std::string itemGroup = "  <ItemGroup Label=\"EngineManagedApiReferences\">\n";

        for (const auto& wrapperPath : wrapperPaths)
        {
            std::error_code relativeError;
            std::filesystem::path includePath = std::filesystem::relative(wrapperPath, scriptProjectDirectory, relativeError);
            if (relativeError || includePath.empty())
                includePath = wrapperPath;

            const std::string includeValue = EscapeXmlAttribute(includePath.generic_string());
            const std::string linkValue = EscapeXmlAttribute((std::string("Engine/") + wrapperPath.filename().string()));
            itemGroup += "    <Compile Include=\"" + includeValue + "\" Link=\"" + linkValue + "\" />\n";
        }

        itemGroup += "  </ItemGroup>\n";

        content.insert(projectTagPos, itemGroup);

        std::ofstream output(scriptProjectPath, std::ios::trunc);
        if (!output.is_open())
        {
            EngineLogger::Errorf("ProjectContext",
                                 "Failed to write patched script project: ",
                                 scriptProjectPath);
            return false;
        }

        output << content;
        if (!output.good())
        {
            EngineLogger::Errorf("ProjectContext",
                                 "Failed while saving patched script project: ",
                                 scriptProjectPath);
            return false;
        }

        EngineLogger::Infof("ProjectContext",
                            "[Migration] Added fallback Engine API source links to legacy script project: ",
                            scriptProjectPath);
        return true;
    }

    bool BuildDotnetProjectAndReport(const std::filesystem::path& projectPath, const char* projectLabel)
    {
        if (projectPath.empty() || !std::filesystem::exists(projectPath))
        {
            EngineLogger::Infof("ProjectContext", "Skip build (project missing): ", projectLabel);
            return false;
        }

        const std::string command = "dotnet build \"" + projectPath.string() + "\" -c Debug -nologo -t:Rebuild 2>&1";
        EngineLogger::Infof("ProjectContext", "Building ", projectLabel, ": ", projectPath);

#if defined(_WIN32)
        FILE* pipe = _popen(command.c_str(), "r");
#else
        FILE* pipe = popen(command.c_str(), "r");
#endif
        if (!pipe)
        {
            EngineLogger::Errorf("ProjectContext", "Failed to start dotnet build process for ", projectLabel, ".");
            return false;
        }

        std::string output;
        std::array<char, 512> buffer{};
        while (std::fgets(buffer.data(), static_cast<int>(buffer.size()), pipe) != nullptr)
            output += buffer.data();

        int exitCode = -1;
#if defined(_WIN32)
        exitCode = _pclose(pipe);
#else
        exitCode = pclose(pipe);
#endif

        std::vector<std::string> warningLines;
        std::vector<std::string> errorLines;

        std::stringstream stream(output);
        std::string line;
        while (std::getline(stream, line))
        {
            const std::string trimmed = Trim(line);
            if (trimmed.empty())
                continue;

            const std::string lowered = ToLowerAscii(trimmed);
            if (lowered.find(": error ") != std::string::npos || lowered.find(" error cs") != std::string::npos)
            {
                errorLines.push_back(trimmed);
            }
            else if (lowered.find(": warning ") != std::string::npos || lowered.find(" warning cs") != std::string::npos)
            {
                warningLines.push_back(trimmed);
            }
        }

        const std::size_t maxDiagnosticLines = 12;
        for (std::size_t i = 0; i < warningLines.size() && i < maxDiagnosticLines; ++i)
            EngineLogger::Warningf("ProjectContext", "[CSharp] ", warningLines[i]);

        for (std::size_t i = 0; i < errorLines.size() && i < maxDiagnosticLines; ++i)
            EngineLogger::Errorf("ProjectContext", "[CSharp] ", errorLines[i]);

        if (exitCode == 0)
        {
            EngineLogger::Infof("ProjectContext",
                                "Build succeeded for ", projectLabel,
                                " (warnings: ", warningLines.size(), ").");
            return true;
        }

        EngineLogger::Errorf("ProjectContext",
                             "Build failed for ", projectLabel,
                             " (exit code: ", exitCode,
                             ", warnings: ", warningLines.size(),
                             ", errors: ", errorLines.size(), ").");

        if (errorLines.empty() && !output.empty())
            EngineLogger::Errorf("ProjectContext", "[CSharp][Output] ", Trim(output));

        return false;
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
        EngineLogger::Errorf("ProjectContext", "Failed to load metadata from: ", m_projectFilePath);
        m_isOpen = false;
        return false;
    }

    ApplyMetadataDefaults();
    if (!UpgradeProjectMetadataIfNeeded())
    {
        EngineLogger::Errorf("ProjectContext", "Failed to upgrade metadata for: ", m_projectFilePath);
        m_isOpen = false;
        return false;
    }

    m_assetsRoot = (m_projectRoot / m_metadata.assetsRoot).lexically_normal();
    m_scriptsRoot = (m_projectRoot / m_metadata.scriptsRoot).lexically_normal();
    m_scenesRoot = (m_projectRoot / m_metadata.scenesRoot).lexically_normal();
    m_libraryRoot = (m_projectRoot / m_metadata.libraryRoot).lexically_normal();

    std::filesystem::create_directories(m_assetsRoot);
    std::filesystem::create_directories(m_scriptsRoot);
    std::filesystem::create_directories(m_scenesRoot);
    std::filesystem::create_directories(m_libraryRoot);

    m_isOpen = true;
    PrepareManagedScriptProject();
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

bool ProjectContext::GetProjectSetting(const std::string& key, std::string& outValue) const
{
    const std::string trimmedKey = Trim(key);
    if (trimmedKey.empty())
        return false;

    const auto it = m_metadata.projectSettings.find(trimmedKey);
    if (it == m_metadata.projectSettings.end())
        return false;

    outValue = it->second;
    return true;
}

bool ProjectContext::SetProjectSetting(const std::string& key, const std::string& value)
{
    if (!m_isOpen)
        return false;

    const std::string trimmedKey = Trim(key);
    if (trimmedKey.empty())
        return false;

    const auto existing = m_metadata.projectSettings.find(trimmedKey);
    const bool hadExisting = existing != m_metadata.projectSettings.end();
    const std::string previousValue = hadExisting ? existing->second : std::string{};

    m_metadata.projectSettings[trimmedKey] = value;
    if (SaveProjectMetadata())
        return true;

    if (hadExisting)
        m_metadata.projectSettings[trimmedKey] = previousValue;
    else
        m_metadata.projectSettings.erase(trimmedKey);

    return false;
}

bool ProjectContext::RemoveProjectSetting(const std::string& key)
{
    if (!m_isOpen)
        return false;

    const std::string trimmedKey = Trim(key);
    if (trimmedKey.empty())
        return false;

    const auto existing = m_metadata.projectSettings.find(trimmedKey);
    if (existing == m_metadata.projectSettings.end())
        return true;

    const std::string previousValue = existing->second;
    m_metadata.projectSettings.erase(existing);
    if (SaveProjectMetadata())
        return true;

    m_metadata.projectSettings[trimmedKey] = previousValue;
    return false;
}

bool ProjectContext::LoadProjectMetadata()
{
    m_metadata = Metadata{};

    if (!std::filesystem::exists(m_projectFilePath))
    {
        EngineLogger::Errorf("ProjectContext", "project.json not found: ", m_projectFilePath);
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

    if (const auto engineVersion = ExtractJsonString(json, "engineVersion"))
        m_metadata.engineVersion = *engineVersion;

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

    if (const auto engineApiProject = ExtractJsonString(json, "engineApiProject"))
        m_metadata.engineApiProject = *engineApiProject;

    if (const auto targetFramework = ExtractJsonString(json, "targetFramework"))
        m_metadata.targetFramework = *targetFramework;

    rapidjson::Document document;
    if (!document.Parse(json.c_str()).HasParseError() && document.IsObject())
    {
        const auto settingsIt = document.FindMember("projectSettings");
        if (settingsIt != document.MemberEnd() && settingsIt->value.IsObject())
        {
            for (auto member = settingsIt->value.MemberBegin(); member != settingsIt->value.MemberEnd(); ++member)
            {
                if (!member->name.IsString())
                    continue;

                const std::string key = member->name.GetString();
                const rapidjson::Value& value = member->value;

                if (value.IsString())
                {
                    m_metadata.projectSettings[key] = value.GetString();
                }
                else if (value.IsBool())
                {
                    m_metadata.projectSettings[key] = value.GetBool() ? "true" : "false";
                }
                else if (value.IsInt())
                {
                    m_metadata.projectSettings[key] = std::to_string(value.GetInt());
                }
                else if (value.IsUint())
                {
                    m_metadata.projectSettings[key] = std::to_string(value.GetUint());
                }
                else if (value.IsInt64())
                {
                    m_metadata.projectSettings[key] = std::to_string(value.GetInt64());
                }
                else if (value.IsUint64())
                {
                    m_metadata.projectSettings[key] = std::to_string(value.GetUint64());
                }
                else if (value.IsDouble())
                {
                    m_metadata.projectSettings[key] = std::to_string(value.GetDouble());
                }
            }
        }
    }

    return true;
}

bool ProjectContext::UpgradeProjectMetadataIfNeeded()
{
    if (m_metadata.version > CurrentProjectVersion)
    {
        EngineLogger::Warningf("ProjectContext",
                               "project.json version ", m_metadata.version,
                               " is newer than supported version ", CurrentProjectVersion,
                               ". Opening without migration.");
        return true;
    }

    const std::filesystem::path workspaceRoot = m_workspaceRoot.empty()
        ? std::filesystem::current_path()
        : m_workspaceRoot;

    std::string expectedEngineApiProject;
    const std::filesystem::path engineApiAbsolutePath = (workspaceRoot / "scripts" / "EngineManagedApi.csproj").lexically_normal();
    if (std::filesystem::exists(engineApiAbsolutePath))
    {
        std::error_code relativeError;
        const std::filesystem::path relativePath = std::filesystem::relative(engineApiAbsolutePath, m_projectRoot, relativeError);
        if (!relativeError && !relativePath.empty())
            expectedEngineApiProject = relativePath.generic_string();
    }

    bool metadataChanged = false;
    if (m_metadata.engineApiProject.empty() && !expectedEngineApiProject.empty())
    {
        m_metadata.engineApiProject = expectedEngineApiProject;
        metadataChanged = true;
    }

    const bool hasExpectedEngineVersion = m_metadata.engineVersion == ProjectEngineVersion;
    if (m_metadata.version == CurrentProjectVersion && hasExpectedEngineVersion && !metadataChanged)
        return true;

    const int previousVersion = m_metadata.version;
    m_metadata.version = CurrentProjectVersion;
    m_metadata.engineVersion = ProjectEngineVersion;

    if (!SaveProjectMetadata())
        return false;

    if (previousVersion < CurrentProjectVersion)
    {
        EngineLogger::Infof("ProjectContext",
                            "Upgraded project.json from version ", previousVersion,
                            " to ", CurrentProjectVersion, ".");
    }
    else
    {
        EngineLogger::Infof("ProjectContext",
                            "Refreshed project.json engineVersion to ", ProjectEngineVersion, ".");
    }

    return true;
}

bool ProjectContext::SaveProjectMetadata() const
{
    std::ofstream output(m_projectFilePath, std::ios::trunc);
    if (!output.is_open())
        return false;

    output << "{\n"
           << "  \"name\": \"" << EscapeJsonString(m_metadata.name) << "\",\n"
           << "  \"template\": \"" << EscapeJsonString(m_metadata.templateName) << "\",\n"
           << "  \"version\": " << m_metadata.version << ",\n"
           << "  \"engineVersion\": \"" << EscapeJsonString(m_metadata.engineVersion) << "\",\n"
           << "  \"assetsRoot\": \"" << EscapeJsonString(m_metadata.assetsRoot) << "\",\n"
           << "  \"scriptsRoot\": \"" << EscapeJsonString(m_metadata.scriptsRoot) << "\",\n"
           << "  \"scenesRoot\": \"" << EscapeJsonString(m_metadata.scenesRoot) << "\",\n"
           << "  \"libraryRoot\": \"" << EscapeJsonString(m_metadata.libraryRoot) << "\",\n"
           << "  \"scriptProject\": \"" << EscapeJsonString(m_metadata.scriptProject) << "\",\n"
           << "  \"scriptSolution\": \"" << EscapeJsonString(m_metadata.scriptSolution) << "\",\n"
           << "  \"assemblyPath\": \"" << EscapeJsonString(m_metadata.assemblyPath) << "\",\n"
           << "  \"engineApiProject\": \"" << EscapeJsonString(m_metadata.engineApiProject) << "\",\n"
           << "  \"targetFramework\": \"" << EscapeJsonString(m_metadata.targetFramework) << "\",\n";

    output << "  \"projectSettings\": ";
    if (m_metadata.projectSettings.empty())
    {
        output << "{}\n";
    }
    else
    {
        std::vector<std::string> keys;
        keys.reserve(m_metadata.projectSettings.size());
        for (const auto& [key, value] : m_metadata.projectSettings)
        {
            (void)value;
            keys.push_back(key);
        }

        std::sort(keys.begin(), keys.end());

        output << "{\n";
        for (std::size_t index = 0; index < keys.size(); ++index)
        {
            const std::string& key = keys[index];
            const auto found = m_metadata.projectSettings.find(key);
            if (found == m_metadata.projectSettings.end())
                continue;

            output << "    \"" << EscapeJsonString(key) << "\": \""
                   << EscapeJsonString(found->second) << "\"";

            if (index + 1 < keys.size())
                output << ",";

            output << "\n";
        }

        output << "  }\n";
    }

    output << "}\n";

    return output.good();
}

void ProjectContext::PrepareManagedScriptProject() const
{
    const std::filesystem::path scriptProjectPath = ScriptProjectPath();
    if (scriptProjectPath.empty())
    {
        EngineLogger::Info("ProjectContext", "Script project path is empty; skipping C# build.");
        return;
    }

    const std::filesystem::path workspaceRoot = m_workspaceRoot.empty()
        ? std::filesystem::current_path()
        : m_workspaceRoot;

    if (!EnsureEngineApiReferencesInCsproj(scriptProjectPath, workspaceRoot, m_metadata.engineApiProject))
    {
        EngineLogger::Errorf("ProjectContext",
                             "[Migration] Could not ensure Engine references in script project: ",
                             scriptProjectPath);
    }
    BuildDotnetProjectAndReport(scriptProjectPath, "script project");
}

void ProjectContext::ApplyMetadataDefaults()
{
    if (m_metadata.version < 1)
        m_metadata.version = 1;

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

    if (m_metadata.engineApiProject.empty())
    {
        const std::filesystem::path workspaceRoot = m_workspaceRoot.empty()
            ? std::filesystem::current_path()
            : m_workspaceRoot;
        const std::filesystem::path engineApiAbsolutePath = (workspaceRoot / "scripts" / "EngineManagedApi.csproj").lexically_normal();
        if (std::filesystem::exists(engineApiAbsolutePath))
        {
            std::error_code relativeError;
            const std::filesystem::path relativePath = std::filesystem::relative(engineApiAbsolutePath, m_projectRoot, relativeError);
            if (!relativeError && !relativePath.empty())
                m_metadata.engineApiProject = relativePath.generic_string();
        }
    }

    if (m_metadata.assemblyPath.empty())
    {
        if (!m_metadata.name.empty())
            m_metadata.assemblyPath = "bin/Debug/" + m_metadata.targetFramework + "/" + m_metadata.name + ".dll";
        else
            m_metadata.assemblyPath = "scripts/bin/Debug/net472/GameScripts.dll";

        EngineLogger::Infof("ProjectContext",
                            "Missing assemblyPath in project.json, using default: ",
                            m_metadata.assemblyPath);
    }
}
