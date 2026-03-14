#include "AnimatorSystem.h"

#include "Components.h"
#include "Scene.h"
#include "engine/Animation/AnimationClip.h"
#include "engine/Assets/ProjectContext.h"

#include <algorithm>
#include <cmath>
#include <filesystem>
#include <unordered_map>

namespace
{
    struct CachedAnimationClip
    {
        AnimationClip clip;
        std::filesystem::file_time_type lastWriteTime{};
        bool hasLastWriteTime = false;
    };

    static std::unordered_map<std::string, CachedAnimationClip> g_clipCache;

    static float Clamp01(float value)
    {
        if (value < 0.0f)
            return 0.0f;
        if (value > 1.0f)
            return 1.0f;
        return value;
    }

    static float Lerp(float a, float b, float t)
    {
        return a + ((b - a) * Clamp01(t));
    }

    static std::filesystem::path ResolveClipPath(const std::string& clipPath, const ProjectContext* projectContext)
    {
        if (clipPath.empty())
            return {};

        std::filesystem::path path(clipPath);
        if (path.is_absolute())
            return path.lexically_normal();

        if (projectContext && projectContext->IsOpen())
            return (projectContext->ProjectRoot() / path).lexically_normal();

        return (std::filesystem::current_path() / path).lexically_normal();
    }

    static const AnimationClip* GetOrLoadClip(const std::filesystem::path& path)
    {
        if (path.empty())
            return nullptr;

        std::error_code existsError;
        if (!std::filesystem::exists(path, existsError) || existsError)
            return nullptr;

        std::error_code timeError;
        const auto writeTime = std::filesystem::last_write_time(path, timeError);
        const bool hasTime = !timeError;

        const std::string key = path.string();
        auto found = g_clipCache.find(key);
        if (found != g_clipCache.end())
        {
            const bool unchanged = (found->second.hasLastWriteTime == hasTime)
                && (!hasTime || found->second.lastWriteTime == writeTime);
            if (unchanged)
                return &found->second.clip;
        }

        AnimationClip loadedClip;
        std::string loadError;
        if (!LoadAnimationClipFromFile(path, loadedClip, loadError))
            return nullptr;

        CachedAnimationClip cached;
        cached.clip = std::move(loadedClip);
        cached.lastWriteTime = writeTime;
        cached.hasLastWriteTime = hasTime;

        g_clipCache[key] = std::move(cached);
        return &g_clipCache[key].clip;
    }

    static float SampleFloatTrack(const AnimationTrack& track, float time)
    {
        if (track.floatKeys.empty())
            return 0.0f;

        if (time <= track.floatKeys.front().time)
            return track.floatKeys.front().value;

        if (time >= track.floatKeys.back().time)
            return track.floatKeys.back().value;

        for (std::size_t i = 1; i < track.floatKeys.size(); ++i)
        {
            const AnimationKeyframeFloat& lhs = track.floatKeys[i - 1];
            const AnimationKeyframeFloat& rhs = track.floatKeys[i];
            if (time < lhs.time || time > rhs.time)
                continue;

            const float segmentLength = rhs.time - lhs.time;
            if (segmentLength <= 0.000001f)
                return rhs.value;

            if (lhs.interpolation == AnimationInterpolation::Step)
                return lhs.value;

            const float t = (time - lhs.time) / segmentLength;
            return Lerp(lhs.value, rhs.value, t);
        }

        return track.floatKeys.back().value;
    }

    static void SampleVec2Track(const AnimationTrack& track, float time, float& outX, float& outY)
    {
        outX = 0.0f;
        outY = 0.0f;

        if (track.vec2Keys.empty())
            return;

        if (time <= track.vec2Keys.front().time)
        {
            outX = track.vec2Keys.front().x;
            outY = track.vec2Keys.front().y;
            return;
        }

        if (time >= track.vec2Keys.back().time)
        {
            outX = track.vec2Keys.back().x;
            outY = track.vec2Keys.back().y;
            return;
        }

        for (std::size_t i = 1; i < track.vec2Keys.size(); ++i)
        {
            const AnimationKeyframeVec2& lhs = track.vec2Keys[i - 1];
            const AnimationKeyframeVec2& rhs = track.vec2Keys[i];
            if (time < lhs.time || time > rhs.time)
                continue;

            const float segmentLength = rhs.time - lhs.time;
            if (segmentLength <= 0.000001f)
            {
                outX = rhs.x;
                outY = rhs.y;
                return;
            }

            if (lhs.interpolation == AnimationInterpolation::Step)
            {
                outX = lhs.x;
                outY = lhs.y;
                return;
            }

            const float t = (time - lhs.time) / segmentLength;
            outX = Lerp(lhs.x, rhs.x, t);
            outY = Lerp(lhs.y, rhs.y, t);
            return;
        }

        outX = track.vec2Keys.back().x;
        outY = track.vec2Keys.back().y;
    }

    static float NormalizeTime(float time, float duration, bool loop)
    {
        if (!std::isfinite(time) || duration <= 0.000001f)
            return 0.0f;

        if (loop)
        {
            float wrapped = std::fmod(time, duration);
            if (wrapped < 0.0f)
                wrapped += duration;
            return wrapped;
        }

        if (time < 0.0f)
            return 0.0f;
        if (time > duration)
            return duration;
        return time;
    }

    static float ClampSpeed(float speed)
    {
        if (!std::isfinite(speed))
            return 1.0f;

        if (speed < -16.0f)
            return -16.0f;
        if (speed > 16.0f)
            return 16.0f;

        return speed;
    }
}

void AnimatorSystem::Update(Scene& scene, float deltaTime, const ProjectContext* projectContext)
{
    auto view = scene.Registry().view<AnimatorComponent>();
    for (const auto entity : view)
    {
        AnimatorComponent& animator = view.get<AnimatorComponent>(entity);
        if (!animator.enabled)
            continue;

        if (animator.clipAssetPath.empty())
            continue;

        const std::filesystem::path clipPath = ResolveClipPath(animator.clipAssetPath, projectContext);
        const AnimationClip* clip = GetOrLoadClip(clipPath);
        if (!clip || clip->tracks.empty())
            continue;

        const float duration = clip->duration > 0.0001f ? clip->duration : 0.0001f;
        const bool shouldLoop = animator.loop && clip->loop;

        animator.speed = ClampSpeed(animator.speed);
        if (animator.playing)
            animator.time += deltaTime * animator.speed;

        animator.time = NormalizeTime(animator.time, duration, shouldLoop);

        if (!animator.playing && !animator.applyPoseWhenStopped)
            continue;

        TransformComponent* transform = scene.TryGetTransform(entity);
        if (!transform)
            transform = &scene.AddTransform(entity);

        for (const AnimationTrack& track : clip->tracks)
        {
            switch (track.property)
            {
            case AnimationTrackProperty::TransformPosition:
            {
                float x = 0.0f;
                float y = 0.0f;
                SampleVec2Track(track, animator.time, x, y);
                transform->x = x;
                transform->y = y;
                break;
            }
            case AnimationTrackProperty::TransformRotation:
            {
                transform->rotation = SampleFloatTrack(track, animator.time);
                break;
            }
            case AnimationTrackProperty::TransformScale:
            {
                float width = 1.0f;
                float height = 1.0f;
                SampleVec2Track(track, animator.time, width, height);
                transform->width = width;
                transform->height = height;
                break;
            }
            default:
                break;
            }
        }
    }
}

void AnimatorSystem::InvalidateClipCache()
{
    g_clipCache.clear();
}
