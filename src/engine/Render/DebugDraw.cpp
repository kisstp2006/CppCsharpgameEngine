#include "DebugDraw.h"

#include <imgui.h>

#include <algorithm>
#include <vector>

namespace
{
    enum class PrimitiveType
    {
        Line,
        Circle,
        Rect,
        FilledCircle,
        FilledRect,
    };

    struct Command
    {
        PrimitiveType type = PrimitiveType::Line;
        float x0 = 0.0f;
        float y0 = 0.0f;
        float x1 = 0.0f;
        float y1 = 0.0f;
        float radius = 0.0f;
        float thickness = 1.0f;
        int segments = 32;
        DebugDraw::Color color;
        float remainingSeconds = 0.0f;
        bool persistent = false;
    };

    std::vector<Command> g_commands;

    static float Clamp01(float value)
    {
        if (value < 0.0f)
            return 0.0f;
        if (value > 1.0f)
            return 1.0f;
        return value;
    }

    static ImU32 ToImColor(const DebugDraw::Color& color)
    {
        return IM_COL32(static_cast<int>(Clamp01(color.r) * 255.0f),
                        static_cast<int>(Clamp01(color.g) * 255.0f),
                        static_cast<int>(Clamp01(color.b) * 255.0f),
                        static_cast<int>(Clamp01(color.a) * 255.0f));
    }

    static ImVec2 ToScreen(float x, float y, int viewHeight)
    {
        return ImVec2(x, static_cast<float>(viewHeight) - y);
    }
}

namespace DebugDraw
{
    void Line(float x0,
              float y0,
              float x1,
              float y1,
              Color color,
              float thickness,
              float durationSeconds)
    {
        Command cmd;
        cmd.type = PrimitiveType::Line;
        cmd.x0 = x0;
        cmd.y0 = y0;
        cmd.x1 = x1;
        cmd.y1 = y1;
        cmd.color = color;
        cmd.thickness = std::max(0.5f, thickness);
        cmd.persistent = durationSeconds > 0.0f;
        cmd.remainingSeconds = durationSeconds;
        g_commands.push_back(cmd);
    }

    void Circle(float centerX,
                float centerY,
                float radius,
                Color color,
                float thickness,
                int segments,
                float durationSeconds)
    {
        if (radius <= 0.0f)
            return;

        Command cmd;
        cmd.type = PrimitiveType::Circle;
        cmd.x0 = centerX;
        cmd.y0 = centerY;
        cmd.radius = radius;
        cmd.color = color;
        cmd.thickness = std::max(0.5f, thickness);
        cmd.segments = std::max(8, segments);
        cmd.persistent = durationSeconds > 0.0f;
        cmd.remainingSeconds = durationSeconds;
        g_commands.push_back(cmd);
    }

    void Rect(float x,
              float y,
              float width,
              float height,
              Color color,
              float thickness,
              float durationSeconds)
    {
        if (width <= 0.0f || height <= 0.0f)
            return;

        Command cmd;
        cmd.type = PrimitiveType::Rect;
        cmd.x0 = x;
        cmd.y0 = y;
        cmd.x1 = width;
        cmd.y1 = height;
        cmd.color = color;
        cmd.thickness = std::max(0.5f, thickness);
        cmd.persistent = durationSeconds > 0.0f;
        cmd.remainingSeconds = durationSeconds;
        g_commands.push_back(cmd);
    }

    void FilledRect(float x,
                    float y,
                    float width,
                    float height,
                    Color color,
                    float durationSeconds)
    {
        if (width <= 0.0f || height <= 0.0f)
            return;

        Command cmd;
        cmd.type = PrimitiveType::FilledRect;
        cmd.x0 = x;
        cmd.y0 = y;
        cmd.x1 = width;
        cmd.y1 = height;
        cmd.color = color;
        cmd.persistent = durationSeconds > 0.0f;
        cmd.remainingSeconds = durationSeconds;
        g_commands.push_back(cmd);
    }

    void Box(float x,
             float y,
             float width,
             float height,
             Color color,
             float thickness,
             float durationSeconds)
    {
        Rect(x, y, width, height, color, thickness, durationSeconds);
    }

    void FilledCircle(float centerX,
                      float centerY,
                      float radius,
                      Color color,
                      int segments,
                      float durationSeconds)
    {
        if (radius <= 0.0f)
            return;

        Command cmd;
        cmd.type = PrimitiveType::FilledCircle;
        cmd.x0 = centerX;
        cmd.y0 = centerY;
        cmd.radius = radius;
        cmd.color = color;
        cmd.segments = std::max(8, segments);
        cmd.persistent = durationSeconds > 0.0f;
        cmd.remainingSeconds = durationSeconds;
        g_commands.push_back(cmd);
    }

    void Clear()
    {
        g_commands.clear();
    }

    void Render(float deltaTime, int viewHeight)
    {
        if (g_commands.empty())
            return;

        ImDrawList* drawList = ImGui::GetForegroundDrawList();
        if (!drawList)
            return;

        for (const Command& cmd : g_commands)
        {
            const ImU32 color = ToImColor(cmd.color);

            switch (cmd.type)
            {
            case PrimitiveType::Line:
            {
                drawList->AddLine(ToScreen(cmd.x0, cmd.y0, viewHeight),
                                  ToScreen(cmd.x1, cmd.y1, viewHeight),
                                  color,
                                  cmd.thickness);
                break;
            }
            case PrimitiveType::Circle:
            {
                drawList->AddCircle(ToScreen(cmd.x0, cmd.y0, viewHeight),
                                    cmd.radius,
                                    color,
                                    cmd.segments,
                                    cmd.thickness);
                break;
            }
            case PrimitiveType::Rect:
            {
                const float x = cmd.x0;
                const float y = cmd.y0;
                const float width = cmd.x1;
                const float height = cmd.y1;

                const ImVec2 p0 = ToScreen(x, y + height, viewHeight);
                const ImVec2 p1 = ToScreen(x + width, y, viewHeight);
                drawList->AddRect(p0, p1, color, 0.0f, 0, cmd.thickness);
                break;
            }
            case PrimitiveType::FilledCircle:
            {
                drawList->AddCircleFilled(ToScreen(cmd.x0, cmd.y0, viewHeight),
                                          cmd.radius,
                                          color,
                                          cmd.segments);
                break;
            }
            case PrimitiveType::FilledRect:
            {
                const float x = cmd.x0;
                const float y = cmd.y0;
                const float width = cmd.x1;
                const float height = cmd.y1;

                const ImVec2 p0 = ToScreen(x, y + height, viewHeight);
                const ImVec2 p1 = ToScreen(x + width, y, viewHeight);
                drawList->AddRectFilled(p0, p1, color);
                break;
            }
            }
        }

        g_commands.erase(
            std::remove_if(
                g_commands.begin(),
                g_commands.end(),
                [deltaTime](Command& cmd)
                {
                    if (!cmd.persistent)
                        return true;

                    cmd.remainingSeconds -= deltaTime;
                    return cmd.remainingSeconds <= 0.0f;
                }),
            g_commands.end());
    }
}
