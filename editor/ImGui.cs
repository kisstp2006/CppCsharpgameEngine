using System.Runtime.CompilerServices;

namespace Engine
{
    public static class ImGui
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool Begin(string title);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool BeginChild(string id, float width, float height, bool border);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void End();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void EndChild();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void Text(string value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool Button(string label);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void OpenPopup(string popupId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool BeginPopupModal(string popupId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void EndPopup();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void CloseCurrentPopup();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void SameLine();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void SetNextItemWidth(float width);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool Selectable(string label, bool selected);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool SelectableNoClose(string label, bool selected);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern string InputText(string label, string value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool InputFloat(string label, ref float value, float step);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void Separator();
    }
}
