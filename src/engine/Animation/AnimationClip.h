#pragma once

#include <cstdint>
#include <filesystem>
#include <string>
#include <vector>

enum class AnimationInterpolation : std::uint8_t
{
    Linear = 0,
    Step = 1,
};

enum class AnimationTrackProperty : std::uint8_t
{
    TransformPosition = 0,
    TransformRotation = 1,
    TransformScale = 2,
};

struct AnimationKeyframeVec2
{
    float time = 0.0f;
    float x = 0.0f;
    float y = 0.0f;
    AnimationInterpolation interpolation = AnimationInterpolation::Linear;
};

struct AnimationKeyframeFloat
{
    float time = 0.0f;
    float value = 0.0f;
    AnimationInterpolation interpolation = AnimationInterpolation::Linear;
};

struct AnimationTrack
{
    AnimationTrackProperty property = AnimationTrackProperty::TransformPosition;
    AnimationInterpolation defaultInterpolation = AnimationInterpolation::Linear;
    std::vector<AnimationKeyframeVec2> vec2Keys;
    std::vector<AnimationKeyframeFloat> floatKeys;
};

struct AnimationClip
{
    std::uint32_t version = 1;
    float duration = 1.0f;
    bool loop = true;
    std::vector<AnimationTrack> tracks;
};

const char* AnimationTrackPropertyToString(AnimationTrackProperty property);
bool AnimationTrackPropertyFromString(const std::string& value, AnimationTrackProperty& outProperty);

const char* AnimationInterpolationToString(AnimationInterpolation interpolation);
bool AnimationInterpolationFromString(const std::string& value, AnimationInterpolation& outInterpolation);

bool LoadAnimationClipFromFile(const std::filesystem::path& path, AnimationClip& outClip, std::string& outError);
bool SaveAnimationClipToFile(const std::filesystem::path& path, const AnimationClip& clip, std::string& outError);
