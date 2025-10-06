#ifndef GEN_RENDER_H
#define GEN_RENDER_H

#include <stdio.h>
#include <iostream>
#include <vector>
#include <string>

#include "jsoncons/json.hpp"

#include "resources.h"

namespace gen {
namespace render {

std::string drawSvg(jsoncons::json map, std::string font);

}
}

#endif
