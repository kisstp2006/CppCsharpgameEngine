#pragma once

#include <string>

class Texture;

struct TransformComponent
{
    float x = 0.0f;
    float y = 0.0f;
    float width = 1.0f;
    float height = 1.0f;
};

struct CameraComponent
{
    float x = 0.0f;
    float y = 0.0f;
    float zoom = 1.0f;
};

struct SpriteComponent
{
    Texture* texture = nullptr;
};

struct ScriptComponent
{
    std::string classNamespace = "GameScripts";
    std::string className = "SpinnerScript";
    bool enabled = true;
};
