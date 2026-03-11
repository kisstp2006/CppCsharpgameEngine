#include "SDLInputState.h"

namespace
{
    constexpr int kMouseButtonCount = 3;

    bool g_mouseHeld[kMouseButtonCount] = { false, false, false };
    bool g_mouseDown[kMouseButtonCount] = { false, false, false };
    bool g_mouseUp[kMouseButtonCount] = { false, false, false };

    bool g_keyHeld[SDL_NUM_SCANCODES] = {};
    bool g_keyDown[SDL_NUM_SCANCODES] = {};
    bool g_keyUp[SDL_NUM_SCANCODES] = {};

    float g_mouseDeltaX = 0.0f;
    float g_mouseDeltaY = 0.0f;
    float g_mouseWheel = 0.0f;
    float g_mousePosX = 0.0f;
    float g_mousePosY = 0.0f;

    int ToMouseIndex(Uint8 sdlButton)
    {
        switch (sdlButton)
        {
        case SDL_BUTTON_LEFT:
            return 0;
        case SDL_BUTTON_RIGHT:
            return 1;
        case SDL_BUTTON_MIDDLE:
            return 2;
        default:
            return -1;
        }
    }

    bool IsValidMouseIndex(int index)
    {
        return index >= 0 && index < kMouseButtonCount;
    }

    bool IsValidScancode(int scancode)
    {
        return scancode >= 0 && scancode < SDL_NUM_SCANCODES;
    }
}

namespace SDLInputState
{
    void BeginFrame()
    {
        g_mouseDown[0] = false;
        g_mouseDown[1] = false;
        g_mouseDown[2] = false;

        g_mouseUp[0] = false;
        g_mouseUp[1] = false;
        g_mouseUp[2] = false;

        for (int i = 0; i < SDL_NUM_SCANCODES; ++i)
        {
            g_keyDown[i] = false;
            g_keyUp[i] = false;
        }

        g_mouseDeltaX = 0.0f;
        g_mouseDeltaY = 0.0f;
        g_mouseWheel = 0.0f;
    }

    void ProcessEvent(const SDL_Event& event)
    {
        switch (event.type)
        {
        case SDL_MOUSEMOTION:
            g_mousePosX = static_cast<float>(event.motion.x);
            g_mousePosY = static_cast<float>(event.motion.y);
            g_mouseDeltaX += static_cast<float>(event.motion.xrel);
            g_mouseDeltaY += static_cast<float>(event.motion.yrel);
            break;

        case SDL_MOUSEBUTTONDOWN:
        {
            const int buttonIndex = ToMouseIndex(event.button.button);
            if (IsValidMouseIndex(buttonIndex) && !g_mouseHeld[buttonIndex])
            {
                g_mouseHeld[buttonIndex] = true;
                g_mouseDown[buttonIndex] = true;
            }

            g_mousePosX = static_cast<float>(event.button.x);
            g_mousePosY = static_cast<float>(event.button.y);
            break;
        }

        case SDL_MOUSEBUTTONUP:
        {
            const int buttonIndex = ToMouseIndex(event.button.button);
            if (IsValidMouseIndex(buttonIndex))
            {
                g_mouseHeld[buttonIndex] = false;
                g_mouseUp[buttonIndex] = true;
            }

            g_mousePosX = static_cast<float>(event.button.x);
            g_mousePosY = static_cast<float>(event.button.y);
            break;
        }

        case SDL_MOUSEWHEEL:
            g_mouseWheel += static_cast<float>(event.wheel.y);
            break;

        case SDL_KEYDOWN:
            if (!event.key.repeat)
            {
                const int scancode = static_cast<int>(event.key.keysym.scancode);
                if (IsValidScancode(scancode) && !g_keyHeld[scancode])
                {
                    g_keyHeld[scancode] = true;
                    g_keyDown[scancode] = true;
                }
            }
            break;

        case SDL_KEYUP:
        {
            const int scancode = static_cast<int>(event.key.keysym.scancode);
            if (IsValidScancode(scancode))
            {
                g_keyHeld[scancode] = false;
                g_keyUp[scancode] = true;
            }
            break;
        }

        default:
            break;
        }
    }

    void EndFrame()
    {
    }

    bool GetMouseButton(int button)
    {
        return IsValidMouseIndex(button) ? g_mouseHeld[button] : false;
    }

    bool GetMouseButtonDown(int button)
    {
        return IsValidMouseIndex(button) ? g_mouseDown[button] : false;
    }

    bool GetMouseButtonUp(int button)
    {
        return IsValidMouseIndex(button) ? g_mouseUp[button] : false;
    }

    float GetMouseDeltaX()
    {
        return g_mouseDeltaX;
    }

    float GetMouseDeltaY()
    {
        return g_mouseDeltaY;
    }

    float GetMouseWheel()
    {
        return g_mouseWheel;
    }

    float GetMousePosX()
    {
        return g_mousePosX;
    }

    float GetMousePosY()
    {
        return g_mousePosY;
    }

    bool GetKey(int scancode)
    {
        return IsValidScancode(scancode) ? g_keyHeld[scancode] : false;
    }

    bool GetKeyDown(int scancode)
    {
        return IsValidScancode(scancode) ? g_keyDown[scancode] : false;
    }

    bool GetKeyUp(int scancode)
    {
        return IsValidScancode(scancode) ? g_keyUp[scancode] : false;
    }
}
