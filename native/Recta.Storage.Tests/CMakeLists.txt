find_package(GTest CONFIG REQUIRED)
find_package(libpqxx CONFIG REQUIRED)

add_executable(recta_storage_tests
    src/StorageTests.cpp
)

target_compile_features(recta_storage_tests PRIVATE cxx_std_20)

target_link_libraries(recta_storage_tests PRIVATE
    Recta::Storage
    Recta::Domain
    libpqxx::pqxx
    GTest::gtest_main
)

add_test(NAME Recta.Storage.Tests COMMAND recta_storage_tests)
