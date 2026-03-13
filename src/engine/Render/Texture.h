#pragma once

#include <string>

class Texture
{
public:
    Texture();
    ~Texture();

    bool CreateCheckerboard(int width, int height, int cellSize);
    bool CreateFromFile(const std::string& path);

    void Bind(unsigned int slot = 0) const;
    unsigned int GetHandle() const { return m_handle; }

    int GetWidth() const { return m_width; }
    int GetHeight() const { return m_height; }

private:
    unsigned int m_handle = 0;
    int m_width = 0;
    int m_height = 0;
};
