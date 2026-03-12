#include "AssetImportPipeline.h"

#include "ProjectContext.h"
#include "engine/Core/Logger.h"

#include <filesystem>
#include <fstream>
#include <string>

#if !defined(__INTELLISENSE__) && !defined(ENGINE_ASSIMP_DISABLED) && __has_include(<assimp/Importer.hpp>) && __has_include(<assimp/postprocess.h>) && __has_include(<assimp/scene.h>)
#define ENGINE_ASSIMP_AVAILABLE 1
#include <assimp/Importer.hpp>
#include <assimp/postprocess.h>
#include <assimp/scene.h>
#else
#define ENGINE_ASSIMP_AVAILABLE 0
#endif

#if !defined(__INTELLISENSE__) && __has_include("stb_image.h")
#define STB_IMAGE_STATIC       // keep stb symbols TU-local, avoids conflicts with Assimp's internal stb_image
#define STB_IMAGE_IMPLEMENTATION
#include "stb_image.h"
#define ENGINE_STB_IMAGE_AVAILABLE 1
#else
#define ENGINE_STB_IMAGE_AVAILABLE 0
#endif

namespace
{
    std::filesystem::path BuildImportedModelsRoot(const ProjectContext& projectContext)
    {
        return projectContext.LibraryRoot() / "Imported" / "Models";
    }

    std::filesystem::path BuildImportedImagesRoot(const ProjectContext& projectContext)
    {
        return projectContext.LibraryRoot() / "Imported" / "Images";
    }

    std::filesystem::path BuildImportedModelPath(const ProjectContext& projectContext, const AssetRecord& record)
    {
        std::string extension = record.sourcePath.extension().generic_string();
        if (extension.empty())
            extension = ".asset";

        return BuildImportedModelsRoot(projectContext) / (std::to_string(record.handle) + extension);
    }

    std::filesystem::path BuildImportedModelMetaPath(const ProjectContext& projectContext, const AssetRecord& record)
    {
        return BuildImportedModelsRoot(projectContext) / (std::to_string(record.handle) + ".meta.json");
    }

    std::filesystem::path BuildImportedImagePath(const ProjectContext& projectContext, const AssetRecord& record)
    {
        std::string extension = record.sourcePath.extension().generic_string();
        if (extension.empty())
            extension = ".asset";

        return BuildImportedImagesRoot(projectContext) / (std::to_string(record.handle) + extension);
    }

    std::filesystem::path BuildImportedImageMetaPath(const ProjectContext& projectContext, const AssetRecord& record)
    {
        return BuildImportedImagesRoot(projectContext) / (std::to_string(record.handle) + ".meta.json");
    }

#if ENGINE_ASSIMP_AVAILABLE
    bool ImportModelWithAssimp(const ProjectContext& projectContext, const AssetRecord& record)
    {
        Assimp::Importer importer;
        const aiScene* scene = importer.ReadFile(
            record.sourcePath.string(),
            aiProcess_Triangulate |
            aiProcess_JoinIdenticalVertices |
            aiProcess_GenSmoothNormals |
            aiProcess_ImproveCacheLocality |
            aiProcess_SortByPType);

        if (!scene)
        {
            EngineLogger::Errorf("Assets",
                                 "Assimp failed to parse model: ", record.sourcePath,
                                 " error=", importer.GetErrorString());
            return false;
        }

        std::size_t vertexCount = 0;
        std::size_t faceCount = 0;
        for (unsigned int i = 0; i < scene->mNumMeshes; ++i)
        {
            const aiMesh* mesh = scene->mMeshes[i];
            if (!mesh)
                continue;

            vertexCount += mesh->mNumVertices;
            faceCount += mesh->mNumFaces;
        }

        const std::filesystem::path importedRoot = BuildImportedModelsRoot(projectContext);
        std::filesystem::create_directories(importedRoot);

        const std::filesystem::path importedModelPath = BuildImportedModelPath(projectContext, record);
        const std::filesystem::path importedMetaPath = BuildImportedModelMetaPath(projectContext, record);

        std::error_code copyError;
        std::filesystem::copy_file(record.sourcePath, importedModelPath, std::filesystem::copy_options::overwrite_existing, copyError);
        if (copyError)
        {
            EngineLogger::Errorf("Assets",
                                 "Failed to cache imported model: ", importedModelPath,
                                 " error=", copyError.message());
            return false;
        }

        std::ofstream metaOutput(importedMetaPath, std::ios::trunc);
        if (!metaOutput.is_open())
        {
            EngineLogger::Errorf("Assets", "Failed to write model meta: ", importedMetaPath);
            return false;
        }

        metaOutput << "{\n";
        metaOutput << "  \"handle\": " << record.handle << ",\n";
        metaOutput << "  \"source\": \"" << record.projectRelativePath.generic_string() << "\",\n";
        metaOutput << "  \"meshCount\": " << scene->mNumMeshes << ",\n";
        metaOutput << "  \"materialCount\": " << scene->mNumMaterials << ",\n";
        metaOutput << "  \"vertexCount\": " << vertexCount << ",\n";
        metaOutput << "  \"faceCount\": " << faceCount << "\n";
        metaOutput << "}\n";

        return true;
    }
#endif

    bool RemoveImportedModelCache(const ProjectContext& projectContext, const AssetRecord& record)
    {
        std::error_code errorCode;
        const std::filesystem::path importedModelPath = BuildImportedModelPath(projectContext, record);
        const std::filesystem::path importedMetaPath = BuildImportedModelMetaPath(projectContext, record);

        if (std::filesystem::exists(importedModelPath))
            std::filesystem::remove(importedModelPath, errorCode);

        if (errorCode)
        {
            EngineLogger::Errorf("Assets",
                                 "Failed to remove cached model: ", importedModelPath,
                                 " error=", errorCode.message());
            return false;
        }

        if (std::filesystem::exists(importedMetaPath))
            std::filesystem::remove(importedMetaPath, errorCode);

        if (errorCode)
        {
            EngineLogger::Errorf("Assets",
                                 "Failed to remove cached model meta: ", importedMetaPath,
                                 " error=", errorCode.message());
            return false;
        }

        return true;
    }

#if ENGINE_STB_IMAGE_AVAILABLE
    bool ImportImageWithStb(const ProjectContext& projectContext, const AssetRecord& record)
    {
        int width = 0, height = 0, channels = 0;
        unsigned char* imageData = stbi_load(record.sourcePath.string().c_str(), &width, &height, &channels, STBI_rgb_alpha);

        if (!imageData)
        {
            EngineLogger::Errorf("Assets",
                                 "stb_image failed to load image: ", record.sourcePath,
                                 " error=", stbi_failure_reason());
            return false;
        }

        const std::filesystem::path importedRoot = BuildImportedImagesRoot(projectContext);
        std::filesystem::create_directories(importedRoot);

        const std::filesystem::path importedImagePath = BuildImportedImagePath(projectContext, record);
        const std::filesystem::path importedMetaPath = BuildImportedImageMetaPath(projectContext, record);

        // For images, we cache the original file since re-encoding can lose quality
        std::error_code copyError;
        std::filesystem::copy_file(record.sourcePath, importedImagePath, std::filesystem::copy_options::overwrite_existing, copyError);
        if (copyError)
        {
            EngineLogger::Errorf("Assets",
                                 "Failed to cache imported image: ", importedImagePath,
                                 " error=", copyError.message());
            stbi_image_free(imageData);
            return false;
        }

        std::ofstream metaOutput(importedMetaPath, std::ios::trunc);
        if (!metaOutput.is_open())
        {
            EngineLogger::Errorf("Assets", "Failed to write image meta: ", importedMetaPath);
            stbi_image_free(imageData);
            return false;
        }

        metaOutput << "{\n";
        metaOutput << "  \"handle\": " << record.handle << ",\n";
        metaOutput << "  \"source\": \"" << record.projectRelativePath.generic_string() << "\",\n";
        metaOutput << "  \"width\": " << width << ",\n";
        metaOutput << "  \"height\": " << height << ",\n";
        metaOutput << "  \"channels\": " << channels << "\n";
        metaOutput << "}\n";

        stbi_image_free(imageData);
        return true;
    }
#endif

    bool RemoveImportedImageCache(const ProjectContext& projectContext, const AssetRecord& record)
    {
        std::error_code errorCode;
        const std::filesystem::path importedImagePath = BuildImportedImagePath(projectContext, record);
        const std::filesystem::path importedMetaPath = BuildImportedImageMetaPath(projectContext, record);

        if (std::filesystem::exists(importedImagePath))
            std::filesystem::remove(importedImagePath, errorCode);

        if (errorCode)
        {
            EngineLogger::Errorf("Assets",
                                 "Failed to remove cached image: ", importedImagePath,
                                 " error=", errorCode.message());
            return false;
        }

        if (std::filesystem::exists(importedMetaPath))
            std::filesystem::remove(importedMetaPath, errorCode);

        if (errorCode)
        {
            EngineLogger::Errorf("Assets",
                                 "Failed to remove cached image meta: ", importedMetaPath,
                                 " error=", errorCode.message());
            return false;
        }

        return true;
    }
}

AssetImportPipeline::Result AssetImportPipeline::Run(
    const ProjectContext& projectContext,
    const std::vector<AssetDatabase::AssetChange>& changes) const
{
    Result result;

    for (const auto& change : changes)
    {
        // Handle model imports
        if (change.record.type == AssetType::ModelSource)
        {
            if (change.kind == AssetDatabase::AssetChangeKind::Deleted)
            {
                if (RemoveImportedModelCache(projectContext, change.record))
                    ++result.removed;
                else
                    ++result.failed;
                continue;
            }

#if ENGINE_ASSIMP_AVAILABLE
            if (ImportModelWithAssimp(projectContext, change.record))
                ++result.imported;
            else
                ++result.failed;
#else
            EngineLogger::Errorf("Assets", "Assimp is disabled; cannot import model: ", change.record.sourcePath);
            ++result.failed;
#endif
            continue;
        }

        // Handle image imports
        if (change.record.type == AssetType::ImageSource)
        {
            if (change.kind == AssetDatabase::AssetChangeKind::Deleted)
            {
                if (RemoveImportedImageCache(projectContext, change.record))
                    ++result.removed;
                else
                    ++result.failed;
                continue;
            }

#if ENGINE_STB_IMAGE_AVAILABLE
            if (ImportImageWithStb(projectContext, change.record))
                ++result.imported;
            else
                ++result.failed;
#else
            EngineLogger::Errorf("Assets", "stb_image is unavailable; cannot import image: ", change.record.sourcePath);
            ++result.failed;
#endif
            continue;
        }
    }

    return result;
}
