cmake_minimum_required(VERSION 3.20)
project(ThirdParty NONE)

# All dependencies are managed as git submodules under thirdparty/.
# After cloning the repo, run:
#   git submodule update --init --recursive
# to check out all dependencies before configuring CMake.

set(_TP_DIR "${CMAKE_CURRENT_SOURCE_DIR}")

# ──────────────────────────────────────────────────────────
# SDL2 (submodule: thirdparty/SDL2)
# ──────────────────────────────────────────────────────────
if(NOT EXISTS "${_TP_DIR}/SDL2/CMakeLists.txt")
  message(FATAL_ERROR "SDL2 submodule is missing. Run: git submodule update --init --recursive")
endif()
add_subdirectory("${_TP_DIR}/SDL2" "${CMAKE_BINARY_DIR}/thirdparty/SDL2-build" EXCLUDE_FROM_ALL)

# ──────────────────────────────────────────────────────────
# Glad (vendored pre-generated: thirdparty/glad-generated)
# ──────────────────────────────────────────────────────────
if(NOT EXISTS "${_TP_DIR}/glad-generated/src/glad.c")
  message(FATAL_ERROR "glad-generated sources are missing under thirdparty/glad-generated/")
endif()
add_subdirectory("${_TP_DIR}/glad-generated" "${CMAKE_BINARY_DIR}/thirdparty/glad-build" EXCLUDE_FROM_ALL)
set(glad_BINARY_DIR "${CMAKE_BINARY_DIR}/thirdparty/glad-build")

# ──────────────────────────────────────────────────────────
# ImGui (submodule: thirdparty/imgui)
# ──────────────────────────────────────────────────────────
if(NOT EXISTS "${_TP_DIR}/imgui/imgui.cpp")
  message(FATAL_ERROR "imgui submodule is missing. Run: git submodule update --init --recursive")
endif()
set(imgui_SOURCE_DIR "${_TP_DIR}/imgui")

# ImGuizmo (vendored: thirdparty/ImGuizmo)
set(imguizmo_SOURCE_DIR "${_TP_DIR}/ImGuizmo")

# ──────────────────────────────────────────────────────────
# EnTT (submodule: thirdparty/entt, header-only)
# ──────────────────────────────────────────────────────────
if(NOT EXISTS "${_TP_DIR}/entt/src/entt/entt.hpp")
  message(FATAL_ERROR "entt submodule is missing. Run: git submodule update --init --recursive")
endif()
set(entt_SOURCE_DIR "${_TP_DIR}/entt")

# ──────────────────────────────────────────────────────────
# GLM (submodule: thirdparty/glm, header-only)
# ──────────────────────────────────────────────────────────
if(NOT EXISTS "${_TP_DIR}/glm/glm/glm.hpp")
  message(FATAL_ERROR "glm submodule is missing. Run: git submodule update --init --recursive")
endif()
set(glm_SOURCE_DIR "${_TP_DIR}/glm")

# ──────────────────────────────────────────────────────────
# Assimp (submodule: thirdparty/assimp)
# ──────────────────────────────────────────────────────────
if(NOT EXISTS "${_TP_DIR}/assimp/CMakeLists.txt")
  message(FATAL_ERROR "assimp submodule is missing. Run: git submodule update --init --recursive")
endif()
set(ASSIMP_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(ASSIMP_BUILD_ASSIMP_TOOLS OFF CACHE BOOL "" FORCE)
set(ASSIMP_INSTALL OFF CACHE BOOL "" FORCE)
set(ASSIMP_USE_STB_IMAGE_STATIC ON CACHE BOOL "" FORCE)  # prevent stbi symbol conflicts
add_subdirectory("${_TP_DIR}/assimp" "${CMAKE_BINARY_DIR}/thirdparty/assimp-build" EXCLUDE_FROM_ALL)
set(assimp_SOURCE_DIR "${_TP_DIR}/assimp")

# ──────────────────────────────────────────────────────────
# Expose include directories to the parent project
# ──────────────────────────────────────────────────────────
set(THIRDPARTY_IMGUI_INCLUDE_DIR "${imgui_SOURCE_DIR}" CACHE PATH "ImGui include directory")
set(THIRDPARTY_GLAD_INCLUDE_DIR "${_TP_DIR}/glad-generated/include" CACHE PATH "glad include directory")
set(THIRDPARTY_ENTT_INCLUDE_DIR "${entt_SOURCE_DIR}/src" CACHE PATH "EnTT include directory")
set(THIRDPARTY_GLM_INCLUDE_DIR "${glm_SOURCE_DIR}" CACHE PATH "GLM include directory")
set(THIRDPARTY_ASSIMP_INCLUDE_DIR "${assimp_SOURCE_DIR}/include" CACHE PATH "Assimp include directory")
set(THIRDPARTY_RAPIDJSON_INCLUDE_DIR "${assimp_SOURCE_DIR}/contrib/rapidjson/include" CACHE PATH "RapidJSON include directory")

# Mono
# Mono is not generally built from source as part of this project; please install it
# or place a prebuilt Mono SDK under thirdparty/mono and set MONO_ROOT to that folder.

# ──────────────────────────────────────────────────────────
# Compatibility alias targets for consumers
# ──────────────────────────────────────────────────────────
if(NOT TARGET SDL2::SDL2 AND TARGET SDL2)
  add_library(SDL2::SDL2 ALIAS SDL2)
elseif(NOT TARGET SDL2::SDL2)
  add_library(SDL2::SDL2 INTERFACE IMPORTED)
  set_target_properties(SDL2::SDL2 PROPERTIES
    INTERFACE_INCLUDE_DIRECTORIES "${_TP_DIR}/SDL2/include"
  )
endif()

if(NOT TARGET SDL2::SDL2main AND TARGET SDL2main)
  add_library(SDL2::SDL2main ALIAS SDL2main)
elseif(NOT TARGET SDL2::SDL2main)
  add_library(SDL2::SDL2main INTERFACE IMPORTED)
endif()

# glad::glad alias — glad target is created by glad-generated/CMakeLists.txt
if(NOT TARGET glad::glad)
  if(TARGET glad)
    add_library(glad::glad ALIAS glad)
  else()
    add_library(glad::glad INTERFACE IMPORTED)
    set_target_properties(glad::glad PROPERTIES
      INTERFACE_INCLUDE_DIRECTORIES "${THIRDPARTY_GLAD_INCLUDE_DIR}"
    )
  endif()
endif()

# ImGui (built here using submodule headers)
if(NOT TARGET imgui)
  file(GLOB IMGUI_SOURCES
    "${imgui_SOURCE_DIR}/imgui.cpp"
    "${imgui_SOURCE_DIR}/imgui_draw.cpp"
    "${imgui_SOURCE_DIR}/imgui_tables.cpp"
    "${imgui_SOURCE_DIR}/imgui_widgets.cpp"
    "${imgui_SOURCE_DIR}/backends/imgui_impl_sdl2.cpp"
    "${imgui_SOURCE_DIR}/backends/imgui_impl_opengl3.cpp"
  )

  if(EXISTS "${imguizmo_SOURCE_DIR}/ImGuizmo.cpp")
    list(APPEND IMGUI_SOURCES "${imguizmo_SOURCE_DIR}/ImGuizmo.cpp")
  endif()

  add_library(imgui STATIC ${IMGUI_SOURCES})
  target_include_directories(imgui PUBLIC "${imgui_SOURCE_DIR}")
  target_include_directories(imgui PUBLIC "${imgui_SOURCE_DIR}/backends")
  if(EXISTS "${imguizmo_SOURCE_DIR}/ImGuizmo.h")
    target_include_directories(imgui PUBLIC "${imguizmo_SOURCE_DIR}")
  endif()

  # ImGui SDL2 backend needs SDL2 headers
  if(TARGET SDL2)
    target_link_libraries(imgui PRIVATE SDL2)
  endif()
  if(EXISTS "${_TP_DIR}/SDL2/include")
    target_include_directories(imgui PUBLIC "${_TP_DIR}/SDL2/include")
  endif()

  # ImGui needs SDL2 generated config headers (platform-specific SDL_config.h etc.)
  foreach(_sdl_conf_dir
      "${CMAKE_BINARY_DIR}/thirdparty/SDL2-build/include"
      "${CMAKE_BINARY_DIR}/thirdparty/SDL2-build/include-config-debug"
      "${CMAKE_BINARY_DIR}/thirdparty/SDL2-build/include-config-release"
  )
    if(EXISTS "${_sdl_conf_dir}")
      target_include_directories(imgui PUBLIC "${_sdl_conf_dir}")
    endif()
  endforeach()

  target_compile_definitions(imgui PUBLIC IMGUI_IMPL_OPENGL_LOADER_GLAD)
  # Give imgui access to glad headers for the OpenGL3 backend
  target_include_directories(imgui PUBLIC "${THIRDPARTY_GLAD_INCLUDE_DIR}")
endif()

if(NOT TARGET imgui::imgui)
  add_library(imgui::imgui ALIAS imgui)
endif()

# EnTT (header-only)
if(NOT TARGET entt::entt)
  add_library(entt::entt INTERFACE IMPORTED)
  set_target_properties(entt::entt PROPERTIES
    INTERFACE_INCLUDE_DIRECTORIES "${THIRDPARTY_ENTT_INCLUDE_DIR}"
  )
endif()

# GLM (header-only)
if(NOT TARGET glm::glm)
  add_library(glm::glm INTERFACE IMPORTED)
  set_target_properties(glm::glm PROPERTIES
    INTERFACE_INCLUDE_DIRECTORIES "${THIRDPARTY_GLM_INCLUDE_DIR}"
  )
endif()

# assimp::assimp alias — assimp target is created by its CMakeLists.txt
if(NOT TARGET assimp::assimp)
  if(TARGET assimp)
    add_library(assimp::assimp ALIAS assimp)
  else()
    add_library(assimp::assimp INTERFACE IMPORTED)
    set_target_properties(assimp::assimp PROPERTIES
      INTERFACE_INCLUDE_DIRECTORIES "${THIRDPARTY_ASSIMP_INCLUDE_DIR}"
    )
  endif()
endif()

# RapidJSON (header-only; vendored via assimp/contrib)
if(NOT TARGET RapidJSON::RapidJSON)
  add_library(RapidJSON::RapidJSON INTERFACE IMPORTED)
  set_target_properties(RapidJSON::RapidJSON PROPERTIES
    INTERFACE_INCLUDE_DIRECTORIES "${THIRDPARTY_RAPIDJSON_INCLUDE_DIR}"
    INTERFACE_COMPILE_DEFINITIONS "RAPIDJSON_HAS_STDSTRING=1"
  )
endif()
