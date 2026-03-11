#include "Scene.h"

#include <algorithm>
#include <array>
#include <fstream>
#include <vector>

#include <rapidjson/document.h>
#include <rapidjson/error/en.h>
#include <rapidjson/istreamwrapper.h>
#include <rapidjson/ostreamwrapper.h>
#include <rapidjson/prettywriter.h>

namespace
{
    constexpr std::uint32_t kSceneFileVersion = 1;

    enum ComponentFlags : std::uint8_t
    {
        Component_Transform = 1 << 0,
        Component_Camera = 1 << 1,
        Component_Sprite = 1 << 2,
        Component_Script = 1 << 3,
    };

    struct PersistedEntity
    {
        std::uint64_t sceneEntityId = 0;
        std::string name;
        bool active = true;

        bool hasTransform = false;
        TransformComponent transform;

        bool hasCamera = false;
        CameraComponent camera;

        bool hasSprite = false;
        std::uint64_t spriteTextureAssetHandle = 0;
        std::string spriteTexturePath;

        bool hasScript = false;
        ScriptComponent script;
    };

    static bool ReadRequiredUInt64(const rapidjson::Value& object,
                                   const char* key,
                                   std::uint64_t& outValue,
                                   std::string& outError)
    {
        if (!object.HasMember(key))
        {
            outError = std::string("Missing required field: ") + key;
            return false;
        }

        const rapidjson::Value& value = object[key];
        if (!value.IsUint64())
        {
            outError = std::string("Field '") + key + "' must be uint64.";
            return false;
        }

        outValue = value.GetUint64();
        return true;
    }

    static bool ReadOptionalUInt64(const rapidjson::Value& object,
                                   const char* key,
                                   std::uint64_t& outValue,
                                   std::string& outError)
    {
        if (!object.HasMember(key))
            return true;

        const rapidjson::Value& value = object[key];
        if (!value.IsUint64())
        {
            outError = std::string("Field '") + key + "' must be uint64.";
            return false;
        }

        outValue = value.GetUint64();
        return true;
    }

    static bool ReadOptionalBool(const rapidjson::Value& object,
                                 const char* key,
                                 bool& outValue,
                                 std::string& outError)
    {
        if (!object.HasMember(key))
            return true;

        const rapidjson::Value& value = object[key];
        if (!value.IsBool())
        {
            outError = std::string("Field '") + key + "' must be bool.";
            return false;
        }

        outValue = value.GetBool();
        return true;
    }

    static bool ReadOptionalFloat(const rapidjson::Value& object,
                                  const char* key,
                                  float& outValue,
                                  std::string& outError)
    {
        if (!object.HasMember(key))
            return true;

        const rapidjson::Value& value = object[key];
        if (!value.IsNumber())
        {
            outError = std::string("Field '") + key + "' must be number.";
            return false;
        }

        outValue = static_cast<float>(value.GetDouble());
        return true;
    }

    static bool ReadOptionalString(const rapidjson::Value& object,
                                   const char* key,
                                   std::string& outValue,
                                   std::string& outError)
    {
        if (!object.HasMember(key))
            return true;

        const rapidjson::Value& value = object[key];
        if (!value.IsString())
        {
            outError = std::string("Field '") + key + "' must be string.";
            return false;
        }

        outValue.assign(value.GetString(), value.GetStringLength());
        return true;
    }

    template<typename T>
    static void WriteBinary(std::ofstream& output, const T& value)
    {
        output.write(reinterpret_cast<const char*>(&value), sizeof(T));
    }

    template<typename T>
    static bool ReadBinary(std::ifstream& input, T& value)
    {
        input.read(reinterpret_cast<char*>(&value), sizeof(T));
        return input.good();
    }

    static void WriteStringBinary(std::ofstream& output, const std::string& value)
    {
        const std::uint32_t length = static_cast<std::uint32_t>(value.size());
        WriteBinary(output, length);
        if (length > 0)
            output.write(value.data(), static_cast<std::streamsize>(length));
    }

    static bool ReadStringBinary(std::ifstream& input, std::string& outValue)
    {
        std::uint32_t length = 0;
        if (!ReadBinary(input, length))
            return false;

        outValue.clear();
        if (length == 0)
            return true;

        outValue.resize(length);
        input.read(&outValue[0], static_cast<std::streamsize>(length));
        return input.good();
    }

    static std::vector<PersistedEntity> CollectPersistedEntities(const Scene& scene)
    {
        std::vector<PersistedEntity> entities;
        entities.reserve(scene.EntityCount());

        auto allEntities = scene.Registry().view<entt::entity>();
        for (const auto entity : allEntities)
        {
            PersistedEntity persisted;

            const EntityMetadataComponent* metadata = scene.TryGetMetadata(entity);
            if (metadata && metadata->sceneEntityId != 0)
            {
                persisted.sceneEntityId = metadata->sceneEntityId;
                persisted.name = metadata->name;
                persisted.active = metadata->active;
            }
            else
            {
                persisted.sceneEntityId = static_cast<std::uint64_t>(scene.ToEntityId(entity));
                persisted.name = "Entity " + std::to_string(persisted.sceneEntityId);
                persisted.active = true;
            }

            if (const TransformComponent* transform = scene.TryGetTransform(entity))
            {
                persisted.hasTransform = true;
                persisted.transform = *transform;
            }

            if (const CameraComponent* camera = scene.TryGetCamera(entity))
            {
                persisted.hasCamera = true;
                persisted.camera = *camera;
            }

            if (const SpriteComponent* sprite = scene.TryGetSprite(entity))
            {
                persisted.hasSprite = true;
                persisted.spriteTextureAssetHandle = sprite->textureAssetHandle;
                persisted.spriteTexturePath = sprite->textureAssetPath;
            }

            if (const ScriptComponent* script = scene.TryGetScript(entity))
            {
                persisted.hasScript = true;
                persisted.script = *script;
            }

            entities.push_back(std::move(persisted));
        }

        std::sort(entities.begin(), entities.end(), [](const PersistedEntity& lhs, const PersistedEntity& rhs)
        {
            return lhs.sceneEntityId < rhs.sceneEntityId;
        });

        return entities;
    }
}

Scene::Entity Scene::CreateEntity()
{
    const std::uint64_t sceneEntityId = m_nextSceneEntityId++;
    return CreateEntityWithSceneEntityId(sceneEntityId);
}

Scene::Entity Scene::CreateEntityWithSceneEntityId(std::uint64_t sceneEntityId, const std::string& name, bool active)
{
    if (sceneEntityId == 0 || m_sceneEntityLookup.find(sceneEntityId) != m_sceneEntityLookup.end())
        return entt::null;

    const auto entity = m_registry.create();

    EntityMetadataComponent metadata;
    metadata.sceneEntityId = sceneEntityId;
    metadata.name = name.empty() ? ("Entity " + std::to_string(sceneEntityId)) : name;
    metadata.active = active;

    AddTransform(entity, TransformComponent{});
    AddMetadata(entity, metadata);

    if (sceneEntityId >= m_nextSceneEntityId)
        m_nextSceneEntityId = sceneEntityId + 1;

    ++m_entityCount;
    return entity;
}

void Scene::DestroyEntity(Entity entity)
{
    if (m_registry.valid(entity))
    {
        const EntityMetadataComponent* metadata = m_registry.try_get<EntityMetadataComponent>(entity);
        if (metadata)
            m_sceneEntityLookup.erase(metadata->sceneEntityId);

        m_registry.destroy(entity);
        if (m_entityCount > 0)
            --m_entityCount;
    }
}

void Scene::Clear()
{
    m_registry.clear();
    m_sceneEntityLookup.clear();
    m_entityCount = 0;
    m_nextSceneEntityId = 1;
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

Scene::Entity Scene::FindBySceneEntityId(std::uint64_t sceneEntityId) const
{
    const auto it = m_sceneEntityLookup.find(sceneEntityId);
    if (it == m_sceneEntityLookup.end())
        return entt::null;

    return m_registry.valid(it->second) ? it->second : entt::null;
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

EntityMetadataComponent& Scene::AddMetadata(Entity entity, const EntityMetadataComponent& metadata)
{
    auto& value = AddComponent<EntityMetadataComponent>(entity);

    if (value.sceneEntityId != 0)
        m_sceneEntityLookup.erase(value.sceneEntityId);

    value = metadata;

    if (value.sceneEntityId == 0)
        value.sceneEntityId = m_nextSceneEntityId++;

    if (value.name.empty())
        value.name = "Entity " + std::to_string(value.sceneEntityId);

    m_sceneEntityLookup[value.sceneEntityId] = entity;

    if (value.sceneEntityId >= m_nextSceneEntityId)
        m_nextSceneEntityId = value.sceneEntityId + 1;

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

bool Scene::HasMetadata(Entity entity) const
{
    return HasComponent<EntityMetadataComponent>(entity);
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

EntityMetadataComponent* Scene::TryGetMetadata(Entity entity)
{
    return TryGetComponent<EntityMetadataComponent>(entity);
}

const EntityMetadataComponent* Scene::TryGetMetadata(Entity entity) const
{
    return TryGetComponent<EntityMetadataComponent>(entity);
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

bool Scene::RemoveMetadata(Entity entity)
{
    EntityMetadataComponent* metadata = TryGetMetadata(entity);
    if (!metadata)
        return false;

    m_sceneEntityLookup.erase(metadata->sceneEntityId);
    return RemoveComponent<EntityMetadataComponent>(entity);
}

bool Scene::SaveToFile(const std::filesystem::path& path, SceneFileFormat format) const
{
    m_lastIoError.clear();
    const std::vector<PersistedEntity> entities = CollectPersistedEntities(*this);

    std::error_code createError;
    const std::filesystem::path parentPath = path.parent_path();
    if (!parentPath.empty())
        std::filesystem::create_directories(parentPath, createError);

    if (createError)
    {
        m_lastIoError = "Failed to create scene directory: " + parentPath.string();
        return false;
    }

    if (format == SceneFileFormat::Binary)
    {
        std::ofstream output(path, std::ios::binary | std::ios::trunc);
        if (!output.is_open())
        {
            m_lastIoError = "Failed to open binary scene file for write: " + path.string();
            return false;
        }

        const std::array<char, 4> magic = { 'S', 'C', 'N', '1' };
        output.write(magic.data(), static_cast<std::streamsize>(magic.size()));
        WriteBinary(output, kSceneFileVersion);

        const std::uint32_t entityCount = static_cast<std::uint32_t>(entities.size());
        WriteBinary(output, entityCount);

        for (const PersistedEntity& entity : entities)
        {
            std::uint8_t componentMask = 0;
            if (entity.hasTransform)
                componentMask |= Component_Transform;
            if (entity.hasCamera)
                componentMask |= Component_Camera;
            if (entity.hasSprite)
                componentMask |= Component_Sprite;
            if (entity.hasScript)
                componentMask |= Component_Script;

            WriteBinary(output, entity.sceneEntityId);
            WriteStringBinary(output, entity.name);

            const std::uint8_t active = entity.active ? 1 : 0;
            WriteBinary(output, active);
            WriteBinary(output, componentMask);

            if (entity.hasTransform)
            {
                WriteBinary(output, entity.transform.x);
                WriteBinary(output, entity.transform.y);
                WriteBinary(output, entity.transform.width);
                WriteBinary(output, entity.transform.height);
            }

            if (entity.hasCamera)
            {
                WriteBinary(output, entity.camera.x);
                WriteBinary(output, entity.camera.y);
                WriteBinary(output, entity.camera.zoom);
            }

            if (entity.hasSprite)
            {
                WriteBinary(output, entity.spriteTextureAssetHandle);
                WriteStringBinary(output, entity.spriteTexturePath);
            }

            if (entity.hasScript)
            {
                WriteStringBinary(output, entity.script.classNamespace);
                WriteStringBinary(output, entity.script.className);
                const std::uint8_t enabled = entity.script.enabled ? 1 : 0;
                WriteBinary(output, enabled);
                WriteStringBinary(output, entity.script.serializedFieldState);
            }
        }

        if (!output.good())
        {
            m_lastIoError = "Failed while writing binary scene file: " + path.string();
            return false;
        }

        return true;
    }

    rapidjson::Document document;
    document.SetObject();
    auto& allocator = document.GetAllocator();

    document.AddMember("sceneVersion", kSceneFileVersion, allocator);

    rapidjson::Value entitiesArray(rapidjson::kArrayType);
    for (const PersistedEntity& entity : entities)
    {
        rapidjson::Value entityObject(rapidjson::kObjectType);
        entityObject.AddMember("id", entity.sceneEntityId, allocator);

        rapidjson::Value nameValue;
        nameValue.SetString(entity.name.c_str(), static_cast<rapidjson::SizeType>(entity.name.size()), allocator);
        entityObject.AddMember("name", nameValue, allocator);
        entityObject.AddMember("active", entity.active, allocator);

        rapidjson::Value componentsObject(rapidjson::kObjectType);

        if (entity.hasTransform)
        {
            rapidjson::Value transformObject(rapidjson::kObjectType);
            transformObject.AddMember("x", entity.transform.x, allocator);
            transformObject.AddMember("y", entity.transform.y, allocator);
            transformObject.AddMember("width", entity.transform.width, allocator);
            transformObject.AddMember("height", entity.transform.height, allocator);
            componentsObject.AddMember("transform", transformObject, allocator);
        }

        if (entity.hasCamera)
        {
            rapidjson::Value cameraObject(rapidjson::kObjectType);
            cameraObject.AddMember("x", entity.camera.x, allocator);
            cameraObject.AddMember("y", entity.camera.y, allocator);
            cameraObject.AddMember("zoom", entity.camera.zoom, allocator);
            componentsObject.AddMember("camera", cameraObject, allocator);
        }

        if (entity.hasSprite)
        {
            rapidjson::Value spriteObject(rapidjson::kObjectType);
            spriteObject.AddMember("textureAssetHandle", entity.spriteTextureAssetHandle, allocator);

            rapidjson::Value texturePathValue;
            texturePathValue.SetString(entity.spriteTexturePath.c_str(), static_cast<rapidjson::SizeType>(entity.spriteTexturePath.size()), allocator);
            spriteObject.AddMember("textureAssetPath", texturePathValue, allocator);
            componentsObject.AddMember("sprite", spriteObject, allocator);
        }

        if (entity.hasScript)
        {
            rapidjson::Value scriptObject(rapidjson::kObjectType);

            rapidjson::Value scriptNamespaceValue;
            scriptNamespaceValue.SetString(entity.script.classNamespace.c_str(), static_cast<rapidjson::SizeType>(entity.script.classNamespace.size()), allocator);
            scriptObject.AddMember("classNamespace", scriptNamespaceValue, allocator);

            rapidjson::Value scriptClassNameValue;
            scriptClassNameValue.SetString(entity.script.className.c_str(), static_cast<rapidjson::SizeType>(entity.script.className.size()), allocator);
            scriptObject.AddMember("className", scriptClassNameValue, allocator);

            scriptObject.AddMember("enabled", entity.script.enabled, allocator);

            rapidjson::Value serializedFieldValue;
            serializedFieldValue.SetString(entity.script.serializedFieldState.c_str(), static_cast<rapidjson::SizeType>(entity.script.serializedFieldState.size()), allocator);
            scriptObject.AddMember("serializedFieldState", serializedFieldValue, allocator);

            componentsObject.AddMember("script", scriptObject, allocator);
        }

        entityObject.AddMember("components", componentsObject, allocator);
        entitiesArray.PushBack(entityObject, allocator);
    }

    document.AddMember("entities", entitiesArray, allocator);

    std::ofstream output(path, std::ios::out | std::ios::trunc);
    if (!output.is_open())
    {
        m_lastIoError = "Failed to open JSON scene file for write: " + path.string();
        return false;
    }

    rapidjson::OStreamWrapper streamWrapper(output);
    rapidjson::PrettyWriter<rapidjson::OStreamWrapper> writer(streamWrapper);
    writer.SetIndent(' ', 2);

    if (!document.Accept(writer) || !output.good())
    {
        m_lastIoError = "Failed while writing JSON scene file: " + path.string();
        return false;
    }

    return true;
}

bool Scene::LoadFromFile(const std::filesystem::path& path, SceneFileFormat format)
{
    m_lastIoError.clear();

    std::vector<PersistedEntity> loadedEntities;
    std::uint32_t fileVersion = 0;

    if (format == SceneFileFormat::Binary)
    {
        std::ifstream input(path, std::ios::binary);
        if (!input.is_open())
        {
            m_lastIoError = "Failed to open binary scene file for read: " + path.string();
            return false;
        }

        std::array<char, 4> magic = {};
        input.read(magic.data(), static_cast<std::streamsize>(magic.size()));
        if (!input.good() || magic != std::array<char, 4>{ 'S', 'C', 'N', '1' })
        {
            m_lastIoError = "Invalid binary scene header: " + path.string();
            return false;
        }

        if (!ReadBinary(input, fileVersion))
        {
            m_lastIoError = "Failed to read binary scene version: " + path.string();
            return false;
        }

        if (fileVersion != kSceneFileVersion)
        {
            m_lastIoError = "Unsupported binary scene version: " + std::to_string(fileVersion);
            return false;
        }

        std::uint32_t entityCount = 0;
        if (!ReadBinary(input, entityCount))
        {
            m_lastIoError = "Failed to read binary entity count: " + path.string();
            return false;
        }

        loadedEntities.reserve(entityCount);

        for (std::uint32_t i = 0; i < entityCount; ++i)
        {
            PersistedEntity entity;
            std::uint8_t active = 0;
            std::uint8_t componentMask = 0;

            if (!ReadBinary(input, entity.sceneEntityId) ||
                !ReadStringBinary(input, entity.name) ||
                !ReadBinary(input, active) ||
                !ReadBinary(input, componentMask))
            {
                m_lastIoError = "Failed to read binary entity header data.";
                return false;
            }

            entity.active = active != 0;
            entity.hasTransform = (componentMask & Component_Transform) != 0;
            entity.hasCamera = (componentMask & Component_Camera) != 0;
            entity.hasSprite = (componentMask & Component_Sprite) != 0;
            entity.hasScript = (componentMask & Component_Script) != 0;

            if (entity.hasTransform)
            {
                if (!ReadBinary(input, entity.transform.x) ||
                    !ReadBinary(input, entity.transform.y) ||
                    !ReadBinary(input, entity.transform.width) ||
                    !ReadBinary(input, entity.transform.height))
                {
                    m_lastIoError = "Failed to read binary transform component.";
                    return false;
                }
            }

            if (entity.hasCamera)
            {
                if (!ReadBinary(input, entity.camera.x) ||
                    !ReadBinary(input, entity.camera.y) ||
                    !ReadBinary(input, entity.camera.zoom))
                {
                    m_lastIoError = "Failed to read binary camera component.";
                    return false;
                }
            }

            if (entity.hasSprite)
            {
                if (!ReadBinary(input, entity.spriteTextureAssetHandle) ||
                    !ReadStringBinary(input, entity.spriteTexturePath))
                {
                    m_lastIoError = "Failed to read binary sprite component.";
                    return false;
                }
            }

            if (entity.hasScript)
            {
                std::uint8_t enabled = 0;
                if (!ReadStringBinary(input, entity.script.classNamespace) ||
                    !ReadStringBinary(input, entity.script.className) ||
                    !ReadBinary(input, enabled) ||
                    !ReadStringBinary(input, entity.script.serializedFieldState))
                {
                    m_lastIoError = "Failed to read binary script component.";
                    return false;
                }

                entity.script.enabled = enabled != 0;
            }

            loadedEntities.push_back(std::move(entity));
        }
    }
    else
    {
        std::ifstream input(path);
        if (!input.is_open())
        {
            m_lastIoError = "Failed to open JSON scene file for read: " + path.string();
            return false;
        }

        rapidjson::IStreamWrapper streamWrapper(input);
        rapidjson::Document document;
        document.ParseStream(streamWrapper);

        if (document.HasParseError())
        {
            m_lastIoError = "Failed to parse JSON scene file: " +
                std::string(rapidjson::GetParseError_En(document.GetParseError())) +
                " at offset " + std::to_string(document.GetErrorOffset());
            return false;
        }

        if (!document.IsObject())
        {
            m_lastIoError = "Invalid JSON scene file root. Expected object.";
            return false;
        }

        std::string parseError;
        std::uint64_t parsedVersion = 0;
        if (!ReadRequiredUInt64(document, "sceneVersion", parsedVersion, parseError))
        {
            m_lastIoError = "Invalid JSON scene file sceneVersion: " + parseError;
            return false;
        }
        fileVersion = static_cast<std::uint32_t>(parsedVersion);

        if (!document.HasMember("entities") || !document["entities"].IsArray())
        {
            m_lastIoError = "Invalid JSON scene file: entities array is missing.";
            return false;
        }

        const rapidjson::Value& entitiesArray = document["entities"];
        loadedEntities.reserve(entitiesArray.Size());

        for (rapidjson::SizeType i = 0; i < entitiesArray.Size(); ++i)
        {
            const rapidjson::Value& entityValue = entitiesArray[i];
            if (!entityValue.IsObject())
            {
                m_lastIoError = "Invalid JSON scene file: entity entry must be object.";
                return false;
            }

            PersistedEntity entity;

            if (!ReadRequiredUInt64(entityValue, "id", entity.sceneEntityId, parseError) || entity.sceneEntityId == 0)
            {
                m_lastIoError = "Invalid JSON entity id: " + parseError;
                return false;
            }

            entity.name = "Entity " + std::to_string(entity.sceneEntityId);
            entity.active = true;

            if (!ReadOptionalString(entityValue, "name", entity.name, parseError) ||
                !ReadOptionalBool(entityValue, "active", entity.active, parseError))
            {
                m_lastIoError = "Invalid JSON entity header: " + parseError;
                return false;
            }

            if (entityValue.HasMember("components"))
            {
                const rapidjson::Value& components = entityValue["components"];
                if (!components.IsObject())
                {
                    m_lastIoError = "Invalid JSON entity components: expected object.";
                    return false;
                }

                if (components.HasMember("transform"))
                {
                    const rapidjson::Value& transform = components["transform"];
                    if (!transform.IsObject())
                    {
                        m_lastIoError = "Invalid JSON transform component: expected object.";
                        return false;
                    }

                    entity.hasTransform = true;
                    if (!ReadOptionalFloat(transform, "x", entity.transform.x, parseError) ||
                        !ReadOptionalFloat(transform, "y", entity.transform.y, parseError) ||
                        !ReadOptionalFloat(transform, "width", entity.transform.width, parseError) ||
                        !ReadOptionalFloat(transform, "height", entity.transform.height, parseError))
                    {
                        m_lastIoError = "Invalid JSON transform component: " + parseError;
                        return false;
                    }
                }

                if (components.HasMember("camera"))
                {
                    const rapidjson::Value& camera = components["camera"];
                    if (!camera.IsObject())
                    {
                        m_lastIoError = "Invalid JSON camera component: expected object.";
                        return false;
                    }

                    entity.hasCamera = true;
                    if (!ReadOptionalFloat(camera, "x", entity.camera.x, parseError) ||
                        !ReadOptionalFloat(camera, "y", entity.camera.y, parseError) ||
                        !ReadOptionalFloat(camera, "zoom", entity.camera.zoom, parseError))
                    {
                        m_lastIoError = "Invalid JSON camera component: " + parseError;
                        return false;
                    }
                }

                if (components.HasMember("sprite"))
                {
                    const rapidjson::Value& sprite = components["sprite"];
                    if (!sprite.IsObject())
                    {
                        m_lastIoError = "Invalid JSON sprite component: expected object.";
                        return false;
                    }

                    entity.hasSprite = true;
                    if (!ReadOptionalUInt64(sprite, "textureAssetHandle", entity.spriteTextureAssetHandle, parseError) ||
                        !ReadOptionalString(sprite, "textureAssetPath", entity.spriteTexturePath, parseError))
                    {
                        m_lastIoError = "Invalid JSON sprite component: " + parseError;
                        return false;
                    }
                }

                if (components.HasMember("script"))
                {
                    const rapidjson::Value& script = components["script"];
                    if (!script.IsObject())
                    {
                        m_lastIoError = "Invalid JSON script component: expected object.";
                        return false;
                    }

                    entity.hasScript = true;
                    if (!ReadOptionalString(script, "classNamespace", entity.script.classNamespace, parseError) ||
                        !ReadOptionalString(script, "className", entity.script.className, parseError) ||
                        !ReadOptionalBool(script, "enabled", entity.script.enabled, parseError) ||
                        !ReadOptionalString(script, "serializedFieldState", entity.script.serializedFieldState, parseError))
                    {
                        m_lastIoError = "Invalid JSON script component: " + parseError;
                        return false;
                    }
                }
            }

            loadedEntities.push_back(std::move(entity));
        }
    }

    if (fileVersion != kSceneFileVersion)
    {
        m_lastIoError = "Unsupported scene version: " + std::to_string(fileVersion);
        return false;
    }

    Clear();

    for (const PersistedEntity& persisted : loadedEntities)
    {
        if (persisted.sceneEntityId == 0)
        {
            m_lastIoError = "Scene contains invalid sceneEntityId=0.";
            return false;
        }

        if (FindBySceneEntityId(persisted.sceneEntityId) != entt::null)
        {
            m_lastIoError = "Duplicate sceneEntityId in scene file: " + std::to_string(persisted.sceneEntityId);
            return false;
        }

        const Entity entity = CreateEntityWithSceneEntityId(persisted.sceneEntityId, persisted.name, persisted.active);
        if (entity == entt::null)
        {
            m_lastIoError = "Failed to create entity while loading scene.";
            return false;
        }

        if (persisted.hasTransform)
        {
            AddTransform(entity, persisted.transform);
        }
        else
        {
            RemoveTransform(entity);
        }

        if (persisted.hasCamera)
            AddCamera(entity, persisted.camera);
        else
            RemoveCamera(entity);

        if (persisted.hasSprite)
        {
            SpriteComponent& sprite = AddSprite(entity);
            sprite.texture = nullptr;
            sprite.textureAssetHandle = persisted.spriteTextureAssetHandle;
            sprite.textureAssetPath = persisted.spriteTexturePath;
        }
        else
        {
            RemoveSprite(entity);
        }

        if (persisted.hasScript)
            AddScript(entity, persisted.script);
        else
            RemoveScript(entity);
    }

    return true;
}

const std::string& Scene::GetLastIoError() const
{
    return m_lastIoError;
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
