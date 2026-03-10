#pragma once

#include "AssetTypes.h"

#include <cstdint>
#include <filesystem>
#include <string>

struct AssetRecord
{
    AssetHandle handle = InvalidAssetHandle;
    AssetType type = AssetType::Unknown;
    std::filesystem::path sourcePath;
    std::filesystem::path projectRelativePath;
    std::uint64_t fileSize = 0;
    std::uint64_t lastWriteTicks = 0;
    std::string hash;
};
