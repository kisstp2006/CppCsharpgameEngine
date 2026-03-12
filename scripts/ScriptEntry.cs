using System;
using Engine;

namespace GameScripts
{
    public static class ScriptEntry
    {
        private static float _timeAccumulator;

        public static void OnEngineStart()
        {
            Debug.Log("[GameScripts] OnEngineStart called.");
        }

        public static void OnEngineUpdate(float deltaTime)
        {
            _timeAccumulator += deltaTime;

            if (_timeAccumulator >= 1.0f)
            {
                _timeAccumulator = 0.0f;
                Debug.Log("[GameScripts] Tick from C# script.");
            }
        }

        public static void OnEngineShutdown()
        {
            Debug.Log("[GameScripts] OnEngineShutdown called.");
        }
    }

    public sealed class SpinnerScript
    {
        private float _moveSpeed = 200.0f;
        private float _logAccumulator;

        public void OnCreate(uint entityId)
        {
            if (!EntityManager.HasTransform(entityId))
            {
                EntityManager.AddTransform(entityId);
            }

            Debug.Log("[GameScripts] SpinnerScript created for entity " + entityId + ". WASD movement enabled.");
        }

        public void OnEnable(uint entityId)
        {
            Debug.Log("[GameScripts] SpinnerScript enabled for entity " + entityId + ".");
        }

        public void OnDisable(uint entityId)
        {
            Debug.Log("[GameScripts] SpinnerScript disabled for entity " + entityId + ".");
        }

        public void OnUpdate(uint entityId, float deltaTime)
        {
            float x;
            float y;
            float width;
            float height;
            if (!EntityManager.GetTransform(entityId, out x, out y, out width, out height))
                return;

            float moveX = 0.0f;
            float moveY = 0.0f;

            if (Input.GetKey(KeyCode.W))
                moveY += _moveSpeed * deltaTime;
            if (Input.GetKey(KeyCode.S))
                moveY -= _moveSpeed * deltaTime;
            if (Input.GetKey(KeyCode.A))
                moveX -= _moveSpeed * deltaTime;
            if (Input.GetKey(KeyCode.D))
                moveX += _moveSpeed * deltaTime;

            if (moveX == 0.0f && moveY == 0.0f)
                return;

            EntityManager.SetTransform(entityId, x + moveX, y + moveY, width, height);

            _logAccumulator += deltaTime;
            if (_logAccumulator >= 0.2f)
            {
                _logAccumulator = 0.0f;

                float newX;
                float newY;
                float newWidth;
                float newHeight;
                if (EntityManager.GetTransform(entityId, out newX, out newY, out newWidth, out newHeight))
                {
                    Debug.Log("[GameScripts] Entity " + entityId + " moved to (" + newX + ", " + newY + ")");
                }
            }
        }

        public void OnDestroy(uint entityId)
        {
            Debug.Log("[GameScripts] SpinnerScript destroyed for entity " + entityId + ".");
        }
    }
}
