#include "Scene.h"

Scene::Entity Scene::CreateEntity()
{
    const auto entity = m_registry.create();
    AddTransform(entity, TransformComponent{});
    ++m_entityCount;
    return entity;
}

void Scene::DestroyEntity(Entity entity)
{
    if (m_registry.valid(entity))
    {
        m_registry.destroy(entity);
        if (m_entityCount > 0)
            --m_entityCount;
    }
}

bool Scene::IsValid(Entity entity) const
{
    return m_registry.valid(entity);
}

Scene::Entity Scene::FromEntityId(EntityId entityId) const
{
    return static_cast<Entity>(entityId);
}

Scene::EntityId Scene::ToEntityId(Entity entity) const
{
    return static_cast<EntityId>(entt::to_integral(entity));
}

std::size_t Scene::EntityCount() const
{
    return m_entityCount;
}

TransformComponent& Scene::AddTransform(Entity entity, const TransformComponent& transform)
{
    auto& value = AddComponent<TransformComponent>(entity);
    value = transform;
    return value;
}

CameraComponent& Scene::AddCamera(Entity entity, const CameraComponent& camera)
{
    auto& value = AddComponent<CameraComponent>(entity);
    value = camera;
    return value;
}

SpriteComponent& Scene::AddSprite(Entity entity, Texture* texture)
{
    auto& value = AddComponent<SpriteComponent>(entity);
    value.texture = texture;
    return value;
}

ScriptComponent& Scene::AddScript(Entity entity, const ScriptComponent& script)
{
    auto& value = AddComponent<ScriptComponent>(entity);
    value = script;
    return value;
}

bool Scene::HasTransform(Entity entity) const
{
    return HasComponent<TransformComponent>(entity);
}

bool Scene::HasCamera(Entity entity) const
{
    return HasComponent<CameraComponent>(entity);
}

bool Scene::HasSprite(Entity entity) const
{
    return HasComponent<SpriteComponent>(entity);
}

bool Scene::HasScript(Entity entity) const
{
    return HasComponent<ScriptComponent>(entity);
}

TransformComponent* Scene::TryGetTransform(Entity entity)
{
    return TryGetComponent<TransformComponent>(entity);
}

const TransformComponent* Scene::TryGetTransform(Entity entity) const
{
    return TryGetComponent<TransformComponent>(entity);
}

CameraComponent* Scene::TryGetCamera(Entity entity)
{
    return TryGetComponent<CameraComponent>(entity);
}

const CameraComponent* Scene::TryGetCamera(Entity entity) const
{
    return TryGetComponent<CameraComponent>(entity);
}

SpriteComponent* Scene::TryGetSprite(Entity entity)
{
    return TryGetComponent<SpriteComponent>(entity);
}

const SpriteComponent* Scene::TryGetSprite(Entity entity) const
{
    return TryGetComponent<SpriteComponent>(entity);
}

ScriptComponent* Scene::TryGetScript(Entity entity)
{
    return TryGetComponent<ScriptComponent>(entity);
}

const ScriptComponent* Scene::TryGetScript(Entity entity) const
{
    return TryGetComponent<ScriptComponent>(entity);
}

bool Scene::RemoveTransform(Entity entity)
{
    return RemoveComponent<TransformComponent>(entity);
}

bool Scene::RemoveCamera(Entity entity)
{
    return RemoveComponent<CameraComponent>(entity);
}

bool Scene::RemoveSprite(Entity entity)
{
    return RemoveComponent<SpriteComponent>(entity);
}

bool Scene::RemoveScript(Entity entity)
{
    return RemoveComponent<ScriptComponent>(entity);
}

Scene::Entity Scene::FindFirstCamera() const
{
    auto view = m_registry.view<CameraComponent>();
    for (const auto entity : view)
        return entity;

    return entt::null;
}

entt::registry& Scene::Registry()
{
    return m_registry;
}

const entt::registry& Scene::Registry() const
{
    return m_registry;
}
