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
    bool centered = true;
    float offsetX = 0.0f;
    float offsetY = 0.0f;
    bool flipH = false;
    bool flipV = false;
    std::uint32_t hframes = 1;
    std::uint32_t vframes = 1;
    std::uint32_t frame = 0;
    bool regionEnabled = false;
    float regionX = 0.0f;
    float regionY = 0.0f;
    float regionWidth = 0.0f;
    float regionHeight = 0.0f;
};

struct EntityMetadataComponent
{
    std::uint64_t sceneEntityId = 0;
    std::string name;
    std::string tag = "Untagged";
    std::uint32_t layer = 0;
    bool active = true;
    bool isStatic = false;
};

struct ScriptComponent
{
    std::string classNamespace = "GameScripts";
    std::string className = "SpinnerScript";
    std::string serializedFieldState;
    bool enabled = true;
};
