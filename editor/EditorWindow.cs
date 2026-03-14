namespace EngineEditor
{
    internal abstract class EditorWindow
    {
        private System.Func<bool> _visibilityPredicate;

        public abstract string Title { get; }

        public virtual bool IsOpen { get; set; } = true;

        public virtual int Order => 0;

        public System.Func<bool> VisibilityPredicate
        {
            get => _visibilityPredicate;
            set => _visibilityPredicate = value;
        }

        public virtual bool ShouldDisplay()
        {
            if (_visibilityPredicate != null && !_visibilityPredicate())
                return false;

            return true;
        }

        // Called once when the window becomes visible (open + displayable).
        public virtual void OnOpened()
        {
        }

        // Called once when the window stops being visible.
        public virtual void OnClosed()
        {
        }

        // Update is called in a dedicated pass for all visible windows before any OnGUI call.
        public virtual void OnUpdate(float deltaTime)
        {
            _ = deltaTime;
        }

        // Optional hook invoked right before OnGUI in the draw pass.
        public virtual void OnBeforeDraw()
        {
        }

        // Optional hook invoked after OnGUI in the draw pass.
        public virtual void OnAfterDraw()
        {
        }

        public abstract void OnGUI();
    }
}
