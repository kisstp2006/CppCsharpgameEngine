#include "SDLWindow.h"

#include "engine/Core/Logger.h"

SDLWindow::SDLWindow() = default;
SDLWindow::~SDLWindow() = default;

bool SDLWindow::Initialize(const std::string& title, int width, int height)
{
    if (SDL_Init(SDL_INIT_VIDEO | SDL_INIT_EVENTS | SDL_INIT_TIMER) != 0)
    {
        EngineLogger::Error("SDL", std::string("SDL_Init failed: ") + SDL_GetError());
        return false;
    }

    // Request OpenGL 3.3 core context
    SDL_GL_SetAttribute(SDL_GL_CONTEXT_MAJOR_VERSION, 3);
    SDL_GL_SetAttribute(SDL_GL_CONTEXT_MINOR_VERSION, 3);
    SDL_GL_SetAttribute(SDL_GL_CONTEXT_PROFILE_MASK, SDL_GL_CONTEXT_PROFILE_CORE);
    SDL_GL_SetAttribute(SDL_GL_DOUBLEBUFFER, 1);
    SDL_GL_SetAttribute(SDL_GL_DEPTH_SIZE, 24);

    m_window = SDL_CreateWindow(
        title.c_str(),
        SDL_WINDOWPOS_CENTERED,
        SDL_WINDOWPOS_CENTERED,
        width,
        height,
        SDL_WINDOW_OPENGL | SDL_WINDOW_RESIZABLE
    );

    if (!m_window)
    {
        EngineLogger::Error("SDL", std::string("SDL_CreateWindow failed: ") + SDL_GetError());
        return false;
    }

    m_glContext = SDL_GL_CreateContext(m_window);
    if (!m_glContext)
    {
        EngineLogger::Error("SDL", std::string("SDL_GL_CreateContext failed: ") + SDL_GetError());
        return false;
    }

    // Enable vsync
    if (SDL_GL_SetSwapInterval(1) != 0)
    {
        EngineLogger::Warning("SDL", std::string("Unable to set VSync: ") + SDL_GetError());
    }

    return true;
}

void SDLWindow::Shutdown()
{
    if (m_glContext)
    {
        SDL_GL_DeleteContext(m_glContext);
        m_glContext = nullptr;
    }

    if (m_window)
    {
        SDL_DestroyWindow(m_window);
        m_window = nullptr;
    }

    SDL_Quit();
}

void SDLWindow::SwapBuffers()
{
    if (m_window)
        SDL_GL_SwapWindow(m_window);
}

void SDLWindow::SetSize(int width, int height)
{
    if (m_window)
        SDL_SetWindowSize(m_window, width, height);
}

void SDLWindow::SetTitle(const std::string& title)
{
    if (m_window)
        SDL_SetWindowTitle(m_window, title.c_str());
}

void SDLWindow::Center()
{
    if (m_window)
        SDL_SetWindowPosition(m_window, SDL_WINDOWPOS_CENTERED, SDL_WINDOWPOS_CENTERED);
}

void SDLWindow::Maximize()
{
    if (m_window)
        SDL_MaximizeWindow(m_window);
}

void SDLWindow::SetResizable(bool enabled)
{
    if (m_window)
        SDL_SetWindowResizable(m_window, enabled ? SDL_TRUE : SDL_FALSE);
}

void SDLWindow::SetBorderless(bool enabled)
{
    if (m_window)
        SDL_SetWindowBordered(m_window, enabled ? SDL_FALSE : SDL_TRUE);
}
