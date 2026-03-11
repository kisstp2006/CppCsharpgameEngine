#if ENGINE_MONO_RUNTIME_AVAILABLE
struct ScriptAssemblyBindings
{
    MonoAssembly* assembly = nullptr;
    MonoImage* image = nullptr;
    MonoClass* scriptClass = nullptr;
    MonoMethod* onStart = nullptr;
    MonoMethod* onUpdate = nullptr;
    MonoMethod* onShutdown = nullptr;
};

static bool TryGetFileWriteTime(const std::filesystem::path& path, std::filesystem::file_time_type& outWriteTime)
{
    std::error_code error;
    outWriteTime = std::filesystem::last_write_time(path, error);
    return !error;
}

static bool TryCreateAssemblyShadowCopy(const std::filesystem::path& sourcePath,
                                        const std::filesystem::path& shadowDirectory,
                                        std::filesystem::path& outShadowPath)
{
    if (sourcePath.empty() || shadowDirectory.empty())
        return false;

    std::error_code error;
    std::filesystem::create_directories(shadowDirectory, error);
    if (error)
        return false;

    static std::uint64_t shadowCopyCounter = 0;
    ++shadowCopyCounter;

    const auto timestampMs = std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now().time_since_epoch()).count();

    const std::string shadowName = sourcePath.stem().string() +
                                   "_shadow_" +
                                   std::to_string(timestampMs) +
                                   "_" +
                                   std::to_string(shadowCopyCounter) +
                                   sourcePath.extension().string();

    outShadowPath = shadowDirectory / shadowName;

    constexpr int maxAttempts = 6;
    for (int attempt = 0; attempt < maxAttempts; ++attempt)
    {
        error.clear();
        std::filesystem::copy_file(sourcePath, outShadowPath, std::filesystem::copy_options::overwrite_existing, error);
        if (!error)
            return true;

        std::this_thread::sleep_for(std::chrono::milliseconds(40));
    }

    return false;
}

static bool TryLoadScriptAssemblyBindings(MonoDomain* domain,
                                          const std::filesystem::path& shadowDirectory,
                                          const std::filesystem::path& sourcePath,
                                          ScriptAssemblyBindings& outBindings)
{
    if (!domain || sourcePath.empty())
        return false;

    std::filesystem::path pathToLoad;
    if (!TryCreateAssemblyShadowCopy(sourcePath, shadowDirectory, pathToLoad))
    {
        std::cerr << "[Mono] Hot-reload copy failed for script assembly: " << sourcePath << std::endl;
        return false;
    }

    const std::string loadPathString = pathToLoad.string();
    outBindings.assembly = mono_domain_assembly_open(domain, loadPathString.c_str());
    if (!outBindings.assembly)
    {
        std::cerr << "[Mono] Failed to load script assembly shadow copy: " << loadPathString << std::endl;
        return false;
    }

    outBindings.image = mono_assembly_get_image(outBindings.assembly);
    if (!outBindings.image)
    {
        std::cerr << "[Mono] Script assembly image is null: " << loadPathString << std::endl;
        return false;
    }

    outBindings.scriptClass = mono_class_from_name(outBindings.image, "GameScripts", "ScriptEntry");
    if (!outBindings.scriptClass)
    {
        std::cerr << "[Mono] Missing GameScripts.ScriptEntry in: " << loadPathString << std::endl;
        return true;
    }

    outBindings.onStart = mono_class_get_method_from_name(outBindings.scriptClass, "OnEngineStart", 0);
    outBindings.onUpdate = mono_class_get_method_from_name(outBindings.scriptClass, "OnEngineUpdate", 1);
    outBindings.onShutdown = mono_class_get_method_from_name(outBindings.scriptClass, "OnEngineShutdown", 0);
    return true;
}
#endif
