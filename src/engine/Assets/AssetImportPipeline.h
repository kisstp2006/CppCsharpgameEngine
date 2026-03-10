#pragma once

#include "AssetDatabase.h"

#include <cstddef>

class ProjectContext;

class AssetImportPipeline
{
public:
    struct Result
    {
        std::size_t imported = 0;
        std::size_t removed = 0;
        std::size_t failed = 0;
    };

public:
    Result Run(const ProjectContext& projectContext, const std::vector<AssetDatabase::AssetChange>& changes) const;
};
