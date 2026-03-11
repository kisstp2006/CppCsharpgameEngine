using System.Runtime.CompilerServices;

namespace Engine
{
    public static class Debug
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void LogInternal(string message);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void LogWarningInternal(string message);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void LogErrorInternal(string message);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern int GetLogCountInternal();

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern string GetLogMessageInternal(int index);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern int GetLogLevelInternal(int index);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void ClearLogsInternal();

        public static void Log(object message)
        {
            LogInternal(message == null ? "null" : message.ToString());
        }

        public static void LogWarning(object message)
        {
            LogWarningInternal(message == null ? "null" : message.ToString());
        }

        public static void LogError(object message)
        {
            LogErrorInternal(message == null ? "null" : message.ToString());
        }

        public static int GetLogCount()
        {
            return GetLogCountInternal();
        }

        public static string GetLogMessage(int index)
        {
            return GetLogMessageInternal(index);
        }

        public static int GetLogLevel(int index)
        {
            return GetLogLevelInternal(index);
        }

        public static void ClearLogs()
        {
            ClearLogsInternal();
        }
    }
}
