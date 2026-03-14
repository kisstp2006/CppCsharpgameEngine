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
        public static extern bool BeginCombo(string label, string previewValue);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void EndCombo();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool BeginPopup(string popupId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void EndPopup();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void CloseCurrentPopup();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void SameLine();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void SetNextWindowFocus();

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

        // ── Tree Node ──────────────────────────────────────────────────

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool TreeNodeEx(string label, int flags);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void TreePop();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void SetNextItemOpen(bool open, int condition);

        // ── Drag & Drop ────────────────────────────────────────────────

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool BeginDragDropSource(int flags);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool SetDragDropPayloadUint(string type, uint data);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void EndDragDropSource();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool BeginDragDropTarget();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern uint AcceptDragDropPayloadUint(string type);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void EndDragDropTarget();

        // ── Styling ────────────────────────────────────────────────────

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void PushStyleColor(int idx, float r, float g, float b, float a);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void PopStyleColor(int count);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void PushStyleVar(int idx, float val);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void PushStyleVar2(int idx, float x, float y);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void PopStyleVar(int count);

        // ── Layout / Cursor ────────────────────────────────────────────

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void SetCursorPosX(float x);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern float GetCursorPosX();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void SetCursorPosY(float y);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern float GetCursorPosY();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern float GetFrameHeight();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void Dummy(float width, float height);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void Spacing();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern float GetScrollY();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void SetScrollY(float y);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern float GetScrollMaxY();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern float GetTreeNodeToLabelSpacing();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern float GetItemRectMinY();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern float GetItemRectMaxY();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern float GetWindowPosY();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern float GetWindowHeight();

        // ── Input Queries ──────────────────────────────────────────────

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool IsItemClicked(int mouseButton);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool IsKeyDown(int scancode);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool IsKeyPressed(int key, bool repeat);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool BeginPopupContextWindow(string id, int mouseButton);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool BeginPopupContextItem(string id, int mouseButton);

        // ── DrawList additions ─────────────────────────────────────────

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void DrawRectFilled(float x,
                                                  float y,
                                                  float width,
                                                  float height,
                                                  float r,
                                                  float g,
                                                  float b,
                                                  float a);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void DrawRectFilledRounded(float x,
                                                         float y,
                                                         float width,
                                                         float height,
                                                         float r,
                                                         float g,
                                                         float b,
                                                         float a,
                                                         float rounding);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void CalcTextSize(string text, out float w, out float h);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void DrawText(float x,
                                            float y,
                                            string text,
                                            float r,
                                            float g,
                                            float b,
                                            float a);

        // ── ImGui Flag Constants ───────────────────────────────────────

        // ImGuiTreeNodeFlags
        public const int TreeNodeFlags_None = 0;
        public const int TreeNodeFlags_Selected = 1 << 0;
        public const int TreeNodeFlags_Framed = 1 << 1;
        public const int TreeNodeFlags_AllowOverlap = 1 << 2;
        public const int TreeNodeFlags_NoTreePushOnOpen = 1 << 3;
        public const int TreeNodeFlags_NoAutoOpenOnLog = 1 << 4;
        public const int TreeNodeFlags_DefaultOpen = 1 << 5;
        public const int TreeNodeFlags_OpenOnDoubleClick = 1 << 6;
        public const int TreeNodeFlags_OpenOnArrow = 1 << 7;
        public const int TreeNodeFlags_Leaf = 1 << 8;
        public const int TreeNodeFlags_Bullet = 1 << 9;
        public const int TreeNodeFlags_FramePadding = 1 << 10;
        public const int TreeNodeFlags_SpanAvailWidth = 1 << 11;
        public const int TreeNodeFlags_SpanFullWidth = 1 << 12;
        public const int TreeNodeFlags_SpanAllColumns = 1 << 13;

        // ImGuiDragDropFlags
        public const int DragDropFlags_None = 0;
        public const int DragDropFlags_SourceNoPreviewTooltip = 1 << 0;
        public const int DragDropFlags_SourceNoDisableHover = 1 << 1;
        public const int DragDropFlags_SourceNoHoldToOpenOthers = 1 << 2;
        public const int DragDropFlags_SourceAllowNullID = 1 << 3;
        public const int DragDropFlags_SourceExtern = 1 << 4;

        // ImGuiCol
        public const int Col_Text = 0;
        public const int Col_TextDisabled = 1;
        public const int Col_WindowBg = 2;
        public const int Col_ChildBg = 3;
        public const int Col_PopupBg = 4;
        public const int Col_Border = 5;
        public const int Col_FrameBg = 7;
        public const int Col_FrameBgHovered = 8;
        public const int Col_FrameBgActive = 9;
        public const int Col_Header = 24;
        public const int Col_HeaderHovered = 25;
        public const int Col_HeaderActive = 26;
        public const int Col_Separator = 27;
        public const int Col_DragDropTarget = 42;
        public const int Col_NavHighlight = 43;

        // ImGuiStyleVar
        public const int StyleVar_FramePadding = 12;
        public const int StyleVar_FrameRounding = 13;
        public const int StyleVar_ItemSpacing = 15;
        public const int StyleVar_IndentSpacing = 17;

        // ImGuiCond
        public const int Cond_Always = 1 << 0;
        public const int Cond_Once = 1 << 1;
        public const int Cond_FirstUseEver = 1 << 2;

        // ImGuiPopupFlags
        public const int PopupFlags_MouseButtonRight = 1;

        // ImGuiKey (subset for common editor keys)
        public const int Key_Delete = 524;
        public const int Key_Escape = 525;
        public const int Key_F2 = 537;
        public const int Key_A = 546;
        public const int Key_C = 548;
        public const int Key_D = 549;
        public const int Key_V = 567;
        public const int Key_LeftCtrl = 583;
        public const int Key_LeftShift = 585;
        public const int Key_RightCtrl = 587;
        public const int Key_RightShift = 589;
    }
}
