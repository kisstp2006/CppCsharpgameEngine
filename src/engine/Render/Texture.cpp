#include "Texture.h"

#include <SDL.h>
#include <glad/glad.h>

#if !defined(__INTELLISENSE__) && __has_include("stb_image.h")
#define STB_IMAGE_STATIC
#define STB_IMAGE_IMPLEMENTATION
#include "stb_image.h"
#define ENGINE_TEXTURE_STB_AVAILABLE 1
#else
#define ENGINE_TEXTURE_STB_AVAILABLE 0
#endif

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
#if ENGINE_TEXTURE_STB_AVAILABLE
    int loadedWidth = 0;
    int loadedHeight = 0;
    int loadedChannels = 0;
    stbi_set_flip_vertically_on_load(1);
    unsigned char* pixels = stbi_load(path.c_str(), &loadedWidth, &loadedHeight, &loadedChannels, 4);
    if (!pixels)
        return false;

    m_width = loadedWidth;
    m_height = loadedHeight;

    if (m_handle)
        glDeleteTextures(1, &m_handle);

    glGenTextures(1, &m_handle);
    glBindTexture(GL_TEXTURE_2D, m_handle);

    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_REPEAT);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_REPEAT);

    glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, m_width, m_height, 0, GL_RGBA, GL_UNSIGNED_BYTE, pixels);

    glBindTexture(GL_TEXTURE_2D, 0);
    stbi_image_free(pixels);

    return true;
#else
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

    GLenum format = (surf->format->BytesPerPixel == 4) ? GL_RGBA : GL_RGB;
    glTexImage2D(GL_TEXTURE_2D, 0, format, m_width, m_height, 0, format, GL_UNSIGNED_BYTE, surf->pixels);

    glBindTexture(GL_TEXTURE_2D, 0);
    SDL_FreeSurface(surf);

    return true;
#endif
}

void Texture::Bind(unsigned int slot) const
{
    glActiveTexture(GL_TEXTURE0 + slot);
    glBindTexture(GL_TEXTURE_2D, m_handle);
}
