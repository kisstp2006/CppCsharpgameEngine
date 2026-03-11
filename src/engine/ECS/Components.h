#pragma once

#include <cstdint>
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
    std::uint64_t textureAssetHandle = 0;
    std::string textureAssetPath;
};

struct EntityMetadataComponent
{
    std::uint64_t sceneEntityId = 0;
    std::string name;
    bool active = true;
};

struct ScriptComponent
{
    std::string classNamespace = "GameScripts";
    std::string className = "SpinnerScript";
    std::string serializedFieldState;
    bool enabled = true;
};
