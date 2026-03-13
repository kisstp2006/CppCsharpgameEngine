#pragma once

#include <string>

enum class BootStage
{
    ProjectSelection,
    Loading,
    Editor,
};

struct BootStageResult
{
    BootStage nextStage = BootStage::ProjectSelection;
    std::string selectedProjectPath;
    bool shouldQuit = false;
};
