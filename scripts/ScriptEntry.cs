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
        private float _accumulator;

        public void OnCreate(uint entityId)
        {
            Debug.Log("[GameScripts] SpinnerScript created for entity " + entityId + ".");
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
            _accumulator += deltaTime;
            if (_accumulator >= 2.0f)
            {
                _accumulator = 0.0f;
                Debug.Log("[GameScripts] SpinnerScript update on entity " + entityId + ".");
            }
        }

        public void OnDestroy(uint entityId)
        {
            Debug.Log("[GameScripts] SpinnerScript destroyed for entity " + entityId + ".");
        }
    }
}
