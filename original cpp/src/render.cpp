#include "render.h"

const std::string STYLE_BACKGROUND = "fill='#ffffffff'";
const std::string STYLE_BORDER = "fill='#00000000' stroke='rgb(0,0,0)' stroke-width='6.0' stroke-linecap='butt' stroke-linejoin='bevel' stroke-dasharray='3 4'";
const std::string STYLE_CONTOUR = "fill='#00000000' stroke='rgb(0,0,0)' stroke-width='1.5' stroke-linecap='round' stroke-linejoin='round'";
const std::string STYLE_RIVER = "fill='#00000000' stroke='rgb(0,0,0)' stroke-width='2.5'";
const std::string STYLE_SLOPE = "stroke='rgb(0,0,0)' stroke-width='1.0' opacity='0.75'";

const std::string STYLE_CITY_MARKER_OUTER = "fill='#000000ff' stroke='rgb(0,0,0)'";
const std::string STYLE_CITY_MARKER_INNER = "fill='#ffffffff'";
const std::string STYLE_TOWN_MARKER = "fill='#000000ff' stroke='rgb(0,0,0)'";

const std::string STYLE_TEXT = "fill='#000000ff' lengthAdjust='spacingAndGlyphs'";
const std::string STYLE_TEXT_BOX = "fill='#ffffffff'";

const int CITY_MARKER_OUTER_RADIUS = 10;
const int CITY_MARKER_INNER_RADIUS = 5;
const int TOWN_MARKER_RADIUS = 5;

void svgStart(std::ostream &o, int w, int h) {
    o << "<?xml version='1.0'?>\n";
    o << "<svg width='" << w << "' height='" << h << "'\n";
    o << "     xmlns='http://www.w3.org/2000/svg'\n";
    o << "     xmlns:xlink='http://www.w3.org/1999/xlink'>\n";
}

void svgEnd(std::ostream &o) {
    o << "</svg>\n";
}

void svgRect(std::ostream &o, int x, int y, int w, int h, std::string style) {
    o << "<rect x='" << x
      << "' y='" << y
      << "' width='" << w
      << "' height='" << h
      << "' " << style << "/>\n";
}

void svgGroup(std::ostream &o, std::string style) {
    o << "<g " << style << " >\n";
}

void svgGend(std::ostream &o) {
    o << "</g>\n";
}

void svgCircle(std::ostream &o, int x, int y, int radius, std::string style) {
    o << "<circle cx='" << x
      << "' cy='" << y
      << "' r='" << radius
      << "' " << style << "/>\n";
}

void svgText(std::ostream &o, int x, int y, std::string text, std::string style) {
    o << "<text x='" << x
      << "' y='" << y
      << "' " << style << ">"
      << text
      << "</text>\n";
}

void svgLine(std::ostream &o, int x1, int y1, int x2, int y2, std::string style) {
    o << "<line x1='" << x1
      << "' y1='" << y1
      << "' x2='" << x2
      << "' y2='" << y2
      << "' " << style << "/>\n";
}

void svgPolyline(std::ostream &o, std::vector<int> xs, std::vector<int> ys, std::string style) {
    o << "<polyline points='";

    int n = xs.size() < ys.size() ? xs.size() : ys.size();

    for (int i=0; i<n; i++) {
        o << xs[i] << "," << ys[i] << " ";
    }
    o << "' " << style << " />\n";
}

// rescale scales v from the range 0.0..1.0 to an integer 0..max
int rescale(double v, int max) {
    return (int)(v*double(max) + 0.5);
}

void drawSegments(std::ostream &o, std::vector<double> segments, int imgw, int imgh, std::string style) {
    svgGroup(o, style);

    // segments are stored sequentially as (x1, y1, x2, y2) tuples.
    for (int i=0; i<segments.size(); i+=4) {
        double nx1 = segments[i+0];
        double ny1 = segments[i+1];
        double nx2 = segments[i+2];
        double ny2 = segments[i+3];

        int x1 = rescale(nx1, imgw);
        int y1 = rescale(1.0-ny1, imgh);
        int x2 = rescale(nx2, imgw);
        int y2 = rescale(1.0-ny2, imgh);

        svgLine(o, x1, y1, x2, y2, "");
    }

    svgGend(o);
}

void drawPaths(std::ostream &o, std::vector<std::vector<double>> paths, int imgw, int imgh, std::string style) {
    svgGroup(o, style);

    for (int i=0; i<paths.size(); i+=1) {
        std::vector<double> path = paths[i];
        std::vector<int> xs;
        std::vector<int> ys;

        for (int j=0; j<path.size(); j+=2) {
            double nx = path[j+0];
            double ny = path[j+1];

            xs.push_back(rescale(nx, imgw));
            ys.push_back(rescale(1.0-ny, imgh));
        }
        svgPolyline(o, xs, ys, "");
    }

    svgGend(o);
}

void drawCircles(std::ostream &o, std::vector<double> points, int imgw, int imgh, int radius, std::string style) {
    svgGroup(o, style);

    for (int i=0; i<points.size(); i+=4) {
        double nx = points[i+0];
        double ny = points[i+1];

        int x = rescale(nx, imgw);
        int y = rescale(1.0-ny, imgh);

        svgCircle(o, x, y, radius, "");
    }

    svgGend(o);
}

void drawLabels(std::ostream &o, std::vector<jsoncons::json> labels, int imgw, int imgh, std::string styleText, std::string styleBox) {
    svgGroup(o, styleBox);
    for (int i=0; i<labels.size(); i++) {
        jsoncons::json label = labels[i];

        std::vector<double> extents = label["extents"].as<std::vector<double>>();
        int minx = rescale(extents[0], imgw);
        int miny = rescale(1.0-extents[1], imgh);
        int maxx = rescale(extents[2], imgw);
        int maxy = rescale(1.0-extents[3], imgh);
        int w = (int)fabs(maxx-minx);
        int h = (int)fabs(maxy-miny);

        svgRect(o, minx, maxy, w, h, "");
    }
    svgGend(o);

    svgGroup(o, styleText);
    for (int i=0; i<labels.size(); i++) {
        jsoncons::json label = labels[i];

        std::vector<double> extents = label["extents"].as<std::vector<double>>();
        int minx = rescale(extents[0], imgw);
        int maxx = rescale(extents[2], imgw);
        int w = (int)fabs(maxx-minx);

        std::vector<double> pos = label["position"].as<std::vector<double>>();
        int px = rescale(pos[0], imgw);
        int py = rescale(1.0-pos[1], imgh);

        std::string fontsize = label["fontsize"].as<std::string>();
        std::string style = "font-size='" + fontsize + "px' textLength='" + std::to_string(w) + "px'";

        svgText(o, px, py, label["text"].as<std::string>(), style);
    }
    svgGend(o);

}

std::string gen::render::drawSvg(jsoncons::json map, std::string font) {
    std::ostringstream ss;

    int w = map["image_width"].as<int>();
    int h = map["image_height"].as<int>();

    svgStart(ss, w, h);
    svgRect(ss, 0, 0, w, h, STYLE_BACKGROUND);

    drawSegments(ss, map["slope"].as<std::vector<double>>(), w, h, STYLE_SLOPE);

    drawPaths(ss, map["territory"].as<std::vector<std::vector<double>>>(), w, h, STYLE_BORDER);
    drawPaths(ss, map["river"].as<std::vector<std::vector<double>>>(), w, h, STYLE_RIVER);
    drawPaths(ss, map["contour"].as<std::vector<std::vector<double>>>(), w, h, STYLE_CONTOUR);

    drawCircles(ss, map["city"].as<std::vector<double>>(), w, h, CITY_MARKER_OUTER_RADIUS, STYLE_CITY_MARKER_OUTER);
    drawCircles(ss, map["city"].as<std::vector<double>>(), w, h, CITY_MARKER_INNER_RADIUS, STYLE_CITY_MARKER_INNER);
    drawCircles(ss, map["town"].as<std::vector<double>>(), w, h, TOWN_MARKER_RADIUS, STYLE_TOWN_MARKER);


    std::string styleText = STYLE_TEXT + " font-family='" + font + "'";
    drawLabels(ss, map["label"].as<std::vector<jsoncons::json>>(), w, h, styleText, STYLE_TEXT_BOX);

    svgEnd(ss);

    return ss.str();
}
