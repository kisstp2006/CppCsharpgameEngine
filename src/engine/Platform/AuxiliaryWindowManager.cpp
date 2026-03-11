#include "AuxiliaryWindowManager.h"

AuxiliaryWindowManager::~AuxiliaryWindowManager()
{
    DestroyAllWindows();
}

AuxiliaryWindowManager::WindowId AuxiliaryWindowManager::CreateWindow(const CreateDesc& desc)
{
    if (desc.width <= 0 || desc.height <= 0)
        return 0;

    std::string title = desc.title;
    if (title.empty())
        title = "Auxiliary Window";

    std::uint32_t flags = 0;
    if (desc.resizable)
        flags |= SDL_WINDOW_RESIZABLE;
    if (desc.borderless)
        flags |= SDL_WINDOW_BORDERLESS;
    if (desc.alwaysOnTop)
        flags |= SDL_WINDOW_ALWAYS_ON_TOP;
    if (desc.startHidden)
        flags |= SDL_WINDOW_HIDDEN;

    SDL_Window* window = SDL_CreateWindow(title.c_str(),
                                          SDL_WINDOWPOS_CENTERED,
                                          SDL_WINDOWPOS_CENTERED,
                                          desc.width,
                                          desc.height,
                                          static_cast<int>(flags));
    if (!window)
        return 0;

    WindowId id = m_nextWindowId++;
    if (id == 0)
        id = m_nextWindowId++;

    Entry entry;
    entry.window = window;
    m_windows.emplace(id, entry);

    return id;
}

bool AuxiliaryWindowManager::DestroyWindow(WindowId id)
{
    const auto it = FindEntry(id);
    if (it == m_windows.end())
        return false;

    SDL_DestroyWindow(it->second.window);
    m_windows.erase(it);
    return true;
}

void AuxiliaryWindowManager::DestroyAllWindows()
{
    for (auto& [id, entry] : m_windows)
    {
        (void)id;
        if (entry.window)
            SDL_DestroyWindow(entry.window);
    }

    m_windows.clear();
}

bool AuxiliaryWindowManager::ShowWindow(WindowId id)
{
    const auto it = FindEntry(id);
    if (it == m_windows.end())
        return false;

    SDL_ShowWindow(it->second.window);
    return true;
}

bool AuxiliaryWindowManager::HideWindow(WindowId id)
{
    const auto it = FindEntry(id);
    if (it == m_windows.end())
        return false;

    SDL_HideWindow(it->second.window);
    return true;
}

bool AuxiliaryWindowManager::SetWindowTitle(WindowId id, const std::string& title)
{
    const auto it = FindEntry(id);
    if (it == m_windows.end())
        return false;

    SDL_SetWindowTitle(it->second.window, title.c_str());
    return true;
}

bool AuxiliaryWindowManager::SetWindowSize(WindowId id, int width, int height)
{
    if (width <= 0 || height <= 0)
        return false;

    const auto it = FindEntry(id);
    if (it == m_windows.end())
        return false;

    SDL_SetWindowSize(it->second.window, width, height);
    return true;
}

bool AuxiliaryWindowManager::CenterWindow(WindowId id)
{
    const auto it = FindEntry(id);
    if (it == m_windows.end())
        return false;

    SDL_SetWindowPosition(it->second.window, SDL_WINDOWPOS_CENTERED, SDL_WINDOWPOS_CENTERED);
    return true;
}

std::size_t AuxiliaryWindowManager::GetWindowCount() const
{
    return m_windows.size();
}

bool AuxiliaryWindowManager::HandleWindowCloseEvent(const SDL_Event& event)
{
    if (event.type != SDL_WINDOWEVENT || event.window.event != SDL_WINDOWEVENT_CLOSE)
        return false;

    auto it = FindBySdlWindowId(event.window.windowID);
    if (it == m_windows.end())
        return false;

    SDL_DestroyWindow(it->second.window);
    m_windows.erase(it);
    return true;
}

AuxiliaryWindowManager::EntryMap::iterator AuxiliaryWindowManager::FindBySdlWindowId(std::uint32_t sdlWindowId)
{
    for (auto it = m_windows.begin(); it != m_windows.end(); ++it)
    {
        if (!it->second.window)
            continue;

        if (SDL_GetWindowID(it->second.window) == sdlWindowId)
            return it;
    }

    return m_windows.end();
}

AuxiliaryWindowManager::EntryMap::iterator AuxiliaryWindowManager::FindEntry(WindowId id)
{
    return m_windows.find(id);
}

AuxiliaryWindowManager::EntryMap::const_iterator AuxiliaryWindowManager::FindEntry(WindowId id) const
{
    return m_windows.find(id);
}
