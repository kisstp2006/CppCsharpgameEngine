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

struct UiCanvasComponent
{
    static constexpr std::int32_t RenderModeScreenSpaceOverlay = 0;
    static constexpr std::int32_t RenderModeScreenSpaceCamera = 1;
    static constexpr std::int32_t RenderModeWorldSpace = 2;

    static constexpr std::uint32_t AdditionalShaderChannelTexCoord1 = 1u << 0;
    static constexpr std::uint32_t AdditionalShaderChannelTexCoord2 = 1u << 1;
    static constexpr std::uint32_t AdditionalShaderChannelTexCoord3 = 1u << 2;
    static constexpr std::uint32_t AdditionalShaderChannelNormal = 1u << 3;
    static constexpr std::uint32_t AdditionalShaderChannelTangent = 1u << 4;

    bool enabled = true;
    std::int32_t renderMode = RenderModeScreenSpaceOverlay;
    std::int32_t sortingOrder = 0;
    bool pixelPerfect = false;
    std::int32_t targetDisplay = 0;
    std::uint32_t additionalShaderChannels = 0;
    bool vertexColorAlwaysGammaSpace = false;
};

struct UiRectTransformComponent
{
    float anchorMinX = 0.5f;
    float anchorMinY = 0.5f;
    float anchorMaxX = 0.5f;
    float anchorMaxY = 0.5f;

    float pivotX = 0.5f;
    float pivotY = 0.5f;

    float anchoredX = 0.0f;
    float anchoredY = 0.0f;
    float sizeDeltaX = 100.0f;
    float sizeDeltaY = 100.0f;
};

struct UiImageComponent
{
    bool enabled = true;
    std::uint64_t textureAssetHandle = 0;
    std::string textureAssetPath;
    std::uint32_t color = 0xFFFFFFFFu;
    bool preserveAspect = true;
    float cornerRadius = 0.0f;
};

struct UiTextComponent
{
    bool enabled = true;
    std::string text;
    float fontSize = 16.0f;
    std::uint32_t color = 0xFFFFFFFFu;
    std::int32_t horizontalAlign = 0;
    bool wrap = false;
};

struct UiButtonComponent
{
    bool enabled = true;
    bool interactable = true;
    std::uint32_t normalColor = 0x4A566EFF;
    std::uint32_t highlightedColor = 0x5E6F8CFF;
    std::uint32_t pressedColor = 0x3A465CFF;
    std::uint32_t disabledColor = 0x4A4A4A88;
};

struct UiInputFieldComponent
{
    bool enabled = true;
    bool interactable = true;
    std::string text;
    std::string placeholder;
    std::uint32_t textColor = 0xF2F2F2FF;
    std::uint32_t placeholderColor = 0xA0A0A0FF;
    std::uint32_t maxLength = 0;
};
