#pragma once

#include <cstddef>
#include <entt/entt.hpp>

class Scene
{
public:
    Scene() = default;
    ~Scene() = default;

    entt::entity CreateEntity();
    void DestroyEntity(entt::entity entity);

    std::size_t EntityCount() const;

    entt::registry& Registry();
    const entt::registry& Registry() const;

private:
    entt::registry m_registry;
    std::size_t m_entityCount = 0;
};
