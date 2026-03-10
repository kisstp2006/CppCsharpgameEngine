#include "Renderer.h"

#include "Shader.h"
#include "Texture.h"

#include <glad/glad.h>
#include <SDL.h>

#include <array>
#include <iostream>

struct Renderer::Impl
{
    Shader shader;
    unsigned int vao = 0;
    unsigned int vbo = 0;
    int projLocation = -1;
    int modelLocation = -1;
    int textureLocation = -1;
};

static std::array<float, 16> Ortho(float left, float right, float bottom, float top)
{
    // Column-major order for OpenGL
    const float rl = 1.0f / (right - left);
    const float tb = 1.0f / (top - bottom);

    return {
        2.0f * rl, 0.0f, 0.0f, 0.0f,
        0.0f, 2.0f * tb, 0.0f, 0.0f,
        0.0f, 0.0f, -1.0f, 0.0f,
        -(right + left) * rl, -(top + bottom) * tb, 0.0f, 1.0f,
    };
}

static std::array<float, 16> TranslateScale(float x, float y, float sx, float sy)
{
    return {
        sx, 0.0f, 0.0f, 0.0f,
        0.0f, sy, 0.0f, 0.0f,
        0.0f, 0.0f, 1.0f, 0.0f,
        x, y, 0.0f, 1.0f,
    };
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
    const auto proj = Ortho(0.0f, (float)m_viewWidth, 0.0f, (float)m_viewHeight);
    glUniformMatrix4fv(m_impl->projLocation, 1, GL_FALSE, proj.data());
    glUniform1i(m_impl->textureLocation, 0);

    return true;
}

void Renderer::Shutdown()
{
    if (!m_impl)
        return;

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

void Renderer::DrawSprite(Texture& texture, float x, float y, float width, float height)
{
    if (!m_impl)
        return;

    m_impl->shader.Bind();

    const auto model = TranslateScale(x, y, width, height);
    glUniformMatrix4fv(m_impl->modelLocation, 1, GL_FALSE, model.data());

    texture.Bind(0);

    glBindVertexArray(m_impl->vao);
    glDrawArrays(GL_TRIANGLES, 0, 6);
    glBindVertexArray(0);
}
