#pragma once

#include <functional>
#include <sstream>
#include <string_view>
#include <utility>

class EngineLogger
{
public:
    enum class Level
    {
        Debug,
        Info,
        Warning,
        Error,
    };

    using Sink = std::function<void(Level level, std::string_view category, std::string_view message)>;

    static void SetSink(Sink sink);
    static void ResetSink();

    static void Log(Level level, std::string_view category, std::string_view message);
    static void Debug(std::string_view category, std::string_view message);
    static void Info(std::string_view category, std::string_view message);
    static void Warning(std::string_view category, std::string_view message);
    static void Error(std::string_view category, std::string_view message);

    template<typename... TArgs>
    static void Logf(Level level, std::string_view category, TArgs&&... args)
    {
        std::ostringstream stream;
        (stream << ... << std::forward<TArgs>(args));
        Log(level, category, stream.str());
    }

    template<typename... TArgs>
    static void Debugf(std::string_view category, TArgs&&... args)
    {
        Logf(Level::Debug, category, std::forward<TArgs>(args)...);
    }

    template<typename... TArgs>
    static void Infof(std::string_view category, TArgs&&... args)
    {
        Logf(Level::Info, category, std::forward<TArgs>(args)...);
    }

    template<typename... TArgs>
    static void Warningf(std::string_view category, TArgs&&... args)
    {
        Logf(Level::Warning, category, std::forward<TArgs>(args)...);
    }

    template<typename... TArgs>
    static void Errorf(std::string_view category, TArgs&&... args)
    {
        Logf(Level::Error, category, std::forward<TArgs>(args)...);
    }

    static const char* LevelToString(Level level);
};
