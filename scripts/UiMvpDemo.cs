using System;
using Engine;

namespace GameScripts
{
    public sealed class UiMvpDemo : MonoBehaviour
    {
        private readonly Button _actionButton = new Button();
        private readonly ProgressBar _bottomProgress = new ProgressBar();

        private int _clickCount;
        private bool _fillForward = true;
        private float _progress = 0.05f;
        private const float ProgressSpeed = 0.18f;

        protected override void Start()
        {
            _actionButton.text = "Button";
            _actionButton.fontSize = 16.0f;
            _actionButton.normalColor = UI.Rgba(67, 86, 114, 255);
            _actionButton.highlightedColor = UI.Rgba(88, 109, 142, 255);
            _actionButton.pressedColor = UI.Rgba(54, 72, 98, 255);
            _actionButton.borderColor = UI.Rgba(121, 141, 174, 235);
            _actionButton.textColor = UI.Rgba(248, 251, 255, 255);

            _bottomProgress.trackColor = UI.Rgba(10, 20, 14, 220);
            _bottomProgress.fillColor = UI.Rgba(45, 219, 112, 255);
            _bottomProgress.smoothing = 8.0f;
            _bottomProgress.Value01 = _progress;
        }

        protected override void Update()
        {
            UpdateProgress();
            DrawUi();
        }

        private void DrawUi()
        {
            Canvas canvas = new Canvas();
            Rect canvasRect = canvas.Bounds;

            Rect panelRect = UI.CenterRect(canvasRect, 560.0f, 310.0f);
            UI.DrawFilledRoundedRect(panelRect, UI.Rgba(18, 25, 33, 232), 12.0f);
            UI.DrawRoundedRect(panelRect, UI.Rgba(74, 87, 103, 230), 12.0f, 1.0f);

            Rect titleRect = new Rect(panelRect.x + 20.0f, panelRect.y + 16.0f, panelRect.width - 40.0f, 30.0f);
            UI.DrawLabel(titleRect,
                         "Runtime UI MVP",
                         UI.Rgba(245, 247, 250, 255),
                         18.0f,
                         true,
                         TextAlign.Center);

            Rect logoRect = new Rect(panelRect.x + (panelRect.width - 180.0f) * 0.5f, panelRect.y + 56.0f, 180.0f, 100.0f);
            UI.DrawImage(logoRect,
                         "assets/editor/splash_logo.png",
                         true,
                         UI.Rgba(255, 255, 255, 255),
                         true,
                         true,
                         9.0f);

            Rect infoRect = new Rect(panelRect.x + 24.0f, panelRect.y + 168.0f, panelRect.width - 48.0f, 26.0f);
            UI.DrawLabel(infoRect,
                         "Clicks: " + _clickCount,
                         UI.Rgba(228, 233, 240, 255),
                         14.0f,
                         false,
                         TextAlign.Center);

            _actionButton.rect = new Rect(panelRect.x + (panelRect.width - 190.0f) * 0.5f,
                                          panelRect.y + 200.0f,
                                          190.0f,
                                          40.0f);
            if (_actionButton.Draw())
            {
                _clickCount += 1;
                _fillForward = !_fillForward;
            }

            Rect hintRect = new Rect(panelRect.x + 24.0f, panelRect.y + 248.0f, panelRect.width - 48.0f, 20.0f);
            UI.DrawLabel(hintRect,
                         "Canvas + Image + Button + ProgressBar",
                         UI.Rgba(180, 188, 200, 255),
                         13.0f,
                         false,
                         TextAlign.Center);

            _bottomProgress.rect = UI.BottomProgressRect(5.0f);
            _bottomProgress.Value01 = _progress;
            _bottomProgress.Draw();

            int percent = (int)Math.Round(_progress * 100.0f);
            Rect percentRect = new Rect(12.0f, canvasRect.height - 26.0f, 120.0f, 20.0f);
            UI.DrawLabel(percentRect,
                         percent + "%",
                         UI.Rgba(196, 252, 210, 255),
                         13.0f,
                         true,
                         TextAlign.Left);
        }

        private void UpdateProgress()
        {
            float delta = Time.deltaTime;
            if (_fillForward)
                _progress += ProgressSpeed * delta;
            else
                _progress -= ProgressSpeed * delta;

            if (_progress >= 1.0f)
            {
                _progress = 1.0f;
                _fillForward = false;
            }
            else if (_progress <= 0.0f)
            {
                _progress = 0.0f;
                _fillForward = true;
            }
        }
    }
}
