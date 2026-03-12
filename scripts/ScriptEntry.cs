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

    public sealed class SpinnerScript : MonoBehaviour
    {
        private float _moveSpeed = 280.0f;
        private float _logAccumulator;

        protected override void Start()
        {
            Debug.Log("[GameScripts] SpinnerScript started on '" + gameObject.name + "'.");
        }

        protected override void Update()
        {
            float deltaTime = Time.deltaTime;

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

            Vector3 current = transform.position;
            Vector3 next = current + new Vector3(moveX, moveY, 0.0f);
            transform.position = next;

            _logAccumulator += deltaTime;
            if (_logAccumulator >= 0.2f)
            {
                _logAccumulator = 0.0f;
                Debug.Log("[GameScripts] " + gameObject.name + " moved to " + transform.position + ".");
            }
        }

        protected override void OnDestroy()
        {
            Debug.Log("[GameScripts] SpinnerScript destroyed on '" + gameObject.name + "'.");
        }
    }
}
