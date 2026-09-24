find_package(GTest CONFIG REQUIRED)

add_executable(recta_domain_tests
    src/MoneyTests.cpp
    src/SplitTests.cpp
    src/AdvanceTests.cpp
    src/ConservationTests.cpp
    src/PermissionTests.cpp
)

target_compile_features(recta_domain_tests PRIVATE cxx_std_20)

target_link_libraries(recta_domain_tests PRIVATE Recta::Domain GTest::gtest_main)

add_test(NAME Recta.Domain.Tests COMMAND recta_domain_tests)
