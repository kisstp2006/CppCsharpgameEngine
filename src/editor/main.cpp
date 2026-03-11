#include "engine/Engine.h"

int main(int argc, char** argv)
{
    (void)argc;
    (void)argv;

    Engine engine;
    engine.SetEditorMode(true);
    if (!engine.Initialize("CppCSharpGameEditor", 1600, 900))
        return -1;

    engine.Run();
    engine.Shutdown();

    return 0;
}
