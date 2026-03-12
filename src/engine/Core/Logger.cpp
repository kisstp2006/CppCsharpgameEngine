#include "Logger.h"

#include <chrono>
#include <ctime>
#include <iomanip>
#include <iostream>
#include <mutex>
#include <sstream>
#include <string>
#include <utility>

namespace
{
std::mutex g_loggerMutex;
EngineLogger::Sink g_sink;

std::tm ToLocalTime(std::time_t timestamp)
{
    std::tm localTime{};
#if defined(_WIN32)
    localtime_s(&localTime, &timestamp);
#else
    localtime_r(&timestamp, &localTime);
#endif
    return localTime;
}

std::string BuildPrefix(EngineLogger::Level level, std::string_view category)
{
    const auto now = std::chrono::system_clock::now();
    const auto epochMillis = std::chrono::duration_cast<std::chrono::milliseconds>(now.time_since_epoch());
    const std::time_t timestamp = std::chrono::system_clock::to_time_t(now);
    const int millisPart = static_cast<int>(epochMillis.count() % 1000);
    const std::tm localTime = ToLocalTime(timestamp);

    std::ostringstream stream;
    stream << '[' << std::put_time(&localTime, "%H:%M:%S") << '.'
           << std::setw(3) << std::setfill('0') << millisPart << ']'
           << '[' << EngineLogger::LevelToString(level) << ']';
    if (!category.empty())
        stream << '[' << category << ']';

    return stream.str();
}

void WriteDefault(EngineLogger::Level level, std::string_view category, std::string_view message)
{
    std::ostream& output = (level == EngineLogger::Level::Warning || level == EngineLogger::Level::Error)
        ? std::cerr
        : std::cout;
    output << BuildPrefix(level, category) << ' ' << message << std::endl;
}
} // namespace

void EngineLogger::SetSink(Sink sink)
{
    std::scoped_lock lock(g_loggerMutex);
    g_sink = std::move(sink);
}

void EngineLogger::ResetSink()
{
    std::scoped_lock lock(g_loggerMutex);
    g_sink = nullptr;
}

void EngineLogger::Log(Level level, std::string_view category, std::string_view message)
{
    Sink sink;
    {
        std::scoped_lock lock(g_loggerMutex);
        sink = g_sink;
    }

    if (sink)
    {
        sink(level, category, message);
        return;
    }

    std::scoped_lock lock(g_loggerMutex);
    WriteDefault(level, category, message);
}

void EngineLogger::Debug(std::string_view category, std::string_view message)
{
    Log(Level::Debug, category, message);
}

void EngineLogger::Info(std::string_view category, std::string_view message)
{
    Log(Level::Info, category, message);
}

void EngineLogger::Warning(std::string_view category, std::string_view message)
{
    Log(Level::Warning, category, message);
}

void EngineLogger::Error(std::string_view category, std::string_view message)
{
    Log(Level::Error, category, message);
}

const char* EngineLogger::LevelToString(Level level)
{
    switch (level)
    {
    case Level::Debug:
        return "Debug";
    case Level::Info:
        return "Info";
    case Level::Warning:
        return "Warning";
    case Level::Error:
        return "Error";
    default:
        return "Unknown";
    }
}
