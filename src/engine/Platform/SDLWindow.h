#pragma once

#include <SDL.h>
#include <string>

class SDLWindow
{
public:
    SDLWindow();
    ~SDLWindow();

    bool Initialize(const std::string& title, int width, int height);
    void Shutdown();

    void SwapBuffers();

    void SetSize(int width, int height);
    void SetTitle(const std::string& title);
    void Center();
    void Maximize();

    SDL_Window* GetSDL_Window() const { return m_window; }
    SDL_GLContext GetGLContext() const { return m_glContext; }

private:
    SDL_Window* m_window = nullptr;
    SDL_GLContext m_glContext = nullptr;
};
