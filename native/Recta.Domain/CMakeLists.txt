# 领域内核:纯 C++,不依赖数据库 / GUI / 网络库(Vibe.md §7)。
add_library(recta_domain STATIC
    src/Money.cpp
    src/Split.cpp
    src/Advance.cpp
    src/Conservation.cpp
    src/Roles.cpp
)

add_library(Recta::Domain ALIAS recta_domain)

target_include_directories(recta_domain PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
)

target_compile_features(recta_domain PUBLIC cxx_std_20)
