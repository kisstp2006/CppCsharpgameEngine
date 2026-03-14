namespace EngineEditor
{
    internal static class AnimationSystem
    {
        public static void RegisterAssetContextMenu()
        {
            AnimationTimelineWindow.RegisterAssetContextMenu();
        }

        public static void DrawWindow(float deltaTime)
        {
            AnimationTimelineWindow.Draw(deltaTime);
        }

        public static void OpenClipFromPath(string clipPath)
        {
            AnimationTimelineWindow.OpenClipFromPath(clipPath);
        }

        public static bool TryManipulateSelectedKeyframeWithGizmo(uint entityId,
                                                                  float viewportX,
                                                                  float viewportY,
                                                                  float viewportWidth,
                                                                  float viewportHeight,
                                                                  float cameraX,
                                                                  float cameraY,
                                                                  float cameraZoom,
                                                                  bool snapEnabled,
                                                                  float snapStep,
                                                                  ref float x,
                                                                  ref float y,
                                                                  ref float width,
                                                                  ref float height,
                                                                  ref float rotationDegrees)
        {
            return AnimationTimelineWindow.TryManipulateSelectedKeyframeWithGizmo(entityId,
                                                                                  viewportX,
                                                                                  viewportY,
                                                                                  viewportWidth,
                                                                                  viewportHeight,
                                                                                  cameraX,
                                                                                  cameraY,
                                                                                  cameraZoom,
                                                                                  snapEnabled,
                                                                                  snapStep,
                                                                                  ref x,
                                                                                  ref y,
                                                                                  ref width,
                                                                                  ref height,
                                                                                  ref rotationDegrees);
        }
    }
}
