#include "Scene.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <fstream>
#include <vector>

#include <rapidjson/document.h>
#include <rapidjson/error/en.h>
#include <rapidjson/istreamwrapper.h>
#include <rapidjson/ostreamwrapper.h>
#include <rapidjson/prettywriter.h>

namespace
{
    constexpr std::uint32_t kSceneFileVersion = 11;

    enum ComponentFlags : std::uint16_t
    {
        Component_Transform = 1 << 0,
        Component_Camera = 1 << 1,
        Component_Sprite = 1 << 2,
        Component_Script = 1 << 3,
        Component_Animator = 1 << 4,
        Component_UiCanvas = 1 << 5,
        Component_UiRectTransform = 1 << 6,
        Component_UiImage = 1 << 7,
        Component_UiText = 1 << 8,
        Component_UiButton = 1 << 9,
        Component_UiInputField = 1 << 10,
    };

    struct PersistedEntity
    {
        std::uint64_t sceneEntityId = 0;
        std::uint64_t parentSceneEntityId = 0;
        std::string name;
        std::string tag = "Untagged";
        std::uint32_t layer = 0;
        bool active = true;
        bool isStatic = false;

        bool hasTransform = false;
        TransformComponent transform;

        bool hasCamera = false;
        CameraComponent camera;

        bool hasSprite = false;
        std::uint64_t spriteTextureAssetHandle = 0;
        std::string spriteTexturePath;
        std::uint32_t spriteFallbackColor = 0xFFFFFFFF;
        bool spriteEnabled = true;
        bool spriteCentered = true;
        float spriteOffsetX = 0.0f;
        float spriteOffsetY = 0.0f;
        bool spriteFlipH = false;
        bool spriteFlipV = false;
        std::uint32_t spriteHframes = 1;
        std::uint32_t spriteVframes = 1;
        std::uint32_t spriteFrame = 0;
        bool spriteRegionEnabled = false;
        float spriteRegionX = 0.0f;
        float spriteRegionY = 0.0f;
        float spriteRegionWidth = 0.0f;
        float spriteRegionHeight = 0.0f;

        bool hasScript = false;
        ScriptComponent script;

        bool hasAnimator = false;
        bool animatorEnabled = true;
        AnimatorComponent animator;

        bool hasUiCanvas = false;
        UiCanvasComponent uiCanvas;

        bool hasUiRectTransform = false;
        UiRectTransformComponent uiRectTransform;

        bool hasUiImage = false;
        UiImageComponent uiImage;

        bool hasUiText = false;
        UiTextComponent uiText;

        bool hasUiButton = false;
        UiButtonComponent uiButton;

        bool hasUiInputField = false;
        UiInputFieldComponent uiInputField;
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

    static bool ReadOptionalUInt32(const rapidjson::Value& object,
                                   const char* key,
                                   std::uint32_t& outValue,
                                   std::string& outError)
    {
        if (!object.HasMember(key))
            return true;

        const rapidjson::Value& value = object[key];
        if (!value.IsUint())
        {
            outError = std::string("Field '") + key + "' must be uint32.";
            return false;
        }

        outValue = value.GetUint();
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

    static void NormalizeSpriteState(PersistedEntity& entity)
    {
        if (entity.spriteHframes < 1)
            entity.spriteHframes = 1;
        if (entity.spriteVframes < 1)
            entity.spriteVframes = 1;

        const std::uint64_t frameCount = static_cast<std::uint64_t>(entity.spriteHframes) *
            static_cast<std::uint64_t>(entity.spriteVframes);
        if (frameCount == 0)
        {
            entity.spriteHframes = 1;
            entity.spriteVframes = 1;
            entity.spriteFrame = 0;
        }
        else if (entity.spriteFrame >= frameCount)
        {
            entity.spriteFrame = static_cast<std::uint32_t>(frameCount - 1);
        }

        if (entity.spriteRegionWidth < 0.0f)
            entity.spriteRegionWidth = 0.0f;
        if (entity.spriteRegionHeight < 0.0f)
            entity.spriteRegionHeight = 0.0f;
    }

    static float ClampRange(float value, float minValue, float maxValue)
    {
        if (value < minValue)
            return minValue;
        if (value > maxValue)
            return maxValue;
        return value;
    }

    static void NormalizeCameraState(PersistedEntity& entity)
    {
        if (!std::isfinite(entity.camera.x))
            entity.camera.x = 0.0f;
        if (!std::isfinite(entity.camera.y))
            entity.camera.y = 0.0f;
        if (!std::isfinite(entity.camera.zoom))
            entity.camera.zoom = 1.0f;
        if (!std::isfinite(entity.camera.orthographicSize))
            entity.camera.orthographicSize = 0.0f;

        entity.camera.zoom = ClampRange(entity.camera.zoom, 0.01f, 100.0f);
        entity.camera.orthographicSize = ClampRange(entity.camera.orthographicSize, 0.0f, 100000.0f);

        entity.camera.viewportX = ClampRange(entity.camera.viewportX, 0.0f, 1.0f);
        entity.camera.viewportY = ClampRange(entity.camera.viewportY, 0.0f, 1.0f);
        entity.camera.viewportWidth = ClampRange(entity.camera.viewportWidth, 0.01f, 1.0f);
        entity.camera.viewportHeight = ClampRange(entity.camera.viewportHeight, 0.01f, 1.0f);

        if (entity.camera.viewportX + entity.camera.viewportWidth > 1.0f)
            entity.camera.viewportWidth = ClampRange(1.0f - entity.camera.viewportX, 0.01f, 1.0f);

        if (entity.camera.viewportY + entity.camera.viewportHeight > 1.0f)
            entity.camera.viewportHeight = ClampRange(1.0f - entity.camera.viewportY, 0.01f, 1.0f);
    }

    static void NormalizeAnimatorState(PersistedEntity& entity)
    {
        if (!std::isfinite(entity.animator.time))
            entity.animator.time = 0.0f;

        if (!std::isfinite(entity.animator.speed))
            entity.animator.speed = 1.0f;

        if (entity.animator.speed < -16.0f)
            entity.animator.speed = -16.0f;
        else if (entity.animator.speed > 16.0f)
            entity.animator.speed = 16.0f;
    }

    static void NormalizeSpriteComponent(SpriteComponent& sprite)
    {
        if (sprite.hframes < 1)
            sprite.hframes = 1;
        if (sprite.vframes < 1)
            sprite.vframes = 1;

        const std::uint64_t frameCount = static_cast<std::uint64_t>(sprite.hframes) *
            static_cast<std::uint64_t>(sprite.vframes);
        if (frameCount == 0)
        {
            sprite.hframes = 1;
            sprite.vframes = 1;
            sprite.frame = 0;
        }
        else if (sprite.frame >= frameCount)
        {
            sprite.frame = static_cast<std::uint32_t>(frameCount - 1);
        }

        if (sprite.regionWidth < 0.0f)
            sprite.regionWidth = 0.0f;
        if (sprite.regionHeight < 0.0f)
            sprite.regionHeight = 0.0f;
    }

    static void NormalizeCameraComponent(CameraComponent& camera)
    {
        PersistedEntity entity;
        entity.camera = camera;
        NormalizeCameraState(entity);
        camera = entity.camera;
    }

    static void NormalizeUiRectTransformComponent(UiRectTransformComponent& rectTransform)
    {
        rectTransform.anchorMinX = ClampRange(rectTransform.anchorMinX, 0.0f, 1.0f);
        rectTransform.anchorMinY = ClampRange(rectTransform.anchorMinY, 0.0f, 1.0f);
        rectTransform.anchorMaxX = ClampRange(rectTransform.anchorMaxX, 0.0f, 1.0f);
        rectTransform.anchorMaxY = ClampRange(rectTransform.anchorMaxY, 0.0f, 1.0f);

        if (rectTransform.anchorMaxX < rectTransform.anchorMinX)
            rectTransform.anchorMaxX = rectTransform.anchorMinX;
        if (rectTransform.anchorMaxY < rectTransform.anchorMinY)
            rectTransform.anchorMaxY = rectTransform.anchorMinY;

        rectTransform.pivotX = ClampRange(rectTransform.pivotX, 0.0f, 1.0f);
        rectTransform.pivotY = ClampRange(rectTransform.pivotY, 0.0f, 1.0f);

        if (!std::isfinite(rectTransform.anchoredX))
            rectTransform.anchoredX = 0.0f;
        if (!std::isfinite(rectTransform.anchoredY))
            rectTransform.anchoredY = 0.0f;
        if (!std::isfinite(rectTransform.sizeDeltaX))
            rectTransform.sizeDeltaX = 100.0f;
        if (!std::isfinite(rectTransform.sizeDeltaY))
            rectTransform.sizeDeltaY = 100.0f;
    }

    static void NormalizeUiCanvasComponent(UiCanvasComponent& canvas)
    {
        if (canvas.renderMode < UiCanvasComponent::RenderModeScreenSpaceOverlay ||
            canvas.renderMode > UiCanvasComponent::RenderModeWorldSpace)
        {
            canvas.renderMode = UiCanvasComponent::RenderModeScreenSpaceOverlay;
        }

        if (canvas.targetDisplay < 0)
            canvas.targetDisplay = 0;
        if (canvas.targetDisplay > 7)
            canvas.targetDisplay = 7;

        constexpr std::uint32_t kSupportedAdditionalChannelsMask =
            UiCanvasComponent::AdditionalShaderChannelTexCoord1 |
            UiCanvasComponent::AdditionalShaderChannelTexCoord2 |
            UiCanvasComponent::AdditionalShaderChannelTexCoord3 |
            UiCanvasComponent::AdditionalShaderChannelNormal |
            UiCanvasComponent::AdditionalShaderChannelTangent;
        canvas.additionalShaderChannels &= kSupportedAdditionalChannelsMask;
    }

    static void NormalizeUiImageComponent(UiImageComponent& image)
    {
        if (!std::isfinite(image.cornerRadius) || image.cornerRadius < 0.0f)
            image.cornerRadius = 0.0f;
    }

    static void NormalizeUiTextComponent(UiTextComponent& text)
    {
        if (!std::isfinite(text.fontSize) || text.fontSize < 6.0f)
            text.fontSize = 16.0f;
        if (text.horizontalAlign < 0 || text.horizontalAlign > 2)
            text.horizontalAlign = 0;
    }

    static void NormalizeUiInputFieldComponent(UiInputFieldComponent& inputField)
    {
        if (inputField.maxLength > 4096u)
            inputField.maxLength = 4096u;
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
                persisted.parentSceneEntityId = metadata->parentSceneEntityId;
                persisted.name = metadata->name;
                persisted.tag = metadata->tag;
                persisted.layer = metadata->layer;
                persisted.active = metadata->active;
                persisted.isStatic = metadata->isStatic;
            }
            else
            {
                persisted.sceneEntityId = static_cast<std::uint64_t>(scene.ToEntityId(entity));
                persisted.name = "Entity " + std::to_string(persisted.sceneEntityId);
                persisted.tag = "Untagged";
                persisted.layer = 0;
                persisted.active = true;
                persisted.isStatic = false;
            }

            if (const TransformComponent* transform = scene.TryGetTransform(entity))
            {
                persisted.hasTransform = true;
                persisted.transform = *transform;
                if (!std::isfinite(persisted.transform.rotation))
                    persisted.transform.rotation = 0.0f;
            }

            if (const CameraComponent* camera = scene.TryGetCamera(entity))
            {
                persisted.hasCamera = true;
                persisted.camera = *camera;
                NormalizeCameraState(persisted);
            }

            if (const SpriteComponent* sprite = scene.TryGetSprite(entity))
            {
                persisted.hasSprite = true;
                persisted.spriteTextureAssetHandle = sprite->textureAssetHandle;
                persisted.spriteTexturePath = sprite->textureAssetPath;
                persisted.spriteFallbackColor = sprite->fallbackColor;
                persisted.spriteEnabled = sprite->enabled;
                persisted.spriteCentered = sprite->centered;
                persisted.spriteOffsetX = sprite->offsetX;
                persisted.spriteOffsetY = sprite->offsetY;
                persisted.spriteFlipH = sprite->flipH;
                persisted.spriteFlipV = sprite->flipV;
                persisted.spriteHframes = sprite->hframes;
                persisted.spriteVframes = sprite->vframes;
                persisted.spriteFrame = sprite->frame;
                persisted.spriteRegionEnabled = sprite->regionEnabled;
                persisted.spriteRegionX = sprite->regionX;
                persisted.spriteRegionY = sprite->regionY;
                persisted.spriteRegionWidth = sprite->regionWidth;
                persisted.spriteRegionHeight = sprite->regionHeight;
                NormalizeSpriteState(persisted);
            }

            if (const ScriptComponent* script = scene.TryGetScript(entity))
            {
                persisted.hasScript = true;
                persisted.script = *script;
            }

            if (const AnimatorComponent* animator = scene.TryGetAnimator(entity))
            {
                persisted.hasAnimator = true;
                persisted.animatorEnabled = animator->enabled;
                persisted.animator = *animator;
                NormalizeAnimatorState(persisted);
            }

            if (const UiCanvasComponent* uiCanvas = scene.TryGetUiCanvas(entity))
            {
                persisted.hasUiCanvas = true;
                persisted.uiCanvas = *uiCanvas;
                NormalizeUiCanvasComponent(persisted.uiCanvas);
            }

            if (const UiRectTransformComponent* uiRectTransform = scene.TryGetUiRectTransform(entity))
            {
                persisted.hasUiRectTransform = true;
                persisted.uiRectTransform = *uiRectTransform;
                NormalizeUiRectTransformComponent(persisted.uiRectTransform);
            }

            if (const UiImageComponent* uiImage = scene.TryGetUiImage(entity))
            {
                persisted.hasUiImage = true;
                persisted.uiImage = *uiImage;
                NormalizeUiImageComponent(persisted.uiImage);
            }

            if (const UiTextComponent* uiText = scene.TryGetUiText(entity))
            {
                persisted.hasUiText = true;
                persisted.uiText = *uiText;
                NormalizeUiTextComponent(persisted.uiText);
            }

            if (const UiButtonComponent* uiButton = scene.TryGetUiButton(entity))
            {
                persisted.hasUiButton = true;
                persisted.uiButton = *uiButton;
            }

            if (const UiInputFieldComponent* uiInputField = scene.TryGetUiInputField(entity))
            {
                persisted.hasUiInputField = true;
                persisted.uiInputField = *uiInputField;
                NormalizeUiInputFieldComponent(persisted.uiInputField);
            }

            entities.push_back(std::move(persisted));
        }

        std::sort(entities.begin(), entities.end(), [](const PersistedEntity& lhs, const PersistedEntity& rhs)
        {
            return lhs.sceneEntityId < rhs.sceneEntityId;
        });

        return entities;
    }

    static std::uint64_t ResolveSceneEntityId(const Scene& scene, Scene::Entity entity)
    {
        if (!scene.IsValid(entity))
            return 0;

        const EntityMetadataComponent* metadata = scene.TryGetMetadata(entity);
        if (metadata && metadata->sceneEntityId != 0)
            return metadata->sceneEntityId;

        return static_cast<std::uint64_t>(scene.ToEntityId(entity));
    }

    static void SortEntitiesBySceneId(const Scene& scene, std::vector<Scene::Entity>& entities)
    {
        std::sort(entities.begin(), entities.end(), [&scene](Scene::Entity lhs, Scene::Entity rhs)
        {
            const std::uint64_t lhsId = ResolveSceneEntityId(scene, lhs);
            const std::uint64_t rhsId = ResolveSceneEntityId(scene, rhs);
            if (lhsId == rhsId)
                return entt::to_integral(lhs) < entt::to_integral(rhs);

            return lhsId < rhsId;
        });
    }

    static std::vector<Scene::Entity> CollectChildrenSorted(const Scene& scene, Scene::Entity parent)
    {
        std::vector<Scene::Entity> children;
        if (!scene.IsValid(parent))
            return children;

        const EntityMetadataComponent* parentMetadata = scene.TryGetMetadata(parent);
        if (!parentMetadata || parentMetadata->sceneEntityId == 0)
            return children;

        const std::uint64_t parentSceneEntityId = parentMetadata->sceneEntityId;

        auto view = scene.Registry().view<EntityMetadataComponent>();
        for (const auto entity : view)
        {
            if (!scene.IsValid(entity) || entity == parent)
                continue;

            const EntityMetadataComponent& metadata = view.get<EntityMetadataComponent>(entity);
            if (metadata.parentSceneEntityId == parentSceneEntityId)
                children.push_back(entity);
        }

        SortEntitiesBySceneId(scene, children);
        return children;
    }

    static std::vector<Scene::Entity> CollectRootEntitiesSorted(const Scene& scene)
    {
        std::vector<Scene::Entity> roots;

        auto entities = scene.Registry().view<entt::entity>();
        for (const auto entity : entities)
        {
            if (!scene.IsValid(entity))
                continue;

            const EntityMetadataComponent* metadata = scene.TryGetMetadata(entity);
            if (!metadata)
            {
                roots.push_back(entity);
                continue;
            }

            if (metadata->parentSceneEntityId == 0)
            {
                roots.push_back(entity);
                continue;
            }

            const Scene::Entity parent = scene.FindBySceneEntityId(metadata->parentSceneEntityId);
            if (parent == entt::null || parent == entity)
                roots.push_back(entity);
        }

        SortEntitiesBySceneId(scene, roots);
        return roots;
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
    if (!m_registry.valid(entity))
        return;

    const std::vector<Entity> children = CollectChildrenSorted(*this, entity);
    for (Entity child : children)
        DestroyEntity(child);

    const EntityMetadataComponent* metadata = m_registry.try_get<EntityMetadataComponent>(entity);
    if (metadata)
    {
        const std::uint64_t removedSceneEntityId = metadata->sceneEntityId;
        m_sceneEntityLookup.erase(removedSceneEntityId);

        auto metadataView = m_registry.view<EntityMetadataComponent>();
        for (const auto candidate : metadataView)
        {
            if (candidate == entity)
                continue;

            EntityMetadataComponent& candidateMetadata = metadataView.get<EntityMetadataComponent>(candidate);
            if (candidateMetadata.parentSceneEntityId == removedSceneEntityId)
                candidateMetadata.parentSceneEntityId = 0;
        }
    }

    m_registry.destroy(entity);
    if (m_entityCount > 0)
        --m_entityCount;
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

Scene::Entity Scene::GetParent(Entity child) const
{
    if (!IsValid(child))
        return entt::null;

    const EntityMetadataComponent* metadata = TryGetMetadata(child);
    if (!metadata || metadata->parentSceneEntityId == 0)
        return entt::null;

    const Entity parent = FindBySceneEntityId(metadata->parentSceneEntityId);
    if (parent == child)
        return entt::null;

    return IsValid(parent) ? parent : entt::null;
}

bool Scene::SetParent(Entity child, Entity parent)
{
    if (!IsValid(child))
        return false;

    if (parent != entt::null && !IsValid(parent))
        return false;

    if (child == parent)
        return false;

    if (parent != entt::null && IsAncestor(child, parent))
        return false;

    EntityMetadataComponent* childMetadata = TryGetMetadata(child);
    if (!childMetadata)
        childMetadata = &AddMetadata(child);

    if (parent == entt::null)
    {
        childMetadata->parentSceneEntityId = 0;
        return true;
    }

    EntityMetadataComponent* parentMetadata = TryGetMetadata(parent);
    if (!parentMetadata)
        parentMetadata = &AddMetadata(parent);

    childMetadata->parentSceneEntityId = parentMetadata->sceneEntityId;
    return true;
}

bool Scene::IsAncestor(Entity potentialAncestor, Entity entity) const
{
    if (!IsValid(potentialAncestor) || !IsValid(entity) || potentialAncestor == entity)
        return false;

    Entity current = GetParent(entity);
    std::size_t remaining = m_entityCount + 1;
    while (current != entt::null && remaining-- > 0)
    {
        if (current == potentialAncestor)
            return true;

        current = GetParent(current);
    }

    return false;
}

std::size_t Scene::GetChildCount(Entity parent) const
{
    return CollectChildrenSorted(*this, parent).size();
}

Scene::Entity Scene::GetChildAt(Entity parent, std::size_t index) const
{
    const std::vector<Entity> children = CollectChildrenSorted(*this, parent);
    if (index >= children.size())
        return entt::null;

    return children[index];
}

std::size_t Scene::GetRootEntityCount() const
{
    return CollectRootEntitiesSorted(*this).size();
}

Scene::Entity Scene::GetRootEntityAt(std::size_t index) const
{
    const std::vector<Entity> roots = CollectRootEntitiesSorted(*this);
    if (index >= roots.size())
        return entt::null;

    return roots[index];
}

Scene::Entity Scene::DuplicateEntity(Entity source)
{
    if (!IsValid(source))
        return entt::null;

    const Entity sourceParent = GetParent(source);

    const auto duplicateRecursive = [this](auto&& self, Entity sourceEntity, Entity duplicatedParent, bool isRoot) -> Entity
    {
        if (!IsValid(sourceEntity))
            return entt::null;

        const Entity duplicated = CreateEntity();
        if (duplicated == entt::null)
            return entt::null;

        const EntityMetadataComponent* sourceMetadata = TryGetMetadata(sourceEntity);
        EntityMetadataComponent* duplicatedMetadata = TryGetMetadata(duplicated);
        if (!duplicatedMetadata)
            duplicatedMetadata = &AddMetadata(duplicated);

        if (sourceMetadata)
        {
            duplicatedMetadata->name = sourceMetadata->name;
            if (isRoot)
                duplicatedMetadata->name += " (Copy)";

            if (duplicatedMetadata->name.empty())
                duplicatedMetadata->name = "Entity " + std::to_string(duplicatedMetadata->sceneEntityId);

            duplicatedMetadata->tag = sourceMetadata->tag.empty() ? "Untagged" : sourceMetadata->tag;
            duplicatedMetadata->layer = sourceMetadata->layer > 31 ? 31 : sourceMetadata->layer;
            duplicatedMetadata->active = sourceMetadata->active;
            duplicatedMetadata->isStatic = sourceMetadata->isStatic;
        }

        if (const TransformComponent* sourceTransform = TryGetTransform(sourceEntity))
            AddTransform(duplicated, *sourceTransform);
        else
            RemoveTransform(duplicated);

        if (const CameraComponent* sourceCamera = TryGetCamera(sourceEntity))
            AddCamera(duplicated, *sourceCamera);
        else
            RemoveCamera(duplicated);

        if (const SpriteComponent* sourceSprite = TryGetSprite(sourceEntity))
            AddComponent<SpriteComponent>(duplicated, *sourceSprite);
        else
            RemoveSprite(duplicated);

        if (const ScriptComponent* sourceScript = TryGetScript(sourceEntity))
            AddScript(duplicated, *sourceScript);
        else
            RemoveScript(duplicated);

        if (const AnimatorComponent* sourceAnimator = TryGetAnimator(sourceEntity))
            AddAnimator(duplicated, *sourceAnimator);
        else
            RemoveAnimator(duplicated);

        if (const UiCanvasComponent* sourceUiCanvas = TryGetUiCanvas(sourceEntity))
            AddUiCanvas(duplicated, *sourceUiCanvas);
        else
            RemoveUiCanvas(duplicated);

        if (const UiRectTransformComponent* sourceUiRectTransform = TryGetUiRectTransform(sourceEntity))
            AddUiRectTransform(duplicated, *sourceUiRectTransform);
        else
            RemoveUiRectTransform(duplicated);

        if (const UiImageComponent* sourceUiImage = TryGetUiImage(sourceEntity))
            AddUiImage(duplicated, *sourceUiImage);
        else
            RemoveUiImage(duplicated);

        if (const UiTextComponent* sourceUiText = TryGetUiText(sourceEntity))
            AddUiText(duplicated, *sourceUiText);
        else
            RemoveUiText(duplicated);

        if (const UiButtonComponent* sourceUiButton = TryGetUiButton(sourceEntity))
            AddUiButton(duplicated, *sourceUiButton);
        else
            RemoveUiButton(duplicated);

        if (const UiInputFieldComponent* sourceUiInputField = TryGetUiInputField(sourceEntity))
            AddUiInputField(duplicated, *sourceUiInputField);
        else
            RemoveUiInputField(duplicated);

        SetParent(duplicated, duplicatedParent);

        const std::vector<Entity> children = CollectChildrenSorted(*this, sourceEntity);
        for (Entity child : children)
            self(self, child, duplicated, false);

        return duplicated;
    };

    return duplicateRecursive(duplicateRecursive, source, sourceParent, true);
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
    NormalizeCameraComponent(value);
    return value;
}

SpriteComponent& Scene::AddSprite(Entity entity, Texture* texture)
{
    auto& value = AddComponent<SpriteComponent>(entity);
    value = SpriteComponent{};
    value.texture = texture;
    NormalizeSpriteComponent(value);
    return value;
}

ScriptComponent& Scene::AddScript(Entity entity, const ScriptComponent& script)
{
    auto& value = AddComponent<ScriptComponent>(entity);
    value = script;
    return value;
}

AnimatorComponent& Scene::AddAnimator(Entity entity, const AnimatorComponent& animator)
{
    auto& value = AddComponent<AnimatorComponent>(entity);
    value = animator;

    PersistedEntity normalized;
    normalized.animator = value;
    NormalizeAnimatorState(normalized);
    value = normalized.animator;

    return value;
}

UiCanvasComponent& Scene::AddUiCanvas(Entity entity, const UiCanvasComponent& canvas)
{
    auto& value = AddComponent<UiCanvasComponent>(entity);
    value = canvas;
    NormalizeUiCanvasComponent(value);
    return value;
}

UiRectTransformComponent& Scene::AddUiRectTransform(Entity entity, const UiRectTransformComponent& rectTransform)
{
    auto& value = AddComponent<UiRectTransformComponent>(entity);
    value = rectTransform;
    NormalizeUiRectTransformComponent(value);
    return value;
}

UiImageComponent& Scene::AddUiImage(Entity entity, const UiImageComponent& image)
{
    auto& value = AddComponent<UiImageComponent>(entity);
    value = image;
    NormalizeUiImageComponent(value);
    return value;
}

UiTextComponent& Scene::AddUiText(Entity entity, const UiTextComponent& text)
{
    auto& value = AddComponent<UiTextComponent>(entity);
    value = text;
    NormalizeUiTextComponent(value);
    return value;
}

UiButtonComponent& Scene::AddUiButton(Entity entity, const UiButtonComponent& button)
{
    auto& value = AddComponent<UiButtonComponent>(entity);
    value = button;
    return value;
}

UiInputFieldComponent& Scene::AddUiInputField(Entity entity, const UiInputFieldComponent& inputField)
{
    auto& value = AddComponent<UiInputFieldComponent>(entity);
    value = inputField;
    NormalizeUiInputFieldComponent(value);
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

    if (value.tag.empty())
        value.tag = "Untagged";

    if (value.layer > 31)
        value.layer = 31;

    if (value.parentSceneEntityId == value.sceneEntityId)
        value.parentSceneEntityId = 0;

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

bool Scene::HasAnimator(Entity entity) const
{
    return HasComponent<AnimatorComponent>(entity);
}

bool Scene::HasUiCanvas(Entity entity) const
{
    return HasComponent<UiCanvasComponent>(entity);
}

bool Scene::HasUiRectTransform(Entity entity) const
{
    return HasComponent<UiRectTransformComponent>(entity);
}

bool Scene::HasUiImage(Entity entity) const
{
    return HasComponent<UiImageComponent>(entity);
}

bool Scene::HasUiText(Entity entity) const
{
    return HasComponent<UiTextComponent>(entity);
}

bool Scene::HasUiButton(Entity entity) const
{
    return HasComponent<UiButtonComponent>(entity);
}

bool Scene::HasUiInputField(Entity entity) const
{
    return HasComponent<UiInputFieldComponent>(entity);
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

AnimatorComponent* Scene::TryGetAnimator(Entity entity)
{
    return TryGetComponent<AnimatorComponent>(entity);
}

const AnimatorComponent* Scene::TryGetAnimator(Entity entity) const
{
    return TryGetComponent<AnimatorComponent>(entity);
}

UiCanvasComponent* Scene::TryGetUiCanvas(Entity entity)
{
    return TryGetComponent<UiCanvasComponent>(entity);
}

const UiCanvasComponent* Scene::TryGetUiCanvas(Entity entity) const
{
    return TryGetComponent<UiCanvasComponent>(entity);
}

UiRectTransformComponent* Scene::TryGetUiRectTransform(Entity entity)
{
    return TryGetComponent<UiRectTransformComponent>(entity);
}

const UiRectTransformComponent* Scene::TryGetUiRectTransform(Entity entity) const
{
    return TryGetComponent<UiRectTransformComponent>(entity);
}

UiImageComponent* Scene::TryGetUiImage(Entity entity)
{
    return TryGetComponent<UiImageComponent>(entity);
}

const UiImageComponent* Scene::TryGetUiImage(Entity entity) const
{
    return TryGetComponent<UiImageComponent>(entity);
}

UiTextComponent* Scene::TryGetUiText(Entity entity)
{
    return TryGetComponent<UiTextComponent>(entity);
}

const UiTextComponent* Scene::TryGetUiText(Entity entity) const
{
    return TryGetComponent<UiTextComponent>(entity);
}

UiButtonComponent* Scene::TryGetUiButton(Entity entity)
{
    return TryGetComponent<UiButtonComponent>(entity);
}

const UiButtonComponent* Scene::TryGetUiButton(Entity entity) const
{
    return TryGetComponent<UiButtonComponent>(entity);
}

UiInputFieldComponent* Scene::TryGetUiInputField(Entity entity)
{
    return TryGetComponent<UiInputFieldComponent>(entity);
}

const UiInputFieldComponent* Scene::TryGetUiInputField(Entity entity) const
{
    return TryGetComponent<UiInputFieldComponent>(entity);
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

bool Scene::RemoveAnimator(Entity entity)
{
    return RemoveComponent<AnimatorComponent>(entity);
}

bool Scene::RemoveUiCanvas(Entity entity)
{
    return RemoveComponent<UiCanvasComponent>(entity);
}

bool Scene::RemoveUiRectTransform(Entity entity)
{
    return RemoveComponent<UiRectTransformComponent>(entity);
}

bool Scene::RemoveUiImage(Entity entity)
{
    return RemoveComponent<UiImageComponent>(entity);
}

bool Scene::RemoveUiText(Entity entity)
{
    return RemoveComponent<UiTextComponent>(entity);
}

bool Scene::RemoveUiButton(Entity entity)
{
    return RemoveComponent<UiButtonComponent>(entity);
}

bool Scene::RemoveUiInputField(Entity entity)
{
    return RemoveComponent<UiInputFieldComponent>(entity);
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
            std::uint16_t componentMask = 0;
            if (entity.hasTransform)
                componentMask |= Component_Transform;
            if (entity.hasCamera)
                componentMask |= Component_Camera;
            if (entity.hasSprite)
                componentMask |= Component_Sprite;
            if (entity.hasScript)
                componentMask |= Component_Script;
            if (entity.hasAnimator)
                componentMask |= Component_Animator;
            if (entity.hasUiCanvas)
                componentMask |= Component_UiCanvas;
            if (entity.hasUiRectTransform)
                componentMask |= Component_UiRectTransform;
            if (entity.hasUiImage)
                componentMask |= Component_UiImage;
            if (entity.hasUiText)
                componentMask |= Component_UiText;
            if (entity.hasUiButton)
                componentMask |= Component_UiButton;
            if (entity.hasUiInputField)
                componentMask |= Component_UiInputField;

            WriteBinary(output, entity.sceneEntityId);
            WriteStringBinary(output, entity.name);

            const std::uint8_t active = entity.active ? 1 : 0;
            WriteBinary(output, active);
            WriteStringBinary(output, entity.tag);
            WriteBinary(output, entity.layer);
            const std::uint8_t isStatic = entity.isStatic ? 1 : 0;
            WriteBinary(output, isStatic);
            WriteBinary(output, entity.parentSceneEntityId);
            WriteBinary(output, componentMask);

            if (entity.hasTransform)
            {
                WriteBinary(output, entity.transform.x);
                WriteBinary(output, entity.transform.y);
                WriteBinary(output, entity.transform.rotation);
                WriteBinary(output, entity.transform.width);
                WriteBinary(output, entity.transform.height);
            }

            if (entity.hasCamera)
            {
                WriteBinary(output, entity.camera.x);
                WriteBinary(output, entity.camera.y);
                WriteBinary(output, entity.camera.zoom);
                WriteBinary(output, entity.camera.orthographicSize);
                const std::uint8_t enabled = entity.camera.enabled ? 1 : 0;
                const std::uint8_t primary = entity.camera.primary ? 1 : 0;
                const std::uint8_t clearColor = entity.camera.clearColor ? 1 : 0;
                WriteBinary(output, enabled);
                WriteBinary(output, primary);
                WriteBinary(output, clearColor);
                WriteBinary(output, entity.camera.backgroundColor);
                WriteBinary(output, entity.camera.cullingMask);
                WriteBinary(output, entity.camera.viewportX);
                WriteBinary(output, entity.camera.viewportY);
                WriteBinary(output, entity.camera.viewportWidth);
                WriteBinary(output, entity.camera.viewportHeight);
            }

            if (entity.hasSprite)
            {
                WriteBinary(output, entity.spriteTextureAssetHandle);
                WriteStringBinary(output, entity.spriteTexturePath);
                WriteBinary(output, entity.spriteFallbackColor);
                const std::uint8_t spriteEnabled = entity.spriteEnabled ? 1 : 0;
                WriteBinary(output, spriteEnabled);
                const std::uint8_t centered = entity.spriteCentered ? 1 : 0;
                WriteBinary(output, centered);
                WriteBinary(output, entity.spriteOffsetX);
                WriteBinary(output, entity.spriteOffsetY);
                const std::uint8_t flipH = entity.spriteFlipH ? 1 : 0;
                const std::uint8_t flipV = entity.spriteFlipV ? 1 : 0;
                WriteBinary(output, flipH);
                WriteBinary(output, flipV);
                WriteBinary(output, entity.spriteHframes);
                WriteBinary(output, entity.spriteVframes);
                WriteBinary(output, entity.spriteFrame);
                const std::uint8_t regionEnabled = entity.spriteRegionEnabled ? 1 : 0;
                WriteBinary(output, regionEnabled);
                WriteBinary(output, entity.spriteRegionX);
                WriteBinary(output, entity.spriteRegionY);
                WriteBinary(output, entity.spriteRegionWidth);
                WriteBinary(output, entity.spriteRegionHeight);
            }

            if (entity.hasScript)
            {
                WriteStringBinary(output, entity.script.classNamespace);
                WriteStringBinary(output, entity.script.className);
                const std::uint8_t enabled = entity.script.enabled ? 1 : 0;
                WriteBinary(output, enabled);
                WriteStringBinary(output, entity.script.serializedFieldState);
            }

            if (entity.hasAnimator)
            {
                WriteStringBinary(output, entity.animator.clipAssetPath);
                const std::uint8_t animatorEnabled = entity.animatorEnabled ? 1 : 0;
                WriteBinary(output, animatorEnabled);
                WriteBinary(output, entity.animator.time);
                const std::uint8_t playing = entity.animator.playing ? 1 : 0;
                const std::uint8_t loop = entity.animator.loop ? 1 : 0;
                const std::uint8_t applyPoseWhenStopped = entity.animator.applyPoseWhenStopped ? 1 : 0;
                WriteBinary(output, playing);
                WriteBinary(output, loop);
                WriteBinary(output, entity.animator.speed);
                WriteBinary(output, applyPoseWhenStopped);
            }

            if (entity.hasUiCanvas)
            {
                const std::uint8_t enabled = entity.uiCanvas.enabled ? 1 : 0;
                const std::uint8_t pixelPerfect = entity.uiCanvas.pixelPerfect ? 1 : 0;
                const std::uint8_t vertexColorAlwaysGammaSpace = entity.uiCanvas.vertexColorAlwaysGammaSpace ? 1 : 0;
                WriteBinary(output, enabled);
                WriteBinary(output, entity.uiCanvas.sortingOrder);
                WriteBinary(output, pixelPerfect);
                WriteBinary(output, entity.uiCanvas.renderMode);
                WriteBinary(output, entity.uiCanvas.targetDisplay);
                WriteBinary(output, entity.uiCanvas.additionalShaderChannels);
                WriteBinary(output, vertexColorAlwaysGammaSpace);
            }

            if (entity.hasUiRectTransform)
            {
                WriteBinary(output, entity.uiRectTransform.anchorMinX);
                WriteBinary(output, entity.uiRectTransform.anchorMinY);
                WriteBinary(output, entity.uiRectTransform.anchorMaxX);
                WriteBinary(output, entity.uiRectTransform.anchorMaxY);
                WriteBinary(output, entity.uiRectTransform.pivotX);
                WriteBinary(output, entity.uiRectTransform.pivotY);
                WriteBinary(output, entity.uiRectTransform.anchoredX);
                WriteBinary(output, entity.uiRectTransform.anchoredY);
                WriteBinary(output, entity.uiRectTransform.sizeDeltaX);
                WriteBinary(output, entity.uiRectTransform.sizeDeltaY);
            }

            if (entity.hasUiImage)
            {
                const std::uint8_t enabled = entity.uiImage.enabled ? 1 : 0;
                const std::uint8_t preserveAspect = entity.uiImage.preserveAspect ? 1 : 0;
                WriteBinary(output, enabled);
                WriteBinary(output, entity.uiImage.textureAssetHandle);
                WriteStringBinary(output, entity.uiImage.textureAssetPath);
                WriteBinary(output, entity.uiImage.color);
                WriteBinary(output, preserveAspect);
                WriteBinary(output, entity.uiImage.cornerRadius);
            }

            if (entity.hasUiText)
            {
                const std::uint8_t enabled = entity.uiText.enabled ? 1 : 0;
                const std::uint8_t wrap = entity.uiText.wrap ? 1 : 0;
                WriteBinary(output, enabled);
                WriteStringBinary(output, entity.uiText.text);
                WriteBinary(output, entity.uiText.fontSize);
                WriteBinary(output, entity.uiText.color);
                WriteBinary(output, entity.uiText.horizontalAlign);
                WriteBinary(output, wrap);
            }

            if (entity.hasUiButton)
            {
                const std::uint8_t enabled = entity.uiButton.enabled ? 1 : 0;
                const std::uint8_t interactable = entity.uiButton.interactable ? 1 : 0;
                WriteBinary(output, enabled);
                WriteBinary(output, interactable);
                WriteBinary(output, entity.uiButton.normalColor);
                WriteBinary(output, entity.uiButton.highlightedColor);
                WriteBinary(output, entity.uiButton.pressedColor);
                WriteBinary(output, entity.uiButton.disabledColor);
            }

            if (entity.hasUiInputField)
            {
                const std::uint8_t enabled = entity.uiInputField.enabled ? 1 : 0;
                const std::uint8_t interactable = entity.uiInputField.interactable ? 1 : 0;
                WriteBinary(output, enabled);
                WriteBinary(output, interactable);
                WriteStringBinary(output, entity.uiInputField.text);
                WriteStringBinary(output, entity.uiInputField.placeholder);
                WriteBinary(output, entity.uiInputField.textColor);
                WriteBinary(output, entity.uiInputField.placeholderColor);
                WriteBinary(output, entity.uiInputField.maxLength);
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

        rapidjson::Value tagValue;
        tagValue.SetString(entity.tag.c_str(), static_cast<rapidjson::SizeType>(entity.tag.size()), allocator);
        entityObject.AddMember("tag", tagValue, allocator);

        entityObject.AddMember("layer", entity.layer, allocator);
        entityObject.AddMember("active", entity.active, allocator);
        entityObject.AddMember("static", entity.isStatic, allocator);
        entityObject.AddMember("parentId", entity.parentSceneEntityId, allocator);

        rapidjson::Value componentsObject(rapidjson::kObjectType);

        if (entity.hasTransform)
        {
            rapidjson::Value transformObject(rapidjson::kObjectType);
            transformObject.AddMember("x", entity.transform.x, allocator);
            transformObject.AddMember("y", entity.transform.y, allocator);
            transformObject.AddMember("rotation", entity.transform.rotation, allocator);
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
            cameraObject.AddMember("orthographicSize", entity.camera.orthographicSize, allocator);
            cameraObject.AddMember("enabled", entity.camera.enabled, allocator);
            cameraObject.AddMember("primary", entity.camera.primary, allocator);
            cameraObject.AddMember("clearColor", entity.camera.clearColor, allocator);
            cameraObject.AddMember("backgroundColor", entity.camera.backgroundColor, allocator);
            cameraObject.AddMember("cullingMask", entity.camera.cullingMask, allocator);
            cameraObject.AddMember("viewportX", entity.camera.viewportX, allocator);
            cameraObject.AddMember("viewportY", entity.camera.viewportY, allocator);
            cameraObject.AddMember("viewportWidth", entity.camera.viewportWidth, allocator);
            cameraObject.AddMember("viewportHeight", entity.camera.viewportHeight, allocator);
            componentsObject.AddMember("camera", cameraObject, allocator);
        }

        if (entity.hasSprite)
        {
            rapidjson::Value spriteObject(rapidjson::kObjectType);
            spriteObject.AddMember("textureAssetHandle", entity.spriteTextureAssetHandle, allocator);

            rapidjson::Value texturePathValue;
            texturePathValue.SetString(entity.spriteTexturePath.c_str(), static_cast<rapidjson::SizeType>(entity.spriteTexturePath.size()), allocator);
            spriteObject.AddMember("textureAssetPath", texturePathValue, allocator);
            spriteObject.AddMember("fallbackColor", entity.spriteFallbackColor, allocator);
            spriteObject.AddMember("enabled", entity.spriteEnabled, allocator);
            spriteObject.AddMember("centered", entity.spriteCentered, allocator);
            spriteObject.AddMember("offsetX", entity.spriteOffsetX, allocator);
            spriteObject.AddMember("offsetY", entity.spriteOffsetY, allocator);
            spriteObject.AddMember("flipH", entity.spriteFlipH, allocator);
            spriteObject.AddMember("flipV", entity.spriteFlipV, allocator);
            spriteObject.AddMember("hframes", entity.spriteHframes, allocator);
            spriteObject.AddMember("vframes", entity.spriteVframes, allocator);
            spriteObject.AddMember("frame", entity.spriteFrame, allocator);
            spriteObject.AddMember("regionEnabled", entity.spriteRegionEnabled, allocator);
            spriteObject.AddMember("regionX", entity.spriteRegionX, allocator);
            spriteObject.AddMember("regionY", entity.spriteRegionY, allocator);
            spriteObject.AddMember("regionWidth", entity.spriteRegionWidth, allocator);
            spriteObject.AddMember("regionHeight", entity.spriteRegionHeight, allocator);
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

        if (entity.hasAnimator)
        {
            rapidjson::Value animatorObject(rapidjson::kObjectType);

            rapidjson::Value clipPathValue;
            clipPathValue.SetString(entity.animator.clipAssetPath.c_str(),
                                    static_cast<rapidjson::SizeType>(entity.animator.clipAssetPath.size()),
                                    allocator);
            animatorObject.AddMember("clipAssetPath", clipPathValue, allocator);
            animatorObject.AddMember("enabled", entity.animatorEnabled, allocator);
            animatorObject.AddMember("time", entity.animator.time, allocator);
            animatorObject.AddMember("playing", entity.animator.playing, allocator);
            animatorObject.AddMember("loop", entity.animator.loop, allocator);
            animatorObject.AddMember("speed", entity.animator.speed, allocator);
            animatorObject.AddMember("applyPoseWhenStopped", entity.animator.applyPoseWhenStopped, allocator);

            componentsObject.AddMember("animator", animatorObject, allocator);
        }

        if (entity.hasUiCanvas)
        {
            rapidjson::Value uiCanvasObject(rapidjson::kObjectType);
            uiCanvasObject.AddMember("enabled", entity.uiCanvas.enabled, allocator);
            uiCanvasObject.AddMember("renderMode", entity.uiCanvas.renderMode, allocator);
            uiCanvasObject.AddMember("sortingOrder", entity.uiCanvas.sortingOrder, allocator);
            uiCanvasObject.AddMember("pixelPerfect", entity.uiCanvas.pixelPerfect, allocator);
            uiCanvasObject.AddMember("targetDisplay", entity.uiCanvas.targetDisplay, allocator);
            uiCanvasObject.AddMember("additionalShaderChannels", entity.uiCanvas.additionalShaderChannels, allocator);
            uiCanvasObject.AddMember("vertexColorAlwaysGammaSpace", entity.uiCanvas.vertexColorAlwaysGammaSpace, allocator);
            componentsObject.AddMember("uiCanvas", uiCanvasObject, allocator);
        }

        if (entity.hasUiRectTransform)
        {
            rapidjson::Value uiRectTransformObject(rapidjson::kObjectType);
            uiRectTransformObject.AddMember("anchorMinX", entity.uiRectTransform.anchorMinX, allocator);
            uiRectTransformObject.AddMember("anchorMinY", entity.uiRectTransform.anchorMinY, allocator);
            uiRectTransformObject.AddMember("anchorMaxX", entity.uiRectTransform.anchorMaxX, allocator);
            uiRectTransformObject.AddMember("anchorMaxY", entity.uiRectTransform.anchorMaxY, allocator);
            uiRectTransformObject.AddMember("pivotX", entity.uiRectTransform.pivotX, allocator);
            uiRectTransformObject.AddMember("pivotY", entity.uiRectTransform.pivotY, allocator);
            uiRectTransformObject.AddMember("anchoredX", entity.uiRectTransform.anchoredX, allocator);
            uiRectTransformObject.AddMember("anchoredY", entity.uiRectTransform.anchoredY, allocator);
            uiRectTransformObject.AddMember("sizeDeltaX", entity.uiRectTransform.sizeDeltaX, allocator);
            uiRectTransformObject.AddMember("sizeDeltaY", entity.uiRectTransform.sizeDeltaY, allocator);
            componentsObject.AddMember("uiRectTransform", uiRectTransformObject, allocator);
        }

        if (entity.hasUiImage)
        {
            rapidjson::Value uiImageObject(rapidjson::kObjectType);
            uiImageObject.AddMember("enabled", entity.uiImage.enabled, allocator);
            uiImageObject.AddMember("textureAssetHandle", entity.uiImage.textureAssetHandle, allocator);
            rapidjson::Value texturePathValue;
            texturePathValue.SetString(entity.uiImage.textureAssetPath.c_str(), static_cast<rapidjson::SizeType>(entity.uiImage.textureAssetPath.size()), allocator);
            uiImageObject.AddMember("textureAssetPath", texturePathValue, allocator);
            uiImageObject.AddMember("color", entity.uiImage.color, allocator);
            uiImageObject.AddMember("preserveAspect", entity.uiImage.preserveAspect, allocator);
            uiImageObject.AddMember("cornerRadius", entity.uiImage.cornerRadius, allocator);
            componentsObject.AddMember("uiImage", uiImageObject, allocator);
        }

        if (entity.hasUiText)
        {
            rapidjson::Value uiTextObject(rapidjson::kObjectType);
            uiTextObject.AddMember("enabled", entity.uiText.enabled, allocator);
            rapidjson::Value textValue;
            textValue.SetString(entity.uiText.text.c_str(), static_cast<rapidjson::SizeType>(entity.uiText.text.size()), allocator);
            uiTextObject.AddMember("text", textValue, allocator);
            uiTextObject.AddMember("fontSize", entity.uiText.fontSize, allocator);
            uiTextObject.AddMember("color", entity.uiText.color, allocator);
            uiTextObject.AddMember("horizontalAlign", entity.uiText.horizontalAlign, allocator);
            uiTextObject.AddMember("wrap", entity.uiText.wrap, allocator);
            componentsObject.AddMember("uiText", uiTextObject, allocator);
        }

        if (entity.hasUiButton)
        {
            rapidjson::Value uiButtonObject(rapidjson::kObjectType);
            uiButtonObject.AddMember("enabled", entity.uiButton.enabled, allocator);
            uiButtonObject.AddMember("interactable", entity.uiButton.interactable, allocator);
            uiButtonObject.AddMember("normalColor", entity.uiButton.normalColor, allocator);
            uiButtonObject.AddMember("highlightedColor", entity.uiButton.highlightedColor, allocator);
            uiButtonObject.AddMember("pressedColor", entity.uiButton.pressedColor, allocator);
            uiButtonObject.AddMember("disabledColor", entity.uiButton.disabledColor, allocator);
            componentsObject.AddMember("uiButton", uiButtonObject, allocator);
        }

        if (entity.hasUiInputField)
        {
            rapidjson::Value uiInputFieldObject(rapidjson::kObjectType);
            uiInputFieldObject.AddMember("enabled", entity.uiInputField.enabled, allocator);
            uiInputFieldObject.AddMember("interactable", entity.uiInputField.interactable, allocator);
            rapidjson::Value inputTextValue;
            inputTextValue.SetString(entity.uiInputField.text.c_str(), static_cast<rapidjson::SizeType>(entity.uiInputField.text.size()), allocator);
            uiInputFieldObject.AddMember("text", inputTextValue, allocator);
            rapidjson::Value placeholderValue;
            placeholderValue.SetString(entity.uiInputField.placeholder.c_str(), static_cast<rapidjson::SizeType>(entity.uiInputField.placeholder.size()), allocator);
            uiInputFieldObject.AddMember("placeholder", placeholderValue, allocator);
            uiInputFieldObject.AddMember("textColor", entity.uiInputField.textColor, allocator);
            uiInputFieldObject.AddMember("placeholderColor", entity.uiInputField.placeholderColor, allocator);
            uiInputFieldObject.AddMember("maxLength", entity.uiInputField.maxLength, allocator);
            componentsObject.AddMember("uiInputField", uiInputFieldObject, allocator);
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

        if (fileVersion < 1 || fileVersion > kSceneFileVersion)
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
            std::uint8_t isStatic = 0;
            std::uint16_t componentMask = 0;

            if (!ReadBinary(input, entity.sceneEntityId) ||
                !ReadStringBinary(input, entity.name) ||
                !ReadBinary(input, active))
            {
                m_lastIoError = "Failed to read binary entity header data.";
                return false;
            }

            if (fileVersion >= 2)
            {
                if (!ReadStringBinary(input, entity.tag) ||
                    !ReadBinary(input, entity.layer) ||
                    !ReadBinary(input, isStatic))
                {
                    m_lastIoError = "Failed to read binary entity metadata data.";
                    return false;
                }

                if (fileVersion >= 8)
                {
                    if (!ReadBinary(input, entity.parentSceneEntityId))
                    {
                        m_lastIoError = "Failed to read binary entity parent id.";
                        return false;
                    }
                }

                if (fileVersion >= 10)
                {
                    if (!ReadBinary(input, componentMask))
                    {
                        m_lastIoError = "Failed to read binary entity component mask.";
                        return false;
                    }
                }
                else
                {
                    std::uint8_t legacyComponentMask = 0;
                    if (!ReadBinary(input, legacyComponentMask))
                    {
                        m_lastIoError = "Failed to read binary entity component mask.";
                        return false;
                    }

                    componentMask = legacyComponentMask;
                }
            }
            else
            {
                std::uint8_t legacyComponentMask = 0;
                if (!ReadBinary(input, legacyComponentMask))
                {
                    m_lastIoError = "Failed to read binary entity component mask.";
                    return false;
                }

                componentMask = legacyComponentMask;

                entity.tag = "Untagged";
                entity.layer = 0;
                entity.parentSceneEntityId = 0;
                isStatic = 0;
            }

            entity.active = active != 0;
            entity.isStatic = isStatic != 0;

            if (entity.layer > 31)
                entity.layer = 31;
            entity.hasTransform = (componentMask & Component_Transform) != 0;
            entity.hasCamera = (componentMask & Component_Camera) != 0;
            entity.hasSprite = (componentMask & Component_Sprite) != 0;
            entity.hasScript = (componentMask & Component_Script) != 0;
            entity.hasAnimator = (componentMask & Component_Animator) != 0;
            entity.hasUiCanvas = (componentMask & Component_UiCanvas) != 0;
            entity.hasUiRectTransform = (componentMask & Component_UiRectTransform) != 0;
            entity.hasUiImage = (componentMask & Component_UiImage) != 0;
            entity.hasUiText = (componentMask & Component_UiText) != 0;
            entity.hasUiButton = (componentMask & Component_UiButton) != 0;
            entity.hasUiInputField = (componentMask & Component_UiInputField) != 0;

            if (entity.hasTransform)
            {
                if (!ReadBinary(input, entity.transform.x) ||
                    !ReadBinary(input, entity.transform.y))
                {
                    m_lastIoError = "Failed to read binary transform component.";
                    return false;
                }

                if (fileVersion >= 7)
                {
                    if (!ReadBinary(input, entity.transform.rotation))
                    {
                        m_lastIoError = "Failed to read binary transform rotation.";
                        return false;
                    }
                }
                else
                {
                    entity.transform.rotation = 0.0f;
                }

                if (!ReadBinary(input, entity.transform.width) ||
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

                if (fileVersion >= 6)
                {
                    if (!ReadBinary(input, entity.camera.orthographicSize))
                    {
                        m_lastIoError = "Failed to read binary camera orthographic size.";
                        return false;
                    }
                }

                if (fileVersion >= 5)
                {
                    std::uint8_t enabled = 0;
                    std::uint8_t primary = 0;
                    std::uint8_t clearColor = 0;
                    if (!ReadBinary(input, enabled) ||
                        !ReadBinary(input, primary) ||
                        !ReadBinary(input, clearColor) ||
                        !ReadBinary(input, entity.camera.backgroundColor) ||
                        !ReadBinary(input, entity.camera.cullingMask) ||
                        !ReadBinary(input, entity.camera.viewportX) ||
                        !ReadBinary(input, entity.camera.viewportY) ||
                        !ReadBinary(input, entity.camera.viewportWidth) ||
                        !ReadBinary(input, entity.camera.viewportHeight))
                    {
                        m_lastIoError = "Failed to read binary camera extended settings.";
                        return false;
                    }

                    entity.camera.enabled = enabled != 0;
                    entity.camera.primary = primary != 0;
                    entity.camera.clearColor = clearColor != 0;
                }

                NormalizeCameraState(entity);
            }

            if (entity.hasSprite)
            {
                if (!ReadBinary(input, entity.spriteTextureAssetHandle) ||
                    !ReadStringBinary(input, entity.spriteTexturePath))
                {
                    m_lastIoError = "Failed to read binary sprite component.";
                    return false;
                }

                if (fileVersion >= 3)
                {
                    if (fileVersion >= 4)
                    {
                        if (!ReadBinary(input, entity.spriteFallbackColor))
                        {
                            m_lastIoError = "Failed to read binary sprite fallback color.";
                            return false;
                        }
                    }

                    if (fileVersion >= 9)
                    {
                        std::uint8_t enabled = 1;
                        if (!ReadBinary(input, enabled))
                        {
                            m_lastIoError = "Failed to read binary sprite enabled state.";
                            return false;
                        }
                        entity.spriteEnabled = enabled != 0;
                    }
                    else
                    {
                        entity.spriteEnabled = true;
                    }

                    std::uint8_t centered = 0;
                    std::uint8_t flipH = 0;
                    std::uint8_t flipV = 0;
                    std::uint8_t regionEnabled = 0;
                    if (!ReadBinary(input, centered) ||
                        !ReadBinary(input, entity.spriteOffsetX) ||
                        !ReadBinary(input, entity.spriteOffsetY) ||
                        !ReadBinary(input, flipH) ||
                        !ReadBinary(input, flipV) ||
                        !ReadBinary(input, entity.spriteHframes) ||
                        !ReadBinary(input, entity.spriteVframes) ||
                        !ReadBinary(input, entity.spriteFrame) ||
                        !ReadBinary(input, regionEnabled) ||
                        !ReadBinary(input, entity.spriteRegionX) ||
                        !ReadBinary(input, entity.spriteRegionY) ||
                        !ReadBinary(input, entity.spriteRegionWidth) ||
                        !ReadBinary(input, entity.spriteRegionHeight))
                    {
                        m_lastIoError = "Failed to read binary sprite settings.";
                        return false;
                    }

                    entity.spriteCentered = centered != 0;
                    entity.spriteFlipH = flipH != 0;
                    entity.spriteFlipV = flipV != 0;
                    entity.spriteRegionEnabled = regionEnabled != 0;
                }

                NormalizeSpriteState(entity);
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

            if (entity.hasAnimator)
            {
                std::uint8_t enabled = 1;
                std::uint8_t playing = 0;
                std::uint8_t loop = 0;
                std::uint8_t applyPoseWhenStopped = 0;
                if (!ReadStringBinary(input, entity.animator.clipAssetPath))
                {
                    m_lastIoError = "Failed to read binary animator component.";
                    return false;
                }

                if (fileVersion >= 9)
                {
                    if (!ReadBinary(input, enabled))
                    {
                        m_lastIoError = "Failed to read binary animator enabled state.";
                        return false;
                    }
                }

                if (!ReadBinary(input, entity.animator.time) ||
                    !ReadBinary(input, playing) ||
                    !ReadBinary(input, loop) ||
                    !ReadBinary(input, entity.animator.speed) ||
                    !ReadBinary(input, applyPoseWhenStopped))
                {
                    m_lastIoError = "Failed to read binary animator component.";
                    return false;
                }

                entity.animatorEnabled = enabled != 0;
                entity.animator.enabled = entity.animatorEnabled;
                entity.animator.playing = playing != 0;
                entity.animator.loop = loop != 0;
                entity.animator.applyPoseWhenStopped = applyPoseWhenStopped != 0;
                NormalizeAnimatorState(entity);
            }

            if (entity.hasUiCanvas)
            {
                std::uint8_t enabled = 1;
                std::uint8_t pixelPerfect = 0;
                if (!ReadBinary(input, enabled) ||
                    !ReadBinary(input, entity.uiCanvas.sortingOrder) ||
                    !ReadBinary(input, pixelPerfect))
                {
                    m_lastIoError = "Failed to read binary UI canvas component.";
                    return false;
                }

                entity.uiCanvas.enabled = enabled != 0;
                entity.uiCanvas.pixelPerfect = pixelPerfect != 0;

                if (fileVersion >= 11)
                {
                    std::uint8_t vertexColorAlwaysGammaSpace = 0;
                    if (!ReadBinary(input, entity.uiCanvas.renderMode) ||
                        !ReadBinary(input, entity.uiCanvas.targetDisplay) ||
                        !ReadBinary(input, entity.uiCanvas.additionalShaderChannels) ||
                        !ReadBinary(input, vertexColorAlwaysGammaSpace))
                    {
                        m_lastIoError = "Failed to read binary UI canvas extended fields.";
                        return false;
                    }

                    entity.uiCanvas.vertexColorAlwaysGammaSpace = vertexColorAlwaysGammaSpace != 0;
                }

                NormalizeUiCanvasComponent(entity.uiCanvas);
            }

            if (entity.hasUiRectTransform)
            {
                if (!ReadBinary(input, entity.uiRectTransform.anchorMinX) ||
                    !ReadBinary(input, entity.uiRectTransform.anchorMinY) ||
                    !ReadBinary(input, entity.uiRectTransform.anchorMaxX) ||
                    !ReadBinary(input, entity.uiRectTransform.anchorMaxY) ||
                    !ReadBinary(input, entity.uiRectTransform.pivotX) ||
                    !ReadBinary(input, entity.uiRectTransform.pivotY) ||
                    !ReadBinary(input, entity.uiRectTransform.anchoredX) ||
                    !ReadBinary(input, entity.uiRectTransform.anchoredY) ||
                    !ReadBinary(input, entity.uiRectTransform.sizeDeltaX) ||
                    !ReadBinary(input, entity.uiRectTransform.sizeDeltaY))
                {
                    m_lastIoError = "Failed to read binary UI rect transform component.";
                    return false;
                }

                NormalizeUiRectTransformComponent(entity.uiRectTransform);
            }

            if (entity.hasUiImage)
            {
                std::uint8_t enabled = 1;
                std::uint8_t preserveAspect = 1;
                if (!ReadBinary(input, enabled) ||
                    !ReadBinary(input, entity.uiImage.textureAssetHandle) ||
                    !ReadStringBinary(input, entity.uiImage.textureAssetPath) ||
                    !ReadBinary(input, entity.uiImage.color) ||
                    !ReadBinary(input, preserveAspect) ||
                    !ReadBinary(input, entity.uiImage.cornerRadius))
                {
                    m_lastIoError = "Failed to read binary UI image component.";
                    return false;
                }

                entity.uiImage.enabled = enabled != 0;
                entity.uiImage.preserveAspect = preserveAspect != 0;
                NormalizeUiImageComponent(entity.uiImage);
            }

            if (entity.hasUiText)
            {
                std::uint8_t enabled = 1;
                std::uint8_t wrap = 0;
                if (!ReadBinary(input, enabled) ||
                    !ReadStringBinary(input, entity.uiText.text) ||
                    !ReadBinary(input, entity.uiText.fontSize) ||
                    !ReadBinary(input, entity.uiText.color) ||
                    !ReadBinary(input, entity.uiText.horizontalAlign) ||
                    !ReadBinary(input, wrap))
                {
                    m_lastIoError = "Failed to read binary UI text component.";
                    return false;
                }

                entity.uiText.enabled = enabled != 0;
                entity.uiText.wrap = wrap != 0;
                NormalizeUiTextComponent(entity.uiText);
            }

            if (entity.hasUiButton)
            {
                std::uint8_t enabled = 1;
                std::uint8_t interactable = 1;
                if (!ReadBinary(input, enabled) ||
                    !ReadBinary(input, interactable) ||
                    !ReadBinary(input, entity.uiButton.normalColor) ||
                    !ReadBinary(input, entity.uiButton.highlightedColor) ||
                    !ReadBinary(input, entity.uiButton.pressedColor) ||
                    !ReadBinary(input, entity.uiButton.disabledColor))
                {
                    m_lastIoError = "Failed to read binary UI button component.";
                    return false;
                }

                entity.uiButton.enabled = enabled != 0;
                entity.uiButton.interactable = interactable != 0;
            }

            if (entity.hasUiInputField)
            {
                std::uint8_t enabled = 1;
                std::uint8_t interactable = 1;
                if (!ReadBinary(input, enabled) ||
                    !ReadBinary(input, interactable) ||
                    !ReadStringBinary(input, entity.uiInputField.text) ||
                    !ReadStringBinary(input, entity.uiInputField.placeholder) ||
                    !ReadBinary(input, entity.uiInputField.textColor) ||
                    !ReadBinary(input, entity.uiInputField.placeholderColor) ||
                    !ReadBinary(input, entity.uiInputField.maxLength))
                {
                    m_lastIoError = "Failed to read binary UI input field component.";
                    return false;
                }

                entity.uiInputField.enabled = enabled != 0;
                entity.uiInputField.interactable = interactable != 0;
                NormalizeUiInputFieldComponent(entity.uiInputField);
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
            entity.tag = "Untagged";
            entity.layer = 0;
            entity.active = true;
            entity.isStatic = false;

            if (!ReadOptionalString(entityValue, "name", entity.name, parseError) ||
                !ReadOptionalString(entityValue, "tag", entity.tag, parseError) ||
                !ReadOptionalUInt32(entityValue, "layer", entity.layer, parseError) ||
                !ReadOptionalBool(entityValue, "active", entity.active, parseError) ||
                !ReadOptionalUInt64(entityValue, "parentId", entity.parentSceneEntityId, parseError))
            {
                m_lastIoError = "Invalid JSON entity header: " + parseError;
                return false;
            }

            if (!ReadOptionalBool(entityValue, "static", entity.isStatic, parseError))
            {
                m_lastIoError = "Invalid JSON entity static flag: " + parseError;
                return false;
            }

            if (entity.layer > 31)
                entity.layer = 31;

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
                        !ReadOptionalFloat(transform, "rotation", entity.transform.rotation, parseError) ||
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
                        !ReadOptionalFloat(camera, "zoom", entity.camera.zoom, parseError) ||
                        !ReadOptionalFloat(camera, "orthographicSize", entity.camera.orthographicSize, parseError) ||
                        !ReadOptionalBool(camera, "enabled", entity.camera.enabled, parseError) ||
                        !ReadOptionalBool(camera, "primary", entity.camera.primary, parseError) ||
                        !ReadOptionalBool(camera, "clearColor", entity.camera.clearColor, parseError) ||
                        !ReadOptionalUInt32(camera, "backgroundColor", entity.camera.backgroundColor, parseError) ||
                        !ReadOptionalUInt32(camera, "cullingMask", entity.camera.cullingMask, parseError) ||
                        !ReadOptionalFloat(camera, "viewportX", entity.camera.viewportX, parseError) ||
                        !ReadOptionalFloat(camera, "viewportY", entity.camera.viewportY, parseError) ||
                        !ReadOptionalFloat(camera, "viewportWidth", entity.camera.viewportWidth, parseError) ||
                        !ReadOptionalFloat(camera, "viewportHeight", entity.camera.viewportHeight, parseError))
                    {
                        m_lastIoError = "Invalid JSON camera component: " + parseError;
                        return false;
                    }

                    NormalizeCameraState(entity);
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
                        !ReadOptionalString(sprite, "textureAssetPath", entity.spriteTexturePath, parseError) ||
                        !ReadOptionalUInt32(sprite, "fallbackColor", entity.spriteFallbackColor, parseError) ||
                        !ReadOptionalBool(sprite, "enabled", entity.spriteEnabled, parseError) ||
                        !ReadOptionalBool(sprite, "centered", entity.spriteCentered, parseError) ||
                        !ReadOptionalFloat(sprite, "offsetX", entity.spriteOffsetX, parseError) ||
                        !ReadOptionalFloat(sprite, "offsetY", entity.spriteOffsetY, parseError) ||
                        !ReadOptionalBool(sprite, "flipH", entity.spriteFlipH, parseError) ||
                        !ReadOptionalBool(sprite, "flipV", entity.spriteFlipV, parseError) ||
                        !ReadOptionalUInt32(sprite, "hframes", entity.spriteHframes, parseError) ||
                        !ReadOptionalUInt32(sprite, "vframes", entity.spriteVframes, parseError) ||
                        !ReadOptionalUInt32(sprite, "frame", entity.spriteFrame, parseError) ||
                        !ReadOptionalBool(sprite, "regionEnabled", entity.spriteRegionEnabled, parseError) ||
                        !ReadOptionalFloat(sprite, "regionX", entity.spriteRegionX, parseError) ||
                        !ReadOptionalFloat(sprite, "regionY", entity.spriteRegionY, parseError) ||
                        !ReadOptionalFloat(sprite, "regionWidth", entity.spriteRegionWidth, parseError) ||
                        !ReadOptionalFloat(sprite, "regionHeight", entity.spriteRegionHeight, parseError))
                    {
                        m_lastIoError = "Invalid JSON sprite component: " + parseError;
                        return false;
                    }

                    NormalizeSpriteState(entity);
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

                if (components.HasMember("animator"))
                {
                    const rapidjson::Value& animator = components["animator"];
                    if (!animator.IsObject())
                    {
                        m_lastIoError = "Invalid JSON animator component: expected object.";
                        return false;
                    }

                    entity.hasAnimator = true;
                    if (!ReadOptionalString(animator, "clipAssetPath", entity.animator.clipAssetPath, parseError) ||
                        !ReadOptionalBool(animator, "enabled", entity.animatorEnabled, parseError) ||
                        !ReadOptionalFloat(animator, "time", entity.animator.time, parseError) ||
                        !ReadOptionalBool(animator, "playing", entity.animator.playing, parseError) ||
                        !ReadOptionalBool(animator, "loop", entity.animator.loop, parseError) ||
                        !ReadOptionalFloat(animator, "speed", entity.animator.speed, parseError) ||
                        !ReadOptionalBool(animator, "applyPoseWhenStopped", entity.animator.applyPoseWhenStopped, parseError))
                    {
                        m_lastIoError = "Invalid JSON animator component: " + parseError;
                        return false;
                    }

                    NormalizeAnimatorState(entity);
                    entity.animator.enabled = entity.animatorEnabled;
                }

                if (components.HasMember("uiCanvas"))
                {
                    const rapidjson::Value& uiCanvas = components["uiCanvas"];
                    if (!uiCanvas.IsObject())
                    {
                        m_lastIoError = "Invalid JSON UI canvas component: expected object.";
                        return false;
                    }

                    entity.hasUiCanvas = true;
                    if (!ReadOptionalBool(uiCanvas, "enabled", entity.uiCanvas.enabled, parseError) ||
                        !ReadOptionalBool(uiCanvas, "pixelPerfect", entity.uiCanvas.pixelPerfect, parseError) ||
                        !ReadOptionalUInt32(uiCanvas, "additionalShaderChannels", entity.uiCanvas.additionalShaderChannels, parseError) ||
                        !ReadOptionalBool(uiCanvas, "vertexColorAlwaysGammaSpace", entity.uiCanvas.vertexColorAlwaysGammaSpace, parseError))
                    {
                        m_lastIoError = "Invalid JSON UI canvas component: " + parseError;
                        return false;
                    }

                    if (uiCanvas.HasMember("renderMode"))
                    {
                        const rapidjson::Value& renderMode = uiCanvas["renderMode"];
                        if (!renderMode.IsInt())
                        {
                            m_lastIoError = "Invalid JSON UI canvas renderMode: expected int.";
                            return false;
                        }

                        entity.uiCanvas.renderMode = renderMode.GetInt();
                    }

                    if (uiCanvas.HasMember("sortingOrder"))
                    {
                        const rapidjson::Value& sortingOrder = uiCanvas["sortingOrder"];
                        if (!sortingOrder.IsInt())
                        {
                            m_lastIoError = "Invalid JSON UI canvas sortingOrder: expected int.";
                            return false;
                        }

                        entity.uiCanvas.sortingOrder = sortingOrder.GetInt();
                    }

                    if (uiCanvas.HasMember("targetDisplay"))
                    {
                        const rapidjson::Value& targetDisplay = uiCanvas["targetDisplay"];
                        if (!targetDisplay.IsInt())
                        {
                            m_lastIoError = "Invalid JSON UI canvas targetDisplay: expected int.";
                            return false;
                        }

                        entity.uiCanvas.targetDisplay = targetDisplay.GetInt();
                    }

                    NormalizeUiCanvasComponent(entity.uiCanvas);
                }

                if (components.HasMember("uiRectTransform"))
                {
                    const rapidjson::Value& uiRectTransform = components["uiRectTransform"];
                    if (!uiRectTransform.IsObject())
                    {
                        m_lastIoError = "Invalid JSON UI rect transform component: expected object.";
                        return false;
                    }

                    entity.hasUiRectTransform = true;
                    if (!ReadOptionalFloat(uiRectTransform, "anchorMinX", entity.uiRectTransform.anchorMinX, parseError) ||
                        !ReadOptionalFloat(uiRectTransform, "anchorMinY", entity.uiRectTransform.anchorMinY, parseError) ||
                        !ReadOptionalFloat(uiRectTransform, "anchorMaxX", entity.uiRectTransform.anchorMaxX, parseError) ||
                        !ReadOptionalFloat(uiRectTransform, "anchorMaxY", entity.uiRectTransform.anchorMaxY, parseError) ||
                        !ReadOptionalFloat(uiRectTransform, "pivotX", entity.uiRectTransform.pivotX, parseError) ||
                        !ReadOptionalFloat(uiRectTransform, "pivotY", entity.uiRectTransform.pivotY, parseError) ||
                        !ReadOptionalFloat(uiRectTransform, "anchoredX", entity.uiRectTransform.anchoredX, parseError) ||
                        !ReadOptionalFloat(uiRectTransform, "anchoredY", entity.uiRectTransform.anchoredY, parseError) ||
                        !ReadOptionalFloat(uiRectTransform, "sizeDeltaX", entity.uiRectTransform.sizeDeltaX, parseError) ||
                        !ReadOptionalFloat(uiRectTransform, "sizeDeltaY", entity.uiRectTransform.sizeDeltaY, parseError))
                    {
                        m_lastIoError = "Invalid JSON UI rect transform component: " + parseError;
                        return false;
                    }

                    NormalizeUiRectTransformComponent(entity.uiRectTransform);
                }

                if (components.HasMember("uiImage"))
                {
                    const rapidjson::Value& uiImage = components["uiImage"];
                    if (!uiImage.IsObject())
                    {
                        m_lastIoError = "Invalid JSON UI image component: expected object.";
                        return false;
                    }

                    entity.hasUiImage = true;
                    if (!ReadOptionalBool(uiImage, "enabled", entity.uiImage.enabled, parseError) ||
                        !ReadOptionalUInt64(uiImage, "textureAssetHandle", entity.uiImage.textureAssetHandle, parseError) ||
                        !ReadOptionalString(uiImage, "textureAssetPath", entity.uiImage.textureAssetPath, parseError) ||
                        !ReadOptionalUInt32(uiImage, "color", entity.uiImage.color, parseError) ||
                        !ReadOptionalBool(uiImage, "preserveAspect", entity.uiImage.preserveAspect, parseError) ||
                        !ReadOptionalFloat(uiImage, "cornerRadius", entity.uiImage.cornerRadius, parseError))
                    {
                        m_lastIoError = "Invalid JSON UI image component: " + parseError;
                        return false;
                    }

                    NormalizeUiImageComponent(entity.uiImage);
                }

                if (components.HasMember("uiText"))
                {
                    const rapidjson::Value& uiText = components["uiText"];
                    if (!uiText.IsObject())
                    {
                        m_lastIoError = "Invalid JSON UI text component: expected object.";
                        return false;
                    }

                    entity.hasUiText = true;
                    if (!ReadOptionalBool(uiText, "enabled", entity.uiText.enabled, parseError) ||
                        !ReadOptionalString(uiText, "text", entity.uiText.text, parseError) ||
                        !ReadOptionalFloat(uiText, "fontSize", entity.uiText.fontSize, parseError) ||
                        !ReadOptionalUInt32(uiText, "color", entity.uiText.color, parseError) ||
                        !ReadOptionalBool(uiText, "wrap", entity.uiText.wrap, parseError))
                    {
                        m_lastIoError = "Invalid JSON UI text component: " + parseError;
                        return false;
                    }

                    if (uiText.HasMember("horizontalAlign"))
                    {
                        const rapidjson::Value& horizontalAlign = uiText["horizontalAlign"];
                        if (!horizontalAlign.IsInt())
                        {
                            m_lastIoError = "Invalid JSON UI text horizontalAlign: expected int.";
                            return false;
                        }

                        entity.uiText.horizontalAlign = horizontalAlign.GetInt();
                    }

                    NormalizeUiTextComponent(entity.uiText);
                }

                if (components.HasMember("uiButton"))
                {
                    const rapidjson::Value& uiButton = components["uiButton"];
                    if (!uiButton.IsObject())
                    {
                        m_lastIoError = "Invalid JSON UI button component: expected object.";
                        return false;
                    }

                    entity.hasUiButton = true;
                    if (!ReadOptionalBool(uiButton, "enabled", entity.uiButton.enabled, parseError) ||
                        !ReadOptionalBool(uiButton, "interactable", entity.uiButton.interactable, parseError) ||
                        !ReadOptionalUInt32(uiButton, "normalColor", entity.uiButton.normalColor, parseError) ||
                        !ReadOptionalUInt32(uiButton, "highlightedColor", entity.uiButton.highlightedColor, parseError) ||
                        !ReadOptionalUInt32(uiButton, "pressedColor", entity.uiButton.pressedColor, parseError) ||
                        !ReadOptionalUInt32(uiButton, "disabledColor", entity.uiButton.disabledColor, parseError))
                    {
                        m_lastIoError = "Invalid JSON UI button component: " + parseError;
                        return false;
                    }
                }

                if (components.HasMember("uiInputField"))
                {
                    const rapidjson::Value& uiInputField = components["uiInputField"];
                    if (!uiInputField.IsObject())
                    {
                        m_lastIoError = "Invalid JSON UI input field component: expected object.";
                        return false;
                    }

                    entity.hasUiInputField = true;
                    if (!ReadOptionalBool(uiInputField, "enabled", entity.uiInputField.enabled, parseError) ||
                        !ReadOptionalBool(uiInputField, "interactable", entity.uiInputField.interactable, parseError) ||
                        !ReadOptionalString(uiInputField, "text", entity.uiInputField.text, parseError) ||
                        !ReadOptionalString(uiInputField, "placeholder", entity.uiInputField.placeholder, parseError) ||
                        !ReadOptionalUInt32(uiInputField, "textColor", entity.uiInputField.textColor, parseError) ||
                        !ReadOptionalUInt32(uiInputField, "placeholderColor", entity.uiInputField.placeholderColor, parseError) ||
                        !ReadOptionalUInt32(uiInputField, "maxLength", entity.uiInputField.maxLength, parseError))
                    {
                        m_lastIoError = "Invalid JSON UI input field component: " + parseError;
                        return false;
                    }

                    NormalizeUiInputFieldComponent(entity.uiInputField);
                }
            }

            loadedEntities.push_back(std::move(entity));
        }
    }

    if (fileVersion < 1 || fileVersion > kSceneFileVersion)
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

        if (EntityMetadataComponent* metadata = TryGetMetadata(entity))
        {
            metadata->tag = persisted.tag.empty() ? "Untagged" : persisted.tag;
            metadata->layer = persisted.layer > 31 ? 31 : persisted.layer;
            metadata->isStatic = persisted.isStatic;
            metadata->parentSceneEntityId = persisted.parentSceneEntityId;
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
        {
            CameraComponent& camera = AddCamera(entity, persisted.camera);
            NormalizeCameraComponent(camera);
        }
        else
            RemoveCamera(entity);

        if (persisted.hasSprite)
        {
            SpriteComponent& sprite = AddSprite(entity);
            sprite.texture = nullptr;
            sprite.textureAssetHandle = persisted.spriteTextureAssetHandle;
            sprite.textureAssetPath = persisted.spriteTexturePath;
            sprite.fallbackColor = persisted.spriteFallbackColor;
            sprite.enabled = persisted.spriteEnabled;
            sprite.centered = persisted.spriteCentered;
            sprite.offsetX = persisted.spriteOffsetX;
            sprite.offsetY = persisted.spriteOffsetY;
            sprite.flipH = persisted.spriteFlipH;
            sprite.flipV = persisted.spriteFlipV;
            sprite.hframes = persisted.spriteHframes;
            sprite.vframes = persisted.spriteVframes;
            sprite.frame = persisted.spriteFrame;
            sprite.regionEnabled = persisted.spriteRegionEnabled;
            sprite.regionX = persisted.spriteRegionX;
            sprite.regionY = persisted.spriteRegionY;
            sprite.regionWidth = persisted.spriteRegionWidth;
            sprite.regionHeight = persisted.spriteRegionHeight;
            NormalizeSpriteComponent(sprite);
        }
        else
        {
            RemoveSprite(entity);
        }

        if (persisted.hasScript)
            AddScript(entity, persisted.script);
        else
            RemoveScript(entity);

        if (persisted.hasAnimator)
            AddAnimator(entity, persisted.animator);
        else
            RemoveAnimator(entity);

        if (persisted.hasUiCanvas)
            AddUiCanvas(entity, persisted.uiCanvas);
        else
            RemoveUiCanvas(entity);

        if (persisted.hasUiRectTransform)
            AddUiRectTransform(entity, persisted.uiRectTransform);
        else
            RemoveUiRectTransform(entity);

        if (persisted.hasUiImage)
            AddUiImage(entity, persisted.uiImage);
        else
            RemoveUiImage(entity);

        if (persisted.hasUiText)
            AddUiText(entity, persisted.uiText);
        else
            RemoveUiText(entity);

        if (persisted.hasUiButton)
            AddUiButton(entity, persisted.uiButton);
        else
            RemoveUiButton(entity);

        if (persisted.hasUiInputField)
            AddUiInputField(entity, persisted.uiInputField);
        else
            RemoveUiInputField(entity);

        if (persisted.hasAnimator)
        {
            AnimatorComponent* animator = TryGetAnimator(entity);
            if (animator)
                animator->enabled = persisted.animatorEnabled;
        }
    }

    auto metadataView = m_registry.view<EntityMetadataComponent>();
    for (const auto entity : metadataView)
    {
        EntityMetadataComponent& metadata = metadataView.get<EntityMetadataComponent>(entity);
        if (metadata.parentSceneEntityId == 0)
            continue;

        const Entity parent = FindBySceneEntityId(metadata.parentSceneEntityId);
        if (parent == entt::null || parent == entity || IsAncestor(entity, parent))
            metadata.parentSceneEntityId = 0;
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
    Entity firstEnabled = entt::null;
    for (const auto entity : view)
    {
        const auto& camera = view.get<CameraComponent>(entity);
        if (!camera.enabled)
            continue;

        if (camera.primary)
            return entity;

        if (firstEnabled == entt::null)
            firstEnabled = entity;
    }

    return firstEnabled;
}

entt::registry& Scene::Registry()
{
    return m_registry;
}

const entt::registry& Scene::Registry() const
{
    return m_registry;
}
