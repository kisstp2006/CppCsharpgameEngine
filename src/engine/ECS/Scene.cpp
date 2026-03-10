#include "Scene.h"

entt::entity Scene::CreateEntity()
{
    ++m_entityCount;
    return m_registry.create();
}

void Scene::DestroyEntity(entt::entity entity)
{
    if (m_registry.valid(entity))
    {
        m_registry.destroy(entity);
        if (m_entityCount > 0)
            --m_entityCount;
    }
}

std::size_t Scene::EntityCount() const
{
    return m_entityCount;
}

entt::registry& Scene::Registry()
{
    return m_registry;
}

const entt::registry& Scene::Registry() const
{
    return m_registry;
}
