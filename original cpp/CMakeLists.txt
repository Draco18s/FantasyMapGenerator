cmake_minimum_required(VERSION 2.8.9)

project(map_generation)
set(CMAKE_BUILD_TYPE Release)

if (MSVC)
    set(CMAKE_CXX_FLAGS "${CMAKE_CXX_FLAGS} /O2 /W4 /D_CRT_SECURE_NO_WARNINGS")
else()
    set(CMAKE_CXX_FLAGS "${CMAKE_CXX_FLAGS} -O3 -std=c++11 -Wall -Doff64_t=_off64_t")
endif()

file(GLOB SOURCES "src/*.cpp" "src/argtable3/*.c")

set(CMAKE_RUNTIME_OUTPUT_DIRECTORY_DEBUG ${CMAKE_BINARY_DIR})
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY_RELEASE ${CMAKE_BINARY_DIR})

add_library(objects OBJECT ${SOURCES})
add_executable(map_generation $<TARGET_OBJECTS:objects>)
target_link_libraries(map_generation ${PYTHON_LIBRARIES})

file(COPY "src/citynames.txt" DESTINATION ${CMAKE_BINARY_DIR})
file(COPY "src/fontdata" DESTINATION ${CMAKE_BINARY_DIR})
file(COPY "src/render" DESTINATION "${CMAKE_BINARY_DIR}")

set(RESOURCES_EXECUTABLE_DIRECTORY  ${CMAKE_BINARY_DIR})
set(RESOURCES_FONT_DATA_DIRECTORY   ${CMAKE_BINARY_DIR}/fontdata)
set(RESOURCES_FONT_DATA_RESOURCE    ${CMAKE_BINARY_DIR}/fontdata/fontdata.json)
configure_file(
  "${PROJECT_SOURCE_DIR}/src/resources.h.in"
  "${PROJECT_SOURCE_DIR}/src/resources.h"
)
