using Engine;

namespace EngineEditor
{
    internal struct BuiltInUiPalette
    {
        public uint text;
        public uint textDisabled;
        public uint windowBg;
        public uint childBg;
        public uint border;
        public uint frameBg;
        public uint frameBgHovered;
        public uint frameBgActive;
        public uint button;
        public uint buttonHovered;
        public uint buttonActive;
        public uint header;
        public uint headerHovered;
        public uint headerActive;
        public uint separator;
        public uint dockingPreview;
    }

    internal static class BuiltInUiTheme
    {
        private static readonly BuiltInUiPalette DefaultPalette = CreateDefaultPalette();

        public static BuiltInUiPalette Capture()
        {
            return DefaultPalette;
        }

        public static uint WithAlpha(uint rgba, byte alpha)
        {
            return (rgba & 0xFFFFFF00u) | alpha;
        }

        public static uint TintTowardWhite(uint rgba, float amount)
        {
            byte alpha = (byte)(rgba & 0xFFu);
            uint target = UI.Rgba(255, 255, 255, alpha);
            return UI.LerpColor(rgba, target, UI.Clamp01(amount));
        }

        public static uint TintTowardBlack(uint rgba, float amount)
        {
            byte alpha = (byte)(rgba & 0xFFu);
            uint target = UI.Rgba(0, 0, 0, alpha);
            return UI.LerpColor(rgba, target, UI.Clamp01(amount));
        }

        public static void ApplyActionButtonTheme(Button button, BuiltInUiPalette palette)
        {
            button.normalColor = WithAlpha(palette.button, 255);
            button.highlightedColor = WithAlpha(palette.buttonHovered, 255);
            button.pressedColor = WithAlpha(palette.buttonActive, 255);
            button.disabledColor = WithAlpha(TintTowardBlack(palette.frameBg, 0.06f), 210);
            button.borderColor = WithAlpha(palette.border, 220);
            button.textColor = WithAlpha(palette.text, 255);
        }

        public static void ApplyListButtonTheme(Button button, BuiltInUiPalette palette, bool selected)
        {
            if (selected)
            {
                button.normalColor = WithAlpha(palette.headerActive, 245);
                button.highlightedColor = WithAlpha(palette.headerHovered, 255);
                button.pressedColor = WithAlpha(palette.headerActive, 255);
                button.borderColor = WithAlpha(palette.separator, 240);
            }
            else
            {
                button.normalColor = WithAlpha(palette.header, 220);
                button.highlightedColor = WithAlpha(palette.headerHovered, 240);
                button.pressedColor = WithAlpha(palette.headerActive, 245);
                button.borderColor = WithAlpha(palette.border, 210);
            }

            button.disabledColor = WithAlpha(TintTowardBlack(palette.frameBg, 0.08f), 195);
            button.textColor = WithAlpha(palette.text, 255);
        }

        private static BuiltInUiPalette CreateDefaultPalette()
        {
            BuiltInUiPalette palette;
            palette.text = UI.Rgba(232, 236, 245, 255);
            palette.textDisabled = UI.Rgba(158, 166, 180, 255);
            palette.windowBg = UI.Rgba(31, 34, 41, 255);
            palette.childBg = UI.Rgba(46, 49, 56, 255);
            palette.border = UI.Rgba(88, 93, 103, 255);
            palette.frameBg = UI.Rgba(59, 63, 72, 255);
            palette.frameBgHovered = UI.Rgba(72, 77, 88, 255);
            palette.frameBgActive = UI.Rgba(82, 88, 100, 255);
            palette.button = UI.Rgba(74, 80, 92, 255);
            palette.buttonHovered = UI.Rgba(95, 102, 116, 255);
            palette.buttonActive = UI.Rgba(107, 115, 131, 255);
            palette.header = UI.Rgba(58, 64, 76, 255);
            palette.headerHovered = UI.Rgba(72, 79, 93, 255);
            palette.headerActive = UI.Rgba(86, 94, 110, 255);
            palette.separator = UI.Rgba(112, 121, 140, 255);
            palette.dockingPreview = UI.Rgba(88, 156, 236, 255);
            return palette;
        }
    }
}
