#pragma once

#include <cstdint>
#include <string>

class Texture;

struct TransformComponent
{
    float x = 0.0f;
    float y = 0.0f;
    float rotation = 0.0f;
    float width = 1.0f;
    float height = 1.0f;
};

struct CameraComponent
{
    float x = 0.0f;
    float y = 0.0f;
    float zoom = 1.0f;
    float orthographicSize = 0.0f;
    bool enabled = true;
    bool primary = true;
    bool clearColor = true;
    std::uint32_t backgroundColor = 0x14141AFF;
    std::uint32_t cullingMask = 0xFFFFFFFFu;
    float viewportX = 0.0f;
    float viewportY = 0.0f;
    float viewportWidth = 1.0f;
    float viewportHeight = 1.0f;
};

struct SpriteComponent
{
    bool enabled = true;
    Texture* texture = nullptr;
    std::uint64_t textureAssetHandle = 0;
    std::string textureAssetPath;
    std::uint32_t fallbackColor = 0xFFFFFFFF;
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
    std::uint64_t parentSceneEntityId = 0;
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

struct AnimatorComponent
{
    bool enabled = true;
    std::string clipAssetPath;
    float time = 0.0f;
    bool playing = false;
    bool loop = true;
    float speed = 1.0f;
    bool applyPoseWhenStopped = true;
};
