#pragma once

#include <string>
#include <vector>

class ExplorerDialog
{
public:
    static std::string PickFolder(const std::string& title, const std::string& initialPath = {});
    static std::string PickFile(const std::string& title, const std::string& initialPath = {});
    static std::vector<std::string> PickFiles(const std::string& title, const std::string& initialPath = {});
};