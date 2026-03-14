#include "AnimationClip.h"

#include <algorithm>
#include <cctype>
#include <cstdlib>
#include <cmath>
#include <fstream>
#include <locale>
#include <sstream>

namespace
{
    static std::string Trim(const std::string& value)
    {
        const std::string whitespace = " \t\r\n";
        const std::size_t start = value.find_first_not_of(whitespace);
        if (start == std::string::npos)
            return {};

        const std::size_t end = value.find_last_not_of(whitespace);
        return value.substr(start, end - start + 1);
    }

    static std::string ToLowerAscii(std::string value)
    {
        for (char& c : value)
            c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
        return value;
    }

    static std::vector<std::string> Tokenize(const std::string& line)
    {
        std::istringstream stream(line);
        std::vector<std::string> tokens;
        std::string token;
        while (stream >> token)
            tokens.push_back(token);
        return tokens;
    }

    static bool ParseFloat(const std::string& token, float& outValue)
    {
        std::istringstream stream(token);
        stream.imbue(std::locale::classic());
        stream >> outValue;
        return !stream.fail() && stream.eof() && std::isfinite(outValue);
    }

    static bool ParseBool(const std::string& token, bool& outValue)
    {
        const std::string lower = ToLowerAscii(token);
        if (lower == "true" || lower == "1")
        {
            outValue = true;
            return true;
        }

        if (lower == "false" || lower == "0")
        {
            outValue = false;
            return true;
        }

        return false;
    }

    static void SortTrackKeys(AnimationTrack& track)
    {
        if (track.property == AnimationTrackProperty::TransformRotation)
        {
            std::sort(track.floatKeys.begin(), track.floatKeys.end(), [](const AnimationKeyframeFloat& lhs, const AnimationKeyframeFloat& rhs)
            {
                return lhs.time < rhs.time;
            });
            return;
        }

        std::sort(track.vec2Keys.begin(), track.vec2Keys.end(), [](const AnimationKeyframeVec2& lhs, const AnimationKeyframeVec2& rhs)
        {
            return lhs.time < rhs.time;
        });
    }

    static float ResolveDuration(const AnimationClip& clip)
    {
        float maxTime = 0.0f;
        for (const AnimationTrack& track : clip.tracks)
        {
            if (track.property == AnimationTrackProperty::TransformRotation)
            {
                if (!track.floatKeys.empty())
                    maxTime = std::max(maxTime, track.floatKeys.back().time);
            }
            else
            {
                if (!track.vec2Keys.empty())
                    maxTime = std::max(maxTime, track.vec2Keys.back().time);
            }
        }

        return maxTime;
    }
}

const char* AnimationTrackPropertyToString(AnimationTrackProperty property)
{
    switch (property)
    {
    case AnimationTrackProperty::TransformPosition:
        return "Transform.Position";
    case AnimationTrackProperty::TransformRotation:
        return "Transform.Rotation";
    case AnimationTrackProperty::TransformScale:
        return "Transform.Scale";
    default:
        return "Transform.Position";
    }
}

bool AnimationTrackPropertyFromString(const std::string& value, AnimationTrackProperty& outProperty)
{
    const std::string lower = ToLowerAscii(Trim(value));
    if (lower == "transform.position")
    {
        outProperty = AnimationTrackProperty::TransformPosition;
        return true;
    }

    if (lower == "transform.rotation")
    {
        outProperty = AnimationTrackProperty::TransformRotation;
        return true;
    }

    if (lower == "transform.scale")
    {
        outProperty = AnimationTrackProperty::TransformScale;
        return true;
    }

    return false;
}

const char* AnimationInterpolationToString(AnimationInterpolation interpolation)
{
    switch (interpolation)
    {
    case AnimationInterpolation::Linear:
        return "Linear";
    case AnimationInterpolation::Step:
        return "Step";
    default:
        return "Linear";
    }
}

bool AnimationInterpolationFromString(const std::string& value, AnimationInterpolation& outInterpolation)
{
    const std::string lower = ToLowerAscii(Trim(value));
    if (lower == "linear")
    {
        outInterpolation = AnimationInterpolation::Linear;
        return true;
    }

    if (lower == "step")
    {
        outInterpolation = AnimationInterpolation::Step;
        return true;
    }

    return false;
}

bool LoadAnimationClipFromFile(const std::filesystem::path& path, AnimationClip& outClip, std::string& outError)
{
    outError.clear();

    std::ifstream input(path);
    if (!input.is_open())
    {
        outError = "Failed to open animation clip: " + path.string();
        return false;
    }

    AnimationClip clip;
    AnimationTrack currentTrack;
    bool inTrack = false;

    std::string line;
    std::size_t lineNumber = 0;
    while (std::getline(input, line))
    {
        ++lineNumber;

        std::string trimmed = Trim(line);
        if (trimmed.empty() || trimmed[0] == '#')
            continue;

        const std::vector<std::string> tokens = Tokenize(trimmed);
        if (tokens.empty())
            continue;

        const std::string& command = tokens[0];

        if (command == "anim_version")
        {
            if (tokens.size() < 2)
            {
                outError = "Invalid anim_version at line " + std::to_string(lineNumber);
                return false;
            }

            const int parsedVersion = std::atoi(tokens[1].c_str());
            if (parsedVersion < 1)
            {
                outError = "Unsupported animation version at line " + std::to_string(lineNumber);
                return false;
            }

            clip.version = static_cast<std::uint32_t>(parsedVersion);
            continue;
        }

        if (command == "duration")
        {
            if (tokens.size() < 2 || !ParseFloat(tokens[1], clip.duration))
            {
                outError = "Invalid duration at line " + std::to_string(lineNumber);
                return false;
            }

            continue;
        }

        if (command == "loop")
        {
            if (tokens.size() < 2 || !ParseBool(tokens[1], clip.loop))
            {
                outError = "Invalid loop value at line " + std::to_string(lineNumber);
                return false;
            }

            continue;
        }

        if (command == "track")
        {
            if (inTrack)
            {
                outError = "Nested track is not allowed at line " + std::to_string(lineNumber);
                return false;
            }

            if (tokens.size() < 2)
            {
                outError = "Missing track property at line " + std::to_string(lineNumber);
                return false;
            }

            currentTrack = AnimationTrack{};
            if (!AnimationTrackPropertyFromString(tokens[1], currentTrack.property))
            {
                outError = "Unknown track property at line " + std::to_string(lineNumber);
                return false;
            }

            if (tokens.size() >= 3 && !AnimationInterpolationFromString(tokens[2], currentTrack.defaultInterpolation))
            {
                outError = "Invalid track interpolation at line " + std::to_string(lineNumber);
                return false;
            }

            inTrack = true;
            continue;
        }

        if (command == "endtrack")
        {
            if (!inTrack)
            {
                outError = "endtrack without track at line " + std::to_string(lineNumber);
                return false;
            }

            SortTrackKeys(currentTrack);
            clip.tracks.push_back(currentTrack);
            currentTrack = AnimationTrack{};
            inTrack = false;
            continue;
        }

        if (command == "key")
        {
            if (!inTrack)
            {
                outError = "key outside track at line " + std::to_string(lineNumber);
                return false;
            }

            if (currentTrack.property == AnimationTrackProperty::TransformRotation)
            {
                if (tokens.size() < 3)
                {
                    outError = "Rotation key expects: key <time> <value> [interp] at line " + std::to_string(lineNumber);
                    return false;
                }

                AnimationKeyframeFloat key;
                if (!ParseFloat(tokens[1], key.time) || !ParseFloat(tokens[2], key.value))
                {
                    outError = "Invalid rotation key values at line " + std::to_string(lineNumber);
                    return false;
                }

                key.interpolation = currentTrack.defaultInterpolation;
                if (tokens.size() >= 4 && !AnimationInterpolationFromString(tokens[3], key.interpolation))
                {
                    outError = "Invalid rotation key interpolation at line " + std::to_string(lineNumber);
                    return false;
                }

                currentTrack.floatKeys.push_back(key);
            }
            else
            {
                if (tokens.size() < 4)
                {
                    outError = "Vec2 key expects: key <time> <x> <y> [interp] at line " + std::to_string(lineNumber);
                    return false;
                }

                AnimationKeyframeVec2 key;
                if (!ParseFloat(tokens[1], key.time) || !ParseFloat(tokens[2], key.x) || !ParseFloat(tokens[3], key.y))
                {
                    outError = "Invalid vec2 key values at line " + std::to_string(lineNumber);
                    return false;
                }

                key.interpolation = currentTrack.defaultInterpolation;
                if (tokens.size() >= 5 && !AnimationInterpolationFromString(tokens[4], key.interpolation))
                {
                    outError = "Invalid vec2 key interpolation at line " + std::to_string(lineNumber);
                    return false;
                }

                currentTrack.vec2Keys.push_back(key);
            }

            continue;
        }

        outError = "Unknown command at line " + std::to_string(lineNumber) + ": " + command;
        return false;
    }

    if (inTrack)
    {
        outError = "Unclosed track at end of file.";
        return false;
    }

    for (AnimationTrack& track : clip.tracks)
        SortTrackKeys(track);

    const float maxTime = ResolveDuration(clip);
    if (clip.duration <= 0.0001f)
        clip.duration = std::max(0.0001f, maxTime);

    outClip = std::move(clip);
    return true;
}

bool SaveAnimationClipToFile(const std::filesystem::path& path, const AnimationClip& clip, std::string& outError)
{
    outError.clear();

    std::error_code createError;
    const std::filesystem::path parentPath = path.parent_path();
    if (!parentPath.empty())
        std::filesystem::create_directories(parentPath, createError);

    if (createError)
    {
        outError = "Failed to create animation clip directory: " + parentPath.string();
        return false;
    }

    std::ofstream output(path, std::ios::out | std::ios::trunc);
    if (!output.is_open())
    {
        outError = "Failed to open animation clip for write: " + path.string();
        return false;
    }

    output << "anim_version 1\n";
    output << "duration " << clip.duration << "\n";
    output << "loop " << (clip.loop ? "true" : "false") << "\n\n";

    for (const AnimationTrack& track : clip.tracks)
    {
        output << "track " << AnimationTrackPropertyToString(track.property)
               << " " << AnimationInterpolationToString(track.defaultInterpolation)
               << "\n";

        if (track.property == AnimationTrackProperty::TransformRotation)
        {
            for (const AnimationKeyframeFloat& key : track.floatKeys)
            {
                output << "key " << key.time << " " << key.value << " "
                       << AnimationInterpolationToString(key.interpolation) << "\n";
            }
        }
        else
        {
            for (const AnimationKeyframeVec2& key : track.vec2Keys)
            {
                output << "key " << key.time << " " << key.x << " " << key.y << " "
                       << AnimationInterpolationToString(key.interpolation) << "\n";
            }
        }

        output << "endtrack\n\n";
    }

    if (!output.good())
    {
        outError = "Failed while writing animation clip: " + path.string();
        return false;
    }

    return true;
}
