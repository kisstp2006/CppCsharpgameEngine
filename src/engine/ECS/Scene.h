#pragma once

#include <cstddef>
#include <cstdint>
#include <filesystem>
#include <string>
#include <unordered_map>
#include <utility>

#include "Components.h"

#include <entt/entt.hpp>

class Scene
{
public:
    using Entity = entt::entity;
    using EntityId = std::uint32_t;

    enum class SceneFileFormat : std::uint8_t
    {
        Json = 0,
        Binary = 1,
    };

    Scene() = default;
    ~Scene() = default;

    Entity CreateEntity();
    Entity CreateEntityWithSceneEntityId(std::uint64_t sceneEntityId, const std::string& name = {}, bool active = true);
    void DestroyEntity(Entity entity);
    void Clear();

    bool IsValid(Entity entity) const;
    Entity FromEntityId(EntityId entityId) const;
    EntityId ToEntityId(Entity entity) const;
    Entity FindBySceneEntityId(std::uint64_t sceneEntityId) const;
    Entity GetParent(Entity child) const;
    bool SetParent(Entity child, Entity parent);
    bool IsAncestor(Entity potentialAncestor, Entity entity) const;
    std::size_t GetChildCount(Entity parent) const;
    Entity GetChildAt(Entity parent, std::size_t index) const;
    std::size_t GetRootEntityCount() const;
    Entity GetRootEntityAt(std::size_t index) const;
    Entity DuplicateEntity(Entity source);

    std::size_t EntityCount() const;

    template<typename T, typename... Args>
    T& AddComponent(Entity entity, Args&&... args)
    {
        return m_registry.get_or_emplace<T>(entity, std::forward<Args>(args)...);
    }

    template<typename T>
    bool HasComponent(Entity entity) const
    {
        return m_registry.valid(entity) && m_registry.all_of<T>(entity);
    }

    template<typename T>
    T* TryGetComponent(Entity entity)
    {
        return m_registry.valid(entity) ? m_registry.try_get<T>(entity) : nullptr;
    }

    template<typename T>
    const T* TryGetComponent(Entity entity) const
    {
        return m_registry.valid(entity) ? m_registry.try_get<T>(entity) : nullptr;
    }

    template<typename T>
    T& GetComponent(Entity entity)
    {
        return m_registry.get<T>(entity);
    }

    template<typename T>
    const T& GetComponent(Entity entity) const
    {
        return m_registry.get<T>(entity);
    }

    template<typename T>
    bool RemoveComponent(Entity entity)
    {
        return m_registry.valid(entity) && (m_registry.remove<T>(entity) > 0);
    }

    TransformComponent& AddTransform(Entity entity, const TransformComponent& transform = TransformComponent{});
    CameraComponent& AddCamera(Entity entity, const CameraComponent& camera = CameraComponent{});
    SpriteComponent& AddSprite(Entity entity, Texture* texture = nullptr);
    ScriptComponent& AddScript(Entity entity, const ScriptComponent& script = ScriptComponent{});
    AnimatorComponent& AddAnimator(Entity entity, const AnimatorComponent& animator = AnimatorComponent{});
    UiCanvasComponent& AddUiCanvas(Entity entity, const UiCanvasComponent& canvas = UiCanvasComponent{});
    UiRectTransformComponent& AddUiRectTransform(Entity entity, const UiRectTransformComponent& rectTransform = UiRectTransformComponent{});
    UiImageComponent& AddUiImage(Entity entity, const UiImageComponent& image = UiImageComponent{});
    UiTextComponent& AddUiText(Entity entity, const UiTextComponent& text = UiTextComponent{});
    UiButtonComponent& AddUiButton(Entity entity, const UiButtonComponent& button = UiButtonComponent{});
    UiInputFieldComponent& AddUiInputField(Entity entity, const UiInputFieldComponent& inputField = UiInputFieldComponent{});
    EntityMetadataComponent& AddMetadata(Entity entity, const EntityMetadataComponent& metadata = EntityMetadataComponent{});

    bool HasTransform(Entity entity) const;
    bool HasCamera(Entity entity) const;
    bool HasSprite(Entity entity) const;
    bool HasScript(Entity entity) const;
    bool HasAnimator(Entity entity) const;
    bool HasUiCanvas(Entity entity) const;
    bool HasUiRectTransform(Entity entity) const;
    bool HasUiImage(Entity entity) const;
    bool HasUiText(Entity entity) const;
    bool HasUiButton(Entity entity) const;
    bool HasUiInputField(Entity entity) const;
    bool HasMetadata(Entity entity) const;

    TransformComponent* TryGetTransform(Entity entity);
    const TransformComponent* TryGetTransform(Entity entity) const;
    CameraComponent* TryGetCamera(Entity entity);
    const CameraComponent* TryGetCamera(Entity entity) const;
    SpriteComponent* TryGetSprite(Entity entity);
    const SpriteComponent* TryGetSprite(Entity entity) const;
    ScriptComponent* TryGetScript(Entity entity);
    const ScriptComponent* TryGetScript(Entity entity) const;
    AnimatorComponent* TryGetAnimator(Entity entity);
    const AnimatorComponent* TryGetAnimator(Entity entity) const;
    UiCanvasComponent* TryGetUiCanvas(Entity entity);
    const UiCanvasComponent* TryGetUiCanvas(Entity entity) const;
    UiRectTransformComponent* TryGetUiRectTransform(Entity entity);
    const UiRectTransformComponent* TryGetUiRectTransform(Entity entity) const;
    UiImageComponent* TryGetUiImage(Entity entity);
    const UiImageComponent* TryGetUiImage(Entity entity) const;
    UiTextComponent* TryGetUiText(Entity entity);
    const UiTextComponent* TryGetUiText(Entity entity) const;
    UiButtonComponent* TryGetUiButton(Entity entity);
    const UiButtonComponent* TryGetUiButton(Entity entity) const;
    UiInputFieldComponent* TryGetUiInputField(Entity entity);
    const UiInputFieldComponent* TryGetUiInputField(Entity entity) const;
    EntityMetadataComponent* TryGetMetadata(Entity entity);
    const EntityMetadataComponent* TryGetMetadata(Entity entity) const;

    bool RemoveTransform(Entity entity);
    bool RemoveCamera(Entity entity);
    bool RemoveSprite(Entity entity);
    bool RemoveScript(Entity entity);
    bool RemoveAnimator(Entity entity);
    bool RemoveUiCanvas(Entity entity);
    bool RemoveUiRectTransform(Entity entity);
    bool RemoveUiImage(Entity entity);
    bool RemoveUiText(Entity entity);
    bool RemoveUiButton(Entity entity);
    bool RemoveUiInputField(Entity entity);
    bool RemoveMetadata(Entity entity);

    bool SaveToFile(const std::filesystem::path& path, SceneFileFormat format) const;
    bool LoadFromFile(const std::filesystem::path& path, SceneFileFormat format);
    const std::string& GetLastIoError() const;

    Entity FindFirstCamera() const;

    entt::registry& Registry();
    const entt::registry& Registry() const;

private:
    entt::registry m_registry;
    std::unordered_map<std::uint64_t, Entity> m_sceneEntityLookup;

    std::size_t m_entityCount = 0;
    std::uint64_t m_nextSceneEntityId = 1;
    mutable std::string m_lastIoError;
};
