cmake_minimum_required(VERSION 3.20)
project(CppCSharpGameEngine VERSION 0.1 LANGUAGES CXX)

if(POLICY CMP0144)
  cmake_policy(SET CMP0144 NEW)
endif()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

if(CMAKE_SOURCE_DIR STREQUAL CMAKE_BINARY_DIR)
  message(FATAL_ERROR "In-source builds are not allowed. Please create a separate build directory.")
endif()

if(NOT DEFINED CMAKE_TOOLCHAIN_FILE)
  message(WARNING "CMAKE_TOOLCHAIN_FILE is not set. To use vcpkg set it to the vcpkg toolchain file: -DCMAKE_TOOLCHAIN_FILE=path/to/vcpkg/scripts/buildsystems/vcpkg.cmake")
endif()

# Allow vendoring dependencies inside thirdparty/
option(USE_THIRDPARTY "Use vendored third-party dependencies in thirdparty/" ON)
option(ENGINE_VERBOSE_LOGS "Enable verbose engine logs (Debug/Info)." OFF)
option(ENGINE_RECURSIVE_LOGS "Legacy alias for ENGINE_VERBOSE_LOGS." OFF)

find_package(OpenGL REQUIRED)

if(USE_THIRDPARTY AND EXISTS "${CMAKE_SOURCE_DIR}/thirdparty/CMakeLists.txt")
  add_subdirectory(${CMAKE_SOURCE_DIR}/thirdparty)

  # Ensure alias targets exist even if the subproject did not create them.
  if(NOT TARGET glad::glad)
    add_library(glad::glad INTERFACE IMPORTED)
    if(DEFINED THIRDPARTY_GLAD_INCLUDE_DIR)
      target_include_directories(glad::glad INTERFACE ${THIRDPARTY_GLAD_INCLUDE_DIR})
    endif()
  endif()

  if(NOT TARGET imgui::imgui)
    add_library(imgui::imgui INTERFACE IMPORTED)
    if(DEFINED THIRDPARTY_IMGUI_INCLUDE_DIR)
      target_include_directories(imgui::imgui INTERFACE ${THIRDPARTY_IMGUI_INCLUDE_DIR})
    endif()
  endif()

  if(NOT TARGET entt::entt)
    add_library(entt::entt INTERFACE IMPORTED)
    if(DEFINED THIRDPARTY_ENTT_INCLUDE_DIR)
      target_include_directories(entt::entt INTERFACE ${THIRDPARTY_ENTT_INCLUDE_DIR})
    endif()
  endif()

  if(NOT TARGET glm::glm)
    add_library(glm::glm INTERFACE IMPORTED)
    if(DEFINED THIRDPARTY_GLM_INCLUDE_DIR)
      target_include_directories(glm::glm INTERFACE ${THIRDPARTY_GLM_INCLUDE_DIR})
    endif()
  endif()

  if(NOT TARGET assimp::assimp)
    add_library(assimp::assimp INTERFACE IMPORTED)
    if(DEFINED THIRDPARTY_ASSIMP_INCLUDE_DIR)
      target_include_directories(assimp::assimp INTERFACE ${THIRDPARTY_ASSIMP_INCLUDE_DIR})
    endif()
  endif()

  if(NOT TARGET RapidJSON::RapidJSON)
    add_library(RapidJSON::RapidJSON INTERFACE IMPORTED)
    if(DEFINED THIRDPARTY_RAPIDJSON_INCLUDE_DIR)
      target_include_directories(RapidJSON::RapidJSON INTERFACE ${THIRDPARTY_RAPIDJSON_INCLUDE_DIR})
    endif()
    target_compile_definitions(RapidJSON::RapidJSON INTERFACE RAPIDJSON_HAS_STDSTRING=1)
  endif()
else()
  # Find dependencies via system/vcpkg
  find_package(SDL2 REQUIRED)
  find_package(glad CONFIG REQUIRED)
  find_package(imgui CONFIG REQUIRED)
  find_package(EnTT CONFIG REQUIRED)
  find_package(glm CONFIG QUIET)
  find_package(assimp CONFIG QUIET)
  find_package(RapidJSON CONFIG QUIET)

  if(NOT TARGET entt::entt)
    add_library(entt::entt INTERFACE IMPORTED)
    if(TARGET EnTT::EnTT)
      set_target_properties(entt::entt PROPERTIES
        INTERFACE_LINK_LIBRARIES EnTT::EnTT
      )
    endif()
  endif()

  if(NOT TARGET assimp::assimp)
    add_library(assimp::assimp INTERFACE IMPORTED)
    add_compile_definitions(ENGINE_ASSIMP_DISABLED=1)
  endif()

  if(NOT TARGET glm::glm)
    add_library(glm::glm INTERFACE IMPORTED)
    if(EXISTS "${CMAKE_SOURCE_DIR}/thirdparty/glm")
      target_include_directories(glm::glm INTERFACE "${CMAKE_SOURCE_DIR}/thirdparty/glm")
    endif()
  endif()

  if(NOT TARGET RapidJSON::RapidJSON)
    add_library(RapidJSON::RapidJSON INTERFACE IMPORTED)

    if(DEFINED RAPIDJSON_INCLUDE_DIRS)
      target_include_directories(RapidJSON::RapidJSON INTERFACE ${RAPIDJSON_INCLUDE_DIRS})
    elseif(DEFINED RapidJSON_INCLUDE_DIRS)
      target_include_directories(RapidJSON::RapidJSON INTERFACE ${RapidJSON_INCLUDE_DIRS})
    elseif(EXISTS "${CMAKE_SOURCE_DIR}/thirdparty/assimp/contrib/rapidjson/include")
      target_include_directories(RapidJSON::RapidJSON INTERFACE "${CMAKE_SOURCE_DIR}/thirdparty/assimp/contrib/rapidjson/include")
    endif()
  endif()

  target_compile_definitions(RapidJSON::RapidJSON INTERFACE RAPIDJSON_HAS_STDSTRING=1)
endif()

# Mono support (optional)
# If Mono is available, it will be used for scripting. Otherwise the engine builds without scripting.
set(_MONO_ROOT_HINT "")
if(DEFINED Mono_ROOT AND NOT "${Mono_ROOT}" STREQUAL "")
  set(_MONO_ROOT_HINT "${Mono_ROOT}")
elseif(DEFINED MONO_ROOT AND NOT "${MONO_ROOT}" STREQUAL "")
  set(_MONO_ROOT_HINT "${MONO_ROOT}")
elseif(DEFINED ENV{Mono_ROOT} AND NOT "$ENV{Mono_ROOT}" STREQUAL "")
  set(_MONO_ROOT_HINT "$ENV{Mono_ROOT}")
elseif(DEFINED ENV{MONO_ROOT} AND NOT "$ENV{MONO_ROOT}" STREQUAL "")
  set(_MONO_ROOT_HINT "$ENV{MONO_ROOT}")
endif()

if(NOT "${_MONO_ROOT_HINT}" STREQUAL "")
  set(MONO_ROOT "${_MONO_ROOT_HINT}" CACHE PATH "Mono install root" FORCE)
else()
  set(MONO_ROOT "${CMAKE_SOURCE_DIR}/thirdparty/mono" CACHE PATH "Mono install root")
endif()

find_package(Mono QUIET)

if(NOT Mono_FOUND)
  find_path(Mono_INCLUDE_DIR
    NAMES mono/jit/jit.h
    HINTS "${MONO_ROOT}"
    PATH_SUFFIXES include/mono-2.0
  )

  find_library(Mono_LIBRARY
    NAMES mono-2.0-sgen monosgen-2.0 mono-2.0
    HINTS "${MONO_ROOT}"
    PATH_SUFFIXES lib
  )

  if(Mono_INCLUDE_DIR AND Mono_LIBRARY)
    set(Mono_FOUND TRUE)
    set(Mono_INCLUDE_DIRS "${Mono_INCLUDE_DIR}")
    set(Mono_LIBRARIES "${Mono_LIBRARY}")
    message(STATUS "Mono found via manual fallback: ${MONO_ROOT}")
  endif()
endif()

if(Mono_FOUND)
  message(STATUS "Mono found: ${Mono_INCLUDE_DIRS}")
  if(NOT TARGET Mono::Mono)
    add_library(Mono::Mono INTERFACE IMPORTED)

    if(DEFINED Mono_INCLUDE_DIRS)
      set_target_properties(Mono::Mono PROPERTIES
        INTERFACE_INCLUDE_DIRECTORIES "${Mono_INCLUDE_DIRS}"
      )
    endif()

    if(DEFINED Mono_LIBRARIES)
      set_target_properties(Mono::Mono PROPERTIES
        INTERFACE_LINK_LIBRARIES "${Mono_LIBRARIES}"
      )
    endif()
  endif()
else()
  message(WARNING "Mono not found; scripting will be disabled.")
  add_library(Mono::Mono INTERFACE IMPORTED)
  set_target_properties(Mono::Mono PROPERTIES
    INTERFACE_INCLUDE_DIRECTORIES "${CMAKE_SOURCE_DIR}/src/engine/Scripting"
  )
endif()

# Engine library
add_library(Engine STATIC
  src/engine/Engine.cpp
  src/engine/Core/Logger.cpp
  src/engine/Animation/AnimationClip.cpp
  src/engine/Assets/AssetDatabase.cpp
  src/engine/Assets/AssetImportPipeline.cpp
  src/engine/Assets/ProjectContext.cpp
  src/engine/ECS/AnimatorSystem.cpp
  src/engine/ECS/Scene.cpp
  src/engine/Platform/ExplorerDialog.cpp
  src/engine/Platform/AuxiliaryWindowManager.cpp
  src/engine/Platform/SDLInputState.cpp
  src/engine/Render/DebugDraw.cpp
  src/engine/Platform/SDLWindow.cpp
  src/engine/Render/Renderer.cpp
  src/engine/Render/Shader.cpp
  src/engine/Render/Texture.cpp
  src/engine/Scripting/MonoRuntime.cpp
)

if(ENGINE_VERBOSE_LOGS OR ENGINE_RECURSIVE_LOGS)
  target_compile_definitions(Engine PUBLIC ENGINE_VERBOSE_LOGS=1)
endif()

target_include_directories(Engine PUBLIC src)

if(USE_THIRDPARTY)
  # stb_image.h — vendored directly in thirdparty/
  target_include_directories(Engine PUBLIC ${CMAKE_SOURCE_DIR}/thirdparty)

  # IconFontCppHeaders
  target_include_directories(Engine PUBLIC ${CMAKE_SOURCE_DIR}/thirdparty/IconFontCppHeaders)

  # ImGui headers (submodule or FetchContent)
  if(DEFINED imgui_SOURCE_DIR)
    target_include_directories(Engine PUBLIC ${imgui_SOURCE_DIR})
  elseif(DEFINED THIRDPARTY_IMGUI_INCLUDE_DIR)
    target_include_directories(Engine PUBLIC ${THIRDPARTY_IMGUI_INCLUDE_DIR})
  endif()

  # Glad headers — submodule uses glad-generated/include, FetchContent used glad_BINARY_DIR
  if(DEFINED THIRDPARTY_GLAD_INCLUDE_DIR)
    target_include_directories(Engine PUBLIC ${THIRDPARTY_GLAD_INCLUDE_DIR})
  elseif(DEFINED glad_BINARY_DIR)
    target_include_directories(Engine PUBLIC ${glad_BINARY_DIR}/include)
  endif()
endif()

target_link_libraries(Engine PUBLIC
  SDL2::SDL2
  SDL2::SDL2main
  OpenGL::GL
  glad::glad
  imgui::imgui
  entt::entt
  glm::glm
  assimp::assimp
  RapidJSON::RapidJSON
  Mono::Mono
)

if(WIN32)
  target_link_libraries(Engine PRIVATE ole32 shell32)
endif()

if(Mono_FOUND AND DEFINED MONO_ROOT)
  file(TO_CMAKE_PATH "${MONO_ROOT}" _MONO_ROOT_CMAKE_PATH)
  target_compile_definitions(Engine PRIVATE ENGINE_MONO_ROOT_PATH="${_MONO_ROOT_CMAKE_PATH}")
endif()

if(NOT Mono_FOUND)
  target_compile_definitions(Engine PUBLIC ENGINE_MONO_DISABLED=1)
endif()

# Application executable
add_executable(App src/app/main.cpp)

target_link_libraries(App PRIVATE Engine)

# Editor executable
add_executable(Editor
  src/editor/main.cpp
  src/editor/BootManager.cpp
)

target_link_libraries(Editor PRIVATE Engine)

if(Mono_FOUND)
  set(_MONO_RUNTIME_DLLS
    "${MONO_ROOT}/bin/mono-2.0-sgen.dll"
    "${MONO_ROOT}/bin/MonoPosixHelper.dll"
  )

  set(_MONO_RUNTIME_TARGETS App Editor)

  foreach(_runtime_target IN LISTS _MONO_RUNTIME_TARGETS)
    foreach(_mono_dll IN LISTS _MONO_RUNTIME_DLLS)
      if(EXISTS "${_mono_dll}")
        add_custom_command(TARGET ${_runtime_target} POST_BUILD
          COMMAND ${CMAKE_COMMAND} -E copy_if_different
            "${_mono_dll}"
            "$<TARGET_FILE_DIR:${_runtime_target}>"
          COMMENT "Copying Mono runtime DLL for ${_runtime_target}: ${_mono_dll}"
        )
      endif()
    endforeach()
  endforeach()
endif()

if(TARGET assimp)
  foreach(_runtime_target IN ITEMS App Editor)
    add_custom_command(TARGET ${_runtime_target} POST_BUILD
      COMMAND ${CMAKE_COMMAND} -E copy_if_different
        "$<TARGET_FILE:assimp>"
        "$<TARGET_FILE_DIR:${_runtime_target}>"
      COMMENT "Copying Assimp runtime DLL for ${_runtime_target}"
    )
  endforeach()
endif()

if(WIN32)
  foreach(_runtime_target IN ITEMS App Editor)
    add_custom_command(TARGET ${_runtime_target} POST_BUILD
      COMMAND ${CMAKE_COMMAND} -E copy_if_different
        $<TARGET_RUNTIME_DLLS:${_runtime_target}>
        $<TARGET_FILE_DIR:${_runtime_target}>
      COMMAND_EXPAND_LISTS
      COMMENT "Copying runtime dependency DLLs for ${_runtime_target}"
    )
  endforeach()
endif()
