cmake_minimum_required(VERSION 3.20)
project(ThirdParty NONE)

include(FetchContent)

# By default, FetchContent uses <build-dir>/_deps and checks out sources there.
# This CMake file exists so that the root project can simply add_subdirectory(thirdparty) and
# get common targets available (SDL2, glad, imgui).

# SDL2
FetchContent_Declare(
  SDL2
  GIT_REPOSITORY https://github.com/libsdl-org/SDL.git
  GIT_TAG release-2.26.5
)
FetchContent_MakeAvailable(SDL2)

# Glad
FetchContent_Declare(
  glad
  GIT_REPOSITORY https://github.com/Dav1dde/glad.git
  GIT_TAG v0.1.36
)
FetchContent_MakeAvailable(glad)

# ImGui
FetchContent_Declare(
  imgui
  GIT_REPOSITORY https://github.com/ocornut/imgui.git
  GIT_TAG v1.89.8-docking
)
FetchContent_MakeAvailable(imgui)

# EnTT (header-only ECS library)
FetchContent_Declare(
  entt
  GIT_REPOSITORY https://github.com/skypjack/entt.git
  GIT_TAG v3.13.2
)
FetchContent_MakeAvailable(entt)

# Assimp (asset import library)
set(ASSIMP_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(ASSIMP_BUILD_ASSIMP_TOOLS OFF CACHE BOOL "" FORCE)
set(ASSIMP_INSTALL OFF CACHE BOOL "" FORCE)
FetchContent_Declare(
  assimp
  GIT_REPOSITORY https://github.com/assimp/assimp.git
  GIT_TAG v5.4.3
)
FetchContent_MakeAvailable(assimp)

# Expose include directories to the parent project (if it wants them)
set(THIRDPARTY_IMGUI_INCLUDE_DIR "${imgui_SOURCE_DIR}" CACHE PATH "ImGui include directory")
set(THIRDPARTY_GLAD_INCLUDE_DIR "${glad_BINARY_DIR}/include" CACHE PATH "glad include directory")
set(THIRDPARTY_ENTT_INCLUDE_DIR "${entt_SOURCE_DIR}/src" CACHE PATH "EnTT include directory")
set(THIRDPARTY_ASSIMP_INCLUDE_DIR "${assimp_SOURCE_DIR}/include" CACHE PATH "Assimp include directory")

# Mono
# Mono is not generally built from source as part of this project; please install it
# or place a prebuilt Mono SDK under thirdparty/mono and set MONO_ROOT to that folder.

# Provide compatibility alias targets for consumers.
if(TARGET SDL2::SDL2)
  # ok
elseif(NOT TARGET SDL2::SDL2)
  add_library(SDL2::SDL2 INTERFACE IMPORTED)
  set_target_properties(SDL2::SDL2 PROPERTIES
    INTERFACE_INCLUDE_DIRECTORIES "${SDL2_SOURCE_DIR}/include"
  )
endif()

if(NOT TARGET SDL2::SDL2main)
  add_library(SDL2::SDL2main INTERFACE IMPORTED)
endif()

# Glad (built by its own CMake)
if(TARGET glad)
  add_library(glad::glad ALIAS glad)
else()
  add_library(glad::glad INTERFACE IMPORTED)
  set_target_properties(glad::glad PROPERTIES
    INTERFACE_INCLUDE_DIRECTORIES "${glad_BINARY_DIR}/include"
  )
endif()

# ImGui (no built-in CMake - build here)
if(NOT TARGET imgui)
  file(GLOB IMGUI_SOURCES
    "${imgui_SOURCE_DIR}/imgui.cpp"
    "${imgui_SOURCE_DIR}/imgui_draw.cpp"
    "${imgui_SOURCE_DIR}/imgui_tables.cpp"
    "${imgui_SOURCE_DIR}/imgui_widgets.cpp"
    "${imgui_SOURCE_DIR}/backends/imgui_impl_sdl2.cpp"
    "${imgui_SOURCE_DIR}/backends/imgui_impl_opengl3.cpp"
  )

  add_library(imgui STATIC ${IMGUI_SOURCES})
  target_include_directories(imgui PUBLIC ${imgui_SOURCE_DIR})
  target_include_directories(imgui PUBLIC ${imgui_SOURCE_DIR}/backends)

  # ImGui SDL2 backend requires SDL headers and config headers.
  if(EXISTS "${CMAKE_BINARY_DIR}/_deps/sdl2-build/include/SDL.h")
    target_include_directories(imgui PUBLIC "${CMAKE_BINARY_DIR}/_deps/sdl2-build/include")
  endif()
  foreach(conf IN ITEMS include-config-debug include-config-release include-config-mingsizerel)
    if(EXISTS "${CMAKE_BINARY_DIR}/_deps/sdl2-build/${conf}")
      target_include_directories(imgui PUBLIC "${CMAKE_BINARY_DIR}/_deps/sdl2-build/${conf}")
    endif()
  endforeach()

  target_compile_definitions(imgui PUBLIC IMGUI_IMPL_OPENGL_LOADER_GLAD)
endif()

if(NOT TARGET imgui::imgui)
  add_library(imgui::imgui ALIAS imgui)
endif()

if(NOT TARGET entt::entt)
  add_library(entt::entt INTERFACE IMPORTED)
  if(TARGET EnTT::EnTT)
    set_target_properties(entt::entt PROPERTIES
      INTERFACE_LINK_LIBRARIES EnTT::EnTT
    )
  elseif(TARGET entt)
    set_target_properties(entt::entt PROPERTIES
      INTERFACE_LINK_LIBRARIES entt
    )
  else()
    set_target_properties(entt::entt PROPERTIES
      INTERFACE_INCLUDE_DIRECTORIES "${THIRDPARTY_ENTT_INCLUDE_DIR}"
    )
  endif()
endif()

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
