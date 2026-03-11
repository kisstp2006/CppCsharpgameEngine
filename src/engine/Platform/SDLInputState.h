#pragma once

#include <SDL.h>

namespace SDLInputState
{
    void BeginFrame();
    void ProcessEvent(const SDL_Event& event);
    void EndFrame();

    bool GetMouseButton(int button);
    bool GetMouseButtonDown(int button);
    bool GetMouseButtonUp(int button);

    float GetMouseDeltaX();
    float GetMouseDeltaY();
    float GetMouseWheel();
    float GetMousePosX();
    float GetMousePosY();

    bool GetKey(int scancode);
    bool GetKeyDown(int scancode);
    bool GetKeyUp(int scancode);
}
