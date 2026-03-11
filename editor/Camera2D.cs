namespace Engine
{
    public sealed class Camera2D
    {
        public float X;
        public float Y;
        public float Zoom = 1.0f;

        public float MinZoom = 0.15f;
        public float MaxZoom = 8.0f;

        public void Reset(float x = 0.0f, float y = 0.0f, float zoom = 1.0f)
        {
            X = x;
            Y = y;
            Zoom = Clamp(zoom, MinZoom, MaxZoom);
        }

        public void ApplyWheel(float wheelDelta, float wheelScale)
        {
            if (wheelDelta > -0.0001f && wheelDelta < 0.0001f)
                return;

            float zoomMultiplier = 1.0f + (wheelDelta * wheelScale);
            if (zoomMultiplier < 0.2f)
                zoomMultiplier = 0.2f;

            Zoom = Clamp(Zoom * zoomMultiplier, MinZoom, MaxZoom);
        }

        public void PanPixels(float deltaX, float deltaY)
        {
            X -= deltaX / Zoom;
            Y += deltaY / Zoom;
        }

        public float WorldToScreenX(float worldX, float centerX)
        {
            return ((worldX - X) * Zoom) + centerX;
        }

        public float WorldToScreenY(float worldY, float centerY)
        {
            return ((worldY - Y) * Zoom) + centerY;
        }

        public float ScreenToWorldX(float screenX, float centerX)
        {
            return ((screenX - centerX) / Zoom) + X;
        }

        public float ScreenToWorldY(float screenYBottomLeft, float centerY)
        {
            return ((screenYBottomLeft - centerY) / Zoom) + Y;
        }

        private static float Clamp(float value, float minValue, float maxValue)
        {
            if (value < minValue)
                return minValue;
            if (value > maxValue)
                return maxValue;
            return value;
        }
    }
}
