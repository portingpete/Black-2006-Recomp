cmake_minimum_required(VERSION 3.20)
project(black_xbox_recomp C)

set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)
add_compile_definitions(WIN32_LEAN_AND_MEAN NOMINMAX)

set(XBOXRECOMP_DIR "${CMAKE_CURRENT_SOURCE_DIR}/third_party/xboxrecomp" CACHE PATH
    "Path to the patched XboxRecomp toolkit")

# Release symbols let the crash handler identify the original guest function.
if(MSVC)
    string(APPEND CMAKE_C_FLAGS_RELEASE " /Zi")
    string(APPEND CMAKE_CXX_FLAGS_RELEASE " /Zi")
    string(APPEND CMAKE_EXE_LINKER_FLAGS_RELEASE " /DEBUG /OPT:REF /OPT:ICF")
endif()

add_subdirectory("${XBOXRECOMP_DIR}" "${CMAKE_BINARY_DIR}/xboxrecomp")
file(GLOB RECOMP_GEN_SOURCES CONFIGURE_DEPENDS "src/recomp/gen/*.c")
if(NOT RECOMP_GEN_SOURCES)
    message(FATAL_ERROR "Run scripts/setup.ps1 with your retail BLACK Xbox files first.")
endif()

add_executable(${PROJECT_NAME} WIN32
    src/main.c
    src/recomp_manual.c
    ${RECOMP_GEN_SOURCES}
)
target_include_directories(${PROJECT_NAME} PRIVATE
    "${CMAKE_CURRENT_SOURCE_DIR}/src"
    "${CMAKE_CURRENT_SOURCE_DIR}/src/recomp"
    "${XBOXRECOMP_DIR}/include"
)
target_link_libraries(${PROJECT_NAME} PRIVATE
    xboxrecomp d3d11 dxgi dxguid xinput winmm dbghelp
)
if(MSVC)
    target_compile_options(${PROJECT_NAME} PRIVATE /bigobj /MP)
    target_link_options(${PROJECT_NAME} PRIVATE /LARGEADDRESSAWARE)
endif()
