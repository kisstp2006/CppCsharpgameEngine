# CppCSharpGameEngine

A minimal Windows-only 2D game engine skeleton using:

- SDL2 (windowing + input)
- OpenGL 3.3 (rendering)
- ImGui (debug/inspector UI)
- EnTT (entity-component system)
- Mono (C# scripting embedding)

## Build (Windows)

### Prerequisites

- CMake 3.20+
- A C++ compiler (MSVC / Visual Studio 2022+)
- [vcpkg](https://github.com/microsoft/vcpkg) (recommended)

### Build steps

#### Option A: Use vendored thirdparty dependencies (recommended for offline/no-vcpkg builds)

This project includes a `thirdparty/` folder that can automatically download and build SDL2, glad, and ImGui at configure time.

```powershell
cd <path-to-repo>
mkdir build
cd build
cmake -DUSE_THIRDPARTY=ON ..
cmake --build . --config Release
```

> Note: Mono is not automatically built; install Mono separately or place a Mono SDK under `thirdparty/mono`.

---

#### Option B: Use vcpkg (recommended for dependency management)

```powershell
# Clone vcpkg if you don't have it already
git clone https://github.com/microsoft/vcpkg.git
cd vcpkg
.ootstrap-vcpkg.bat

# Install required libraries
.
vcpkg.exe install sdl2 sdl2-main imgui glad mono

# Configure + build
cd <path-to-repo>
mkdir build
cd build
cmake -DCMAKE_TOOLCHAIN_FILE=<path-to-vcpkg>\scripts\buildsystems\vcpkg.cmake ..
cmake --build . --config Release
```

### Run

After building, run the executable from the build folder:

```powershell
.\App.exe
```

## Debug Drawing (C++)

You can draw simple debug primitives from native C++ code each frame via:

- `DebugDraw::Line(...)`
- `DebugDraw::Circle(...)`
- `DebugDraw::Rect(...)`
- `DebugDraw::Box(...)`
- `DebugDraw::FilledCircle(...)`
- `DebugDraw::FilledRect(...)`

Include:

```cpp
#include "engine/Render/DebugDraw.h"
```

Example:

```cpp
DebugDraw::Line(100.0f, 100.0f, 300.0f, 180.0f, DebugDraw::Color(1.0f, 0.2f, 0.2f), 2.0f);
DebugDraw::Circle(400.0f, 220.0f, 40.0f, DebugDraw::Color(0.2f, 1.0f, 0.2f), 2.0f);
DebugDraw::Box(500.0f, 120.0f, 140.0f, 80.0f, DebugDraw::Color(0.2f, 0.7f, 1.0f), 2.0f, 3.0f);
DebugDraw::FilledRect(700.0f, 140.0f, 120.0f, 70.0f, DebugDraw::Color(1.0f, 0.8f, 0.1f, 0.35f), 2.0f);
```

Notes:

- Coordinates match the renderer world-space (`(0, 0)` at bottom-left).
- `durationSeconds = 0` means draw once for the current frame.
- `durationSeconds > 0` keeps the primitive alive across multiple frames.

The same API is exposed to C# through `Engine.DebugDraw` in both editor and script assemblies:

```csharp
Engine.DebugDraw.Line(100f, 100f, 240f, 180f, 1f, 0f, 0f, 1f, 2f);
Engine.DebugDraw.FilledCircle(360f, 200f, 24f, 0.2f, 0.8f, 1f, 0.5f, 24, 0.5f);
```

## ECS World API (C++)

`Scene` now provides a direct world-style API for entity/component operations:

- `CreateEntity()`, `DestroyEntity(entity)`, `IsValid(entity)`
- `AddComponent<T>()`, `HasComponent<T>()`, `TryGetComponent<T>()`, `RemoveComponent<T>()`
- Typed helpers for built-in components:
	`AddTransform`, `AddSprite`, `AddScript`, `TryGetTransform`, `TryGetSprite`, `TryGetScript`

Quick example:

```cpp
Scene* world = engine.GetScene();
if (world)
{
		const Scene::Entity e = world->CreateEntity();

		world->AddTransform(e, TransformComponent{ 100.0f, 120.0f, 64.0f, 64.0f });
		world->AddScript(e, ScriptComponent{ "GameScripts", "SpinnerScript", true });

		if (TransformComponent* tr = world->TryGetTransform(e))
				tr->x += 16.0f;
}
```

## C# Script Component (Entity level)

The engine now supports an ECS `ScriptComponent` that can be attached to entities.

- `classNamespace` (default: `GameScripts`)
- `className` (default: `SpinnerScript`)
- `enabled`

At runtime (when Mono is available and `GameScripts.dll` is present), the engine creates one C# object instance per scripted entity and calls:

- `OnCreate(uint entityId)` once
- `OnUpdate(uint entityId, float deltaTime)` every frame
- `OnDestroy(uint entityId)` on removal/shutdown

The sample script assembly project is under `scripts/` and builds to:

`scripts/bin/Debug/net472/GameScripts.dll`

Build it with:

```powershell
dotnet build scripts\GameScripts.csproj -c Debug
```

## C# Editor Project (Separate assembly)

A separate editor project is available under `editor/`.

- Project: `editor/EngineEditor.csproj`
- Output: `editor/bin/Debug/net472/EngineEditor.dll`

When Mono is available, the engine loads this assembly and calls:

- `EngineEditor.EditorHost.OnEditorStart()`
- `EngineEditor.EditorHost.OnEditorUpdate(float deltaTime)`
- `EngineEditor.EditorHost.OnEditorShutdown()`

The editor assembly can call engine APIs via Mono internal calls exposed in `Engine.EditorBridge`:

- `GetEntityCount()`
- `GetScriptedEntityCount()`
- `SetScriptEnabled(uint entityId, bool enabled)`
- `GetScriptEnabled(uint entityId)`

The engine also exposes a minimal ImGui API to C# via `Engine.ImGui`:

- `Begin(string title)`
- `End()`
- `Text(string value)`
- `Button(string label)`
- `Separator()`

Build the editor assembly with:

```powershell
dotnet build editor\EngineEditor.csproj -c Debug
```

## Next steps

- Add Mono script hot-reload and managed exception reporting.
- Expand ECS with systems (movement, lifetime, animation).
- Add robust texture loading + sprite batching.
- Expand native-to-managed bindings so scripts/editor can modify transforms/components.
