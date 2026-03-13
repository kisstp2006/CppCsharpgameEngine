#if ENGINE_MONO_RUNTIME_AVAILABLE
static Scene* g_editorSceneContext = nullptr;
static Renderer* g_editorRendererContext = nullptr;
static Engine* g_editorEngineContext = nullptr;
static Scene* g_runtimeSceneForScriptApi = nullptr;
static bool g_editorTopBarStylePushed = false;
static std::string g_editorSceneIoStatus = "Ready.";

enum class EngineLogLevel : std::int32_t
{
    Log = 0,
    Warning = 1,
    Error = 2
};

struct EngineLogEntry
{
    EngineLogLevel level = EngineLogLevel::Log;
    std::string line;
};

static std::vector<EngineLogEntry> g_engineLogEntries;
static constexpr std::size_t kMaxEngineLogEntries = 4096;

enum class EditorComponentType : std::uint32_t
{
    Transform = 0,
    Camera = 1,
    Sprite = 2,
    Script = 3
};

static std::string MonoStringToUtf8(MonoString* monoString)
{
    if (!monoString)
        return {};

    char* utf8 = mono_string_to_utf8(monoString);
    if (!utf8)
        return {};

    std::string result(utf8);
    mono_free(utf8);
    return result;
}

static std::string BuildEngineLogPrefix(const char* level)
{
    const auto now = std::chrono::system_clock::now();
    const auto nowMilliseconds = std::chrono::duration_cast<std::chrono::milliseconds>(now.time_since_epoch());
    const auto millis = nowMilliseconds.count() % 1000;

    const std::time_t nowTimeT = std::chrono::system_clock::to_time_t(now);
    std::tm localTime = {};
#if defined(_WIN32)
    localtime_s(&localTime, &nowTimeT);
#else
    localtime_r(&nowTimeT, &localTime);
#endif

    std::ostringstream stream;
    stream << "[" << std::setfill('0')
           << std::setw(2) << localTime.tm_hour << ":"
           << std::setw(2) << localTime.tm_min << ":"
           << std::setw(2) << localTime.tm_sec << "."
           << std::setw(3) << millis
           << "][" << level << "] ";
    return stream.str();
}

static void AppendEngineLogEntry(EngineLogLevel level, const std::string& line)
{
    EngineLogEntry entry;
    entry.level = level;
    entry.line = line;
    g_engineLogEntries.push_back(std::move(entry));

    if (g_engineLogEntries.size() > kMaxEngineLogEntries)
        g_engineLogEntries.erase(g_engineLogEntries.begin());
}

static void EngineDebug_LogInternal(const char* level, EngineLogLevel logLevel, MonoString* message)
{
    const std::string utf8Message = MonoStringToUtf8(message);
    const std::string content = utf8Message.empty() ? "<null>" : utf8Message;
    const std::string line = BuildEngineLogPrefix(level) + content;

    switch (logLevel)
    {
    case EngineLogLevel::Log:
        EngineLogger::Info("Engine.Debug", content);
        break;
    case EngineLogLevel::Warning:
        EngineLogger::Warning("Engine.Debug", content);
        break;
    case EngineLogLevel::Error:
        EngineLogger::Error("Engine.Debug", content);
        break;
    default:
        EngineLogger::Info("Engine.Debug", content);
        break;
    }

    AppendEngineLogEntry(logLevel, line);
}

static void EngineDebug_Log(MonoString* message)
{
    EngineDebug_LogInternal("Log", EngineLogLevel::Log, message);
}

static void EngineDebug_LogWarning(MonoString* message)
{
    EngineDebug_LogInternal("Warning", EngineLogLevel::Warning, message);
}

static void EngineDebug_LogError(MonoString* message)
{
    EngineDebug_LogInternal("Error", EngineLogLevel::Error, message);
}

static int EngineDebug_GetLogCount()
{
    return static_cast<int>(g_engineLogEntries.size());
}

static MonoString* EngineDebug_GetLogMessage(int index)
{
    if (index < 0 || static_cast<std::size_t>(index) >= g_engineLogEntries.size())
        return nullptr;

    MonoDomain* domain = mono_domain_get();
    if (!domain)
        return nullptr;

    const std::string& line = g_engineLogEntries[static_cast<std::size_t>(index)].line;
    return mono_string_new(domain, line.c_str());
}

static int EngineDebug_GetLogLevel(int index)
{
    if (index < 0 || static_cast<std::size_t>(index) >= g_engineLogEntries.size())
        return static_cast<int>(EngineLogLevel::Log);

    return static_cast<int>(g_engineLogEntries[static_cast<std::size_t>(index)].level);
}

static void EngineDebug_ClearLogs()
{
    g_engineLogEntries.clear();
}

static Scene* GetSceneContextForSpriteApi()
{
    if (g_runtimeSceneForScriptApi)
        return g_runtimeSceneForScriptApi;

    return g_editorSceneContext;
}

static Scene* GetSceneContextForEntityApi()
{
    if (g_runtimeSceneForScriptApi)
        return g_runtimeSceneForScriptApi;

    return g_editorSceneContext;
}

static float ClampFloatRange(float value, float minValue, float maxValue)
{
    if (value < minValue)
        return minValue;
    if (value > maxValue)
        return maxValue;
    return value;
}

static void NormalizeCameraValues(CameraComponent& camera)
{
    if (!std::isfinite(camera.x))
        camera.x = 0.0f;
    if (!std::isfinite(camera.y))
        camera.y = 0.0f;
    if (!std::isfinite(camera.zoom))
        camera.zoom = 1.0f;
    if (!std::isfinite(camera.orthographicSize))
        camera.orthographicSize = 0.0f;

    camera.zoom = ClampFloatRange(camera.zoom, 0.01f, 100.0f);
    camera.orthographicSize = ClampFloatRange(camera.orthographicSize, 0.0f, 100000.0f);

    camera.viewportX = ClampFloatRange(camera.viewportX, 0.0f, 1.0f);
    camera.viewportY = ClampFloatRange(camera.viewportY, 0.0f, 1.0f);
    camera.viewportWidth = ClampFloatRange(camera.viewportWidth, 0.01f, 1.0f);
    camera.viewportHeight = ClampFloatRange(camera.viewportHeight, 0.01f, 1.0f);

    if (camera.viewportX + camera.viewportWidth > 1.0f)
        camera.viewportWidth = ClampFloatRange(1.0f - camera.viewportX, 0.01f, 1.0f);

    if (camera.viewportY + camera.viewportHeight > 1.0f)
        camera.viewportHeight = ClampFloatRange(1.0f - camera.viewportY, 0.01f, 1.0f);
}

static TransformComponent* EnsureTransformComponent(Scene* scene, entt::entity entity)
{
    if (!scene)
        return nullptr;

    if (TransformComponent* existing = scene->TryGetTransform(entity))
        return existing;

    auto& created = scene->AddTransform(entity);
    created.x = 0.0f;
    created.y = 0.0f;
    created.width = 100.0f;
    created.height = 100.0f;
    return &created;
}

static void ResolveCameraPosition(const Scene* scene, entt::entity entity, const CameraComponent& camera, float& outX, float& outY)
{
    outX = camera.x;
    outY = camera.y;

    if (!scene)
        return;

    if (const TransformComponent* transform = scene->TryGetTransform(entity))
    {
        outX = transform->x;
        outY = transform->y;
    }
}

static void ApplyCameraPosition(Scene* scene, entt::entity entity, CameraComponent& camera, float x, float y)
{
    camera.x = x;
    camera.y = y;

    TransformComponent* transform = EnsureTransformComponent(scene, entity);
    if (transform)
    {
        transform->x = x;
        transform->y = y;
    }
}

static std::uint32_t EntityManager_CreateEntityInternal()
{
    Scene* scene = GetSceneContextForEntityApi();
    if (!scene)
        return static_cast<std::uint32_t>(entt::null);

    const auto entity = scene->CreateEntity();
    return static_cast<std::uint32_t>(entt::to_integral(entity));
}

static void EntityManager_DestroyEntityInternal(std::uint32_t entityId)
{
    Scene* scene = GetSceneContextForEntityApi();
    if (!scene)
        return;

    const auto entity = scene->FromEntityId(entityId);
    if (!scene->IsValid(entity))
        return;

    scene->DestroyEntity(entity);
}

static bool EntityManager_IsEntityValidInternal(std::uint32_t entityId)
{
    Scene* scene = GetSceneContextForEntityApi();
    if (!scene)
        return false;

    return scene->IsValid(scene->FromEntityId(entityId));
}

static int EntityManager_GetEntityCountInternal()
{
    Scene* scene = GetSceneContextForEntityApi();
    return scene ? static_cast<int>(scene->EntityCount()) : 0;
}

static std::uint32_t EntityManager_GetEntityIdAtIndexInternal(int index)
{
    Scene* scene = GetSceneContextForEntityApi();
    if (!scene || index < 0)
        return static_cast<std::uint32_t>(entt::null);

    auto& registry = scene->Registry();
    auto entities = registry.view<entt::entity>();
    int current = 0;
    for (const auto entity : entities)
    {
        if (current == index)
            return scene->ToEntityId(entity);
        ++current;
    }

    return static_cast<std::uint32_t>(entt::null);
}

static MonoString* EntityManager_GetEntityNameInternal(std::uint32_t entityId)
{
    Scene* scene = GetSceneContextForEntityApi();
    if (!scene)
        return nullptr;

    const auto entity = scene->FromEntityId(entityId);
    if (!scene->IsValid(entity))
        return nullptr;

    const EntityMetadataComponent* metadata = scene->TryGetMetadata(entity);
    if (!metadata)
        return nullptr;

    MonoDomain* domain = mono_domain_get();
    return domain ? mono_string_new(domain, metadata->name.c_str()) : nullptr;
}

static void EntityManager_SetEntityNameInternal(std::uint32_t entityId, MonoString* value)
{
    Scene* scene = GetSceneContextForEntityApi();
    if (!scene)
        return;

    const auto entity = scene->FromEntityId(entityId);
    if (!scene->IsValid(entity))
        return;

    EntityMetadataComponent* metadata = scene->TryGetMetadata(entity);
    if (!metadata)
        metadata = &scene->AddMetadata(entity);

    std::string name = MonoStringToUtf8(value);
    if (name.empty())
        name = "Entity " + std::to_string(scene->ToEntityId(entity));

    metadata->name = name;
}

static bool EntityManager_GetEntityActiveInternal(std::uint32_t entityId)
{
    Scene* scene = GetSceneContextForEntityApi();
    if (!scene)
        return false;

    const auto entity = scene->FromEntityId(entityId);
    const EntityMetadataComponent* metadata = scene->TryGetMetadata(entity);
    return metadata ? metadata->active : false;
}

static void EntityManager_SetEntityActiveInternal(std::uint32_t entityId, bool value)
{
    Scene* scene = GetSceneContextForEntityApi();
    if (!scene)
        return;

    const auto entity = scene->FromEntityId(entityId);
    if (!scene->IsValid(entity))
        return;

    EntityMetadataComponent* metadata = scene->TryGetMetadata(entity);
    if (!metadata)
        metadata = &scene->AddMetadata(entity);

    metadata->active = value;
}

static bool EntityManager_HasComponentInternal(std::uint32_t entityId, int componentType)
{
    Scene* scene = GetSceneContextForEntityApi();
    if (!scene)
        return false;

    const auto entity = scene->FromEntityId(entityId);
    if (!scene->IsValid(entity))
        return false;

    switch (static_cast<EditorComponentType>(componentType))
    {
    case EditorComponentType::Transform:
        return scene->HasTransform(entity);
    case EditorComponentType::Camera:
        return scene->HasCamera(entity);
    case EditorComponentType::Sprite:
        return scene->HasSprite(entity);
    case EditorComponentType::Script:
        return scene->HasScript(entity);
    default:
        return false;
    }
}

static void EntityManager_AddComponentInternal(std::uint32_t entityId, int componentType)
{
    Scene* scene = GetSceneContextForEntityApi();
    if (!scene)
        return;

    const auto entity = scene->FromEntityId(entityId);
    if (!scene->IsValid(entity))
        return;

    switch (static_cast<EditorComponentType>(componentType))
    {
    case EditorComponentType::Transform:
        if (scene->HasTransform(entity))
            return;
        {
            auto& transform = scene->AddTransform(entity);
            transform.x = 0.0f;
            transform.y = 0.0f;
            transform.width = 100.0f;
            transform.height = 100.0f;
        }
        return;
    case EditorComponentType::Camera:
        if (scene->HasCamera(entity))
            return;
        {
            auto& camera = scene->AddCamera(entity);
            TransformComponent* transform = EnsureTransformComponent(scene, entity);
            camera.x = transform ? transform->x : 0.0f;
            camera.y = transform ? transform->y : 0.0f;
            camera.zoom = 1.0f;
            camera.orthographicSize = 0.0f;
            camera.enabled = true;
            camera.primary = true;
            camera.clearColor = true;
            camera.backgroundColor = 0x14141AFFu;
            camera.cullingMask = 0xFFFFFFFFu;
            camera.viewportX = 0.0f;
            camera.viewportY = 0.0f;
            camera.viewportWidth = 1.0f;
            camera.viewportHeight = 1.0f;
            NormalizeCameraValues(camera);
        }
        return;
    case EditorComponentType::Sprite:
        if (scene->HasSprite(entity))
            return;
        scene->AddSprite(entity);
        return;
    case EditorComponentType::Script:
        if (scene->HasScript(entity))
            return;
        scene->AddScript(entity);
        return;
    default:
        return;
    }
}

static void EntityManager_RemoveComponentInternal(std::uint32_t entityId, int componentType)
{
    Scene* scene = GetSceneContextForEntityApi();
    if (!scene)
        return;

    const auto entity = scene->FromEntityId(entityId);
    if (!scene->IsValid(entity))
        return;

    switch (static_cast<EditorComponentType>(componentType))
    {
    case EditorComponentType::Transform:
        scene->RemoveTransform(entity);
        return;
    case EditorComponentType::Camera:
        scene->RemoveCamera(entity);
        return;
    case EditorComponentType::Sprite:
        scene->RemoveSprite(entity);
        return;
    case EditorComponentType::Script:
        scene->RemoveScript(entity);
        return;
    default:
        return;
    }
}

static bool EntityManager_HasTransformInternal(std::uint32_t entityId)
{
    Scene* scene = GetSceneContextForEntityApi();
    if (!scene)
        return false;

    const auto entity = scene->FromEntityId(entityId);
    return scene->HasTransform(entity);
}

static void EntityManager_AddTransformInternal(std::uint32_t entityId)
{
    EntityManager_AddComponentInternal(entityId, static_cast<int>(EditorComponentType::Transform));
}

static bool EntityManager_GetTransformInternal(std::uint32_t entityId, float* x, float* y, float* width, float* height)
{
    Scene* scene = GetSceneContextForEntityApi();
    if (!scene)
        return false;

    const auto entity = scene->FromEntityId(entityId);
    const TransformComponent* transform = scene->TryGetTransform(entity);
    if (!transform)
        return false;

    if (x)
        *x = transform->x;
    if (y)
        *y = transform->y;
    if (width)
        *width = transform->width;
    if (height)
        *height = transform->height;
    return true;
}

static void EntityManager_SetTransformInternal(std::uint32_t entityId, float x, float y, float width, float height)
{
    Scene* scene = GetSceneContextForEntityApi();
    if (!scene)
        return;

    const auto entity = scene->FromEntityId(entityId);
    TransformComponent* transform = scene->TryGetTransform(entity);
    if (!transform)
        return;

    transform->x = x;
    transform->y = y;
    transform->width = width;
    transform->height = height;
}

static bool EntityManager_HasCameraInternal(std::uint32_t entityId)
{
    return EntityManager_HasComponentInternal(entityId, static_cast<int>(EditorComponentType::Camera));
}

static void EntityManager_AddCameraInternal(std::uint32_t entityId)
{
    EntityManager_AddComponentInternal(entityId, static_cast<int>(EditorComponentType::Camera));
}

static bool EntityManager_GetCameraInternal(std::uint32_t entityId, float* x, float* y, float* zoom)
{
    Scene* scene = GetSceneContextForEntityApi();
    if (!scene)
        return false;

    const auto entity = scene->FromEntityId(entityId);
    CameraComponent* camera = scene->TryGetCamera(entity);
    if (!camera)
        return false;

    NormalizeCameraValues(*camera);

    float resolvedX = camera->x;
    float resolvedY = camera->y;
    ResolveCameraPosition(scene, entity, *camera, resolvedX, resolvedY);
    if (x)
        *x = resolvedX;
    if (y)
        *y = resolvedY;
    if (zoom)
        *zoom = camera->zoom;
    return true;
}

static void EntityManager_SetCameraInternal(std::uint32_t entityId, float x, float y, float zoom)
{
    Scene* scene = GetSceneContextForEntityApi();
    if (!scene)
        return;

    const auto entity = scene->FromEntityId(entityId);
    CameraComponent* camera = scene->TryGetCamera(entity);
    if (!camera)
        return;

    ApplyCameraPosition(scene, entity, *camera, x, y);
    camera->zoom = zoom;
    NormalizeCameraValues(*camera);
}

static bool EntityManager_GetCameraSettingsInternal(std::uint32_t entityId,
                                                    float* x,
                                                    float* y,
                                                    float* zoom,
                                                    bool* enabled,
                                                    bool* primary,
                                                    bool* clearColor,
                                                    std::uint32_t* backgroundColor,
                                                    std::uint32_t* cullingMask,
                                                    float* viewportX,
                                                    float* viewportY,
                                                    float* viewportWidth,
                                                    float* viewportHeight)
{
    Scene* scene = GetSceneContextForEntityApi();
    if (!scene)
        return false;

    const auto entity = scene->FromEntityId(entityId);
    CameraComponent* camera = scene->TryGetCamera(entity);
    if (!camera)
        return false;

    NormalizeCameraValues(*camera);

    float resolvedX = camera->x;
    float resolvedY = camera->y;
    ResolveCameraPosition(scene, entity, *camera, resolvedX, resolvedY);
    if (x)
        *x = resolvedX;
    if (y)
        *y = resolvedY;
    if (zoom)
        *zoom = camera->zoom;
    if (enabled)
        *enabled = camera->enabled;
    if (primary)
        *primary = camera->primary;
    if (clearColor)
        *clearColor = camera->clearColor;
    if (backgroundColor)
        *backgroundColor = camera->backgroundColor;
    if (cullingMask)
        *cullingMask = camera->cullingMask;
    if (viewportX)
        *viewportX = camera->viewportX;
    if (viewportY)
        *viewportY = camera->viewportY;
    if (viewportWidth)
        *viewportWidth = camera->viewportWidth;
    if (viewportHeight)
        *viewportHeight = camera->viewportHeight;

    return true;
}

static void EntityManager_SetCameraSettingsInternal(std::uint32_t entityId,
                                                    float x,
                                                    float y,
                                                    float zoom,
                                                    bool enabled,
                                                    bool primary,
                                                    bool clearColor,
                                                    std::uint32_t backgroundColor,
                                                    std::uint32_t cullingMask,
                                                    float viewportX,
                                                    float viewportY,
                                                    float viewportWidth,
                                                    float viewportHeight)
{
    Scene* scene = GetSceneContextForEntityApi();
    if (!scene)
        return;

    const auto entity = scene->FromEntityId(entityId);
    CameraComponent* camera = scene->TryGetCamera(entity);
    if (!camera)
        return;

    ApplyCameraPosition(scene, entity, *camera, x, y);
    camera->zoom = zoom;
    camera->enabled = enabled;
    camera->primary = primary;
    camera->clearColor = clearColor;
    camera->backgroundColor = backgroundColor;
    camera->cullingMask = cullingMask;
    camera->viewportX = viewportX;
    camera->viewportY = viewportY;
    camera->viewportWidth = viewportWidth;
    camera->viewportHeight = viewportHeight;
    NormalizeCameraValues(*camera);
}

static bool EntityManager_GetCameraSettingsV2Internal(std::uint32_t entityId,
                                                      float* x,
                                                      float* y,
                                                      float* zoom,
                                                      bool* enabled,
                                                      bool* primary,
                                                      bool* clearColor,
                                                      std::uint32_t* backgroundColor,
                                                      std::uint32_t* cullingMask,
                                                      float* viewportX,
                                                      float* viewportY,
                                                      float* viewportWidth,
                                                      float* viewportHeight,
                                                      float* orthographicSize)
{
    Scene* scene = GetSceneContextForEntityApi();
    if (!scene)
        return false;

    const auto entity = scene->FromEntityId(entityId);
    CameraComponent* camera = scene->TryGetCamera(entity);
    if (!camera)
        return false;

    NormalizeCameraValues(*camera);

    float resolvedX = camera->x;
    float resolvedY = camera->y;
    ResolveCameraPosition(scene, entity, *camera, resolvedX, resolvedY);
    if (x)
        *x = resolvedX;
    if (y)
        *y = resolvedY;
    if (zoom)
        *zoom = camera->zoom;
    if (enabled)
        *enabled = camera->enabled;
    if (primary)
        *primary = camera->primary;
    if (clearColor)
        *clearColor = camera->clearColor;
    if (backgroundColor)
        *backgroundColor = camera->backgroundColor;
    if (cullingMask)
        *cullingMask = camera->cullingMask;
    if (viewportX)
        *viewportX = camera->viewportX;
    if (viewportY)
        *viewportY = camera->viewportY;
    if (viewportWidth)
        *viewportWidth = camera->viewportWidth;
    if (viewportHeight)
        *viewportHeight = camera->viewportHeight;
    if (orthographicSize)
        *orthographicSize = camera->orthographicSize;

    return true;
}

static void EntityManager_SetCameraSettingsV2Internal(std::uint32_t entityId,
                                                      float x,
                                                      float y,
                                                      float zoom,
                                                      bool enabled,
                                                      bool primary,
                                                      bool clearColor,
                                                      std::uint32_t backgroundColor,
                                                      std::uint32_t cullingMask,
                                                      float viewportX,
                                                      float viewportY,
                                                      float viewportWidth,
                                                      float viewportHeight,
                                                      float orthographicSize)
{
    Scene* scene = GetSceneContextForEntityApi();
    if (!scene)
        return;

    const auto entity = scene->FromEntityId(entityId);
    CameraComponent* camera = scene->TryGetCamera(entity);
    if (!camera)
        return;

    ApplyCameraPosition(scene, entity, *camera, x, y);
    camera->zoom = zoom;
    camera->enabled = enabled;
    camera->primary = primary;
    camera->clearColor = clearColor;
    camera->backgroundColor = backgroundColor;
    camera->cullingMask = cullingMask;
    camera->viewportX = viewportX;
    camera->viewportY = viewportY;
    camera->viewportWidth = viewportWidth;
    camera->viewportHeight = viewportHeight;
    camera->orthographicSize = orthographicSize;
    NormalizeCameraValues(*camera);
}

static void EntityManager_RemoveCameraInternal(std::uint32_t entityId)
{
    EntityManager_RemoveComponentInternal(entityId, static_cast<int>(EditorComponentType::Camera));
}

static bool EntityManager_HasSpriteInternal(std::uint32_t entityId)
{
    return EntityManager_HasComponentInternal(entityId, static_cast<int>(EditorComponentType::Sprite));
}

static void EntityManager_AddSpriteInternal(std::uint32_t entityId)
{
    EntityManager_AddComponentInternal(entityId, static_cast<int>(EditorComponentType::Sprite));
}

static void EntityManager_RemoveSpriteInternal(std::uint32_t entityId)
{
    EntityManager_RemoveComponentInternal(entityId, static_cast<int>(EditorComponentType::Sprite));
}

static bool EntityManager_HasScriptInternal(std::uint32_t entityId)
{
    return EntityManager_HasComponentInternal(entityId, static_cast<int>(EditorComponentType::Script));
}

static void EntityManager_AddScriptInternal(std::uint32_t entityId)
{
    EntityManager_AddComponentInternal(entityId, static_cast<int>(EditorComponentType::Script));
}

static void EntityManager_RemoveScriptInternal(std::uint32_t entityId)
{
    EntityManager_RemoveComponentInternal(entityId, static_cast<int>(EditorComponentType::Script));
}

static bool RuntimeSprite_Has(std::uint32_t entityId)
{
    Scene* scene = GetSceneContextForSpriteApi();
    if (!scene)
        return false;

    const auto entity = scene->FromEntityId(entityId);
    return scene->HasSprite(entity);
}

static void RuntimeSprite_Add(std::uint32_t entityId)
{
    Scene* scene = GetSceneContextForSpriteApi();
    if (!scene)
        return;

    const auto entity = scene->FromEntityId(entityId);
    if (!scene->IsValid(entity) || scene->HasSprite(entity))
        return;

    scene->AddSprite(entity);
}

static void RuntimeSprite_Remove(std::uint32_t entityId)
{
    Scene* scene = GetSceneContextForSpriteApi();
    if (!scene)
        return;

    const auto entity = scene->FromEntityId(entityId);
    scene->RemoveSprite(entity);
}

static MonoString* RuntimeSprite_GetTexturePath(std::uint32_t entityId)
{
    Scene* scene = GetSceneContextForSpriteApi();
    if (!scene)
        return nullptr;

    const auto entity = scene->FromEntityId(entityId);
    const SpriteComponent* sprite = scene->TryGetSprite(entity);
    if (!sprite)
        return nullptr;

    MonoDomain* domain = mono_domain_get();
    return domain ? mono_string_new(domain, sprite->textureAssetPath.c_str()) : nullptr;
}

static void RuntimeSprite_SetTexturePath(std::uint32_t entityId, MonoString* texturePath)
{
    Scene* scene = GetSceneContextForSpriteApi();
    if (!scene)
        return;

    const auto entity = scene->FromEntityId(entityId);
    SpriteComponent* sprite = scene->TryGetSprite(entity);
    if (!sprite)
        return;

    sprite->textureAssetPath = MonoStringToUtf8(texturePath);
    sprite->textureAssetHandle = 0;
    sprite->texture = nullptr;
}

static std::uint32_t RuntimeSprite_GetFallbackColor(std::uint32_t entityId)
{
    Scene* scene = GetSceneContextForSpriteApi();
    if (!scene)
        return 0xFFFFFFFFu;

    const auto entity = scene->FromEntityId(entityId);
    const SpriteComponent* sprite = scene->TryGetSprite(entity);
    if (!sprite)
        return 0xFFFFFFFFu;

    return sprite->fallbackColor;
}

static void RuntimeSprite_SetFallbackColor(std::uint32_t entityId, std::uint32_t color)
{
    Scene* scene = GetSceneContextForSpriteApi();
    if (!scene)
        return;

    const auto entity = scene->FromEntityId(entityId);
    SpriteComponent* sprite = scene->TryGetSprite(entity);
    if (!sprite)
        return;

    sprite->fallbackColor = color;
}

static bool RuntimeSprite_GetSettings(std::uint32_t entityId,
                                      bool* centered,
                                      float* offsetX,
                                      float* offsetY,
                                      bool* flipH,
                                      bool* flipV,
                                      std::uint32_t* hframes,
                                      std::uint32_t* vframes,
                                      std::uint32_t* frame,
                                      bool* regionEnabled,
                                      float* regionX,
                                      float* regionY,
                                      float* regionWidth,
                                      float* regionHeight)
{
    Scene* scene = GetSceneContextForSpriteApi();
    if (!scene)
        return false;

    const auto entity = scene->FromEntityId(entityId);
    const SpriteComponent* sprite = scene->TryGetSprite(entity);
    if (!sprite)
        return false;

    if (centered)
        *centered = sprite->centered;
    if (offsetX)
        *offsetX = sprite->offsetX;
    if (offsetY)
        *offsetY = sprite->offsetY;
    if (flipH)
        *flipH = sprite->flipH;
    if (flipV)
        *flipV = sprite->flipV;
    if (hframes)
        *hframes = sprite->hframes;
    if (vframes)
        *vframes = sprite->vframes;
    if (frame)
        *frame = sprite->frame;
    if (regionEnabled)
        *regionEnabled = sprite->regionEnabled;
    if (regionX)
        *regionX = sprite->regionX;
    if (regionY)
        *regionY = sprite->regionY;
    if (regionWidth)
        *regionWidth = sprite->regionWidth;
    if (regionHeight)
        *regionHeight = sprite->regionHeight;

    return true;
}

static void RuntimeSprite_SetSettings(std::uint32_t entityId,
                                      bool centered,
                                      float offsetX,
                                      float offsetY,
                                      bool flipH,
                                      bool flipV,
                                      std::uint32_t hframes,
                                      std::uint32_t vframes,
                                      std::uint32_t frame,
                                      bool regionEnabled,
                                      float regionX,
                                      float regionY,
                                      float regionWidth,
                                      float regionHeight)
{
    Scene* scene = GetSceneContextForSpriteApi();
    if (!scene)
        return;

    const auto entity = scene->FromEntityId(entityId);
    SpriteComponent* sprite = scene->TryGetSprite(entity);
    if (!sprite)
        return;

    if (hframes < 1)
        hframes = 1;
    if (vframes < 1)
        vframes = 1;

    std::uint64_t frameCount = static_cast<std::uint64_t>(hframes) * static_cast<std::uint64_t>(vframes);
    if (frameCount == 0)
    {
        hframes = 1;
        vframes = 1;
        frame = 0;
    }
    else if (frame >= frameCount)
    {
        frame = static_cast<std::uint32_t>(frameCount - 1);
    }

    if (regionWidth < 0.0f)
        regionWidth = 0.0f;
    if (regionHeight < 0.0f)
        regionHeight = 0.0f;

    sprite->centered = centered;
    sprite->offsetX = offsetX;
    sprite->offsetY = offsetY;
    sprite->flipH = flipH;
    sprite->flipV = flipV;
    sprite->hframes = hframes;
    sprite->vframes = vframes;
    sprite->frame = frame;
    sprite->regionEnabled = regionEnabled;
    sprite->regionX = regionX;
    sprite->regionY = regionY;
    sprite->regionWidth = regionWidth;
    sprite->regionHeight = regionHeight;
}

static void RuntimeGlm_Vec2Add(float ax, float ay, float bx, float by, float* rx, float* ry)
{
    const glm::vec2 result = glm::vec2(ax, ay) + glm::vec2(bx, by);
    if (rx)
        *rx = result.x;
    if (ry)
        *ry = result.y;
}

static void RuntimeGlm_Vec2Sub(float ax, float ay, float bx, float by, float* rx, float* ry)
{
    const glm::vec2 result = glm::vec2(ax, ay) - glm::vec2(bx, by);
    if (rx)
        *rx = result.x;
    if (ry)
        *ry = result.y;
}

static void RuntimeGlm_Vec2Scale(float x, float y, float scale, float* rx, float* ry)
{
    const glm::vec2 result = glm::vec2(x, y) * scale;
    if (rx)
        *rx = result.x;
    if (ry)
        *ry = result.y;
}

static float RuntimeGlm_Vec2Length(float x, float y)
{
    return glm::length(glm::vec2(x, y));
}

static float RuntimeGlm_Vec2Dot(float ax, float ay, float bx, float by)
{
    return glm::dot(glm::vec2(ax, ay), glm::vec2(bx, by));
}

static void RuntimeGlm_Vec2Normalize(float x, float y, float* rx, float* ry)
{
    const glm::vec2 input(x, y);
    const float len = glm::length(input);
    const glm::vec2 result = (len > 0.0f) ? (input / len) : glm::vec2(0.0f, 0.0f);
    if (rx)
        *rx = result.x;
    if (ry)
        *ry = result.y;
}

static void RuntimeGlm_Vec3Add(float ax, float ay, float az,
                               float bx, float by, float bz,
                               float* rx, float* ry, float* rz)
{
    const glm::vec3 result = glm::vec3(ax, ay, az) + glm::vec3(bx, by, bz);
    if (rx)
        *rx = result.x;
    if (ry)
        *ry = result.y;
    if (rz)
        *rz = result.z;
}

static void RuntimeGlm_Vec3Sub(float ax, float ay, float az,
                               float bx, float by, float bz,
                               float* rx, float* ry, float* rz)
{
    const glm::vec3 result = glm::vec3(ax, ay, az) - glm::vec3(bx, by, bz);
    if (rx)
        *rx = result.x;
    if (ry)
        *ry = result.y;
    if (rz)
        *rz = result.z;
}

static void RuntimeGlm_Vec3Scale(float x, float y, float z, float scale,
                                 float* rx, float* ry, float* rz)
{
    const glm::vec3 result = glm::vec3(x, y, z) * scale;
    if (rx)
        *rx = result.x;
    if (ry)
        *ry = result.y;
    if (rz)
        *rz = result.z;
}

static float RuntimeGlm_Vec3Length(float x, float y, float z)
{
    return glm::length(glm::vec3(x, y, z));
}

static float RuntimeGlm_Vec3Dot(float ax, float ay, float az,
                                float bx, float by, float bz)
{
    return glm::dot(glm::vec3(ax, ay, az), glm::vec3(bx, by, bz));
}

static void RuntimeGlm_Vec3Cross(float ax, float ay, float az,
                                 float bx, float by, float bz,
                                 float* rx, float* ry, float* rz)
{
    const glm::vec3 result = glm::cross(glm::vec3(ax, ay, az), glm::vec3(bx, by, bz));
    if (rx)
        *rx = result.x;
    if (ry)
        *ry = result.y;
    if (rz)
        *rz = result.z;
}

static void RuntimeGlm_Vec3Normalize(float x, float y, float z, float* rx, float* ry, float* rz)
{
    const glm::vec3 input(x, y, z);
    const float len = glm::length(input);
    const glm::vec3 result = (len > 0.0f) ? (input / len) : glm::vec3(0.0f, 0.0f, 0.0f);
    if (rx)
        *rx = result.x;
    if (ry)
        *ry = result.y;
    if (rz)
        *rz = result.z;
}

static int EditorBridge_GetEntityCount()
{
    if (!g_editorSceneContext)
        return 0;

    return static_cast<int>(g_editorSceneContext->EntityCount());
}

static std::uint32_t EditorBridge_GetEntityIdAtIndex(int index)
{
    if (!g_editorSceneContext || index < 0)
        return static_cast<std::uint32_t>(entt::null);

    auto& registry = g_editorSceneContext->Registry();
    auto entities = registry.view<entt::entity>();

    int current = 0;
    for (const auto entity : entities)
    {
        if (current == index)
            return static_cast<std::uint32_t>(entt::to_integral(entity));
        ++current;
    }

    return static_cast<std::uint32_t>(entt::null);
}

static bool EditorBridge_IsEntityValid(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return false;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    return g_editorSceneContext->IsValid(entity);
}

static EntityMetadataComponent* EditorBridge_GetMetadata(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return nullptr;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    if (!g_editorSceneContext->IsValid(entity))
        return nullptr;

    return g_editorSceneContext->TryGetMetadata(entity);
}

static const EntityMetadataComponent* EditorBridge_GetMetadataConst(std::uint32_t entityId)
{
    return EditorBridge_GetMetadata(entityId);
}

static std::string TrimWhitespace(const std::string& value)
{
    const std::string whitespace = " \t\r\n";
    const std::size_t start = value.find_first_not_of(whitespace);
    if (start == std::string::npos)
        return {};

    const std::size_t end = value.find_last_not_of(whitespace);
    return value.substr(start, end - start + 1);
}

static MonoString* EditorBridge_GetEntityName(std::uint32_t entityId)
{
    const EntityMetadataComponent* metadata = EditorBridge_GetMetadataConst(entityId);
    if (!metadata)
        return nullptr;

    MonoDomain* domain = mono_domain_get();
    return domain ? mono_string_new(domain, metadata->name.c_str()) : nullptr;
}

static void EditorBridge_SetEntityName(std::uint32_t entityId, MonoString* value)
{
    EntityMetadataComponent* metadata = EditorBridge_GetMetadata(entityId);
    if (!metadata)
        return;

    std::string name = TrimWhitespace(MonoStringToUtf8(value));
    if (name.empty())
        name = "Entity " + std::to_string(metadata->sceneEntityId);

    metadata->name = name;
}

static MonoString* EditorBridge_GetEntityTag(std::uint32_t entityId)
{
    const EntityMetadataComponent* metadata = EditorBridge_GetMetadataConst(entityId);
    if (!metadata)
        return nullptr;

    MonoDomain* domain = mono_domain_get();
    return domain ? mono_string_new(domain, metadata->tag.c_str()) : nullptr;
}

static void EditorBridge_SetEntityTag(std::uint32_t entityId, MonoString* value)
{
    EntityMetadataComponent* metadata = EditorBridge_GetMetadata(entityId);
    if (!metadata)
        return;

    std::string tag = TrimWhitespace(MonoStringToUtf8(value));
    if (tag.empty())
        tag = "Untagged";

    metadata->tag = tag;
}

static std::uint32_t EditorBridge_GetEntityLayer(std::uint32_t entityId)
{
    const EntityMetadataComponent* metadata = EditorBridge_GetMetadataConst(entityId);
    if (!metadata)
        return 0;

    return metadata->layer > 31 ? 31 : metadata->layer;
}

static void EditorBridge_SetEntityLayer(std::uint32_t entityId, std::uint32_t value)
{
    EntityMetadataComponent* metadata = EditorBridge_GetMetadata(entityId);
    if (!metadata)
        return;

    metadata->layer = value > 31 ? 31 : value;
}

static bool EditorBridge_GetEntityStatic(std::uint32_t entityId)
{
    const EntityMetadataComponent* metadata = EditorBridge_GetMetadataConst(entityId);
    if (!metadata)
        return false;

    return metadata->isStatic;
}

static void EditorBridge_SetEntityStatic(std::uint32_t entityId, bool value)
{
    EntityMetadataComponent* metadata = EditorBridge_GetMetadata(entityId);
    if (!metadata)
        return;

    metadata->isStatic = value;
}

static bool EditorBridge_GetEntityActive(std::uint32_t entityId)
{
    const EntityMetadataComponent* metadata = EditorBridge_GetMetadataConst(entityId);
    if (!metadata)
        return false;

    return metadata->active;
}

static void EditorBridge_SetEntityActive(std::uint32_t entityId, bool value)
{
    EntityMetadataComponent* metadata = EditorBridge_GetMetadata(entityId);
    if (!metadata)
        return;

    metadata->active = value;
}

static std::uint32_t EditorBridge_CreateEntity()
{
    if (!g_editorSceneContext)
        return static_cast<std::uint32_t>(entt::null);

    const auto entity = g_editorSceneContext->CreateEntity();
    return static_cast<std::uint32_t>(entt::to_integral(entity));
}

static void EditorBridge_DestroyEntity(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return;

    g_editorSceneContext->DestroyEntity(g_editorSceneContext->FromEntityId(entityId));
}

static void EditorBridge_NewScene()
{
    if (!g_editorSceneContext)
    {
        g_editorSceneIoStatus = "New scene failed: scene context unavailable.";
        return;
    }

    g_editorSceneContext->Clear();
    g_editorSceneIoStatus = "Created new scene.";
}

static bool EditorBridge_SaveScene(MonoString* scenePath, int storageFormat)
{
    if (!g_editorSceneContext)
    {
        g_editorSceneIoStatus = "Save failed: scene context unavailable.";
        return false;
    }

    const std::string pathUtf8 = MonoStringToUtf8(scenePath);
    if (pathUtf8.empty())
    {
        g_editorSceneIoStatus = "Save failed: scene path is empty.";
        return false;
    }

    const std::filesystem::path resolvedPath = std::filesystem::path(pathUtf8).lexically_normal();
    const Scene::SceneFileFormat format = (storageFormat == 1)
        ? Scene::SceneFileFormat::Binary
        : Scene::SceneFileFormat::Json;

    if (!g_editorSceneContext->SaveToFile(resolvedPath, format))
    {
        g_editorSceneIoStatus = g_editorSceneContext->GetLastIoError();
        if (g_editorSceneIoStatus.empty())
            g_editorSceneIoStatus = "Save failed: " + resolvedPath.string();
        return false;
    }

    g_editorSceneIoStatus = "Saved scene: " + resolvedPath.string();
    return true;
}

static bool EditorBridge_LoadScene(MonoString* scenePath, int storageFormat)
{
    if (!g_editorSceneContext)
    {
        g_editorSceneIoStatus = "Load failed: scene context unavailable.";
        return false;
    }

    const std::string pathUtf8 = MonoStringToUtf8(scenePath);
    if (pathUtf8.empty())
    {
        g_editorSceneIoStatus = "Load failed: scene path is empty.";
        return false;
    }

    const std::filesystem::path resolvedPath = std::filesystem::path(pathUtf8).lexically_normal();
    const Scene::SceneFileFormat format = (storageFormat == 1)
        ? Scene::SceneFileFormat::Binary
        : Scene::SceneFileFormat::Json;

    if (!g_editorSceneContext->LoadFromFile(resolvedPath, format))
    {
        g_editorSceneIoStatus = g_editorSceneContext->GetLastIoError();
        if (g_editorSceneIoStatus.empty())
            g_editorSceneIoStatus = "Load failed: " + resolvedPath.string();
        return false;
    }

    g_editorSceneIoStatus = "Loaded scene: " + resolvedPath.string();
    return true;
}

static MonoString* EditorBridge_GetLastSceneIoStatus()
{
    MonoDomain* domain = mono_domain_get();
    return domain ? mono_string_new(domain, g_editorSceneIoStatus.c_str()) : nullptr;
}

static int EditorBridge_GetSimulationState()
{
    if (!g_monoRuntimeImplForEditorBridge)
        return static_cast<int>(MonoRuntime::SimulationState::Edit);

    if (!g_monoRuntimeImplForEditorBridge->editorMode)
        return static_cast<int>(MonoRuntime::SimulationState::Play);

    return static_cast<int>(g_monoRuntimeImplForEditorBridge->simulationState);
}

static bool EditorBridge_StartPlayMode()
{
    if (!g_monoRuntimeImplForEditorBridge)
    {
        g_editorSceneIoStatus = "Play failed: runtime context unavailable.";
        return false;
    }

    // IMPORTANT: We must NOT call MonoRuntime_ReloadScriptAssemblyIfNeeded here!
    // This function is called from managed C# code via an internal call.
    // Reloading the AppDomain would destroy the calling C# stack frame,
    // causing a crash when this function returns.
    // Instead, set a flag and let MonoRuntime::Update() handle the transition
    // from native code where no managed frames are on the stack.
    g_monoRuntimeImplForEditorBridge->playModeRequested = true;
    g_editorSceneIoStatus = "Play mode requested (will start next frame).";
    return true;
}

static void EditorBridge_StopPlayMode()
{
    if (!g_monoRuntimeImplForEditorBridge)
    {
        g_editorSceneIoStatus = "Stop failed: runtime context unavailable.";
        return;
    }

    // Defer stop to C++ Update for the same reason as StartPlayMode:
    // this is called from managed code and StopPlaySession tears down
    // script instances which may invoke managed callbacks.
    g_monoRuntimeImplForEditorBridge->stopModeRequested = true;
    g_editorSceneIoStatus = "Stop mode requested (will stop next frame).";
}

static void EditorBridge_SetSimulationPaused(bool paused)
{
    if (!g_monoRuntimeImplForEditorBridge)
    {
        g_editorSceneIoStatus = "Pause failed: runtime context unavailable.";
        return;
    }

    if (g_monoRuntimeImplForEditorBridge->simulationState == MonoRuntime::SimulationState::Edit)
    {
        g_editorSceneIoStatus = "Pause ignored: simulation is in edit mode.";
        return;
    }

    g_monoRuntimeImplForEditorBridge->simulationState = paused
        ? MonoRuntime::SimulationState::Pause
        : MonoRuntime::SimulationState::Play;

    g_editorSceneIoStatus = paused ? "Paused play mode." : "Resumed play mode.";
}

static void EditorBridge_RequestScriptAssemblyReload()
{
    if (!g_monoRuntimeImplForEditorBridge)
    {
        g_editorSceneIoStatus = "Script reload request ignored: runtime context unavailable.";
        return;
    }

    if (g_monoRuntimeImplForEditorBridge->isReloadingScripts)
    {
        g_editorSceneIoStatus = "Script reload request ignored: reload already in progress.";
        return;
    }

    g_monoRuntimeImplForEditorBridge->scriptReloadRequested = true;
    g_editorSceneIoStatus = "Script assembly reload requested.";
}

static ProjectContext* EditorBridge_GetProjectContext()
{
    if (!g_editorEngineContext)
        return nullptr;

    ProjectContext* projectContext = g_editorEngineContext->GetProjectContext();
    if (!projectContext || !projectContext->IsOpen())
        return nullptr;

    return projectContext;
}

static void EditorBridge_SetScriptAutoReloadEnabled(bool enabled)
{
    if (!g_monoRuntimeImplForEditorBridge)
    {
        g_editorSceneIoStatus = "Set script auto-reload ignored: runtime context unavailable.";
        return;
    }

    g_monoRuntimeImplForEditorBridge->autoScriptReloadEnabled = enabled;

    if (!enabled)
        g_monoRuntimeImplForEditorBridge->scriptReloadRequested = false;
    else if (g_monoRuntimeImplForEditorBridge->scriptReloadDeferredUntilEdit
             && (!g_monoRuntimeImplForEditorBridge->editorMode
                 || g_monoRuntimeImplForEditorBridge->simulationState == MonoRuntime::SimulationState::Edit))
        g_monoRuntimeImplForEditorBridge->scriptReloadRequested = true;

    g_editorSceneIoStatus = enabled
        ? "Enabled automatic script reload."
        : "Disabled automatic script reload.";
}

static MonoString* EditorBridge_GetProjectSetting(MonoString* key, MonoString* fallbackValue)
{
    const std::string settingKey = TrimWhitespace(MonoStringToUtf8(key));
    std::string resolvedValue = MonoStringToUtf8(fallbackValue);

    if (!settingKey.empty())
    {
        if (ProjectContext* projectContext = EditorBridge_GetProjectContext())
        {
            std::string storedValue;
            if (projectContext->GetProjectSetting(settingKey, storedValue))
                resolvedValue = storedValue;
        }
    }

    MonoDomain* domain = mono_domain_get();
    return domain ? mono_string_new(domain, resolvedValue.c_str()) : nullptr;
}

static bool EditorBridge_SetProjectSetting(MonoString* key, MonoString* value)
{
    ProjectContext* projectContext = EditorBridge_GetProjectContext();
    if (!projectContext)
    {
        g_editorSceneIoStatus = "Set project setting failed: project context unavailable.";
        return false;
    }

    const std::string settingKey = TrimWhitespace(MonoStringToUtf8(key));
    if (settingKey.empty())
    {
        g_editorSceneIoStatus = "Set project setting failed: key is empty.";
        return false;
    }

    const std::string settingValue = MonoStringToUtf8(value);
    if (!projectContext->SetProjectSetting(settingKey, settingValue))
    {
        g_editorSceneIoStatus = "Set project setting failed: could not save project metadata.";
        return false;
    }

    g_editorSceneIoStatus = "Updated project setting: " + settingKey + ".";
    return true;
}

static void EditorBridge_SetPreferredScriptAssemblyPath(MonoString* assemblyPath)
{
    if (!g_monoRuntimeImplForEditorBridge)
    {
        g_editorSceneIoStatus = "Set script assembly path ignored: runtime context unavailable.";
        return;
    }

    std::string path = TrimWhitespace(MonoStringToUtf8(assemblyPath));
    if (path.empty())
        g_monoRuntimeImplForEditorBridge->preferredScriptAssemblyPath.clear();
    else
        g_monoRuntimeImplForEditorBridge->preferredScriptAssemblyPath = std::filesystem::path(path).lexically_normal();

    g_monoRuntimeImplForEditorBridge->scriptReloadRequested = true;
    g_editorSceneIoStatus = "Updated preferred script assembly path.";
}

static void EditorBridge_SetPreferredScriptProjectPath(MonoString* projectPath)
{
    if (!g_monoRuntimeImplForEditorBridge)
    {
        g_editorSceneIoStatus = "Set script project path ignored: runtime context unavailable.";
        return;
    }

    std::string path = TrimWhitespace(MonoStringToUtf8(projectPath));
    if (path.empty())
        g_monoRuntimeImplForEditorBridge->preferredScriptProjectPath.clear();
    else
        g_monoRuntimeImplForEditorBridge->preferredScriptProjectPath = std::filesystem::path(path).lexically_normal();

    g_editorSceneIoStatus = "Updated preferred script project path.";
}

static std::uint32_t EditorBridge_CreateAuxiliaryWindow(MonoString* title,
                                                        int width,
                                                        int height,
                                                        bool resizable,
                                                        bool borderless,
                                                        bool alwaysOnTop,
                                                        bool startHidden)
{
    if (!g_editorEngineContext || !g_editorEngineContext->IsEditorMode())
    {
        g_editorSceneIoStatus = "Aux window create failed: editor engine context unavailable.";
        return 0;
    }

    Engine::AuxiliaryWindowDesc desc;
    desc.title = MonoStringToUtf8(title);
    desc.width = width;
    desc.height = height;
    desc.resizable = resizable;
    desc.borderless = borderless;
    desc.alwaysOnTop = alwaysOnTop;
    desc.startHidden = startHidden;

    const Engine::AuxiliaryWindowId id = g_editorEngineContext->CreateAuxiliaryWindow(desc);
    if (id == 0)
        g_editorSceneIoStatus = "Aux window create failed.";
    else
        g_editorSceneIoStatus = "Created auxiliary window.";

    return id;
}

static bool EditorBridge_DestroyAuxiliaryWindow(std::uint32_t id)
{
    if (!g_editorEngineContext || !g_editorEngineContext->IsEditorMode())
        return false;

    const bool ok = g_editorEngineContext->DestroyAuxiliaryWindow(id);
    if (!ok)
        g_editorSceneIoStatus = "Aux window destroy failed.";
    return ok;
}

static void EditorBridge_DestroyAllAuxiliaryWindows()
{
    if (!g_editorEngineContext || !g_editorEngineContext->IsEditorMode())
        return;

    g_editorEngineContext->DestroyAllAuxiliaryWindows();
    g_editorSceneIoStatus = "Destroyed all auxiliary windows.";
}

static bool EditorBridge_ShowAuxiliaryWindow(std::uint32_t id)
{
    if (!g_editorEngineContext || !g_editorEngineContext->IsEditorMode())
        return false;

    return g_editorEngineContext->ShowAuxiliaryWindow(id);
}

static bool EditorBridge_HideAuxiliaryWindow(std::uint32_t id)
{
    if (!g_editorEngineContext || !g_editorEngineContext->IsEditorMode())
        return false;

    return g_editorEngineContext->HideAuxiliaryWindow(id);
}

static bool EditorBridge_SetAuxiliaryWindowTitle(std::uint32_t id, MonoString* title)
{
    if (!g_editorEngineContext || !g_editorEngineContext->IsEditorMode())
        return false;

    return g_editorEngineContext->SetAuxiliaryWindowTitle(id, MonoStringToUtf8(title));
}

static bool EditorBridge_SetAuxiliaryWindowSize(std::uint32_t id, int width, int height)
{
    if (!g_editorEngineContext || !g_editorEngineContext->IsEditorMode())
        return false;

    return g_editorEngineContext->SetAuxiliaryWindowSize(id, width, height);
}

static bool EditorBridge_CenterAuxiliaryWindow(std::uint32_t id)
{
    if (!g_editorEngineContext || !g_editorEngineContext->IsEditorMode())
        return false;

    return g_editorEngineContext->CenterAuxiliaryWindow(id);
}

static int EditorBridge_GetAuxiliaryWindowCount()
{
    if (!g_editorEngineContext || !g_editorEngineContext->IsEditorMode())
        return 0;

    return static_cast<int>(g_editorEngineContext->GetAuxiliaryWindowCount());
}

static int EditorBridge_GetScriptedEntityCount()
{
    if (!g_editorSceneContext)
        return 0;

    auto& registry = g_editorSceneContext->Registry();
    int count = 0;
    auto view = registry.view<ScriptComponent>();
    for (const auto entity : view)
    {
        (void)entity;
        ++count;
    }
    return count;
}

static bool EditorBridge_HasComponent(std::uint32_t entityId, int componentType)
{
    if (!g_editorSceneContext)
        return false;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    if (!g_editorSceneContext->IsValid(entity))
        return false;

    switch (static_cast<EditorComponentType>(componentType))
    {
    case EditorComponentType::Transform:
        return g_editorSceneContext->HasTransform(entity);
    case EditorComponentType::Camera:
        return g_editorSceneContext->HasCamera(entity);
    case EditorComponentType::Sprite:
        return g_editorSceneContext->HasSprite(entity);
    case EditorComponentType::Script:
        return g_editorSceneContext->HasScript(entity);
    default:
        return false;
    }
}

static void EditorBridge_AddComponent(std::uint32_t entityId, int componentType)
{
    if (!g_editorSceneContext)
        return;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    if (!g_editorSceneContext->IsValid(entity))
        return;

    switch (static_cast<EditorComponentType>(componentType))
    {
    case EditorComponentType::Transform:
        if (g_editorSceneContext->HasTransform(entity))
            return;
        {
            auto& transform = g_editorSceneContext->AddTransform(entity);
            transform.x = 0.0f;
            transform.y = 0.0f;
            transform.width = 100.0f;
            transform.height = 100.0f;
        }
        return;
    case EditorComponentType::Camera:
        if (g_editorSceneContext->HasCamera(entity))
            return;
        {
            auto& camera = g_editorSceneContext->AddCamera(entity);
            TransformComponent* transform = EnsureTransformComponent(g_editorSceneContext, entity);
            camera.x = transform ? transform->x : 0.0f;
            camera.y = transform ? transform->y : 0.0f;
            camera.zoom = 1.0f;
            camera.orthographicSize = 0.0f;
            camera.enabled = true;
            camera.primary = true;
            camera.clearColor = true;
            camera.backgroundColor = 0x14141AFFu;
            camera.cullingMask = 0xFFFFFFFFu;
            camera.viewportX = 0.0f;
            camera.viewportY = 0.0f;
            camera.viewportWidth = 1.0f;
            camera.viewportHeight = 1.0f;
            NormalizeCameraValues(camera);
        }
        return;
    case EditorComponentType::Sprite:
        if (g_editorSceneContext->HasSprite(entity))
            return;
        g_editorSceneContext->AddSprite(entity);
        return;
    case EditorComponentType::Script:
        if (g_editorSceneContext->HasScript(entity))
            return;
        g_editorSceneContext->AddScript(entity);
        return;
    default:
        return;
    }
}

static void EditorBridge_RemoveComponent(std::uint32_t entityId, int componentType)
{
    if (!g_editorSceneContext)
        return;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    if (!g_editorSceneContext->IsValid(entity))
        return;

    switch (static_cast<EditorComponentType>(componentType))
    {
    case EditorComponentType::Transform:
        g_editorSceneContext->RemoveTransform(entity);
        return;
    case EditorComponentType::Camera:
        g_editorSceneContext->RemoveCamera(entity);
        return;
    case EditorComponentType::Sprite:
        g_editorSceneContext->RemoveSprite(entity);
        return;
    case EditorComponentType::Script:
        g_editorSceneContext->RemoveScript(entity);
        return;
    default:
        return;
    }
}

static void EditorBridge_SetScriptEnabled(std::uint32_t entityId, bool enabled)
{
    if (!g_editorSceneContext)
        return;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    ScriptComponent* script = g_editorSceneContext->TryGetScript(entity);
    if (!script)
        return;

    script->enabled = enabled;
}

static bool EditorBridge_GetScriptEnabled(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return false;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    const ScriptComponent* script = g_editorSceneContext->TryGetScript(entity);
    if (!script)
        return false;

    return script->enabled;
}

static MonoString* EditorBridge_GetScriptTypeName(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return nullptr;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    const ScriptComponent* script = g_editorSceneContext->TryGetScript(entity);
    if (!script)
        return nullptr;

    std::string fullName;
    if (script->classNamespace.empty())
        fullName = script->className;
    else
        fullName = script->classNamespace + "." + script->className;

    MonoDomain* domain = mono_domain_get();
    return domain ? mono_string_new(domain, fullName.c_str()) : nullptr;
}

static void EditorBridge_SetScriptTypeName(std::uint32_t entityId, MonoString* scriptTypeName)
{
    if (!g_editorSceneContext)
        return;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    ScriptComponent* script = g_editorSceneContext->TryGetScript(entity);
    if (!script)
        return;

    std::string fullName = MonoStringToUtf8(scriptTypeName);
    const std::size_t start = fullName.find_first_not_of(" \t\r\n");
    if (start == std::string::npos)
        return;

    const std::size_t end = fullName.find_last_not_of(" \t\r\n");
    fullName = fullName.substr(start, end - start + 1);

    const std::size_t dotPos = fullName.rfind('.');
    if (dotPos == std::string::npos)
    {
        script->className = fullName;
        if (script->classNamespace.empty())
            script->classNamespace = "GameScripts";
        return;
    }

    const std::string ns = fullName.substr(0, dotPos);
    const std::string className = fullName.substr(dotPos + 1);
    if (className.empty())
        return;

    script->classNamespace = ns.empty() ? "GameScripts" : ns;
    script->className = className;
}

static std::string ScriptFieldStateEscape(const std::string& value)
{
    std::string escaped;
    escaped.reserve(value.size());

    for (char c : value)
    {
        if (c == '\\')
            escaped += "\\\\";
        else if (c == '\n')
            escaped += "\\n";
        else if (c == '\t')
            escaped += "\\t";
        else
            escaped += c;
    }

    return escaped;
}

static std::string ScriptFieldStateUnescape(const std::string& value)
{
    std::string unescaped;
    unescaped.reserve(value.size());

    bool escaped = false;
    for (char c : value)
    {
        if (!escaped)
        {
            if (c == '\\')
            {
                escaped = true;
                continue;
            }

            unescaped += c;
            continue;
        }

        if (c == 'n')
            unescaped += '\n';
        else if (c == 't')
            unescaped += '\t';
        else
            unescaped += c;

        escaped = false;
    }

    if (escaped)
        unescaped += '\\';

    return unescaped;
}

static std::map<std::string, std::string> ParseSerializedScriptFieldState(const std::string& serialized)
{
    std::map<std::string, std::string> fields;
    if (serialized.empty())
        return fields;

    std::size_t lineStart = 0;
    while (lineStart < serialized.size())
    {
        const std::size_t lineEnd = serialized.find('\n', lineStart);
        const std::size_t lineLength = (lineEnd == std::string::npos)
            ? (serialized.size() - lineStart)
            : (lineEnd - lineStart);

        const std::string line = serialized.substr(lineStart, lineLength);
        const std::size_t tabPos = line.find('\t');
        if (tabPos != std::string::npos)
        {
            const std::string key = ScriptFieldStateUnescape(line.substr(0, tabPos));
            const std::string value = ScriptFieldStateUnescape(line.substr(tabPos + 1));
            if (!key.empty())
                fields[key] = value;
        }

        if (lineEnd == std::string::npos)
            break;

        lineStart = lineEnd + 1;
    }

    return fields;
}

static std::string SerializeScriptFieldState(const std::map<std::string, std::string>& fields)
{
    std::string serialized;
    for (const auto& [key, value] : fields)
    {
        serialized += ScriptFieldStateEscape(key);
        serialized += '\t';
        serialized += ScriptFieldStateEscape(value);
        serialized += '\n';
    }

    return serialized;
}

static void UpsertSerializedScriptFieldState(ScriptComponent& script, const std::string& fieldName, const std::string& value)
{
    auto fields = ParseSerializedScriptFieldState(script.serializedFieldState);
    fields[fieldName] = value;
    script.serializedFieldState = SerializeScriptFieldState(fields);
}

static std::optional<std::string> FindSerializedScriptFieldState(const ScriptComponent& script, const std::string& fieldName)
{
    if (fieldName.empty() || script.serializedFieldState.empty())
        return std::nullopt;

    const auto fields = ParseSerializedScriptFieldState(script.serializedFieldState);
    const auto it = fields.find(fieldName);
    if (it == fields.end())
        return std::nullopt;

    return it->second;
}

static std::string ToLowerAscii(std::string value)
{
    for (char& c : value)
        c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
    return value;
}

static MonoClassField* FindFieldByName(MonoClass* klass, const std::string& fieldName)
{
    if (!klass || fieldName.empty())
        return nullptr;

    void* iter = nullptr;
    while (MonoClassField* field = mono_class_get_fields(klass, &iter))
    {
        const char* rawName = mono_field_get_name(field);
        if (!rawName)
            continue;

        if (fieldName == rawName)
            return field;
    }

    return nullptr;
}

static bool IsStringField(MonoClassField* field)
{
    if (!field)
        return false;

    MonoType* fieldType = mono_field_get_type(field);
    if (!fieldType)
        return false;

    return mono_type_get_type(fieldType) == MONO_TYPE_STRING;
}

static MonoClassField* FindAssetPathField(MonoClass* klass)
{
    if (!klass)
        return nullptr;

    static const std::array<const char*, 3> preferredNames = { "path", "assetPath", "filePath" };

    for (const char* preferredName : preferredNames)
    {
        MonoClassField* field = FindFieldByName(klass, preferredName);
        if (IsStringField(field))
            return field;
    }

    void* iter = nullptr;
    while (MonoClassField* field = mono_class_get_fields(klass, &iter))
    {
        if (IsStringField(field))
            return field;
    }

    return nullptr;
}

static MonoObject* FindRuntimeScriptInstance(std::uint32_t entityId)
{
    if (!g_monoRuntimeImplForEditorBridge)
        return nullptr;

    auto it = g_monoRuntimeImplForEditorBridge->entityScripts.find(entityId);
    if (it == g_monoRuntimeImplForEditorBridge->entityScripts.end())
        return nullptr;

    return it->second.instance;
}

static bool ParseInt64(const std::string& text, std::int64_t& value)
{
    if (text.empty())
        return false;

    std::istringstream stream(text);
    stream >> value;
    return !stream.fail() && stream.eof();
}

static bool ParseDoubleValue(const std::string& text, double& value)
{
    if (text.empty())
        return false;

    std::istringstream stream(text);
    stream.imbue(std::locale::classic());
    stream >> value;
    return !stream.fail() && stream.eof();
}

static bool ParseBoolValue(const std::string& text, bool& value)
{
    const std::string lowered = ToLowerAscii(text);
    if (lowered == "true" || lowered == "1")
    {
        value = true;
        return true;
    }

    if (lowered == "false" || lowered == "0")
    {
        value = false;
        return true;
    }

    return false;
}

static std::vector<float> ParseFloatList(const std::string& text)
{
    std::vector<float> result;
    std::size_t cursor = 0;

    while (cursor <= text.size())
    {
        const std::size_t commaPos = text.find(',', cursor);
        const std::size_t tokenLength = (commaPos == std::string::npos)
            ? (text.size() - cursor)
            : (commaPos - cursor);

        std::string token = text.substr(cursor, tokenLength);

        const std::size_t start = token.find_first_not_of(" \t\r\n");
        if (start == std::string::npos)
        {
            if (commaPos == std::string::npos)
                break;
            cursor = commaPos + 1;
            continue;
        }

        const std::size_t end = token.find_last_not_of(" \t\r\n");
        token = token.substr(start, end - start + 1);

        double parsed = 0.0;
        if (!ParseDoubleValue(token, parsed))
            return {};

        result.push_back(static_cast<float>(parsed));

        if (commaPos == std::string::npos)
            break;

        cursor = commaPos + 1;
    }

    return result;
}

static bool SetValueTypeFieldFromText(MonoDomain* domain, MonoObject* instance, MonoClassField* field, MonoClass* fieldClass, const std::string& valueText)
{
    if (!domain || !instance || !field || !fieldClass)
        return false;

    std::map<std::string, MonoClassField*> componentFields;
    void* componentIter = nullptr;
    while (MonoClassField* component = mono_class_get_fields(fieldClass, &componentIter))
    {
        const char* componentName = mono_field_get_name(component);
        if (!componentName)
            continue;

        MonoType* componentType = mono_field_get_type(component);
        if (!componentType || mono_type_get_type(componentType) != MONO_TYPE_R4)
            continue;

        componentFields[ToLowerAscii(componentName)] = component;
    }

    if (componentFields.empty())
        return false;

    MonoObject* boxed = mono_field_get_value_object(domain, field, instance);
    if (!boxed)
    {
        std::uint32_t align = 0;
        const int valueTypeSize = mono_class_value_size(fieldClass, &align);
        if (valueTypeSize <= 0)
            return false;

        std::vector<char> zeroData(static_cast<std::size_t>(valueTypeSize), 0);
        boxed = mono_value_box(domain, fieldClass, zeroData.data());
        if (!boxed)
            return false;
    }

    const std::vector<float> values = ParseFloatList(valueText);
    if (values.empty())
        return false;

    std::vector<std::string> preferredOrder;
    if (componentFields.count("r") > 0 && componentFields.count("g") > 0 && componentFields.count("b") > 0)
    {
        preferredOrder = { "r", "g", "b", "a" };
    }
    else if (componentFields.count("x") > 0 && componentFields.count("y") > 0)
    {
        preferredOrder = { "x", "y", "z", "w" };
    }
    else
    {
        for (const auto& [name, component] : componentFields)
        {
            (void)component;
            preferredOrder.push_back(name);
        }
    }

    std::size_t valueIndex = 0;
    for (const std::string& componentName : preferredOrder)
    {
        const auto componentIt = componentFields.find(componentName);
        if (componentIt == componentFields.end())
            continue;

        if (valueIndex >= values.size())
            break;

        float componentValue = values[valueIndex++];
        mono_field_set_value(boxed, componentIt->second, &componentValue);
    }

    if (valueIndex == 0)
        return false;

    void* unboxed = mono_object_unbox(boxed);
    if (!unboxed)
        return false;

    mono_field_set_value(instance, field, unboxed);
    return true;
}

static bool TrySetRuntimeScriptFieldValue(MonoDomain* domain,
                                          MonoObject* instance,
                                          MonoClass* klass,
                                          const std::string& fieldName,
                                          const std::string& fieldValue)
{
    if (!domain || !instance || !klass || fieldName.empty())
        return false;

    MonoClassField* field = FindFieldByName(klass, fieldName);
    if (!field)
        return false;

    MonoType* fieldType = mono_field_get_type(field);
    if (!fieldType)
        return false;

    MonoClass* fieldClass = mono_type_get_class(fieldType);
    const MonoTypeEnum typeEnum = static_cast<MonoTypeEnum>(mono_type_get_type(fieldType));

    switch (typeEnum)
    {
    case MONO_TYPE_BOOLEAN:
    {
        bool parsed = false;
        if (!ParseBoolValue(fieldValue, parsed))
            return false;
        const mono_bool value = parsed ? 1 : 0;
        mono_field_set_value(instance, field, (void*)&value);
        return true;
    }
    case MONO_TYPE_R4:
    {
        double parsed = 0.0;
        if (!ParseDoubleValue(fieldValue, parsed))
            return false;
        const float value = static_cast<float>(parsed);
        mono_field_set_value(instance, field, (void*)&value);
        return true;
    }
    case MONO_TYPE_R8:
    {
        double value = 0.0;
        if (!ParseDoubleValue(fieldValue, value))
            return false;
        mono_field_set_value(instance, field, (void*)&value);
        return true;
    }
    case MONO_TYPE_STRING:
    {
        MonoString* value = mono_string_new(domain, fieldValue.c_str());
        mono_field_set_value(instance, field, (void*)&value);
        return true;
    }
    case MONO_TYPE_CLASS:
    {
        if (!fieldClass)
            return false;

        MonoClassField* pathField = FindAssetPathField(fieldClass);
        if (!pathField)
            return false;

        MonoObject* objectValue = nullptr;
        mono_field_get_value(instance, field, &objectValue);
        if (!objectValue)
        {
            objectValue = mono_object_new(domain, fieldClass);
            if (!objectValue)
                return false;

            mono_runtime_object_init(objectValue);
            mono_field_set_value(instance, field, &objectValue);
        }

        MonoString* pathValue = mono_string_new(domain, fieldValue.c_str());
        mono_field_set_value(objectValue, pathField, &pathValue);
        return true;
    }
    case MONO_TYPE_I1:
    case MONO_TYPE_U1:
    case MONO_TYPE_I2:
    case MONO_TYPE_U2:
    case MONO_TYPE_I4:
    case MONO_TYPE_U4:
    case MONO_TYPE_I8:
    case MONO_TYPE_U8:
    {
        std::int64_t parsed = 0;
        if (!ParseInt64(fieldValue, parsed))
            return false;

        switch (typeEnum)
        {
        case MONO_TYPE_I1:
        {
            const std::int8_t value = static_cast<std::int8_t>(parsed);
            mono_field_set_value(instance, field, (void*)&value);
            return true;
        }
        case MONO_TYPE_U1:
        {
            const std::uint8_t value = static_cast<std::uint8_t>(parsed);
            mono_field_set_value(instance, field, (void*)&value);
            return true;
        }
        case MONO_TYPE_I2:
        {
            const std::int16_t value = static_cast<std::int16_t>(parsed);
            mono_field_set_value(instance, field, (void*)&value);
            return true;
        }
        case MONO_TYPE_U2:
        {
            const std::uint16_t value = static_cast<std::uint16_t>(parsed);
            mono_field_set_value(instance, field, (void*)&value);
            return true;
        }
        case MONO_TYPE_I4:
        {
            const std::int32_t value = static_cast<std::int32_t>(parsed);
            mono_field_set_value(instance, field, (void*)&value);
            return true;
        }
        case MONO_TYPE_U4:
        {
            const std::uint32_t value = static_cast<std::uint32_t>(parsed);
            mono_field_set_value(instance, field, (void*)&value);
            return true;
        }
        case MONO_TYPE_I8:
        {
            const std::int64_t value = static_cast<std::int64_t>(parsed);
            mono_field_set_value(instance, field, (void*)&value);
            return true;
        }
        case MONO_TYPE_U8:
        {
            const std::uint64_t value = static_cast<std::uint64_t>(parsed);
            mono_field_set_value(instance, field, (void*)&value);
            return true;
        }
        default:
            break;
        }
        return false;
    }
    case MONO_TYPE_VALUETYPE:
    {
        if (!fieldClass)
            return false;

        if (mono_class_is_enum(fieldClass) != 0)
        {
            std::int64_t parsed = 0;
            if (!ParseInt64(fieldValue, parsed))
                return false;

            MonoType* enumBaseType = mono_class_enum_basetype(fieldClass);
            const MonoTypeEnum enumBaseTypeEnum = enumBaseType
                ? static_cast<MonoTypeEnum>(mono_type_get_type(enumBaseType))
                : MONO_TYPE_I4;

            switch (enumBaseTypeEnum)
            {
            case MONO_TYPE_I1:
            {
                const std::int8_t value = static_cast<std::int8_t>(parsed);
                mono_field_set_value(instance, field, (void*)&value);
                return true;
            }
            case MONO_TYPE_U1:
            {
                const std::uint8_t value = static_cast<std::uint8_t>(parsed);
                mono_field_set_value(instance, field, (void*)&value);
                return true;
            }
            case MONO_TYPE_I2:
            {
                const std::int16_t value = static_cast<std::int16_t>(parsed);
                mono_field_set_value(instance, field, (void*)&value);
                return true;
            }
            case MONO_TYPE_U2:
            {
                const std::uint16_t value = static_cast<std::uint16_t>(parsed);
                mono_field_set_value(instance, field, (void*)&value);
                return true;
            }
            case MONO_TYPE_U4:
            {
                const std::uint32_t value = static_cast<std::uint32_t>(parsed);
                mono_field_set_value(instance, field, (void*)&value);
                return true;
            }
            case MONO_TYPE_I8:
            {
                const std::int64_t value = static_cast<std::int64_t>(parsed);
                mono_field_set_value(instance, field, (void*)&value);
                return true;
            }
            case MONO_TYPE_U8:
            {
                const std::uint64_t value = static_cast<std::uint64_t>(parsed);
                mono_field_set_value(instance, field, (void*)&value);
                return true;
            }
            case MONO_TYPE_I4:
            default:
            {
                const std::int32_t value = static_cast<std::int32_t>(parsed);
                mono_field_set_value(instance, field, (void*)&value);
                return true;
            }
            }
        }

        return SetValueTypeFieldFromText(domain, instance, field, fieldClass, fieldValue);
    }
    default:
        return false;
    }
}

static bool TryGetRuntimeScriptFieldValue(MonoDomain* domain,
                                          MonoObject* instance,
                                          MonoClass* klass,
                                          const std::string& fieldName,
                                          std::string& outValue)
{
    if (!domain || !instance || !klass || fieldName.empty())
        return false;

    MonoClassField* field = FindFieldByName(klass, fieldName);
    if (!field)
        return false;

    MonoType* fieldType = mono_field_get_type(field);
    if (!fieldType)
        return false;

    MonoClass* fieldClass = mono_type_get_class(fieldType);
    const MonoTypeEnum typeEnum = static_cast<MonoTypeEnum>(mono_type_get_type(fieldType));

    switch (typeEnum)
    {
    case MONO_TYPE_BOOLEAN:
    {
        mono_bool value = 0;
        mono_field_get_value(instance, field, &value);
        outValue = value ? "true" : "false";
        return true;
    }
    case MONO_TYPE_R4:
    {
        float value = 0.0f;
        mono_field_get_value(instance, field, &value);
        std::ostringstream stream;
        stream.imbue(std::locale::classic());
        stream << value;
        outValue = stream.str();
        return true;
    }
    case MONO_TYPE_R8:
    {
        double value = 0.0;
        mono_field_get_value(instance, field, &value);
        std::ostringstream stream;
        stream.imbue(std::locale::classic());
        stream << value;
        outValue = stream.str();
        return true;
    }
    case MONO_TYPE_STRING:
    {
        MonoString* value = nullptr;
        mono_field_get_value(instance, field, &value);
        outValue = MonoStringToUtf8(value);
        return true;
    }
    case MONO_TYPE_CLASS:
    {
        if (!fieldClass)
            return false;

        MonoClassField* pathField = FindAssetPathField(fieldClass);
        if (!pathField)
            return false;

        MonoObject* objectValue = nullptr;
        mono_field_get_value(instance, field, &objectValue);
        if (!objectValue)
        {
            outValue.clear();
            return true;
        }

        MonoString* pathValue = nullptr;
        mono_field_get_value(objectValue, pathField, &pathValue);
        outValue = MonoStringToUtf8(pathValue);
        return true;
    }
    case MONO_TYPE_I1:
    {
        std::int8_t value = 0;
        mono_field_get_value(instance, field, &value);
        outValue = std::to_string(static_cast<int>(value));
        return true;
    }
    case MONO_TYPE_U1:
    {
        std::uint8_t value = 0;
        mono_field_get_value(instance, field, &value);
        outValue = std::to_string(static_cast<unsigned int>(value));
        return true;
    }
    case MONO_TYPE_I2:
    {
        std::int16_t value = 0;
        mono_field_get_value(instance, field, &value);
        outValue = std::to_string(static_cast<int>(value));
        return true;
    }
    case MONO_TYPE_U2:
    {
        std::uint16_t value = 0;
        mono_field_get_value(instance, field, &value);
        outValue = std::to_string(static_cast<unsigned int>(value));
        return true;
    }
    case MONO_TYPE_I4:
    {
        std::int32_t value = 0;
        mono_field_get_value(instance, field, &value);
        outValue = std::to_string(value);
        return true;
    }
    case MONO_TYPE_U4:
    {
        std::uint32_t value = 0;
        mono_field_get_value(instance, field, &value);
        outValue = std::to_string(value);
        return true;
    }
    case MONO_TYPE_I8:
    {
        std::int64_t value = 0;
        mono_field_get_value(instance, field, &value);
        outValue = std::to_string(value);
        return true;
    }
    case MONO_TYPE_U8:
    {
        std::uint64_t value = 0;
        mono_field_get_value(instance, field, &value);
        outValue = std::to_string(value);
        return true;
    }
    case MONO_TYPE_VALUETYPE:
    {
        if (!fieldClass)
            return false;

        if (mono_class_is_enum(fieldClass) != 0)
        {
            MonoType* enumBaseType = mono_class_enum_basetype(fieldClass);
            const MonoTypeEnum enumBaseTypeEnum = enumBaseType
                ? static_cast<MonoTypeEnum>(mono_type_get_type(enumBaseType))
                : MONO_TYPE_I4;

            switch (enumBaseTypeEnum)
            {
            case MONO_TYPE_I1:
            {
                std::int8_t value = 0;
                mono_field_get_value(instance, field, &value);
                outValue = std::to_string(static_cast<int>(value));
                return true;
            }
            case MONO_TYPE_U1:
            {
                std::uint8_t value = 0;
                mono_field_get_value(instance, field, &value);
                outValue = std::to_string(static_cast<unsigned int>(value));
                return true;
            }
            case MONO_TYPE_I2:
            {
                std::int16_t value = 0;
                mono_field_get_value(instance, field, &value);
                outValue = std::to_string(static_cast<int>(value));
                return true;
            }
            case MONO_TYPE_U2:
            {
                std::uint16_t value = 0;
                mono_field_get_value(instance, field, &value);
                outValue = std::to_string(static_cast<unsigned int>(value));
                return true;
            }
            case MONO_TYPE_U4:
            {
                std::uint32_t value = 0;
                mono_field_get_value(instance, field, &value);
                outValue = std::to_string(value);
                return true;
            }
            case MONO_TYPE_I8:
            {
                std::int64_t value = 0;
                mono_field_get_value(instance, field, &value);
                outValue = std::to_string(value);
                return true;
            }
            case MONO_TYPE_U8:
            {
                std::uint64_t value = 0;
                mono_field_get_value(instance, field, &value);
                outValue = std::to_string(value);
                return true;
            }
            case MONO_TYPE_I4:
            default:
            {
                std::int32_t value = 0;
                mono_field_get_value(instance, field, &value);
                outValue = std::to_string(value);
                return true;
            }
            }

            return true;
        }

        MonoObject* boxed = mono_field_get_value_object(domain, field, instance);
        if (!boxed)
            return false;

        std::map<std::string, float> components;
        void* componentIter = nullptr;
        while (MonoClassField* component = mono_class_get_fields(fieldClass, &componentIter))
        {
            const char* componentName = mono_field_get_name(component);
            if (!componentName)
                continue;

            MonoType* componentType = mono_field_get_type(component);
            if (!componentType || mono_type_get_type(componentType) != MONO_TYPE_R4)
                continue;

            float value = 0.0f;
            mono_field_get_value(boxed, component, &value);
            components[ToLowerAscii(componentName)] = value;
        }

        if (components.empty())
            return false;

        std::vector<std::string> order;
        if (components.count("r") > 0 && components.count("g") > 0 && components.count("b") > 0)
            order = { "r", "g", "b", "a" };
        else if (components.count("x") > 0 && components.count("y") > 0)
            order = { "x", "y", "z", "w" };
        else
        {
            for (const auto& [name, value] : components)
            {
                (void)value;
                order.push_back(name);
            }
        }

        std::ostringstream stream;
        stream.imbue(std::locale::classic());

        bool wroteAny = false;
        for (const std::string& componentName : order)
        {
            const auto it = components.find(componentName);
            if (it == components.end())
                continue;

            if (wroteAny)
                stream << ',';

            stream << it->second;
            wroteAny = true;
        }

        if (!wroteAny)
            return false;

        outValue = stream.str();
        return true;
    }
    default:
        return false;
    }
}

static void EditorBridge_ApplyPersistedScriptFields(std::uint32_t entityId,
                                                    const ScriptComponent& script,
                                                    MonoObject* instance,
                                                    MonoClass* klass,
                                                    MonoDomain* domain)
{
    if (!instance || !klass || !domain || script.serializedFieldState.empty())
        return;

    const auto persistedFields = ParseSerializedScriptFieldState(script.serializedFieldState);
    for (const auto& [fieldName, fieldValue] : persistedFields)
    {
        if (fieldName.empty())
            continue;

        (void)entityId;
        TrySetRuntimeScriptFieldValue(domain, instance, klass, fieldName, fieldValue);
    }
}

static MonoString* EditorBridge_GetScriptFieldValue(std::uint32_t entityId, MonoString* fieldName)
{
    if (!g_editorSceneContext)
        return nullptr;

    const std::string fieldNameUtf8 = MonoStringToUtf8(fieldName);
    if (fieldNameUtf8.empty())
        return nullptr;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    const ScriptComponent* script = g_editorSceneContext->TryGetScript(entity);
    if (!script)
        return nullptr;

    std::string value;
    if (MonoObject* instance = FindRuntimeScriptInstance(entityId))
    {
        MonoClass* klass = mono_object_get_class(instance);
        MonoDomain* domain = mono_domain_get();
        if (TryGetRuntimeScriptFieldValue(domain, instance, klass, fieldNameUtf8, value))
        {
            MonoDomain* currentDomain = mono_domain_get();
            return currentDomain ? mono_string_new(currentDomain, value.c_str()) : nullptr;
        }
    }

    const std::optional<std::string> persisted = FindSerializedScriptFieldState(*script, fieldNameUtf8);
    if (!persisted.has_value())
        return nullptr;

    MonoDomain* currentDomain = mono_domain_get();
    return currentDomain ? mono_string_new(currentDomain, persisted->c_str()) : nullptr;
}

static bool EditorBridge_SetScriptFieldValue(std::uint32_t entityId, MonoString* fieldName, MonoString* fieldValue)
{
    if (!g_editorSceneContext)
        return false;

    const std::string fieldNameUtf8 = MonoStringToUtf8(fieldName);
    if (fieldNameUtf8.empty())
        return false;

    const std::string fieldValueUtf8 = MonoStringToUtf8(fieldValue);

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    ScriptComponent* script = g_editorSceneContext->TryGetScript(entity);
    if (!script)
        return false;

    bool appliedToRuntime = false;
    if (MonoObject* instance = FindRuntimeScriptInstance(entityId))
    {
        MonoClass* klass = mono_object_get_class(instance);
        MonoDomain* domain = mono_domain_get();
        appliedToRuntime = TrySetRuntimeScriptFieldValue(domain, instance, klass, fieldNameUtf8, fieldValueUtf8);
        if (!appliedToRuntime)
            return false;
    }

    UpsertSerializedScriptFieldState(*script, fieldNameUtf8, fieldValueUtf8);
    (void)appliedToRuntime;
    return true;
}

static void EditorBridge_SetGameViewSize(float width, float height)
{
    if (!g_editorRendererContext)
        return;

    const int w = static_cast<int>(width);
    const int h = static_cast<int>(height);
    if (w < 1 || h < 1)
        return;

    g_editorRendererContext->SetGameViewSize(w, h);
}

static void EditorBridge_SetEditorPreviewCamera(float x, float y, float zoom, bool enabled)
{
    if (!g_editorEngineContext)
        return;

    g_editorEngineContext->SetEditorPreviewCamera(x, y, zoom, enabled);
}

static std::uint64_t EditorBridge_GetGameViewTextureHandle()
{
    if (!g_editorRendererContext)
        return 0;

    return static_cast<std::uint64_t>(g_editorRendererContext->GetGameViewTextureHandle());
}

static void EditorDebugDraw_Line(float x0, float y0, float x1, float y1,
                                 float r, float g, float b, float a,
                                 float thickness, float durationSeconds)
{
    DebugDraw::Line(x0, y0, x1, y1, DebugDraw::Color(r, g, b, a), thickness, durationSeconds);
}

static void EditorDebugDraw_Circle(float centerX, float centerY, float radius,
                                   float r, float g, float b, float a,
                                   float thickness, int segments, float durationSeconds)
{
    DebugDraw::Circle(centerX, centerY, radius, DebugDraw::Color(r, g, b, a), thickness, segments, durationSeconds);
}

static void EditorDebugDraw_FilledCircle(float centerX, float centerY, float radius,
                                         float r, float g, float b, float a,
                                         int segments, float durationSeconds)
{
    DebugDraw::FilledCircle(centerX, centerY, radius, DebugDraw::Color(r, g, b, a), segments, durationSeconds);
}

static void EditorDebugDraw_Rect(float x, float y, float width, float height,
                                 float r, float g, float b, float a,
                                 float thickness, float durationSeconds)
{
    DebugDraw::Rect(x, y, width, height, DebugDraw::Color(r, g, b, a), thickness, durationSeconds);
}

static void EditorDebugDraw_FilledRect(float x, float y, float width, float height,
                                       float r, float g, float b, float a,
                                       float durationSeconds)
{
    DebugDraw::FilledRect(x, y, width, height, DebugDraw::Color(r, g, b, a), durationSeconds);
}

static void EditorDebugDraw_Clear()
{
    DebugDraw::Clear();
}

static MonoString* EditorExplorer_PickFolder(MonoString* title, MonoString* initialPath)
{
    const std::string selectedPath = ExplorerDialog::PickFolder(MonoStringToUtf8(title), MonoStringToUtf8(initialPath));
    MonoDomain* domain = mono_domain_get();
    return domain ? mono_string_new(domain, selectedPath.c_str()) : nullptr;
}

static MonoString* EditorExplorer_PickFile(MonoString* title, MonoString* initialPath)
{
    const std::string selectedPath = ExplorerDialog::PickFile(MonoStringToUtf8(title), MonoStringToUtf8(initialPath));
    MonoDomain* domain = mono_domain_get();
    return domain ? mono_string_new(domain, selectedPath.c_str()) : nullptr;
}

static MonoString* EditorExplorer_PickFiles(MonoString* title, MonoString* initialPath)
{
    const std::vector<std::string> selectedPaths = ExplorerDialog::PickFiles(MonoStringToUtf8(title), MonoStringToUtf8(initialPath));

    std::string joinedPaths;
    for (std::size_t index = 0; index < selectedPaths.size(); ++index)
    {
        if (index > 0)
            joinedPaths += '\n';
        joinedPaths += selectedPaths[index];
    }

    MonoDomain* domain = mono_domain_get();
    return domain ? mono_string_new(domain, joinedPaths.c_str()) : nullptr;
}

static bool EditorBridge_HasTransform(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return false;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    return g_editorSceneContext->HasTransform(entity);
}

static void EditorBridge_AddTransform(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    if (!g_editorSceneContext->IsValid(entity) || g_editorSceneContext->HasTransform(entity))
        return;

    auto& transform = g_editorSceneContext->AddTransform(entity);
    transform.x = 0.0f;
    transform.y = 0.0f;
    transform.width = 100.0f;
    transform.height = 100.0f;
}

static bool EditorBridge_GetTransform(std::uint32_t entityId, float* x, float* y, float* width, float* height)
{
    if (!g_editorSceneContext)
        return false;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    const TransformComponent* transform = g_editorSceneContext->TryGetTransform(entity);
    if (!transform)
        return false;

    if (x)
        *x = transform->x;
    if (y)
        *y = transform->y;
    if (width)
        *width = transform->width;
    if (height)
        *height = transform->height;
    return true;
}

static void EditorBridge_SetTransform(std::uint32_t entityId, float x, float y, float width, float height)
{
    if (!g_editorSceneContext)
        return;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    TransformComponent* transform = g_editorSceneContext->TryGetTransform(entity);
    if (!transform)
        return;

    transform->x = x;
    transform->y = y;
    transform->width = width;
    transform->height = height;
}

static bool EditorBridge_HasCamera(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return false;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    return g_editorSceneContext->HasCamera(entity);
}

static void EditorBridge_AddCamera(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    if (!g_editorSceneContext->IsValid(entity) || g_editorSceneContext->HasCamera(entity))
        return;

    auto& camera = g_editorSceneContext->AddCamera(entity);
    TransformComponent* transform = EnsureTransformComponent(g_editorSceneContext, entity);
    camera.x = transform ? transform->x : 0.0f;
    camera.y = transform ? transform->y : 0.0f;
    camera.zoom = 1.0f;
    camera.orthographicSize = 0.0f;
    camera.enabled = true;
    camera.primary = true;
    camera.clearColor = true;
    camera.backgroundColor = 0x14141AFFu;
    camera.cullingMask = 0xFFFFFFFFu;
    camera.viewportX = 0.0f;
    camera.viewportY = 0.0f;
    camera.viewportWidth = 1.0f;
    camera.viewportHeight = 1.0f;
    NormalizeCameraValues(camera);
}

static bool EditorBridge_GetCamera(std::uint32_t entityId, float* x, float* y, float* zoom)
{
    if (!g_editorSceneContext)
        return false;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    CameraComponent* camera = g_editorSceneContext->TryGetCamera(entity);
    if (!camera)
        return false;

    NormalizeCameraValues(*camera);

    float resolvedX = camera->x;
    float resolvedY = camera->y;
    ResolveCameraPosition(g_editorSceneContext, entity, *camera, resolvedX, resolvedY);
    if (x)
        *x = resolvedX;
    if (y)
        *y = resolvedY;
    if (zoom)
        *zoom = camera->zoom;
    return true;
}

static void EditorBridge_SetCamera(std::uint32_t entityId, float x, float y, float zoom)
{
    if (!g_editorSceneContext)
        return;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    CameraComponent* camera = g_editorSceneContext->TryGetCamera(entity);
    if (!camera)
        return;

    ApplyCameraPosition(g_editorSceneContext, entity, *camera, x, y);
    camera->zoom = zoom;
    NormalizeCameraValues(*camera);
}

static bool EditorBridge_GetCameraSettings(std::uint32_t entityId,
                                           float* x,
                                           float* y,
                                           float* zoom,
                                           bool* enabled,
                                           bool* primary,
                                           bool* clearColor,
                                           std::uint32_t* backgroundColor,
                                           std::uint32_t* cullingMask,
                                           float* viewportX,
                                           float* viewportY,
                                           float* viewportWidth,
                                           float* viewportHeight)
{
    if (!g_editorSceneContext)
        return false;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    CameraComponent* camera = g_editorSceneContext->TryGetCamera(entity);
    if (!camera)
        return false;

    NormalizeCameraValues(*camera);

    float resolvedX = camera->x;
    float resolvedY = camera->y;
    ResolveCameraPosition(g_editorSceneContext, entity, *camera, resolvedX, resolvedY);
    if (x)
        *x = resolvedX;
    if (y)
        *y = resolvedY;
    if (zoom)
        *zoom = camera->zoom;
    if (enabled)
        *enabled = camera->enabled;
    if (primary)
        *primary = camera->primary;
    if (clearColor)
        *clearColor = camera->clearColor;
    if (backgroundColor)
        *backgroundColor = camera->backgroundColor;
    if (cullingMask)
        *cullingMask = camera->cullingMask;
    if (viewportX)
        *viewportX = camera->viewportX;
    if (viewportY)
        *viewportY = camera->viewportY;
    if (viewportWidth)
        *viewportWidth = camera->viewportWidth;
    if (viewportHeight)
        *viewportHeight = camera->viewportHeight;

    return true;
}

static void EditorBridge_SetCameraSettings(std::uint32_t entityId,
                                           float x,
                                           float y,
                                           float zoom,
                                           bool enabled,
                                           bool primary,
                                           bool clearColor,
                                           std::uint32_t backgroundColor,
                                           std::uint32_t cullingMask,
                                           float viewportX,
                                           float viewportY,
                                           float viewportWidth,
                                           float viewportHeight)
{
    if (!g_editorSceneContext)
        return;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    CameraComponent* camera = g_editorSceneContext->TryGetCamera(entity);
    if (!camera)
        return;

    ApplyCameraPosition(g_editorSceneContext, entity, *camera, x, y);
    camera->zoom = zoom;
    camera->enabled = enabled;
    camera->primary = primary;
    camera->clearColor = clearColor;
    camera->backgroundColor = backgroundColor;
    camera->cullingMask = cullingMask;
    camera->viewportX = viewportX;
    camera->viewportY = viewportY;
    camera->viewportWidth = viewportWidth;
    camera->viewportHeight = viewportHeight;
    NormalizeCameraValues(*camera);
}

static bool EditorBridge_GetCameraSettingsV2(std::uint32_t entityId,
                                             float* x,
                                             float* y,
                                             float* zoom,
                                             bool* enabled,
                                             bool* primary,
                                             bool* clearColor,
                                             std::uint32_t* backgroundColor,
                                             std::uint32_t* cullingMask,
                                             float* viewportX,
                                             float* viewportY,
                                             float* viewportWidth,
                                             float* viewportHeight,
                                             float* orthographicSize)
{
    if (!g_editorSceneContext)
        return false;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    CameraComponent* camera = g_editorSceneContext->TryGetCamera(entity);
    if (!camera)
        return false;

    NormalizeCameraValues(*camera);

    float resolvedX = camera->x;
    float resolvedY = camera->y;
    ResolveCameraPosition(g_editorSceneContext, entity, *camera, resolvedX, resolvedY);
    if (x)
        *x = resolvedX;
    if (y)
        *y = resolvedY;
    if (zoom)
        *zoom = camera->zoom;
    if (enabled)
        *enabled = camera->enabled;
    if (primary)
        *primary = camera->primary;
    if (clearColor)
        *clearColor = camera->clearColor;
    if (backgroundColor)
        *backgroundColor = camera->backgroundColor;
    if (cullingMask)
        *cullingMask = camera->cullingMask;
    if (viewportX)
        *viewportX = camera->viewportX;
    if (viewportY)
        *viewportY = camera->viewportY;
    if (viewportWidth)
        *viewportWidth = camera->viewportWidth;
    if (viewportHeight)
        *viewportHeight = camera->viewportHeight;
    if (orthographicSize)
        *orthographicSize = camera->orthographicSize;

    return true;
}

static void EditorBridge_SetCameraSettingsV2(std::uint32_t entityId,
                                             float x,
                                             float y,
                                             float zoom,
                                             bool enabled,
                                             bool primary,
                                             bool clearColor,
                                             std::uint32_t backgroundColor,
                                             std::uint32_t cullingMask,
                                             float viewportX,
                                             float viewportY,
                                             float viewportWidth,
                                             float viewportHeight,
                                             float orthographicSize)
{
    if (!g_editorSceneContext)
        return;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    CameraComponent* camera = g_editorSceneContext->TryGetCamera(entity);
    if (!camera)
        return;

    ApplyCameraPosition(g_editorSceneContext, entity, *camera, x, y);
    camera->zoom = zoom;
    camera->enabled = enabled;
    camera->primary = primary;
    camera->clearColor = clearColor;
    camera->backgroundColor = backgroundColor;
    camera->cullingMask = cullingMask;
    camera->viewportX = viewportX;
    camera->viewportY = viewportY;
    camera->viewportWidth = viewportWidth;
    camera->viewportHeight = viewportHeight;
    camera->orthographicSize = orthographicSize;
    NormalizeCameraValues(*camera);
}

static void EditorBridge_RemoveCamera(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    g_editorSceneContext->RemoveCamera(entity);
}

static bool EditorBridge_HasSprite(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return false;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    return g_editorSceneContext->HasSprite(entity);
}

static void EditorBridge_AddSprite(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    if (!g_editorSceneContext->IsValid(entity) || g_editorSceneContext->HasSprite(entity))
        return;

    g_editorSceneContext->AddSprite(entity);
}

static void EditorBridge_RemoveSprite(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    g_editorSceneContext->RemoveSprite(entity);
}

static MonoString* EditorBridge_GetSpriteTexturePath(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return nullptr;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    const SpriteComponent* sprite = g_editorSceneContext->TryGetSprite(entity);
    if (!sprite)
        return nullptr;

    MonoDomain* domain = mono_domain_get();
    return domain ? mono_string_new(domain, sprite->textureAssetPath.c_str()) : nullptr;
}

static void EditorBridge_SetSpriteTexturePath(std::uint32_t entityId, MonoString* texturePath)
{
    if (!g_editorSceneContext)
        return;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    SpriteComponent* sprite = g_editorSceneContext->TryGetSprite(entity);
    if (!sprite)
        return;

    sprite->textureAssetPath = TrimWhitespace(MonoStringToUtf8(texturePath));
    sprite->textureAssetHandle = 0;
    sprite->texture = nullptr;
}

static std::uint32_t EditorBridge_GetSpriteFallbackColor(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return 0xFFFFFFFFu;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    const SpriteComponent* sprite = g_editorSceneContext->TryGetSprite(entity);
    if (!sprite)
        return 0xFFFFFFFFu;

    return sprite->fallbackColor;
}

static void EditorBridge_SetSpriteFallbackColor(std::uint32_t entityId, std::uint32_t color)
{
    if (!g_editorSceneContext)
        return;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    SpriteComponent* sprite = g_editorSceneContext->TryGetSprite(entity);
    if (!sprite)
        return;

    sprite->fallbackColor = color;
}

static bool EditorBridge_GetSpriteSettings(std::uint32_t entityId,
                                           bool* centered,
                                           float* offsetX,
                                           float* offsetY,
                                           bool* flipH,
                                           bool* flipV,
                                           std::uint32_t* hframes,
                                           std::uint32_t* vframes,
                                           std::uint32_t* frame,
                                           bool* regionEnabled,
                                           float* regionX,
                                           float* regionY,
                                           float* regionWidth,
                                           float* regionHeight)
{
    if (!g_editorSceneContext)
        return false;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    const SpriteComponent* sprite = g_editorSceneContext->TryGetSprite(entity);
    if (!sprite)
        return false;

    if (centered)
        *centered = sprite->centered;
    if (offsetX)
        *offsetX = sprite->offsetX;
    if (offsetY)
        *offsetY = sprite->offsetY;
    if (flipH)
        *flipH = sprite->flipH;
    if (flipV)
        *flipV = sprite->flipV;
    if (hframes)
        *hframes = sprite->hframes;
    if (vframes)
        *vframes = sprite->vframes;
    if (frame)
        *frame = sprite->frame;
    if (regionEnabled)
        *regionEnabled = sprite->regionEnabled;
    if (regionX)
        *regionX = sprite->regionX;
    if (regionY)
        *regionY = sprite->regionY;
    if (regionWidth)
        *regionWidth = sprite->regionWidth;
    if (regionHeight)
        *regionHeight = sprite->regionHeight;

    return true;
}

static void EditorBridge_SetSpriteSettings(std::uint32_t entityId,
                                           bool centered,
                                           float offsetX,
                                           float offsetY,
                                           bool flipH,
                                           bool flipV,
                                           std::uint32_t hframes,
                                           std::uint32_t vframes,
                                           std::uint32_t frame,
                                           bool regionEnabled,
                                           float regionX,
                                           float regionY,
                                           float regionWidth,
                                           float regionHeight)
{
    if (!g_editorSceneContext)
        return;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    SpriteComponent* sprite = g_editorSceneContext->TryGetSprite(entity);
    if (!sprite)
        return;

    if (hframes < 1)
        hframes = 1;
    if (vframes < 1)
        vframes = 1;

    std::uint64_t frameCount = static_cast<std::uint64_t>(hframes) * static_cast<std::uint64_t>(vframes);
    if (frameCount == 0)
    {
        hframes = 1;
        vframes = 1;
        frame = 0;
    }
    else if (frame >= frameCount)
    {
        frame = static_cast<std::uint32_t>(frameCount - 1);
    }

    if (regionWidth < 0.0f)
        regionWidth = 0.0f;
    if (regionHeight < 0.0f)
        regionHeight = 0.0f;

    sprite->centered = centered;
    sprite->offsetX = offsetX;
    sprite->offsetY = offsetY;
    sprite->flipH = flipH;
    sprite->flipV = flipV;
    sprite->hframes = hframes;
    sprite->vframes = vframes;
    sprite->frame = frame;
    sprite->regionEnabled = regionEnabled;
    sprite->regionX = regionX;
    sprite->regionY = regionY;
    sprite->regionWidth = regionWidth;
    sprite->regionHeight = regionHeight;
}

static bool EditorBridge_HasScript(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return false;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    return g_editorSceneContext->HasScript(entity);
}

static void EditorBridge_AddScript(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    if (!g_editorSceneContext->IsValid(entity) || g_editorSceneContext->HasScript(entity))
        return;

    g_editorSceneContext->AddScript(entity);
}

static void EditorBridge_RemoveScript(std::uint32_t entityId)
{
    if (!g_editorSceneContext)
        return;

    const auto entity = g_editorSceneContext->FromEntityId(entityId);
    g_editorSceneContext->RemoveScript(entity);
}

static bool EditorImGui_Begin(MonoString* title)
{
    if (!ImGui::GetCurrentContext())
        return false;

    const std::string text = MonoStringToUtf8(title);
    const char* windowTitle = text.empty() ? "C# Window" : text.c_str();
    return ImGui::Begin(windowTitle);
}

static bool EditorImGui_BeginTopBar(MonoString* id, float height)
{
    if (!ImGui::GetCurrentContext())
        return false;

    // Route to main menu bar so dockspace reserves top strip.
    // Apply dynamic frame padding from requested height for compact/expanded variants.
    (void)id;
    const float requestedHeight = height > 8.0f ? height : 36.0f;

    ImGuiStyle& style = ImGui::GetStyle();
    const float fontSize = ImGui::GetFontSize();
    float framePaddingY = (requestedHeight - fontSize) * 0.5f;
    if (framePaddingY < 0.0f)
        framePaddingY = 0.0f;
    else if (framePaddingY > 24.0f)
        framePaddingY = 24.0f;

    ImGui::PushStyleVar(ImGuiStyleVar_FramePadding, ImVec2(style.FramePadding.x, framePaddingY));
    g_editorTopBarStylePushed = true;

    if (ImGui::BeginMainMenuBar())
        return true;

    ImGui::PopStyleVar();
    g_editorTopBarStylePushed = false;
    return false;
}

static void EditorImGui_EndTopBar()
{
    if (ImGui::GetCurrentContext())
        ImGui::EndMainMenuBar();

    if (g_editorTopBarStylePushed)
    {
        ImGui::PopStyleVar();
        g_editorTopBarStylePushed = false;
    }
}

static bool EditorImGui_BeginChild(MonoString* id, float width, float height, bool border)
{
    if (!ImGui::GetCurrentContext())
        return false;

    const std::string value = MonoStringToUtf8(id);
    const char* childId = value.empty() ? "Child" : value.c_str();
    return ImGui::BeginChild(childId, ImVec2(width, height), border);
}

static void EditorImGui_End()
{
    if (ImGui::GetCurrentContext())
        ImGui::End();
}

static void EditorImGui_EndChild()
{
    if (ImGui::GetCurrentContext())
        ImGui::EndChild();
}

static void EditorImGui_Text(MonoString* text)
{
    if (!ImGui::GetCurrentContext())
        return;

    const std::string value = MonoStringToUtf8(text);
    ImGui::TextUnformatted(value.c_str());
}

static void EditorImGui_SetTooltip(MonoString* text)
{
    if (!ImGui::GetCurrentContext())
        return;

    const std::string value = MonoStringToUtf8(text);
    if (value.empty())
        return;

    ImGui::SetTooltip("%s", value.c_str());
}

static bool EditorImGui_Button(MonoString* label)
{
    if (!ImGui::GetCurrentContext())
        return false;

    const std::string value = MonoStringToUtf8(label);
    const char* buttonLabel = value.empty() ? "Button" : value.c_str();
    return ImGui::Button(buttonLabel);
}

static void EditorImGui_OpenPopup(MonoString* popupId)
{
    if (!ImGui::GetCurrentContext())
        return;

    const std::string value = MonoStringToUtf8(popupId);
    const char* popupName = value.empty() ? "Popup" : value.c_str();
    ImGui::OpenPopup(popupName);
}

static bool EditorImGui_BeginPopupModal(MonoString* popupId)
{
    if (!ImGui::GetCurrentContext())
        return false;

    const std::string value = MonoStringToUtf8(popupId);
    const char* popupName = value.empty() ? "Popup" : value.c_str();
    return ImGui::BeginPopupModal(popupName, nullptr, ImGuiWindowFlags_AlwaysAutoResize);
}

static bool EditorImGui_BeginPopup(MonoString* popupId)
{
    if (!ImGui::GetCurrentContext())
        return false;

    const std::string value = MonoStringToUtf8(popupId);
    const char* popupName = value.empty() ? "Popup" : value.c_str();
    return ImGui::BeginPopup(popupName);
}

static void EditorImGui_EndPopup()
{
    if (ImGui::GetCurrentContext())
        ImGui::EndPopup();
}

static void EditorImGui_CloseCurrentPopup()
{
    if (ImGui::GetCurrentContext())
        ImGui::CloseCurrentPopup();
}

static void EditorImGui_SameLine()
{
    if (ImGui::GetCurrentContext())
        ImGui::SameLine();
}

static void EditorImGui_SetNextItemWidth(float width)
{
    if (ImGui::GetCurrentContext())
        ImGui::SetNextItemWidth(width);
}

static bool EditorImGui_Selectable(MonoString* label, bool selected)
{
    if (!ImGui::GetCurrentContext())
        return false;

    const std::string value = MonoStringToUtf8(label);
    const char* selectableLabel = value.empty() ? "Item" : value.c_str();
    return ImGui::Selectable(selectableLabel, selected);
}

static bool EditorImGui_SelectableNoClose(MonoString* label, bool selected)
{
    if (!ImGui::GetCurrentContext())
        return false;

    const std::string value = MonoStringToUtf8(label);
    const char* selectableLabel = value.empty() ? "Item" : value.c_str();
    return ImGui::Selectable(selectableLabel, selected, ImGuiSelectableFlags_DontClosePopups);
}

static MonoString* EditorImGui_InputText(MonoString* label, MonoString* value)
{
    std::string inputValue = MonoStringToUtf8(value);

    if (ImGui::GetCurrentContext())
    {
        const std::string text = MonoStringToUtf8(label);
        const char* inputLabel = text.empty() ? "Text" : text.c_str();

        char buffer[256] = {};
        std::snprintf(buffer, sizeof(buffer), "%s", inputValue.c_str());
        ImGui::InputText(inputLabel, buffer, sizeof(buffer));
        inputValue = buffer;
    }

    MonoDomain* domain = mono_domain_get();
    return domain ? mono_string_new(domain, inputValue.c_str()) : nullptr;
}

static bool EditorImGui_InputFloat(MonoString* label, float* value, float step)
{
    if (!ImGui::GetCurrentContext() || !value)
        return false;

    const std::string text = MonoStringToUtf8(label);
    const char* inputLabel = text.empty() ? "Value" : text.c_str();
    return ImGui::InputFloat(inputLabel, value, step);
}

static void EditorImGui_Separator()
{
    if (ImGui::GetCurrentContext())
        ImGui::Separator();
}

static bool EditorImGui_Checkbox(MonoString* label, MonoBoolean* value)
{
    if (!ImGui::GetCurrentContext() || !value)
        return false;

    const std::string text = MonoStringToUtf8(label);
    const char* checkLabel = text.empty() ? "##cb" : text.c_str();
    bool v = (*value != 0);
    bool changed = ImGui::Checkbox(checkLabel, &v);
    *value = v ? 1 : 0;
    return changed;
}

static bool EditorImGui_ColorButton(MonoString* id, float r, float g, float b, float a)
{
    if (!ImGui::GetCurrentContext())
        return false;

    const std::string value = MonoStringToUtf8(id);
    const char* buttonId = value.empty() ? "##ColorButton" : value.c_str();

    if (r < 0.0f)
        r = 0.0f;
    else if (r > 1.0f)
        r = 1.0f;

    if (g < 0.0f)
        g = 0.0f;
    else if (g > 1.0f)
        g = 1.0f;

    if (b < 0.0f)
        b = 0.0f;
    else if (b > 1.0f)
        b = 1.0f;

    if (a < 0.0f)
        a = 0.0f;
    else if (a > 1.0f)
        a = 1.0f;

    return ImGui::ColorButton(buttonId,
                              ImVec4(r, g, b, a),
                              ImGuiColorEditFlags_NoTooltip,
                              ImVec2(30.0f, 16.0f));
}

static bool EditorImGui_ColorPicker4(MonoString* label,
                                     float* r,
                                     float* g,
                                     float* b,
                                     float* a,
                                     bool showAlpha)
{
    if (!ImGui::GetCurrentContext() || !r || !g || !b || !a)
        return false;

    const std::string value = MonoStringToUtf8(label);
    const char* pickerLabel = value.empty() ? "##ColorPicker" : value.c_str();

    float color[4] = { *r, *g, *b, *a };
    int flags = ImGuiColorEditFlags_DisplayRGB |
        ImGuiColorEditFlags_DisplayHex |
        ImGuiColorEditFlags_Uint8 |
        ImGuiColorEditFlags_PickerHueWheel;

    if (!showAlpha)
        flags |= ImGuiColorEditFlags_NoAlpha;

    const bool changed = ImGui::ColorPicker4(pickerLabel, color, flags);

    *r = color[0];
    *g = color[1];
    *b = color[2];
    *a = showAlpha ? color[3] : 1.0f;

    return changed;
}

static bool EditorImGui_IsWindowHovered()
{
    return ImGui::GetCurrentContext() ? ImGui::IsWindowHovered() : false;
}

static bool EditorImGui_GetWantCaptureMouse()
{
    return ImGui::GetCurrentContext() ? ImGui::GetIO().WantCaptureMouse : false;
}

static bool EditorImGui_GetWantCaptureKeyboard()
{
    return ImGui::GetCurrentContext() ? ImGui::GetIO().WantCaptureKeyboard : false;
}

static bool EditorImGui_IsMouseDown(int button)
{
    return ImGui::GetCurrentContext() ? ImGui::IsMouseDown(button) : false;
}

static float EditorImGui_GetMouseDeltaX()
{
    return ImGui::GetCurrentContext() ? ImGui::GetIO().MouseDelta.x : 0.0f;
}

static float EditorImGui_GetMouseDeltaY()
{
    return ImGui::GetCurrentContext() ? ImGui::GetIO().MouseDelta.y : 0.0f;
}

static float EditorImGui_GetMouseWheel()
{
    return ImGui::GetCurrentContext() ? ImGui::GetIO().MouseWheel : 0.0f;
}

static float EditorImGui_GetMousePosX()
{
    return ImGui::GetCurrentContext() ? ImGui::GetIO().MousePos.x : 0.0f;
}

static float EditorImGui_GetMousePosY()
{
    return ImGui::GetCurrentContext() ? ImGui::GetIO().MousePos.y : 0.0f;
}

static float EditorImGui_GetDisplayWidth()
{
    return ImGui::GetCurrentContext() ? ImGui::GetIO().DisplaySize.x : 0.0f;
}

static float EditorImGui_GetDisplayHeight()
{
    return ImGui::GetCurrentContext() ? ImGui::GetIO().DisplaySize.y : 0.0f;
}

static float EditorImGui_GetContentRegionAvailX()
{
    return ImGui::GetCurrentContext() ? ImGui::GetContentRegionAvail().x : 0.0f;
}

static float EditorImGui_GetContentRegionAvailY()
{
    return ImGui::GetCurrentContext() ? ImGui::GetContentRegionAvail().y : 0.0f;
}

static float EditorImGui_GetCursorScreenPosX()
{
    return ImGui::GetCurrentContext() ? ImGui::GetCursorScreenPos().x : 0.0f;
}

static float EditorImGui_GetCursorScreenPosY()
{
    return ImGui::GetCurrentContext() ? ImGui::GetCursorScreenPos().y : 0.0f;
}

static void EditorImGui_Image(std::uint64_t textureHandle, float width, float height)
{
    if (!ImGui::GetCurrentContext())
        return;

    ImTextureID textureId = reinterpret_cast<ImTextureID>(static_cast<uintptr_t>(textureHandle));
    ImGui::Image(textureId, ImVec2(width, height), ImVec2(0.0f, 1.0f), ImVec2(1.0f, 0.0f));
}

static bool EditorImGui_InvisibleButton(MonoString* id, float width, float height)
{
    if (!ImGui::GetCurrentContext())
        return false;

    const std::string value = MonoStringToUtf8(id);
    const char* buttonId = value.empty() ? "##InvisibleButton" : value.c_str();
    return ImGui::InvisibleButton(buttonId, ImVec2(width, height));
}

static bool EditorImGui_IsItemHovered()
{
    return ImGui::GetCurrentContext() ? ImGui::IsItemHovered() : false;
}

static bool EditorImGui_IsItemActive()
{
    return ImGui::GetCurrentContext() ? ImGui::IsItemActive() : false;
}

static bool EditorImGui_IsMouseClicked(int button)
{
    return ImGui::GetCurrentContext() ? ImGui::IsMouseClicked(button) : false;
}

static void EditorImGui_DrawLine(float x0,
                                 float y0,
                                 float x1,
                                 float y1,
                                 float r,
                                 float g,
                                 float b,
                                 float a,
                                 float thickness)
{
    if (!ImGui::GetCurrentContext())
        return;

    ImDrawList* drawList = ImGui::GetWindowDrawList();
    if (!drawList)
        return;

    drawList->AddLine(ImVec2(x0, y0),
                      ImVec2(x1, y1),
                      IM_COL32(static_cast<int>(r * 255.0f),
                               static_cast<int>(g * 255.0f),
                               static_cast<int>(b * 255.0f),
                               static_cast<int>(a * 255.0f)),
                      thickness);
}

static void EditorImGui_DrawRect(float x,
                                 float y,
                                 float width,
                                 float height,
                                 float r,
                                 float g,
                                 float b,
                                 float a,
                                 float thickness)
{
    if (!ImGui::GetCurrentContext())
        return;

    ImDrawList* drawList = ImGui::GetWindowDrawList();
    if (!drawList)
        return;

    drawList->AddRect(ImVec2(x, y),
                      ImVec2(x + width, y + height),
                      IM_COL32(static_cast<int>(r * 255.0f),
                               static_cast<int>(g * 255.0f),
                               static_cast<int>(b * 255.0f),
                               static_cast<int>(a * 255.0f)),
                      0.0f,
                      0,
                      thickness);
}

static bool EditorImGuizmo_IsUsing()
{
    return ImGui::GetCurrentContext() ? ImGuizmo::IsUsing() : false;
}

static bool EditorImGuizmo_Manipulate2DTranslate(float viewportX,
                                                 float viewportY,
                                                 float viewportWidth,
                                                 float viewportHeight,
                                                 float cameraX,
                                                 float cameraY,
                                                 float cameraZoom,
                                                 float* x,
                                                 float* y,
                                                 float objectWidth,
                                                 float objectHeight)
{
    if (!ImGui::GetCurrentContext() || !x || !y)
        return false;

    if (viewportWidth < 1.0f || viewportHeight < 1.0f)
        return false;

    float zoom = cameraZoom;
    if (!std::isfinite(zoom) || zoom < 0.01f)
        zoom = 0.01f;
    else if (zoom > 100.0f)
        zoom = 100.0f;

    const float halfWorldWidth = (viewportWidth * 0.5f) / zoom;
    const float halfWorldHeight = (viewportHeight * 0.5f) / zoom;
    const float left = cameraX - halfWorldWidth;
    const float right = cameraX + halfWorldWidth;
    const float bottom = cameraY - halfWorldHeight;
    const float top = cameraY + halfWorldHeight;

    const float view[16] = {
        1.0f, 0.0f, 0.0f, 0.0f,
        0.0f, 1.0f, 0.0f, 0.0f,
        0.0f, 0.0f, 1.0f, 0.0f,
        0.0f, 0.0f, 0.0f, 1.0f,
    };

    const float rl = 1.0f / (right - left);
    const float tb = 1.0f / (top - bottom);
    const float projection[16] = {
        2.0f * rl, 0.0f, 0.0f, 0.0f,
        0.0f, 2.0f * tb, 0.0f, 0.0f,
        0.0f, 0.0f, -1.0f, 0.0f,
        -(right + left) * rl, -(top + bottom) * tb, 0.0f, 1.0f,
    };

    float matrix[16] = {
        objectWidth, 0.0f, 0.0f, 0.0f,
        0.0f, objectHeight, 0.0f, 0.0f,
        0.0f, 0.0f, 1.0f, 0.0f,
        *x, *y, 0.0f, 1.0f,
    };

    ImGuizmo::SetOrthographic(true);
    ImGuizmo::SetDrawlist(ImGui::GetWindowDrawList());
    ImGuizmo::SetRect(viewportX, viewportY, viewportWidth, viewportHeight);

    const bool changed = ImGuizmo::Manipulate(view,
                                              projection,
                                              ImGuizmo::TRANSLATE,
                                              ImGuizmo::WORLD,
                                              matrix,
                                              nullptr,
                                              nullptr,
                                              nullptr,
                                              nullptr);

    if (changed)
    {
        *x = matrix[12];
        *y = matrix[13];
    }

    return changed;
}

static bool EngineInput_GetMouseButton(int button)
{
    return SDLInputState::GetMouseButton(button);
}

static bool EngineInput_GetMouseButtonDown(int button)
{
    return SDLInputState::GetMouseButtonDown(button);
}

static bool EngineInput_GetMouseButtonUp(int button)
{
    return SDLInputState::GetMouseButtonUp(button);
}

static float EngineInput_GetMouseDeltaX()
{
    return SDLInputState::GetMouseDeltaX();
}

static float EngineInput_GetMouseDeltaY()
{
    return SDLInputState::GetMouseDeltaY();
}

static float EngineInput_GetMouseWheel()
{
    return SDLInputState::GetMouseWheel();
}

static float EngineInput_GetMousePosX()
{
    return SDLInputState::GetMousePosX();
}

static float EngineInput_GetMousePosY()
{
    return SDLInputState::GetMousePosY();
}

static bool EngineInput_GetKey(int scancode)
{
    return SDLInputState::GetKey(scancode);
}

static bool EngineInput_GetKeyDown(int scancode)
{
    return SDLInputState::GetKeyDown(scancode);
}

static bool EngineInput_GetKeyUp(int scancode)
{
    return SDLInputState::GetKeyUp(scancode);
}
#endif
