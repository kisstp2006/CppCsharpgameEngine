#include "engine/Engine.h"

int main(int argc, char** argv)
{
    (void)argc;
    (void)argv;

    Engine engine;
    engine.SetEditorMode(true);
    engine.SetDockspaceEnabled(false);
    if (!engine.Initialize("CppCSharp Editor — Project Selector", 727, 480))
        return -1;

    engine.Run();
    engine.Shutdown();

    return 0;
}
