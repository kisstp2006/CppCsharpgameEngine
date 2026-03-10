using System;

namespace GameScripts
{
    public static class ScriptEntry
    {
        private static float _timeAccumulator;

        public static void OnEngineStart()
        {
            Console.WriteLine("[GameScripts] OnEngineStart called.");
        }

        public static void OnEngineUpdate(float deltaTime)
        {
            _timeAccumulator += deltaTime;

            if (_timeAccumulator >= 1.0f)
            {
                _timeAccumulator = 0.0f;
                Console.WriteLine("[GameScripts] Tick from C# script.");
            }
        }

        public static void OnEngineShutdown()
        {
            Console.WriteLine("[GameScripts] OnEngineShutdown called.");
        }
    }

    public sealed class SpinnerScript
    {
        private float _accumulator;

        public void OnCreate(uint entityId)
        {
            Console.WriteLine("[GameScripts] SpinnerScript created for entity " + entityId + ".");
        }

        public void OnUpdate(uint entityId, float deltaTime)
        {
            _accumulator += deltaTime;
            if (_accumulator >= 2.0f)
            {
                _accumulator = 0.0f;
                Console.WriteLine("[GameScripts] SpinnerScript update on entity " + entityId + ".");
            }
        }

        public void OnDestroy(uint entityId)
        {
            Console.WriteLine("[GameScripts] SpinnerScript destroyed for entity " + entityId + ".");
        }
    }
}
