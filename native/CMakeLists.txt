cmake_minimum_required(VERSION 3.24)

project(RectaNative VERSION 0.1.0 LANGUAGES C CXX
        DESCRIPTION "Recta native core (C++20): money domain, approval workflow, settlement, custody ledger")

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

if(MSVC)
    add_compile_options(/utf-8 /W4 /permissive-)
else()
    add_compile_options(-Wall -Wextra -Wpedantic)
endif()

option(RECTA_BUILD_TESTS "Build Recta unit tests" ON)

# 模块依赖方向(禁止循环):
# Domain <- Storage <- Core <- CApi <- (tests)
add_subdirectory(Recta.Domain)
add_subdirectory(Recta.Storage)
add_subdirectory(Recta.Core)
add_subdirectory(Recta.CApi)

if(RECTA_BUILD_TESTS)
    enable_testing()
    add_subdirectory(Recta.Domain.Tests)
    add_subdirectory(Recta.Storage.Tests)
    add_subdirectory(Recta.Core.Tests)
endif()
