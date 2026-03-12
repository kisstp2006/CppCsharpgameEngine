#include "Renderer.h"

#include "Shader.h"
#include "Texture.h"

#include <glad/glad.h>
#include <SDL.h>

#include <glm/glm.hpp>
#include <glm/gtc/matrix_transform.hpp>
#include <glm/gtc/type_ptr.hpp>

#include <cmath>
#include <iostream>

struct Renderer::Impl
{
    Shader shader;
    unsigned int vao = 0;
    unsigned int vbo = 0;
    int projLocation = -1;
    int modelLocation = -1;
    int textureLocation = -1;

    unsigned int gameViewFbo = 0;
    unsigned int gameViewColorTexture = 0;
    int gameViewWidth = 0;
    int gameViewHeight = 0;
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

Renderer::Renderer() = default;
Renderer::~Renderer() = default;

bool Renderer::Initialize(int width, int height)
{
    m_viewWidth = width;
    m_viewHeight = height;

    if (!gladLoadGLLoader((GLADloadproc)SDL_GL_GetProcAddress))
    {
        std::cerr << "Failed to initialize GLAD" << std::endl;
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

        void main()
        {
            FragColor = texture(u_Texture, v_TexCoord);
        }
    )glsl";

    m_impl = std::make_unique<Impl>();
    if (!m_impl->shader.Load(vertexSource, fragmentSource))
    {
        std::cerr << "Failed to compile basic sprite shader" << std::endl;
        return false;
    }

    m_impl->shader.Bind();
    m_impl->projLocation = glGetUniformLocation(m_impl->shader.GetHandle(), "u_Projection");
    m_impl->modelLocation = glGetUniformLocation(m_impl->shader.GetHandle(), "u_Model");
    m_impl->textureLocation = glGetUniformLocation(m_impl->shader.GetHandle(), "u_Texture");

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

void Renderer::SetCameraProjection(float cameraX, float cameraY, float cameraZoom)
{
    if (!m_impl)
        return;

    const float zoom = ClampCameraZoom(cameraZoom);
    const int projectionWidth = (m_impl->gameViewWidth > 0) ? m_impl->gameViewWidth : m_viewWidth;
    const int projectionHeight = (m_impl->gameViewHeight > 0) ? m_impl->gameViewHeight : m_viewHeight;
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

int Renderer::GetViewWidth() const
{
    return m_viewWidth;
}

int Renderer::GetViewHeight() const
{
    return m_viewHeight;
}
