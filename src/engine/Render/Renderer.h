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
    void SetGameViewSize(int width, int height);
    void BeginGameView();
    void EndGameView();
    unsigned long long GetGameViewTextureHandle() const;
    void SetCameraViewportNormalized(float viewportX, float viewportY, float viewportWidth, float viewportHeight);
    void ClearCameraViewport(float r, float g, float b, float a);
    void SetCameraProjection(float cameraX, float cameraY, float cameraZoom);
    void DrawSprite(Texture& texture, float x, float y, float width, float height);
    void DrawSprite(Texture& texture,
                    float x,
                    float y,
                    float width,
                    float height,
                    float uvMinX,
                    float uvMinY,
                    float uvMaxX,
                    float uvMaxY);
    void DrawSolidSprite(float x,
                         float y,
                         float width,
                         float height,
                         float r,
                         float g,
                         float b,
                         float a);

    int GetViewWidth() const;
    int GetViewHeight() const;
    int GetCameraViewportWidth() const;
    int GetCameraViewportHeight() const;

private:
    int m_viewWidth = 0;
    int m_viewHeight = 0;

    struct Impl;
    std::unique_ptr<Impl> m_impl;
};
