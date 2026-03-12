#include "Shader.h"

#include "engine/Core/Logger.h"

#include <glad/glad.h>

static unsigned int CompileShader(unsigned int type, const std::string& source)
{
    unsigned int id = glCreateShader(type);
    const char* src = source.c_str();
    glShaderSource(id, 1, &src, nullptr);
    glCompileShader(id);

    int result;
    glGetShaderiv(id, GL_COMPILE_STATUS, &result);
    if (result == GL_FALSE)
    {
        int length;
        glGetShaderiv(id, GL_INFO_LOG_LENGTH, &length);
        std::string message(length, '\0');
        glGetShaderInfoLog(id, length, &length, message.data());
        EngineLogger::Error("Shader", std::string("Failed to compile shader: ") + message);
        glDeleteShader(id);
        return 0;
    }

    return id;
}

Shader::Shader() = default;
Shader::~Shader()
{
    if (m_handle)
    {
        glDeleteProgram(m_handle);
        m_handle = 0;
    }
}

bool Shader::Load(const std::string& vertexSrc, const std::string& fragmentSrc)
{
    if (m_handle)
        glDeleteProgram(m_handle);

    unsigned int program = glCreateProgram();
    unsigned int vs = CompileShader(GL_VERTEX_SHADER, vertexSrc);
    unsigned int fs = CompileShader(GL_FRAGMENT_SHADER, fragmentSrc);
    if (!vs || !fs)
    {
        glDeleteProgram(program);
        return false;
    }

    glAttachShader(program, vs);
    glAttachShader(program, fs);
    glLinkProgram(program);
    glValidateProgram(program);

    glDeleteShader(vs);
    glDeleteShader(fs);

    int result;
    glGetProgramiv(program, GL_LINK_STATUS, &result);
    if (result == GL_FALSE)
    {
        int length;
        glGetProgramiv(program, GL_INFO_LOG_LENGTH, &length);
        std::string message(length, '\0');
        glGetProgramInfoLog(program, length, &length, message.data());
        EngineLogger::Error("Shader", std::string("Failed to link shader program: ") + message);
        glDeleteProgram(program);
        return false;
    }

    m_handle = program;
    return true;
}

void Shader::Bind() const
{
    glUseProgram(m_handle);
}

void Shader::Unbind() const
{
    glUseProgram(0);
}

unsigned int Shader::GetHandle() const
{
    return m_handle;
}

void Shader::SetUniformMat4(const std::string& name, const float* matrix) const
{
    int location = glGetUniformLocation(m_handle, name.c_str());
    if (location != -1)
        glUniformMatrix4fv(location, 1, GL_FALSE, matrix);
}
