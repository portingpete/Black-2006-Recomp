find_package(Python3 REQUIRED COMPONENTS Interpreter)
if(NOT DEFINED BLACK_MANUAL_SOURCE)
    set(BLACK_MANUAL_SOURCE "${CMAKE_CURRENT_SOURCE_DIR}/../../../src/recomp_manual.c"
        CACHE FILEPATH "Production authored camera wrapper source")
endif()

set(camera_extracted "${CMAKE_CURRENT_BINARY_DIR}/camera.extracted.inc")
add_custom_command(OUTPUT "${camera_extracted}"
    COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/extract_camera.py"
        --source "${BLACK_MANUAL_SOURCE}" --output "${camera_extracted}"
    DEPENDS "${BLACK_MANUAL_SOURCE}" "${CMAKE_CURRENT_SOURCE_DIR}/extract_camera.py"
    COMMENT "Extracting the real authored AO camera helper and validating its wrapper call"
    VERBATIM)
set_source_files_properties("${camera_extracted}" PROPERTIES GENERATED TRUE HEADER_FILE_ONLY TRUE)
add_executable(ao-camera-test test_camera.c "${camera_extracted}")
target_include_directories(ao-camera-test PRIVATE "${CMAKE_CURRENT_BINARY_DIR}")
set_property(TARGET ao-camera-test PROPERTY C_STANDARD 11)
set_property(TARGET ao-camera-test PROPERTY C_STANDARD_REQUIRED ON)
if(MSVC)
    target_compile_definitions(ao-camera-test PRIVATE _CRT_SECURE_NO_WARNINGS)
    target_compile_options(ao-camera-test PRIVATE /W4 /fp:strict)
else()
    target_compile_options(ao-camera-test PRIVATE -Wall -Wextra -frounding-math -fno-fast-math)
    if(NOT WIN32)
        target_link_libraries(ao-camera-test PRIVATE m)
    endif()
endif()
add_test(NAME ao-camera-controls COMMAND ao-camera-test)
add_test(NAME ao-camera-trace-controls COMMAND ao-camera-test trace)
set_tests_properties(ao-camera-controls ao-camera-trace-controls PROPERTIES TIMEOUT 15)
