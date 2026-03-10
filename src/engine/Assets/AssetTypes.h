#pragma once

#include <cstdint>

enum class AssetType
{
    Unknown = 0,
    ModelSource,
    ImageSource,
    AudioSource,
    ScriptSource,
    ManagedAssembly,
    ProjectFile,
    SolutionFile,
    ScriptProjectFile,
};

using AssetHandle = std::uint64_t;

constexpr AssetHandle InvalidAssetHandle = 0;
