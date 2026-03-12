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

static bool TryGetFileSize(const std::filesystem::path& path, std::uintmax_t& outFileSize)
{
    std::error_code error;
    outFileSize = std::filesystem::file_size(path, error);
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

    const std::string shadowSetName = sourcePath.stem().string() +
                                      "_shadowset_" +
                                      std::to_string(timestampMs) +
                                      "_" +
                                      std::to_string(shadowCopyCounter);

    const std::filesystem::path shadowSetDirectory = shadowDirectory / shadowSetName;
    outShadowPath = shadowSetDirectory / sourcePath.filename();

    constexpr int maxAttempts = 30;
    for (int attempt = 0; attempt < maxAttempts; ++attempt)
    {
        error.clear();
        std::filesystem::create_directories(shadowSetDirectory, error);
        if (error)
        {
            std::this_thread::sleep_for(std::chrono::milliseconds(75));
            continue;
        }

        error.clear();
        std::filesystem::copy_file(sourcePath, outShadowPath, std::filesystem::copy_options::overwrite_existing, error);
        if (!error)
        {
            // Keep direct managed dependencies next to the shadow copy so Mono can resolve them by simple name.
            std::error_code iterError;
            const std::filesystem::path sourceDirectory = sourcePath.parent_path();
            for (const auto& entry : std::filesystem::directory_iterator(sourceDirectory, iterError))
            {
                if (iterError)
                    break;

                std::error_code statusError;
                if (!entry.is_regular_file(statusError) || statusError)
                    continue;

                const std::filesystem::path dependencyPath = entry.path();
                if (dependencyPath == sourcePath)
                    continue;

                std::string extension = dependencyPath.extension().string();
                for (char& c : extension)
                    c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));

                if (extension != ".dll")
                    continue;

                const std::filesystem::path targetPath = shadowSetDirectory / dependencyPath.filename();
                std::error_code copyDependencyError;
                std::filesystem::copy_file(dependencyPath, targetPath, std::filesystem::copy_options::overwrite_existing, copyDependencyError);
                if (copyDependencyError)
                {
                    EngineLogger::Warningf("Mono",
                                           "Failed to shadow-copy dependency ",
                                           dependencyPath,
                                           " -> ",
                                           targetPath,
                                           " (",
                                           copyDependencyError.message(),
                                           ")");
                }
            }

            return true;
        }

        std::this_thread::sleep_for(std::chrono::milliseconds(75));
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
        EngineLogger::Errorf("Mono", "Hot-reload copy failed for script assembly: ", sourcePath);
        return false;
    }

    const std::string loadPathString = pathToLoad.string();

    std::error_code fileSizeError;
    const std::uintmax_t fileSize = std::filesystem::file_size(pathToLoad, fileSizeError);
    if (fileSizeError || fileSize == 0 || fileSize > static_cast<std::uintmax_t>(std::numeric_limits<int>::max()))
    {
        EngineLogger::Errorf("Mono", "Invalid script assembly shadow copy size: ", loadPathString);
        return false;
    }

    std::vector<char> assemblyBytes(static_cast<std::size_t>(fileSize));
    {
        std::ifstream input(loadPathString, std::ios::binary);
        if (!input)
        {
            EngineLogger::Errorf("Mono", "Failed to open script assembly shadow copy: ", loadPathString);
            return false;
        }

        input.read(assemblyBytes.data(), static_cast<std::streamsize>(assemblyBytes.size()));
        if (!input)
        {
            EngineLogger::Errorf("Mono", "Failed to read script assembly shadow copy: ", loadPathString);
            return false;
        }
    }

    MonoImageOpenStatus imageStatus = MONO_IMAGE_OK;
    MonoImage* transientImage = mono_image_open_from_data_full(assemblyBytes.data(),
                                                                static_cast<uint32_t>(assemblyBytes.size()),
                                                                1,
                                                                &imageStatus,
                                                                0);
    if (!transientImage)
    {
        EngineLogger::Errorf("Mono",
                             "Failed to open script assembly image from data: ",
                             loadPathString,
                             " (",
                             mono_image_strerror(imageStatus),
                             ")");
        return false;
    }

    MonoImageOpenStatus assemblyStatus = MONO_IMAGE_OK;
    outBindings.assembly = mono_assembly_load_from_full(transientImage,
                                                        loadPathString.c_str(),
                                                        &assemblyStatus,
                                                        0);
    if (!outBindings.assembly)
    {
        EngineLogger::Errorf("Mono",
                             "Failed to load script assembly shadow copy: ",
                             loadPathString,
                             " (",
                             mono_image_strerror(assemblyStatus),
                             ")");
        mono_image_close(transientImage);
        return false;
    }

    outBindings.image = mono_assembly_get_image(outBindings.assembly);
    if (!outBindings.image)
    {
        EngineLogger::Errorf("Mono", "Script assembly image is null: ", loadPathString);
        return false;
    }

    outBindings.scriptClass = mono_class_from_name(outBindings.image, "GameScripts", "ScriptEntry");
    if (!outBindings.scriptClass)
    {
        outBindings.onStart = nullptr;
        outBindings.onUpdate = nullptr;
        outBindings.onShutdown = nullptr;
        return true;
    }

    outBindings.onStart = mono_class_get_method_from_name(outBindings.scriptClass, "OnEngineStart", 0);
    outBindings.onUpdate = mono_class_get_method_from_name(outBindings.scriptClass, "OnEngineUpdate", 1);
    outBindings.onShutdown = mono_class_get_method_from_name(outBindings.scriptClass, "OnEngineShutdown", 0);
    return true;
}
#endif
