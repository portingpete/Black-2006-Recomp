cmake_minimum_required(VERSION 3.20)
project(apu_pitch_test LANGUAGES C)
if(NOT WIN32)
    message(FATAL_ERROR "This isolated production VP regression requires Windows.")
endif()
set(XBOXRECOMP_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../third_party/xboxrecomp"
    CACHE PATH "Pinned XboxRecomp checkout with patches/xboxrecomp.patch applied")
set(DSP56300_SOURCE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../.work/err009-dsp56300"
    CACHE PATH "Pinned DSP56300 source checkout")
set(APU_VP_TEST_SOURCE "${XBOXRECOMP_DIR}/src/apu/apu_vp.c"
    CACHE FILEPATH "Production VP source, or a baseline for a negative control")
file(TO_CMAKE_PATH "${APU_VP_TEST_SOURCE}" APU_VP_TEST_INCLUDE)
add_executable(apu-pitch-test test_main.c)
target_include_directories(apu-pitch-test PRIVATE
    "${XBOXRECOMP_DIR}/src/apu" "${XBOXRECOMP_DIR}/src"
    "${DSP56300_SOURCE_DIR}/crates/emu-ffi/include")
target_compile_definitions(apu-pitch-test PRIVATE
    APU_VP_TEST_SOURCE="${APU_VP_TEST_INCLUDE}" _CRT_SECURE_NO_WARNINGS _USE_MATH_DEFINES)
set_property(TARGET apu-pitch-test PROPERTY C_STANDARD 11)
target_link_libraries(apu-pitch-test PRIVATE kernel32)
enable_testing()
add_test(NAME apu-pitch COMMAND apu-pitch-test)
set_tests_properties(apu-pitch PROPERTIES TIMEOUT 15)
