#include "resources.h"

namespace gen{
namespace resources {
    std::string getExecutableDirectory() {
        return std::string(RESOURCES_EXECUTABLE_DIRECTORY);
    }

    std::string getFontDataDirectory() {
        return std::string(RESOURCES_FONT_DATA_DIRECTORY);
    }

    std::string getFontDataResource() {
        return std::string(RESOURCES_FONT_DATA_RESOURCE);
    }
}
}
