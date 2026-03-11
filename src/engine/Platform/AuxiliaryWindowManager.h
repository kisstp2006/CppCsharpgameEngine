#pragma once

#include <SDL.h>

#include <cstddef>
#include <cstdint>
#include <string>
#include <unordered_map>

class AuxiliaryWindowManager
{
public:
    using WindowId = std::uint32_t;

    struct CreateDesc
    {
        std::string title = "Auxiliary Window";
        int width = 640;
        int height = 360;
        bool resizable = true;
        bool borderless = false;
        bool alwaysOnTop = false;
        bool startHidden = false;
    };

    AuxiliaryWindowManager() = default;
    ~AuxiliaryWindowManager();

    WindowId CreateWindow(const CreateDesc& desc);
    bool DestroyWindow(WindowId id);
    void DestroyAllWindows();

    bool ShowWindow(WindowId id);
    bool HideWindow(WindowId id);
    bool SetWindowTitle(WindowId id, const std::string& title);
    bool SetWindowSize(WindowId id, int width, int height);
    bool CenterWindow(WindowId id);

    std::size_t GetWindowCount() const;
    bool HandleWindowCloseEvent(const SDL_Event& event);

private:
    struct Entry
    {
        SDL_Window* window = nullptr;
    };

    using EntryMap = std::unordered_map<WindowId, Entry>;

    EntryMap::iterator FindBySdlWindowId(std::uint32_t sdlWindowId);
    EntryMap::iterator FindEntry(WindowId id);
    EntryMap::const_iterator FindEntry(WindowId id) const;

private:
    std::unordered_map<WindowId, Entry> m_windows;
    WindowId m_nextWindowId = 1;
};
