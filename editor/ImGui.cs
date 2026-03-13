using System.Runtime.CompilerServices;

namespace Engine
{
    public static class ImGui
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool Begin(string title);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool BeginCenteredFixed(string title, float width, float height, bool noCollapse);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool BeginFillNoDecoration(string title, float topInset);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool BeginTopBar(string id, float height);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void EndTopBar();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool BeginMenu(string label);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void EndMenu();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool MenuItem(string label, bool enabled);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool BeginChild(string id, float width, float height, bool border);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void End();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void EndChild();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void Text(string value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void SetTooltip(string value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool Button(string label);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void OpenPopup(string popupId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool BeginPopupModal(string popupId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool BeginPopup(string popupId);

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

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool Checkbox(string label, ref bool value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool ColorButton(string id, float r, float g, float b, float a);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool ColorPicker4(string label,
                               ref float r,
                               ref float g,
                               ref float b,
                               ref float a,
                               bool showAlpha);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool IsWindowHovered();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool GetWantCaptureMouse();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool GetWantCaptureKeyboard();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool IsMouseDown(int button);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern float GetMouseDeltaX();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern float GetMouseDeltaY();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern float GetMouseWheel();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern float GetMousePosX();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern float GetMousePosY();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern float GetDisplayWidth();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern float GetDisplayHeight();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern float GetContentRegionAvailX();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern float GetContentRegionAvailY();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern float GetCursorScreenPosX();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern float GetCursorScreenPosY();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern ulong GetImageHandle(string path);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void Image(ulong textureHandle, float width, float height);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool InvisibleButton(string id, float width, float height);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool IsItemHovered();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool IsItemActive();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool IsMouseClicked(int button);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void DrawLine(float x0,
                                           float y0,
                                           float x1,
                                           float y1,
                                           float r,
                                           float g,
                                           float b,
                                           float a,
                                           float thickness);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void DrawRect(float x,
                                           float y,
                                           float width,
                                           float height,
                                           float r,
                                           float g,
                                           float b,
                                           float a,
                                           float thickness);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void SetStyleColor(int colorIdx, float r, float g, float b, float a);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void GetStyleColor(int colorIdx, out float r, out float g, out float b, out float a);
    }
}
