#pragma once

class ProjectContext;
class Scene;

class AnimatorSystem
{
public:
    static void Update(Scene& scene, float deltaTime, const ProjectContext* projectContext);
    static void InvalidateClipCache();
};
