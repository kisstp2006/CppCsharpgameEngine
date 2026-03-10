#pragma once

#include <string>

class Shader
{
public:
    Shader();
    ~Shader();

    bool Load(const std::string& vertexSrc, const std::string& fragmentSrc);
    void Bind() const;
    void Unbind() const;

    unsigned int GetHandle() const;

    void SetUniformMat4(const std::string& name, const float* matrix) const;

private:
    unsigned int m_handle = 0;
};
