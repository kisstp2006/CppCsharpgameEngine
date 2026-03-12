#include "Renderer.h"

#include "engine/Core/Logger.h"
#include "Shader.h"
#include "Texture.h"

#include <glad/glad.h>
#include <SDL.h>

#include <glm/glm.hpp>
#include <glm/gtc/matrix_transform.hpp>
#include <glm/gtc/type_ptr.hpp>

#include <cmath>

struct Renderer::Impl
{
    Shader shader;
    unsigned int vao = 0;
    unsigned int vbo = 0;
    int projLocation = -1;
    int modelLocation = -1;
    int textureLocation = -1;
    int useSolidColorLocation = -1;
    int solidColorLocation = -1;

    unsigned int gameViewFbo = 0;
    unsigned int gameViewColorTexture = 0;
    int gameViewWidth = 0;
    int gameViewHeight = 0;
    int cameraViewportX = 0;
    int cameraViewportY = 0;
    int cameraViewportWidth = 0;
    int cameraViewportHeight = 0;
};

static float ClampCameraZoom(float zoom)
{
    if (!std::isfinite(zoom))
        return 1.0f;
    if (zoom < 0.01f)
        return 0.01f;
    if (zoom > 100.0f)
        return 100.0f;
    return zoom;
}

static float Clamp01(float value)
{
    if (value < 0.0f)
        return 0.0f;
    if (value > 1.0f)
        return 1.0f;
    return value;
}

Renderer::Renderer() = default;
Renderer::~Renderer() = default;

bool Renderer::Initialize(int width, int height)
{
    m_viewWidth = width;
    m_viewHeight = height;

    if (!gladLoadGLLoader((GLADloadproc)SDL_GL_GetProcAddress))
    {
        EngineLogger::Error("Renderer", "Failed to initialize GLAD");
        return false;
    }

    // Setup basic OpenGL state
    glEnable(GL_BLEND);
    glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);

    // Create shader
    const char* vertexSource = R"glsl(
        #version 330 core
        layout(location = 0) in vec2 aPos;
        layout(location = 1) in vec2 aTexCoord;

        uniform mat4 u_Projection;
        uniform mat4 u_Model;

        out vec2 v_TexCoord;

        void main()
        {
            v_TexCoord = aTexCoord;
            gl_Position = u_Projection * u_Model * vec4(aPos, 0.0, 1.0);
        }
    )glsl";

    const char* fragmentSource = R"glsl(
        #version 330 core
        in vec2 v_TexCoord;
        out vec4 FragColor;

        uniform sampler2D u_Texture;
        uniform int u_UseSolidColor;
        uniform vec4 u_SolidColor;

        void main()
        {
            if (u_UseSolidColor != 0)
                FragColor = u_SolidColor;
            else
                FragColor = texture(u_Texture, v_TexCoord);
        }
    )glsl";

    m_impl = std::make_unique<Impl>();
    if (!m_impl->shader.Load(vertexSource, fragmentSource))
    {
        EngineLogger::Error("Renderer", "Failed to compile basic sprite shader");
        return false;
    }

    m_impl->shader.Bind();
    m_impl->projLocation = glGetUniformLocation(m_impl->shader.GetHandle(), "u_Projection");
    m_impl->modelLocation = glGetUniformLocation(m_impl->shader.GetHandle(), "u_Model");
    m_impl->textureLocation = glGetUniformLocation(m_impl->shader.GetHandle(), "u_Texture");
    m_impl->useSolidColorLocation = glGetUniformLocation(m_impl->shader.GetHandle(), "u_UseSolidColor");
    m_impl->solidColorLocation = glGetUniformLocation(m_impl->shader.GetHandle(), "u_SolidColor");

    // Setup quad with UVs
    float vertices[] = {
        // pos    // uv
        0.0f, 1.0f, 0.0f, 1.0f,
        1.0f, 0.0f, 1.0f, 0.0f,
        0.0f, 0.0f, 0.0f, 0.0f,

        0.0f, 1.0f, 0.0f, 1.0f,
        1.0f, 1.0f, 1.0f, 1.0f,
        1.0f, 0.0f, 1.0f, 0.0f,
    };

    glGenVertexArrays(1, &m_impl->vao);
    glGenBuffers(1, &m_impl->vbo);

    glBindVertexArray(m_impl->vao);
    glBindBuffer(GL_ARRAY_BUFFER, m_impl->vbo);
    glBufferData(GL_ARRAY_BUFFER, sizeof(vertices), vertices, GL_STATIC_DRAW);

    glEnableVertexAttribArray(0);
    glVertexAttribPointer(0, 2, GL_FLOAT, GL_FALSE, 4 * sizeof(float), (void*)0);
    glEnableVertexAttribArray(1);
    glVertexAttribPointer(1, 2, GL_FLOAT, GL_FALSE, 4 * sizeof(float), (void*)(2 * sizeof(float)));

    glBindBuffer(GL_ARRAY_BUFFER, 0);
    glBindVertexArray(0);

    // Set projection once
    m_impl->shader.Bind();
    const glm::mat4 projection = glm::ortho(0.0f,
                                            static_cast<float>(m_viewWidth),
                                            0.0f,
                                            static_cast<float>(m_viewHeight));
    glUniformMatrix4fv(m_impl->projLocation, 1, GL_FALSE, glm::value_ptr(projection));
    glUniform1i(m_impl->textureLocation, 0);
    glUniform1i(m_impl->useSolidColorLocation, 0);
    glUniform4f(m_impl->solidColorLocation, 1.0f, 1.0f, 1.0f, 1.0f);

    return true;
}

void Renderer::Shutdown()
{
    if (!m_impl)
        return;

    if (m_impl->gameViewColorTexture)
        glDeleteTextures(1, &m_impl->gameViewColorTexture);
    if (m_impl->gameViewFbo)
        glDeleteFramebuffers(1, &m_impl->gameViewFbo);

    if (m_impl->vbo)
        glDeleteBuffers(1, &m_impl->vbo);
    if (m_impl->vao)
        glDeleteVertexArrays(1, &m_impl->vao);

    m_impl.reset();
}

void Renderer::BeginFrame()
{
    glViewport(0, 0, m_viewWidth, m_viewHeight);
    glClearColor(0.1f, 0.1f, 0.1f, 1.0f);
    glClear(GL_COLOR_BUFFER_BIT);
}

void Renderer::SetGameViewSize(int width, int height)
{
    if (!m_impl)
        return;

    if (width < 1 || height < 1)
        return;

    if (m_impl->gameViewWidth == width &&
        m_impl->gameViewHeight == height &&
        m_impl->gameViewFbo != 0 &&
        m_impl->gameViewColorTexture != 0)
    {
        return;
    }

    m_impl->gameViewWidth = width;
    m_impl->gameViewHeight = height;

    if (!m_impl->gameViewFbo)
        glGenFramebuffers(1, &m_impl->gameViewFbo);

    if (!m_impl->gameViewColorTexture)
        glGenTextures(1, &m_impl->gameViewColorTexture);

    glBindTexture(GL_TEXTURE_2D, m_impl->gameViewColorTexture);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
    glTexImage2D(GL_TEXTURE_2D,
                 0,
                 GL_RGBA8,
                 m_impl->gameViewWidth,
                 m_impl->gameViewHeight,
                 0,
                 GL_RGBA,
                 GL_UNSIGNED_BYTE,
                 nullptr);
    glBindTexture(GL_TEXTURE_2D, 0);

    glBindFramebuffer(GL_FRAMEBUFFER, m_impl->gameViewFbo);
    glFramebufferTexture2D(GL_FRAMEBUFFER,
                           GL_COLOR_ATTACHMENT0,
                           GL_TEXTURE_2D,
                           m_impl->gameViewColorTexture,
                           0);
    glBindFramebuffer(GL_FRAMEBUFFER, 0);
}

void Renderer::BeginGameView()
{
    if (!m_impl || !m_impl->gameViewFbo || m_impl->gameViewWidth < 1 || m_impl->gameViewHeight < 1)
        return;

    glBindFramebuffer(GL_FRAMEBUFFER, m_impl->gameViewFbo);
    glViewport(0, 0, m_impl->gameViewWidth, m_impl->gameViewHeight);
    m_impl->cameraViewportX = 0;
    m_impl->cameraViewportY = 0;
    m_impl->cameraViewportWidth = m_impl->gameViewWidth;
    m_impl->cameraViewportHeight = m_impl->gameViewHeight;
    glClearColor(0.08f, 0.08f, 0.10f, 1.0f);
    glClear(GL_COLOR_BUFFER_BIT);
}

void Renderer::EndGameView()
{
    glBindFramebuffer(GL_FRAMEBUFFER, 0);
}

unsigned long long Renderer::GetGameViewTextureHandle() const
{
    if (!m_impl)
        return 0;

    return static_cast<unsigned long long>(m_impl->gameViewColorTexture);
}

void Renderer::SetCameraViewportNormalized(float viewportX,
                                           float viewportY,
                                           float viewportWidth,
                                           float viewportHeight)
{
    if (!m_impl)
        return;

    const int baseWidth = (m_impl->gameViewWidth > 0) ? m_impl->gameViewWidth : m_viewWidth;
    const int baseHeight = (m_impl->gameViewHeight > 0) ? m_impl->gameViewHeight : m_viewHeight;
    if (baseWidth < 1 || baseHeight < 1)
        return;

    float x = Clamp01(viewportX);
    float y = Clamp01(viewportY);
    float width = viewportWidth;
    float height = viewportHeight;

    if (width < 0.01f)
        width = 0.01f;
    if (width > 1.0f)
        width = 1.0f;
    if (height < 0.01f)
        height = 0.01f;
    if (height > 1.0f)
        height = 1.0f;

    if (x + width > 1.0f)
        width = 1.0f - x;
    if (y + height > 1.0f)
        height = 1.0f - y;

    if (width < 0.01f)
        width = 0.01f;
    if (height < 0.01f)
        height = 0.01f;

    int pixelX = static_cast<int>(x * static_cast<float>(baseWidth));
    int pixelY = static_cast<int>(y * static_cast<float>(baseHeight));
    int pixelWidth = static_cast<int>(width * static_cast<float>(baseWidth));
    int pixelHeight = static_cast<int>(height * static_cast<float>(baseHeight));

    if (pixelWidth < 1)
        pixelWidth = 1;
    if (pixelHeight < 1)
        pixelHeight = 1;

    if (pixelX < 0)
        pixelX = 0;
    if (pixelY < 0)
        pixelY = 0;

    if (pixelX >= baseWidth)
        pixelX = baseWidth - 1;
    if (pixelY >= baseHeight)
        pixelY = baseHeight - 1;

    if (pixelX + pixelWidth > baseWidth)
        pixelWidth = baseWidth - pixelX;
    if (pixelY + pixelHeight > baseHeight)
        pixelHeight = baseHeight - pixelY;

    if (pixelWidth < 1)
        pixelWidth = 1;
    if (pixelHeight < 1)
        pixelHeight = 1;

    m_impl->cameraViewportX = pixelX;
    m_impl->cameraViewportY = pixelY;
    m_impl->cameraViewportWidth = pixelWidth;
    m_impl->cameraViewportHeight = pixelHeight;

    glViewport(pixelX, pixelY, pixelWidth, pixelHeight);
}

void Renderer::ClearCameraViewport(float r, float g, float b, float a)
{
    if (!m_impl)
        return;

    if (m_impl->cameraViewportWidth < 1 || m_impl->cameraViewportHeight < 1)
        return;

    glEnable(GL_SCISSOR_TEST);
    glScissor(m_impl->cameraViewportX,
              m_impl->cameraViewportY,
              m_impl->cameraViewportWidth,
              m_impl->cameraViewportHeight);
    glClearColor(Clamp01(r), Clamp01(g), Clamp01(b), Clamp01(a));
    glClear(GL_COLOR_BUFFER_BIT);
    glDisable(GL_SCISSOR_TEST);
    glViewport(m_impl->cameraViewportX,
               m_impl->cameraViewportY,
               m_impl->cameraViewportWidth,
               m_impl->cameraViewportHeight);
}

void Renderer::SetCameraProjection(float cameraX, float cameraY, float cameraZoom)
{
    if (!m_impl)
        return;

    const float zoom = ClampCameraZoom(cameraZoom);
    int projectionWidth = m_impl->cameraViewportWidth;
    int projectionHeight = m_impl->cameraViewportHeight;
    if (projectionWidth < 1 || projectionHeight < 1)
    {
        projectionWidth = (m_impl->gameViewWidth > 0) ? m_impl->gameViewWidth : m_viewWidth;
        projectionHeight = (m_impl->gameViewHeight > 0) ? m_impl->gameViewHeight : m_viewHeight;
    }
    const float halfWorldWidth = (static_cast<float>(projectionWidth) * 0.5f) / zoom;
    const float halfWorldHeight = (static_cast<float>(projectionHeight) * 0.5f) / zoom;

    const float left = cameraX - halfWorldWidth;
    const float right = cameraX + halfWorldWidth;
    const float bottom = cameraY - halfWorldHeight;
    const float top = cameraY + halfWorldHeight;

    m_impl->shader.Bind();
    const glm::mat4 projection = glm::ortho(left, right, bottom, top);
    glUniformMatrix4fv(m_impl->projLocation, 1, GL_FALSE, glm::value_ptr(projection));
}

void Renderer::DrawSprite(Texture& texture, float x, float y, float width, float height)
{
    DrawSprite(texture, x, y, width, height, 0.0f, 0.0f, 1.0f, 1.0f);
}

void Renderer::DrawSprite(Texture& texture,
                          float x,
                          float y,
                          float width,
                          float height,
                          float uvMinX,
                          float uvMinY,
                          float uvMaxX,
                          float uvMaxY)
{
    if (!m_impl)
        return;

    float vertices[] = {
        // pos    // uv
        0.0f, 1.0f, uvMinX, uvMaxY,
        1.0f, 0.0f, uvMaxX, uvMinY,
        0.0f, 0.0f, uvMinX, uvMinY,

        0.0f, 1.0f, uvMinX, uvMaxY,
        1.0f, 1.0f, uvMaxX, uvMaxY,
        1.0f, 0.0f, uvMaxX, uvMinY,
    };

    m_impl->shader.Bind();
    glUniform1i(m_impl->useSolidColorLocation, 0);

    glm::mat4 model(1.0f);
    model = glm::translate(model, glm::vec3(x, y, 0.0f));
    model = glm::scale(model, glm::vec3(width, height, 1.0f));
    glUniformMatrix4fv(m_impl->modelLocation, 1, GL_FALSE, glm::value_ptr(model));

    texture.Bind(0);

    glBindBuffer(GL_ARRAY_BUFFER, m_impl->vbo);
    glBufferSubData(GL_ARRAY_BUFFER, 0, sizeof(vertices), vertices);
    glBindBuffer(GL_ARRAY_BUFFER, 0);

    glBindVertexArray(m_impl->vao);
    glDrawArrays(GL_TRIANGLES, 0, 6);
    glBindVertexArray(0);
}

void Renderer::DrawSolidSprite(float x,
                               float y,
                               float width,
                               float height,
                               float r,
                               float g,
                               float b,
                               float a)
{
    if (!m_impl)
        return;

    float vertices[] = {
        // pos    // uv
        0.0f, 1.0f, 0.0f, 1.0f,
        1.0f, 0.0f, 1.0f, 0.0f,
        0.0f, 0.0f, 0.0f, 0.0f,

        0.0f, 1.0f, 0.0f, 1.0f,
        1.0f, 1.0f, 1.0f, 1.0f,
        1.0f, 0.0f, 1.0f, 0.0f,
    };

    m_impl->shader.Bind();
    glUniform1i(m_impl->useSolidColorLocation, 1);
    glUniform4f(m_impl->solidColorLocation, r, g, b, a);

    glm::mat4 model(1.0f);
    model = glm::translate(model, glm::vec3(x, y, 0.0f));
    model = glm::scale(model, glm::vec3(width, height, 1.0f));
    glUniformMatrix4fv(m_impl->modelLocation, 1, GL_FALSE, glm::value_ptr(model));

    glBindTexture(GL_TEXTURE_2D, 0);

    glBindBuffer(GL_ARRAY_BUFFER, m_impl->vbo);
    glBufferSubData(GL_ARRAY_BUFFER, 0, sizeof(vertices), vertices);
    glBindBuffer(GL_ARRAY_BUFFER, 0);

    glBindVertexArray(m_impl->vao);
    glDrawArrays(GL_TRIANGLES, 0, 6);
    glBindVertexArray(0);

    glUniform1i(m_impl->useSolidColorLocation, 0);
}

int Renderer::GetViewWidth() const
{
    return m_viewWidth;
}

int Renderer::GetViewHeight() const
{
    return m_viewHeight;
}

int Renderer::GetCameraViewportWidth() const
{
    if (!m_impl)
        return m_viewWidth;

    if (m_impl->cameraViewportWidth > 0)
        return m_impl->cameraViewportWidth;

    if (m_impl->gameViewWidth > 0)
        return m_impl->gameViewWidth;

    return m_viewWidth;
}

int Renderer::GetCameraViewportHeight() const
{
    if (!m_impl)
        return m_viewHeight;

    if (m_impl->cameraViewportHeight > 0)
        return m_impl->cameraViewportHeight;

    if (m_impl->gameViewHeight > 0)
        return m_impl->gameViewHeight;

    return m_viewHeight;
}
