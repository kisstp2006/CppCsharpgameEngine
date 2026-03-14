using Engine;

namespace EngineEditor
{
    internal static class SplashUiSystem
    {
        private const string SplashImagePath = "assets/editor/splash_logo.png";

        private static bool _initialized;
        private static float _loadingProgress = 0.15f;
        private static bool _loadingForward = true;

        private static readonly Image _brandImage = new Image();
        private static readonly Button _refreshButton = new Button();
        private static readonly ProgressBar _loadingBar = new ProgressBar();

        public static void Reset()
        {
            _loadingProgress = 0.15f;
            _loadingForward = true;
        }

        public static void DrawProjectSelectorOverlay(float deltaTime)
        {
            EnsureInitialized();
            _ = deltaTime;
            BuiltInUiPalette palette = BuiltInUiTheme.Capture();

            Canvas canvas = new Canvas();
            Rect canvasRect = canvas.Bounds;

            float cardWidth = 350.0f;
            float cardHeight = 162.0f;
            Rect cardRect = new Rect(canvasRect.width - cardWidth - 20.0f,
                                     16.0f,
                                     cardWidth,
                                     cardHeight);

            UI.DrawFilledRoundedRect(cardRect, BuiltInUiTheme.WithAlpha(palette.childBg, 200), 8.0f);
            UI.DrawRoundedRect(cardRect, BuiltInUiTheme.WithAlpha(palette.border, 225), 8.0f, 1.0f);

            _brandImage.rect = new Rect(cardRect.x + 14.0f,
                                        cardRect.y + 18.0f,
                                        96.0f,
                                        96.0f);
            _brandImage.Draw();

            Rect titleRect = new Rect(cardRect.x + 122.0f,
                                      cardRect.y + 20.0f,
                                      cardRect.width - 136.0f,
                                      28.0f);
            UI.DrawLabel(titleRect,
                         "CppCSharp Editor",
                         BuiltInUiTheme.WithAlpha(palette.text, 255),
                         17.0f,
                         true,
                         TextAlign.Left);

            Rect subtitleRect = new Rect(cardRect.x + 122.0f,
                                         cardRect.y + 52.0f,
                                         cardRect.width - 136.0f,
                                         38.0f);
            UI.DrawLabel(subtitleRect,
                         "Project selector\nmodernized splash overlay",
                         BuiltInUiTheme.WithAlpha(palette.textDisabled, 255),
                         12.0f,
                         false,
                         TextAlign.Left);

            _refreshButton.rect = new Rect(cardRect.x + 122.0f,
                                           cardRect.y + 108.0f,
                                           cardRect.width - 136.0f,
                                           36.0f);
            BuiltInUiTheme.ApplyActionButtonTheme(_refreshButton, palette);
            _refreshButton.Draw();
        }

        public static void DrawLoadingOverlay(float deltaTime)
        {
            EnsureInitialized();
            UpdateLoadingProgress(deltaTime);
            BuiltInUiPalette palette = BuiltInUiTheme.Capture();

            Canvas canvas = new Canvas();
            Rect canvasRect = canvas.Bounds;

            float panelWidth = canvasRect.width * 0.80f;
            if (panelWidth < 320.0f)
                panelWidth = 320.0f;
            if (panelWidth > 520.0f)
                panelWidth = 520.0f;

            Rect panelRect = new Rect((canvasRect.width - panelWidth) * 0.5f,
                                      canvasRect.height * 0.63f,
                                      panelWidth,
                                      90.0f);

            UI.DrawFilledRoundedRect(panelRect, BuiltInUiTheme.WithAlpha(palette.childBg, 200), 7.0f);
            UI.DrawRoundedRect(panelRect, BuiltInUiTheme.WithAlpha(palette.border, 220), 7.0f, 1.0f);

            _brandImage.rect = new Rect(panelRect.x + 12.0f,
                                        panelRect.y + 11.0f,
                                        68.0f,
                                        68.0f);
            _brandImage.Draw();

            Rect loadingTitleRect = new Rect(panelRect.x + 90.0f,
                                             panelRect.y + 14.0f,
                                             panelRect.width - 102.0f,
                                             22.0f);
            UI.DrawLabel(loadingTitleRect,
                         "Loading project...",
                         BuiltInUiTheme.WithAlpha(palette.text, 255),
                         15.0f,
                         true,
                         TextAlign.Left);

            Rect loadingHintRect = new Rect(panelRect.x + 90.0f,
                                            panelRect.y + 37.0f,
                                            panelRect.width - 102.0f,
                                            18.0f);
            UI.DrawLabel(loadingHintRect,
                         "Initializing assets and script domain",
                         BuiltInUiTheme.WithAlpha(palette.textDisabled, 255),
                         11.0f,
                         false,
                         TextAlign.Left);

            _loadingBar.rect = new Rect(panelRect.x + 90.0f,
                                        panelRect.y + 61.0f,
                                        panelRect.width - 102.0f,
                                        10.0f);
            _loadingBar.trackColor = BuiltInUiTheme.WithAlpha(palette.frameBg, 235);
            _loadingBar.fillColor = BuiltInUiTheme.WithAlpha(BuiltInUiTheme.TintTowardWhite(palette.dockingPreview, 0.05f), 255);
            _loadingBar.Value01 = _loadingProgress;
            _loadingBar.Draw();
        }

        private static void EnsureInitialized()
        {
            if (_initialized)
                return;

            _initialized = true;

            _brandImage.sourceImage = SplashImagePath;
            _brandImage.color = UI.Rgba(255, 255, 255, 255);
            _brandImage.imageType = ImageType.Sliced;
            _brandImage.preserveAspect = true;
            _brandImage.maskable = true;
            _brandImage.cornerRadius = 6.0f;

            _refreshButton.text = "Refresh";
            _refreshButton.sourceImage = string.Empty;
            _refreshButton.interactable = true;
            _refreshButton.transition = ButtonTransition.ColorTint;
            _refreshButton.normalColor = UI.Rgba(247, 250, 255, 255);
            _refreshButton.highlightedColor = UI.Rgba(255, 255, 255, 255);
            _refreshButton.pressedColor = UI.Rgba(232, 238, 247, 255);
            _refreshButton.disabledColor = UI.Rgba(198, 208, 223, 255);
            _refreshButton.borderColor = UI.Rgba(207, 216, 230, 255);
            _refreshButton.textColor = UI.Rgba(24, 30, 42, 255);
            _refreshButton.imageTintColor = UI.Rgba(255, 255, 255, 255);
            _refreshButton.fadeDuration = 0.10f;
            _refreshButton.hoverStrength = 0.85f;
            _refreshButton.pressedScale = 0.985f;
            _refreshButton.preserveImageAspect = true;
            _refreshButton.maskable = true;
            _refreshButton.cornerRadius = 6.0f;
            _refreshButton.hitTestOrder = 3000;
            _refreshButton.consumeInput = true;
            _refreshButton.OnClick += OnRefreshClicked;

            _loadingBar.fillColor = UI.Rgba(58, 213, 132, 255);
            _loadingBar.trackColor = UI.Rgba(11, 20, 18, 226);
            _loadingBar.smoothing = 9.0f;
            _loadingBar.Value01 = _loadingProgress;
        }

        private static void UpdateLoadingProgress(float deltaTime)
        {
            float dt = deltaTime;
            if (dt <= 0.0f)
                dt = 1.0f / 60.0f;

            float speed = 0.50f;
            if (_loadingForward)
                _loadingProgress += speed * dt;
            else
                _loadingProgress -= speed * dt;

            if (_loadingProgress >= 0.95f)
            {
                _loadingProgress = 0.95f;
                _loadingForward = false;
            }
            else if (_loadingProgress <= 0.18f)
            {
                _loadingProgress = 0.18f;
                _loadingForward = true;
            }
        }

        private static void OnRefreshClicked()
        {
            ProjectManager.RefreshProjectList();
            EditorContext.StatusMessage = "Project list refreshed.";
        }
    }
}
