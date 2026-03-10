#pragma once

#include <memory>

class Texture;

class Renderer
{
public:
    Renderer();
    ~Renderer();

    bool Initialize(int width, int height);
    void Shutdown();

    void BeginFrame();
    void DrawSprite(Texture& texture, float x, float y, float width, float height);

private:
    int m_viewWidth = 0;
    int m_viewHeight = 0;

    struct Impl;
    std::unique_ptr<Impl> m_impl;
};
