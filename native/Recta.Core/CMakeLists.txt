# 服务层:组合 Domain + Storage,实现身份认证与(后续 P5)两阶段流转。
find_package(unofficial-sodium CONFIG REQUIRED)

add_library(recta_core STATIC
    src/PasswordHasher.cpp
    src/AuthService.cpp
    src/WorkflowService.cpp
)

add_library(Recta::Core ALIAS recta_core)

target_include_directories(recta_core PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
)

target_compile_features(recta_core PUBLIC cxx_std_20)

target_link_libraries(recta_core PUBLIC
    Recta::Domain
    Recta::Storage
    unofficial-sodium::sodium
)
