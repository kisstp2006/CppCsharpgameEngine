cmake_minimum_required(VERSION 3.15)
project(glad C)

add_library(glad SHARED
    src/glad.c
)

target_include_directories(glad PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
    $<INSTALL_INTERFACE:include>
)

set_target_properties(glad PROPERTIES
    DEFINE_SYMBOL "GLAD_GLAPI_EXPORT_BUILD"
)
target_compile_definitions(glad PUBLIC GLAD_GLAPI_EXPORT)

if(NOT TARGET glad::glad)
    add_library(glad::glad ALIAS glad)
endif()
