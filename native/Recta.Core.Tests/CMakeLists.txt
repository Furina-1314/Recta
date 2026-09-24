find_package(GTest CONFIG REQUIRED)
find_package(libpqxx CONFIG REQUIRED)
find_package(unofficial-sodium CONFIG REQUIRED)

add_executable(recta_core_tests
    src/PasswordHasherTests.cpp
    src/AuthServiceTests.cpp
)

target_compile_features(recta_core_tests PRIVATE cxx_std_20)

target_link_libraries(recta_core_tests PRIVATE
    Recta::Core
    Recta::Storage
    libpqxx::pqxx
    GTest::gtest_main
)

add_test(NAME Recta.Core.Tests COMMAND recta_core_tests)
