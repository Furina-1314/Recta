# C ABI 导出层:recta_capi.dll / librecta_capi.so——Avalonia 前端经 P/Invoke 调用。
find_package(nlohmann_json CONFIG REQUIRED)

add_library(recta_capi SHARED
    src/RectaCApi.cpp
)

set_target_properties(recta_capi PROPERTIES
    OUTPUT_NAME recta_capi
    MSVC_RUNTIME_LIBRARY "MultiThreadedDLL"   # 与 vcpkg x64-windows-static-md 的动态 CRT 一致
)

target_include_directories(recta_capi PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
)

target_compile_features(recta_capi PUBLIC cxx_std_20)
target_compile_definitions(recta_capi PRIVATE RECTA_API_EXPORTS)

# 开发/测试构建暴露 recta_dev_truncate_all;发布打包必须 -DRECTA_DEV_TOOLS=OFF。
option(RECTA_DEV_TOOLS "Expose dev-only C ABI helpers (truncate)" ON)
if(RECTA_DEV_TOOLS)
    target_compile_definitions(recta_capi PRIVATE RECTA_DEV_TOOLS)
endif()

target_link_libraries(recta_capi PRIVATE
    Recta::Core
    nlohmann_json::nlohmann_json
)
