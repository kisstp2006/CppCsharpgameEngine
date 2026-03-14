namespace EngineEditor
{
    public static class EditorHost
    {
        public static string StatusMessage => EditorApplication.StatusMessage;

        public static void OnEditorStart()
        {
            EditorApplication.OnEditorStart();
        }

        public static void OnEditorUpdate(float deltaTime)
        {
            EditorApplication.OnEditorUpdate(deltaTime);
        }

        public static void SetStatusMessage(string message)
        {
            EditorApplication.SetStatusMessage(message);
        }

        public static void SetShowProjectManagerView(bool show)
        {
            EditorApplication.SetShowProjectManagerView(show);
        }

        public static void OnEditorShutdown()
        {
            EditorApplication.OnEditorShutdown();
        }
    }
}


