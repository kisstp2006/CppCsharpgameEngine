#pragma once

namespace DebugDraw
{
    struct Color
    {
        float r = 1.0f;
        float g = 1.0f;
        float b = 1.0f;
        float a = 1.0f;

        constexpr Color() = default;
        constexpr Color(float red, float green, float blue, float alpha = 1.0f)
            : r(red), g(green), b(blue), a(alpha)
        {
        }
    };

    void Line(float x0,
              float y0,
              float x1,
              float y1,
              Color color = Color{},
              float thickness = 1.0f,
              float durationSeconds = 0.0f);

    void Circle(float centerX,
                float centerY,
                float radius,
                Color color = Color{},
                float thickness = 1.0f,
                int segments = 32,
                float durationSeconds = 0.0f);

    void Rect(float x,
              float y,
              float width,
              float height,
              Color color = Color{},
              float thickness = 1.0f,
              float durationSeconds = 0.0f);

    void FilledRect(float x,
                    float y,
                    float width,
                    float height,
                    Color color = Color{},
                    float durationSeconds = 0.0f);

    void Box(float x,
             float y,
             float width,
             float height,
             Color color = Color{},
             float thickness = 1.0f,
             float durationSeconds = 0.0f);

    void FilledCircle(float centerX,
                      float centerY,
                      float radius,
                      Color color = Color{},
                      int segments = 32,
                      float durationSeconds = 0.0f);

    void Clear();

    // Called once per frame by the engine.
    void Render(float deltaTime, int viewHeight);
}
