using System;
using System.Runtime.CompilerServices;

namespace Engine
{
    public static class Explorer
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern string PickFolderInternal(string title, string initialPath);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern string PickFileInternal(string title, string initialPath);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern string PickFilesInternal(string title, string initialPath);

        public static string PickFolder(string title, string initialPath = "")
        {
            return PickFolderInternal(title ?? string.Empty, initialPath ?? string.Empty);
        }

        public static string PickFile(string title, string initialPath = "")
        {
            return PickFileInternal(title ?? string.Empty, initialPath ?? string.Empty);
        }

        public static string[] PickFiles(string title, string initialPath = "")
        {
            string rawResult = PickFilesInternal(title ?? string.Empty, initialPath ?? string.Empty);
            if (string.IsNullOrEmpty(rawResult))
                return Array.Empty<string>();

            return rawResult.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}