#include "engine/Engine.h"

int main(int argc, char** argv)
{
    Engine engine;
    if (!engine.Initialize("CppCSharpGameEngine", 1280, 720))
        return -1;

    engine.Run();
    engine.Shutdown();

    return 0;
}
