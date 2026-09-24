# 存储层:唯一允许接触 PostgreSQL 的模块(pqxx)。
# 不依赖 Recta::Domain——仓储以 int64 分往返,Money 换算归服务层(P5)。
find_package(libpqxx CONFIG REQUIRED)

add_library(recta_storage STATIC
    src/EnvConfig.cpp
    src/UsersRepo.cpp
    src/StudentAccountsRepo.cpp
    src/EntityAccountsRepo.cpp
    src/RequestRepo.cpp
    src/LedgerRepo.cpp
)

add_library(Recta::Storage ALIAS recta_storage)

target_include_directories(recta_storage PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
)

target_compile_features(recta_storage PUBLIC cxx_std_20)

target_link_libraries(recta_storage PRIVATE libpqxx::pqxx)
