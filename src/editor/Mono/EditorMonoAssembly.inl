#if ENGINE_MONO_RUNTIME_AVAILABLE
struct EditorAssemblyBindings
{
    MonoAssembly* assembly = nullptr;
    MonoImage* image = nullptr;
    MonoClass* editorClass = nullptr;
    MonoMethod* onStart = nullptr;
    MonoMethod* onUpdate = nullptr;
    MonoMethod* onShutdown = nullptr;
};

static std::filesystem::path FindEditorAssemblyPath()
{
    const auto cwd = std::filesystem::current_path();
    const std::vector<std::filesystem::path> candidates = {
        cwd / "editor" / "bin" / "Debug" / "net472" / "EngineEditor.dll",
        cwd / "editor" / "bin" / "Release" / "net472" / "EngineEditor.dll",
        cwd / "editor" / "bin" / "Debug" / "net8.0" / "EngineEditor.dll",
        cwd / "editor" / "bin" / "Release" / "net8.0" / "EngineEditor.dll",
        cwd / "EngineEditor.dll",
    };

    for (const auto& candidate : candidates)
    {
        if (std::filesystem::exists(candidate))
            return candidate;
    }

    return {};
}

static std::filesystem::path FindEditorProjectPath()
{
    const auto cwd = std::filesystem::current_path();
    const std::vector<std::filesystem::path> candidates = {
        cwd / "editor" / "EngineEditor.csproj",
    };

    for (const auto& candidate : candidates)
    {
        if (std::filesystem::exists(candidate))
            return candidate;
    }

    return {};
}

static bool TryLoadEditorAssemblyBindings(MonoDomain* domain,
                                          const std::filesystem::path& shadowDirectory,
                                          const std::filesystem::path& sourcePath,
                                          EditorAssemblyBindings& outBindings)
{
    if (!domain || sourcePath.empty())
        return false;

    std::filesystem::path pathToLoad;
    if (!TryCreateAssemblyShadowCopy(sourcePath, shadowDirectory, pathToLoad))
    {
        std::cerr << "[Mono] Hot-reload copy failed for editor assembly: " << sourcePath << std::endl;
        return false;
    }

    const std::string loadPathString = pathToLoad.string();
    outBindings.assembly = mono_domain_assembly_open(domain, loadPathString.c_str());
    if (!outBindings.assembly)
    {
        std::cerr << "[Mono] Failed to load editor assembly shadow copy: " << loadPathString << std::endl;
        return false;
    }

    outBindings.image = mono_assembly_get_image(outBindings.assembly);
    if (!outBindings.image)
    {
        std::cerr << "[Mono] Editor assembly image is null: " << loadPathString << std::endl;
        return false;
    }

    outBindings.editorClass = mono_class_from_name(outBindings.image, "EngineEditor", "EditorHost");
    if (!outBindings.editorClass)
    {
        std::cerr << "[Mono] Missing EngineEditor.EditorHost in: " << loadPathString << std::endl;
        return false;
    }

    outBindings.onStart = mono_class_get_method_from_name(outBindings.editorClass, "OnEditorStart", 0);
    outBindings.onUpdate = mono_class_get_method_from_name(outBindings.editorClass, "OnEditorUpdate", 1);
    outBindings.onShutdown = mono_class_get_method_from_name(outBindings.editorClass, "OnEditorShutdown", 0);

    if (!outBindings.onUpdate)
    {
        std::cerr << "[Mono] OnEditorUpdate(float) missing in EngineEditor.EditorHost: " << loadPathString << std::endl;
        return false;
    }

    return true;
}
#endif
