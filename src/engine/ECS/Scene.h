#pragma once

#include <cstddef>
#include <cstdint>
#include <utility>

#include "Components.h"

#include <entt/entt.hpp>

class Scene
{
public:
    using Entity = entt::entity;
    using EntityId = std::uint32_t;

    Scene() = default;
    ~Scene() = default;

    Entity CreateEntity();
    void DestroyEntity(Entity entity);

    bool IsValid(Entity entity) const;
    Entity FromEntityId(EntityId entityId) const;
    EntityId ToEntityId(Entity entity) const;

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

    bool HasTransform(Entity entity) const;
    bool HasCamera(Entity entity) const;
    bool HasSprite(Entity entity) const;
    bool HasScript(Entity entity) const;

    TransformComponent* TryGetTransform(Entity entity);
    const TransformComponent* TryGetTransform(Entity entity) const;
    CameraComponent* TryGetCamera(Entity entity);
    const CameraComponent* TryGetCamera(Entity entity) const;
    SpriteComponent* TryGetSprite(Entity entity);
    const SpriteComponent* TryGetSprite(Entity entity) const;
    ScriptComponent* TryGetScript(Entity entity);
    const ScriptComponent* TryGetScript(Entity entity) const;

    bool RemoveTransform(Entity entity);
    bool RemoveCamera(Entity entity);
    bool RemoveSprite(Entity entity);
    bool RemoveScript(Entity entity);

    Entity FindFirstCamera() const;

    entt::registry& Registry();
    const entt::registry& Registry() const;

private:
    entt::registry m_registry;

    std::size_t m_entityCount = 0;
};
