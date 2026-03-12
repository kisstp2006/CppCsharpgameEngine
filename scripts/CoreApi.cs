using System;
using System.Globalization;

namespace Engine
{
    public struct Vector3
    {
        public float x;
        public float y;
        public float z;

        public Vector3(float x, float y, float z = 0.0f)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static Vector3 operator +(Vector3 a, Vector3 b)
        {
            return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        }

        public static Vector3 operator -(Vector3 a, Vector3 b)
        {
            return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        }

        public static Vector3 operator *(Vector3 a, float scalar)
        {
            return new Vector3(a.x * scalar, a.y * scalar, a.z * scalar);
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###}, {2:0.###})", x, y, z);
        }
    }

    public static class Time
    {
        private static float _deltaTime;

        public static float deltaTime => _deltaTime;

        internal static void SetDeltaTime(float deltaTime)
        {
            _deltaTime = deltaTime;
        }
    }

    public abstract class Component
    {
        internal uint EntityId;

        internal void Attach(uint entityId)
        {
            EntityId = entityId;
        }

        public GameObject gameObject => new GameObject(EntityId);
        public Transform transform => new Transform(EntityId);
    }

    public sealed class GameObject
    {
        internal uint EntityId;

        internal GameObject(uint entityId)
        {
            EntityId = entityId;
        }

        public string name
        {
            get
            {
                string value = EntityManager.GetEntityName(EntityId);
                return string.IsNullOrEmpty(value) ? ("Entity " + EntityId) : value;
            }
            set
            {
                EntityManager.SetEntityName(EntityId, value ?? string.Empty);
            }
        }

        public Transform transform => new Transform(EntityId);

        public T GetComponent<T>() where T : Component, new()
        {
            if (!TryGetComponent<T>(out T component))
                return null;

            return component;
        }

        public bool TryGetComponent<T>(out T component) where T : Component, new()
        {
            component = null;

            if (!EntityManager.IsEntityValid(EntityId))
                return false;

            Type type = typeof(T);
            bool hasComponent = false;

            if (type == typeof(Transform))
                hasComponent = EntityManager.HasTransform(EntityId);
            else if (type == typeof(CameraComponent))
                hasComponent = EntityManager.HasCamera(EntityId);
            else if (type == typeof(SpriteRenderer))
                hasComponent = EntityManager.HasSprite(EntityId);
            else
                return false;

            if (!hasComponent)
                return false;

            component = new T();
            component.Attach(EntityId);
            return true;
        }
    }

    public sealed class Transform : Component
    {
        public Transform()
        {
        }

        internal Transform(uint entityId)
        {
            Attach(entityId);
        }

        public Vector3 position
        {
            get
            {
                float x;
                float y;
                float width;
                float height;
                if (!EntityManager.GetTransform(EntityId, out x, out y, out width, out height))
                    return new Vector3();

                return new Vector3(x, y, 0.0f);
            }
            set
            {
                if (!EntityManager.HasTransform(EntityId))
                    EntityManager.AddTransform(EntityId);

                float x;
                float y;
                float width;
                float height;
                if (!EntityManager.GetTransform(EntityId, out x, out y, out width, out height))
                {
                    width = 100.0f;
                    height = 100.0f;
                }

                EntityManager.SetTransform(EntityId, value.x, value.y, width, height);
            }
        }

        public Vector3 localScale
        {
            get
            {
                float x;
                float y;
                float width;
                float height;
                if (!EntityManager.GetTransform(EntityId, out x, out y, out width, out height))
                    return new Vector3(1.0f, 1.0f, 1.0f);

                return new Vector3(width, height, 1.0f);
            }
            set
            {
                if (!EntityManager.HasTransform(EntityId))
                    EntityManager.AddTransform(EntityId);

                float x;
                float y;
                float width;
                float height;
                if (!EntityManager.GetTransform(EntityId, out x, out y, out width, out height))
                {
                    x = 0.0f;
                    y = 0.0f;
                }

                EntityManager.SetTransform(EntityId, x, y, value.x, value.y);
            }
        }
    }

    public sealed class CameraComponent : Component
    {
        public CameraComponent()
        {
        }

        internal CameraComponent(uint entityId)
        {
            Attach(entityId);
        }

        private bool GetSettings(out float x,
                                 out float y,
                                 out float zoomValue,
                                 out bool enabledValue,
                                 out bool primaryValue,
                                 out bool clearColorValue,
                                 out uint backgroundColorValue,
                                 out uint cullingMaskValue,
                                 out float viewportXValue,
                                 out float viewportYValue,
                                 out float viewportWidthValue,
                                 out float viewportHeightValue)
        {
            return EntityManager.GetCameraSettings(EntityId,
                                                   out x,
                                                   out y,
                                                   out zoomValue,
                                                   out enabledValue,
                                                   out primaryValue,
                                                   out clearColorValue,
                                                   out backgroundColorValue,
                                                   out cullingMaskValue,
                                                   out viewportXValue,
                                                   out viewportYValue,
                                                   out viewportWidthValue,
                                                   out viewportHeightValue);
        }

        private void ApplySettings(float x,
                                   float y,
                                   float zoomValue,
                                   bool enabledValue,
                                   bool primaryValue,
                                   bool clearColorValue,
                                   uint backgroundColorValue,
                                   uint cullingMaskValue,
                                   float viewportXValue,
                                   float viewportYValue,
                                   float viewportWidthValue,
                                   float viewportHeightValue)
        {
            EntityManager.SetCameraSettings(EntityId,
                                            x,
                                            y,
                                            zoomValue,
                                            enabledValue,
                                            primaryValue,
                                            clearColorValue,
                                            backgroundColorValue,
                                            cullingMaskValue,
                                            viewportXValue,
                                            viewportYValue,
                                            viewportWidthValue,
                                            viewportHeightValue);
        }

        private bool GetSettingsV2(out float x,
                                   out float y,
                                   out float zoomValue,
                                   out bool enabledValue,
                                   out bool primaryValue,
                                   out bool clearColorValue,
                                   out uint backgroundColorValue,
                                   out uint cullingMaskValue,
                                   out float viewportXValue,
                                   out float viewportYValue,
                                   out float viewportWidthValue,
                                   out float viewportHeightValue,
                                   out float orthographicSizeValue)
        {
            return EntityManager.GetCameraSettingsV2(EntityId,
                                                     out x,
                                                     out y,
                                                     out zoomValue,
                                                     out enabledValue,
                                                     out primaryValue,
                                                     out clearColorValue,
                                                     out backgroundColorValue,
                                                     out cullingMaskValue,
                                                     out viewportXValue,
                                                     out viewportYValue,
                                                     out viewportWidthValue,
                                                     out viewportHeightValue,
                                                     out orthographicSizeValue);
        }

        private void ApplySettingsV2(float x,
                                     float y,
                                     float zoomValue,
                                     bool enabledValue,
                                     bool primaryValue,
                                     bool clearColorValue,
                                     uint backgroundColorValue,
                                     uint cullingMaskValue,
                                     float viewportXValue,
                                     float viewportYValue,
                                     float viewportWidthValue,
                                     float viewportHeightValue,
                                     float orthographicSizeValue)
        {
            EntityManager.SetCameraSettingsV2(EntityId,
                                              x,
                                              y,
                                              zoomValue,
                                              enabledValue,
                                              primaryValue,
                                              clearColorValue,
                                              backgroundColorValue,
                                              cullingMaskValue,
                                              viewportXValue,
                                              viewportYValue,
                                              viewportWidthValue,
                                              viewportHeightValue,
                                              orthographicSizeValue);
        }

        public float x
        {
            get
            {
                float xValue;
                float yValue;
                float zoomValue;
                bool enabledValue;
                bool primaryValue;
                bool clearColorValue;
                uint backgroundColorValue;
                uint cullingMaskValue;
                float viewportXValue;
                float viewportYValue;
                float viewportWidthValue;
                float viewportHeightValue;
                if (!GetSettings(out xValue,
                                 out yValue,
                                 out zoomValue,
                                 out enabledValue,
                                 out primaryValue,
                                 out clearColorValue,
                                 out backgroundColorValue,
                                 out cullingMaskValue,
                                 out viewportXValue,
                                 out viewportYValue,
                                 out viewportWidthValue,
                                 out viewportHeightValue))
                {
                    return 0.0f;
                }

                return xValue;
            }
            set
            {
                float xValue;
                float yValue;
                float zoomValue;
                bool enabledValue;
                bool primaryValue;
                bool clearColorValue;
                uint backgroundColorValue;
                uint cullingMaskValue;
                float viewportXValue;
                float viewportYValue;
                float viewportWidthValue;
                float viewportHeightValue;
                if (!GetSettings(out xValue,
                                 out yValue,
                                 out zoomValue,
                                 out enabledValue,
                                 out primaryValue,
                                 out clearColorValue,
                                 out backgroundColorValue,
                                 out cullingMaskValue,
                                 out viewportXValue,
                                 out viewportYValue,
                                 out viewportWidthValue,
                                 out viewportHeightValue))
                {
                    return;
                }

                ApplySettings(value,
                              yValue,
                              zoomValue,
                              enabledValue,
                              primaryValue,
                              clearColorValue,
                              backgroundColorValue,
                              cullingMaskValue,
                              viewportXValue,
                              viewportYValue,
                              viewportWidthValue,
                              viewportHeightValue);
            }
        }

        public float y
        {
            get
            {
                float xValue;
                float yValue;
                float zoomValue;
                bool enabledValue;
                bool primaryValue;
                bool clearColorValue;
                uint backgroundColorValue;
                uint cullingMaskValue;
                float viewportXValue;
                float viewportYValue;
                float viewportWidthValue;
                float viewportHeightValue;
                if (!GetSettings(out xValue,
                                 out yValue,
                                 out zoomValue,
                                 out enabledValue,
                                 out primaryValue,
                                 out clearColorValue,
                                 out backgroundColorValue,
                                 out cullingMaskValue,
                                 out viewportXValue,
                                 out viewportYValue,
                                 out viewportWidthValue,
                                 out viewportHeightValue))
                {
                    return 0.0f;
                }

                return yValue;
            }
            set
            {
                float xValue;
                float yValue;
                float zoomValue;
                bool enabledValue;
                bool primaryValue;
                bool clearColorValue;
                uint backgroundColorValue;
                uint cullingMaskValue;
                float viewportXValue;
                float viewportYValue;
                float viewportWidthValue;
                float viewportHeightValue;
                if (!GetSettings(out xValue,
                                 out yValue,
                                 out zoomValue,
                                 out enabledValue,
                                 out primaryValue,
                                 out clearColorValue,
                                 out backgroundColorValue,
                                 out cullingMaskValue,
                                 out viewportXValue,
                                 out viewportYValue,
                                 out viewportWidthValue,
                                 out viewportHeightValue))
                {
                    return;
                }

                ApplySettings(xValue,
                              value,
                              zoomValue,
                              enabledValue,
                              primaryValue,
                              clearColorValue,
                              backgroundColorValue,
                              cullingMaskValue,
                              viewportXValue,
                              viewportYValue,
                              viewportWidthValue,
                              viewportHeightValue);
            }
        }

        public float zoom
        {
            get
            {
                float xValue;
                float yValue;
                float zoomValue;
                bool enabledValue;
                bool primaryValue;
                bool clearColorValue;
                uint backgroundColorValue;
                uint cullingMaskValue;
                float viewportXValue;
                float viewportYValue;
                float viewportWidthValue;
                float viewportHeightValue;
                if (!GetSettings(out xValue,
                                 out yValue,
                                 out zoomValue,
                                 out enabledValue,
                                 out primaryValue,
                                 out clearColorValue,
                                 out backgroundColorValue,
                                 out cullingMaskValue,
                                 out viewportXValue,
                                 out viewportYValue,
                                 out viewportWidthValue,
                                 out viewportHeightValue))
                {
                    return 1.0f;
                }

                return zoomValue;
            }
            set
            {
                float xValue;
                float yValue;
                float zoomValue;
                bool enabledValue;
                bool primaryValue;
                bool clearColorValue;
                uint backgroundColorValue;
                uint cullingMaskValue;
                float viewportXValue;
                float viewportYValue;
                float viewportWidthValue;
                float viewportHeightValue;
                if (!GetSettings(out xValue,
                                 out yValue,
                                 out zoomValue,
                                 out enabledValue,
                                 out primaryValue,
                                 out clearColorValue,
                                 out backgroundColorValue,
                                 out cullingMaskValue,
                                 out viewportXValue,
                                 out viewportYValue,
                                 out viewportWidthValue,
                                 out viewportHeightValue))
                {
                    return;
                }

                ApplySettings(xValue,
                              yValue,
                              value,
                              enabledValue,
                              primaryValue,
                              clearColorValue,
                              backgroundColorValue,
                              cullingMaskValue,
                              viewportXValue,
                              viewportYValue,
                              viewportWidthValue,
                              viewportHeightValue);
            }
        }

        public bool enabled
        {
            get
            {
                float xValue;
                float yValue;
                float zoomValue;
                bool enabledValue;
                bool primaryValue;
                bool clearColorValue;
                uint backgroundColorValue;
                uint cullingMaskValue;
                float viewportXValue;
                float viewportYValue;
                float viewportWidthValue;
                float viewportHeightValue;
                if (!GetSettings(out xValue,
                                 out yValue,
                                 out zoomValue,
                                 out enabledValue,
                                 out primaryValue,
                                 out clearColorValue,
                                 out backgroundColorValue,
                                 out cullingMaskValue,
                                 out viewportXValue,
                                 out viewportYValue,
                                 out viewportWidthValue,
                                 out viewportHeightValue))
                {
                    return true;
                }

                return enabledValue;
            }
            set
            {
                float xValue;
                float yValue;
                float zoomValue;
                bool enabledValue;
                bool primaryValue;
                bool clearColorValue;
                uint backgroundColorValue;
                uint cullingMaskValue;
                float viewportXValue;
                float viewportYValue;
                float viewportWidthValue;
                float viewportHeightValue;
                if (!GetSettings(out xValue,
                                 out yValue,
                                 out zoomValue,
                                 out enabledValue,
                                 out primaryValue,
                                 out clearColorValue,
                                 out backgroundColorValue,
                                 out cullingMaskValue,
                                 out viewportXValue,
                                 out viewportYValue,
                                 out viewportWidthValue,
                                 out viewportHeightValue))
                {
                    return;
                }

                ApplySettings(xValue,
                              yValue,
                              zoomValue,
                              value,
                              primaryValue,
                              clearColorValue,
                              backgroundColorValue,
                              cullingMaskValue,
                              viewportXValue,
                              viewportYValue,
                              viewportWidthValue,
                              viewportHeightValue);
            }
        }

        public bool main
        {
            get
            {
                float xValue;
                float yValue;
                float zoomValue;
                bool enabledValue;
                bool primaryValue;
                bool clearColorValue;
                uint backgroundColorValue;
                uint cullingMaskValue;
                float viewportXValue;
                float viewportYValue;
                float viewportWidthValue;
                float viewportHeightValue;
                if (!GetSettings(out xValue,
                                 out yValue,
                                 out zoomValue,
                                 out enabledValue,
                                 out primaryValue,
                                 out clearColorValue,
                                 out backgroundColorValue,
                                 out cullingMaskValue,
                                 out viewportXValue,
                                 out viewportYValue,
                                 out viewportWidthValue,
                                 out viewportHeightValue))
                {
                    return true;
                }

                return primaryValue;
            }
            set
            {
                float xValue;
                float yValue;
                float zoomValue;
                bool enabledValue;
                bool primaryValue;
                bool clearColorValue;
                uint backgroundColorValue;
                uint cullingMaskValue;
                float viewportXValue;
                float viewportYValue;
                float viewportWidthValue;
                float viewportHeightValue;
                if (!GetSettings(out xValue,
                                 out yValue,
                                 out zoomValue,
                                 out enabledValue,
                                 out primaryValue,
                                 out clearColorValue,
                                 out backgroundColorValue,
                                 out cullingMaskValue,
                                 out viewportXValue,
                                 out viewportYValue,
                                 out viewportWidthValue,
                                 out viewportHeightValue))
                {
                    return;
                }

                ApplySettings(xValue,
                              yValue,
                              zoomValue,
                              enabledValue,
                              value,
                              clearColorValue,
                              backgroundColorValue,
                              cullingMaskValue,
                              viewportXValue,
                              viewportYValue,
                              viewportWidthValue,
                              viewportHeightValue);
            }
        }

        public float orthographicSize
        {
            get
            {
                float xValue;
                float yValue;
                float zoomValue;
                bool enabledValue;
                bool primaryValue;
                bool clearColorValue;
                uint backgroundColorValue;
                uint cullingMaskValue;
                float viewportXValue;
                float viewportYValue;
                float viewportWidthValue;
                float viewportHeightValue;
                float orthographicSizeValue;
                if (!GetSettingsV2(out xValue,
                                   out yValue,
                                   out zoomValue,
                                   out enabledValue,
                                   out primaryValue,
                                   out clearColorValue,
                                   out backgroundColorValue,
                                   out cullingMaskValue,
                                   out viewportXValue,
                                   out viewportYValue,
                                   out viewportWidthValue,
                                   out viewportHeightValue,
                                   out orthographicSizeValue))
                {
                    return 0.0f;
                }

                return orthographicSizeValue;
            }
            set
            {
                float xValue;
                float yValue;
                float zoomValue;
                bool enabledValue;
                bool primaryValue;
                bool clearColorValue;
                uint backgroundColorValue;
                uint cullingMaskValue;
                float viewportXValue;
                float viewportYValue;
                float viewportWidthValue;
                float viewportHeightValue;
                float orthographicSizeValue;
                if (!GetSettingsV2(out xValue,
                                   out yValue,
                                   out zoomValue,
                                   out enabledValue,
                                   out primaryValue,
                                   out clearColorValue,
                                   out backgroundColorValue,
                                   out cullingMaskValue,
                                   out viewportXValue,
                                   out viewportYValue,
                                   out viewportWidthValue,
                                   out viewportHeightValue,
                                   out orthographicSizeValue))
                {
                    return;
                }

                ApplySettingsV2(xValue,
                                yValue,
                                zoomValue,
                                enabledValue,
                                primaryValue,
                                clearColorValue,
                                backgroundColorValue,
                                cullingMaskValue,
                                viewportXValue,
                                viewportYValue,
                                viewportWidthValue,
                                viewportHeightValue,
                                value);
            }
        }
    }

    public sealed class SpriteRenderer : Component
    {
        public SpriteRenderer()
        {
        }

        internal SpriteRenderer(uint entityId)
        {
            Attach(entityId);
        }

        public string texturePath
        {
            get
            {
                return Sprite.GetTexturePath(EntityId) ?? string.Empty;
            }
            set
            {
                Sprite.SetTexturePath(EntityId, value ?? string.Empty);
            }
        }

        public uint fallbackColor
        {
            get
            {
                return Sprite.GetFallbackColor(EntityId);
            }
            set
            {
                Sprite.SetFallbackColor(EntityId, value);
            }
        }
    }

    public class MonoBehaviour : Component
    {
        private void ReportLifecycleException(string lifecycleName, Exception ex)
        {
            string typeName;
            try
            {
                typeName = GetType().FullName ?? GetType().Name;
            }
            catch
            {
                typeName = "UnknownScript";
            }

            string message = ex == null ? "Unknown exception" : ex.ToString();
            Debug.LogError("[MonoBehaviour] Exception in " + typeName + "." + lifecycleName + ": " + message);
        }

        public T GetComponent<T>() where T : Component, new()
        {
            return gameObject.GetComponent<T>();
        }

        public bool TryGetComponent<T>(out T component) where T : Component, new()
        {
            return gameObject.TryGetComponent(out component);
        }

        public void OnCreate(uint entityId)
        {
            Attach(entityId);

            try
            {
                Start();
            }
            catch (Exception ex)
            {
                ReportLifecycleException("Start", ex);
            }
        }

        public void OnEnable(uint entityId)
        {
            Attach(entityId);

            try
            {
                OnEnable();
            }
            catch (Exception ex)
            {
                ReportLifecycleException("OnEnable", ex);
            }
        }

        public void OnDisable(uint entityId)
        {
            Attach(entityId);

            try
            {
                OnDisable();
            }
            catch (Exception ex)
            {
                ReportLifecycleException("OnDisable", ex);
            }
        }

        public void OnDestroy(uint entityId)
        {
            Attach(entityId);

            try
            {
                OnDestroy();
            }
            catch (Exception ex)
            {
                ReportLifecycleException("OnDestroy", ex);
            }
        }

        public void OnUpdate(uint entityId, float deltaTime)
        {
            Attach(entityId);

            if (!EntityManager.IsEntityValid(entityId))
                return;

            Time.SetDeltaTime(deltaTime);

            try
            {
                Update();
            }
            catch (Exception ex)
            {
                ReportLifecycleException("Update", ex);
            }
        }

        protected virtual void Start()
        {
        }

        protected virtual void Update()
        {
        }

        protected virtual void OnEnable()
        {
        }

        protected virtual void OnDisable()
        {
        }

        protected virtual void OnDestroy()
        {
        }
    }
}
