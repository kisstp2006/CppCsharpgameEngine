#include "Texture.h"

#include <SDL.h>
#include <glad/glad.h>

#include <vector>

Texture::Texture() = default;
Texture::~Texture()
{
    if (m_handle)
        glDeleteTextures(1, &m_handle);
}

bool Texture::CreateCheckerboard(int width, int height, int cellSize)
{
    m_width = width;
    m_height = height;

    const int channels = 4;
    std::vector<unsigned char> data(width * height * channels);

    for (int y = 0; y < height; y++)
    {
        for (int x = 0; x < width; x++)
        {
            int cx = x / cellSize;
            int cy = y / cellSize;
            bool white = ((cx + cy) % 2) == 0;
            unsigned char value = white ? 0xFF : 0x33;

            int idx = (y * width + x) * channels;
            data[idx + 0] = value;
            data[idx + 1] = value;
            data[idx + 2] = value;
            data[idx + 3] = 0xFF;
        }
    }

    if (m_handle)
        glDeleteTextures(1, &m_handle);

    glGenTextures(1, &m_handle);
    glBindTexture(GL_TEXTURE_2D, m_handle);

    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_REPEAT);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_REPEAT);

    glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, width, height, 0, GL_RGBA, GL_UNSIGNED_BYTE, data.data());
    glBindTexture(GL_TEXTURE_2D, 0);

    return true;
}

bool Texture::CreateFromFile(const std::string& path)
{
    SDL_Surface* surf = SDL_LoadBMP(path.c_str());
    if (!surf)
        return false;

    m_width = surf->w;
    m_height = surf->h;

    if (m_handle)
        glDeleteTextures(1, &m_handle);

    glGenTextures(1, &m_handle);
    glBindTexture(GL_TEXTURE_2D, m_handle);

    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_REPEAT);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_REPEAT);

    // SDL_Surface is usually BGR(A) for BMP; we assume it is in 32-bit format.
    GLenum format = (surf->format->BytesPerPixel == 4) ? GL_RGBA : GL_RGB;

    glTexImage2D(GL_TEXTURE_2D, 0, format, m_width, m_height, 0, format, GL_UNSIGNED_BYTE, surf->pixels);

    glBindTexture(GL_TEXTURE_2D, 0);
    SDL_FreeSurface(surf);

    return true;
}

void Texture::Bind(unsigned int slot) const
{
    glActiveTexture(GL_TEXTURE0 + slot);
    glBindTexture(GL_TEXTURE_2D, m_handle);
}
