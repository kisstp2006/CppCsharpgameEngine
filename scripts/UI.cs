using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Engine
{
    public enum TextAlign
    {
        Left = 0,
        Center = 1,
        Right = 2
    }

    public enum ImageType
    {
        Simple = 0,
        Sliced = 1
    }

    public enum ButtonTransition
    {
        ColorTint = 0
    }

    public enum StackOrientation
    {
        Horizontal = 0,
        Vertical = 1
    }

    public enum AnchorPreset
    {
        TopLeft = 0,
        TopCenter = 1,
        TopRight = 2,
        MiddleLeft = 3,
        Center = 4,
        MiddleRight = 5,
        BottomLeft = 6,
        BottomCenter = 7,
        BottomRight = 8,
        StretchFull = 9,
        StretchTop = 10,
        StretchBottom = 11,
        StretchLeft = 12,
        StretchRight = 13
    }

    public struct Rect
    {
        public float x;
        public float y;
        public float width;
        public float height;

        public Rect(float x, float y, float width, float height)
        {
            this.x = x;
            this.y = y;
            this.width = width;
            this.height = height;
        }

        public float Right => x + width;
        public float Bottom => y + height;
        public float CenterX => x + width * 0.5f;
        public float CenterY => y + height * 0.5f;

        public bool Contains(float px, float py)
        {
            return px >= x && px <= Right && py >= y && py <= Bottom;
        }
    }

    public sealed class RectTransform2D
    {
        public RectTransform2D parent;

        public float anchorMinX = 0.5f;
        public float anchorMinY = 0.5f;
        public float anchorMaxX = 0.5f;
        public float anchorMaxY = 0.5f;

        public float pivotX = 0.5f;
        public float pivotY = 0.5f;

        public float anchoredX;
        public float anchoredY;
        public float sizeDeltaX = 100.0f;
        public float sizeDeltaY = 100.0f;

        public RectTransform2D()
        {
        }

        public static RectTransform2D Anchored(float anchoredX,
                                               float anchoredY,
                                               float width,
                                               float height,
                                               AnchorPreset preset = AnchorPreset.Center)
        {
            RectTransform2D transform = new RectTransform2D();
            transform.ApplyPreset(preset);
            transform.anchoredX = anchoredX;
            transform.anchoredY = anchoredY;
            transform.sizeDeltaX = width;
            transform.sizeDeltaY = height;
            return transform;
        }

        public void ApplyPreset(AnchorPreset preset)
        {
            switch (preset)
            {
                case AnchorPreset.TopLeft:
                    SetAnchors(0.0f, 0.0f, 0.0f, 0.0f);
                    SetPivot(0.0f, 0.0f);
                    break;
                case AnchorPreset.TopCenter:
                    SetAnchors(0.5f, 0.0f, 0.5f, 0.0f);
                    SetPivot(0.5f, 0.0f);
                    break;
                case AnchorPreset.TopRight:
                    SetAnchors(1.0f, 0.0f, 1.0f, 0.0f);
                    SetPivot(1.0f, 0.0f);
                    break;
                case AnchorPreset.MiddleLeft:
                    SetAnchors(0.0f, 0.5f, 0.0f, 0.5f);
                    SetPivot(0.0f, 0.5f);
                    break;
                case AnchorPreset.MiddleRight:
                    SetAnchors(1.0f, 0.5f, 1.0f, 0.5f);
                    SetPivot(1.0f, 0.5f);
                    break;
                case AnchorPreset.BottomLeft:
                    SetAnchors(0.0f, 1.0f, 0.0f, 1.0f);
                    SetPivot(0.0f, 1.0f);
                    break;
                case AnchorPreset.BottomCenter:
                    SetAnchors(0.5f, 1.0f, 0.5f, 1.0f);
                    SetPivot(0.5f, 1.0f);
                    break;
                case AnchorPreset.BottomRight:
                    SetAnchors(1.0f, 1.0f, 1.0f, 1.0f);
                    SetPivot(1.0f, 1.0f);
                    break;
                case AnchorPreset.StretchTop:
                    SetAnchors(0.0f, 0.0f, 1.0f, 0.0f);
                    SetPivot(0.5f, 0.0f);
                    break;
                case AnchorPreset.StretchBottom:
                    SetAnchors(0.0f, 1.0f, 1.0f, 1.0f);
                    SetPivot(0.5f, 1.0f);
                    break;
                case AnchorPreset.StretchLeft:
                    SetAnchors(0.0f, 0.0f, 0.0f, 1.0f);
                    SetPivot(0.0f, 0.5f);
                    break;
                case AnchorPreset.StretchRight:
                    SetAnchors(1.0f, 0.0f, 1.0f, 1.0f);
                    SetPivot(1.0f, 0.5f);
                    break;
                case AnchorPreset.StretchFull:
                    SetAnchors(0.0f, 0.0f, 1.0f, 1.0f);
                    SetPivot(0.5f, 0.5f);
                    break;
                case AnchorPreset.Center:
                default:
                    SetAnchors(0.5f, 0.5f, 0.5f, 0.5f);
                    SetPivot(0.5f, 0.5f);
                    break;
            }
        }

        public void SetAnchors(float minX, float minY, float maxX, float maxY)
        {
            anchorMinX = UI.Clamp01(minX);
            anchorMinY = UI.Clamp01(minY);
            anchorMaxX = UI.Clamp01(maxX);
            anchorMaxY = UI.Clamp01(maxY);
            if (anchorMaxX < anchorMinX)
                anchorMaxX = anchorMinX;
            if (anchorMaxY < anchorMinY)
                anchorMaxY = anchorMinY;
        }

        public void SetPivot(float x, float y)
        {
            pivotX = UI.Clamp01(x);
            pivotY = UI.Clamp01(y);
        }

        public void SetParent(RectTransform2D parentTransform)
        {
            if (ReferenceEquals(parentTransform, this))
                return;

            parent = parentTransform;
        }

        public Rect Resolve(Rect parent)
        {
            float parentAnchorX = parent.x + parent.width * anchorMinX;
            float parentAnchorY = parent.y + parent.height * anchorMinY;
            float parentAnchorWidth = parent.width * (anchorMaxX - anchorMinX);
            float parentAnchorHeight = parent.height * (anchorMaxY - anchorMinY);

            float resolvedWidth = parentAnchorWidth + sizeDeltaX;
            float resolvedHeight = parentAnchorHeight + sizeDeltaY;
            if (resolvedWidth < 0.0f)
                resolvedWidth = 0.0f;
            if (resolvedHeight < 0.0f)
                resolvedHeight = 0.0f;

            float resolvedX = parentAnchorX + anchoredX - resolvedWidth * pivotX;
            float resolvedY = parentAnchorY + anchoredY - resolvedHeight * pivotY;

            return new Rect(resolvedX, resolvedY, resolvedWidth, resolvedHeight);
        }

        public Rect ResolveHierarchy(Rect rootRect)
        {
            return ResolveHierarchyInternal(rootRect, 0);
        }

        public Rect ResolveToCanvas()
        {
            Canvas canvas = new Canvas();
            return ResolveHierarchy(canvas.Bounds);
        }

        private Rect ResolveHierarchyInternal(Rect rootRect, int depth)
        {
            if (depth > 64)
                return Resolve(rootRect);

            RectTransform2D parentTransform = parent;
            if (parentTransform == null || ReferenceEquals(parentTransform, this))
                return Resolve(rootRect);

            Rect parentRect = parentTransform.ResolveHierarchyInternal(rootRect, depth + 1);
            return Resolve(parentRect);
        }
    }

    public sealed class Canvas
    {
        public Rect Bounds => new Rect(0.0f, 0.0f, UI.GetDisplayWidth(), UI.GetDisplayHeight());
        public Rect PixelRect => Bounds;
    }

    public sealed class HorizontalStack
    {
        private readonly Rect _container;
        private readonly float _spacing;
        private readonly float _padding;
        private float _cursorX;

        public HorizontalStack(Rect container, float spacing = 8.0f, float startOffset = 0.0f, float padding = 0.0f)
        {
            _container = container;
            _spacing = spacing;
            _padding = padding;
            _cursorX = _padding + startOffset;
        }

        public Rect Next(float width, float height = -1.0f)
        {
            float maxHeight = _container.height - _padding * 2.0f;
            float resolvedHeight = height <= 0.0f ? maxHeight : height;
            if (resolvedHeight < 0.0f)
                resolvedHeight = 0.0f;
            Rect rect = new Rect(_container.x + _cursorX, _container.y, width, resolvedHeight);
            _cursorX += width + _spacing;
            return rect;
        }

        public Rect NextFill()
        {
            float remaining = _container.width - _padding - _cursorX;
            if (remaining < 0.0f)
                remaining = 0.0f;
            return Next(remaining, _container.height - _padding * 2.0f);
        }
    }

    public sealed class VerticalStack
    {
        private readonly Rect _container;
        private readonly float _spacing;
        private readonly float _padding;
        private float _cursorY;

        public VerticalStack(Rect container, float spacing = 8.0f, float startOffset = 0.0f, float padding = 0.0f)
        {
            _container = container;
            _spacing = spacing;
            _padding = padding;
            _cursorY = _padding + startOffset;
        }

        public Rect Next(float height, float width = -1.0f)
        {
            float maxWidth = _container.width - _padding * 2.0f;
            float resolvedWidth = width <= 0.0f ? maxWidth : width;
            if (resolvedWidth < 0.0f)
                resolvedWidth = 0.0f;
            Rect rect = new Rect(_container.x + _padding, _container.y + _cursorY, resolvedWidth, height);
            _cursorY += height + _spacing;
            return rect;
        }

        public Rect NextFill()
        {
            float remaining = _container.height - _padding - _cursorY;
            if (remaining < 0.0f)
                remaining = 0.0f;
            return Next(remaining, _container.width - _padding * 2.0f);
        }
    }

    public sealed class StackPanel
    {
        private struct ScrollState
        {
            public float offset;
            public float maxOffset;
        }

        private static readonly Dictionary<int, ScrollState> _scrollStates = new Dictionary<int, ScrollState>();

        private readonly Rect _container;
        private readonly float _spacing;
        private readonly float _padding;
        private readonly float _startOffset;
        private readonly int _scrollStateId;

        private float _cursorPrimary;
        private float _contentPrimaryEnd;
        private float _scrollOffset;
        private float _maxScrollOffset;

        public StackOrientation orientation;
        public bool scrollEnabled = true;
        public float scrollStep = 36.0f;

        public float ScrollOffset => _scrollOffset;
        public float MaxScrollOffset => _maxScrollOffset;

        public StackPanel(Rect container,
                          StackOrientation orientation = StackOrientation.Vertical,
                          float spacing = 8.0f,
                          float startOffset = 0.0f,
                          float padding = 0.0f,
                          int scrollStateId = 0)
        {
            _container = container;
            _spacing = spacing;
            _padding = padding;
            _startOffset = startOffset;
            this.orientation = orientation;

            _cursorPrimary = _padding + _startOffset;
            _contentPrimaryEnd = _cursorPrimary;

            _scrollStateId = scrollStateId != 0 ? scrollStateId : ComputeAutoScrollStateId(container, orientation, spacing, padding);
            LoadScrollState();
            ApplyWheelScrollInput();
            PersistScrollState();
        }

        public Rect Next(float primarySize, float secondarySize = -1.0f)
        {
            if (orientation == StackOrientation.Horizontal)
            {
                float maxHeight = _container.height - _padding * 2.0f;
                float resolvedHeight = secondarySize <= 0.0f ? maxHeight : secondarySize;
                if (resolvedHeight < 0.0f)
                    resolvedHeight = 0.0f;

                Rect rect = new Rect(_container.x + _cursorPrimary - _scrollOffset,
                                     _container.y,
                                     primarySize,
                                     resolvedHeight);
                float end = _cursorPrimary + primarySize;
                _cursorPrimary += primarySize + _spacing;
                UpdateScrollMetrics(end);
                return rect;
            }

            float maxWidth = _container.width - _padding * 2.0f;
            float resolvedWidth = secondarySize <= 0.0f ? maxWidth : secondarySize;
            if (resolvedWidth < 0.0f)
                resolvedWidth = 0.0f;

            Rect verticalRect = new Rect(_container.x + _padding,
                                         _container.y + _cursorPrimary - _scrollOffset,
                                         resolvedWidth,
                                         primarySize);
            float verticalEnd = _cursorPrimary + primarySize;
            _cursorPrimary += primarySize + _spacing;
            UpdateScrollMetrics(verticalEnd);
            return verticalRect;
        }

        public Rect NextFill()
        {
            if (orientation == StackOrientation.Horizontal)
            {
                float remaining = _container.width - _padding - _cursorPrimary;
                if (remaining < 0.0f)
                    remaining = 0.0f;

                float maxHeight = _container.height - _padding * 2.0f;
                return Next(remaining, maxHeight);
            }

            float verticalRemaining = _container.height - _padding - _cursorPrimary;
            if (verticalRemaining < 0.0f)
                verticalRemaining = 0.0f;

            float maxWidth = _container.width - _padding * 2.0f;
            return Next(verticalRemaining, maxWidth);
        }

        private void ApplyWheelScrollInput()
        {
            if (!scrollEnabled)
            {
                _scrollOffset = 0.0f;
                _maxScrollOffset = 0.0f;
                return;
            }

            float mouseX = Input.GetMousePosX();
            float mouseY = Input.GetMousePosY();
            if (!_container.Contains(mouseX, mouseY))
                return;

            float wheel = Input.GetMouseWheel();
            if (Math.Abs(wheel) <= 0.0001f)
                return;

            float step = scrollStep;
            if (step < 1.0f)
                step = 1.0f;

            _scrollOffset -= wheel * step;
            ClampScrollOffset();
        }

        private void UpdateScrollMetrics(float contentEnd)
        {
            if (contentEnd > _contentPrimaryEnd)
                _contentPrimaryEnd = contentEnd;

            float viewportPrimary = orientation == StackOrientation.Horizontal
                ? (_container.width - _padding * 2.0f)
                : (_container.height - _padding * 2.0f);
            if (viewportPrimary < 0.0f)
                viewportPrimary = 0.0f;

            float contentPrimary = _contentPrimaryEnd - (_padding + _startOffset);
            if (contentPrimary < 0.0f)
                contentPrimary = 0.0f;

            _maxScrollOffset = contentPrimary - viewportPrimary;
            if (_maxScrollOffset < 0.0f)
                _maxScrollOffset = 0.0f;

            ClampScrollOffset();
            PersistScrollState();
        }

        private void ClampScrollOffset()
        {
            if (_scrollOffset < 0.0f)
                _scrollOffset = 0.0f;
            if (_scrollOffset > _maxScrollOffset)
                _scrollOffset = _maxScrollOffset;
        }

        private void LoadScrollState()
        {
            ScrollState state;
            if (_scrollStates.TryGetValue(_scrollStateId, out state))
            {
                _scrollOffset = state.offset;
                _maxScrollOffset = state.maxOffset;
                ClampScrollOffset();
                return;
            }

            _scrollOffset = 0.0f;
            _maxScrollOffset = 0.0f;
        }

        private void PersistScrollState()
        {
            ScrollState state;
            state.offset = _scrollOffset;
            state.maxOffset = _maxScrollOffset;
            _scrollStates[_scrollStateId] = state;
        }

        private static int ComputeAutoScrollStateId(Rect container,
                                                    StackOrientation orientation,
                                                    float spacing,
                                                    float padding)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + Quantize(container.x);
                hash = hash * 31 + Quantize(container.y);
                hash = hash * 31 + Quantize(container.width);
                hash = hash * 31 + Quantize(container.height);
                hash = hash * 31 + (int)orientation;
                hash = hash * 31 + Quantize(spacing);
                hash = hash * 31 + Quantize(padding);
                if (hash == 0)
                    hash = 1;

                return hash;
            }
        }

        private static int Quantize(float value)
        {
            return (int)Math.Round(value * 10.0f);
        }

        public static void ClearScrollState(int scrollStateId)
        {
            if (scrollStateId == 0)
                return;

            _scrollStates.Remove(scrollStateId);
        }

        public static void ClearAllScrollStates()
        {
            _scrollStates.Clear();
        }
    }

    public sealed class Label
    {
        public Rect rect;
        public string text = string.Empty;
        public uint color = 0xFFFFFFFFu;
        public float fontSize = 14.0f;
        public bool bold = false;
        public TextAlign align = TextAlign.Left;

        public Label()
        {
        }

        public Label(Rect rect, string text)
        {
            this.rect = rect;
            this.text = text ?? string.Empty;
        }

        public void Draw()
        {
            UI.DrawLabel(rect, text, color, fontSize, bold, align);
        }
    }

    public sealed class Image
    {
        public Rect rect;
        public string texturePath = string.Empty;
        public uint color = 0xFFFFFFFFu;
        public ImageType imageType = ImageType.Sliced;
        public bool preserveAspect = true;
        public bool maskable = true;
        public float cornerRadius = 8.0f;

        public string sourceImage
        {
            get => texturePath;
            set => texturePath = value ?? string.Empty;
        }

        public Image()
        {
        }

        public Image(Rect rect, string texturePath)
        {
            this.rect = rect;
            this.texturePath = texturePath ?? string.Empty;
        }

        public void Draw()
        {
            if (imageType == ImageType.Sliced)
                UI.DrawFilledRoundedRect(rect, color, cornerRadius);

            UI.DrawImage(rect,
                         texturePath,
                         preserveAspect,
                         color,
                         maskable,
                         imageType == ImageType.Sliced,
                         cornerRadius);
        }
    }

    public sealed class Button
    {
        public Rect rect;
        public string text = "Gomb";
        public string sourceImage = string.Empty;
        public int hitTestOrder = 0;
        public bool consumeInput = true;
        public bool interactable = true;
        public ButtonTransition transition = ButtonTransition.ColorTint;

        public uint normalColor = 0xFFFFFFFFu;
        public uint highlightedColor = 0xF2F5FAFFu;
        public uint pressedColor = 0xE7EBF2FFu;
        public uint disabledColor = 0xD4DAE4FFu;
        public uint borderColor = 0xD1D8E3FFu;
        public uint textColor = 0xFFFFFFFFu;
        public uint imageTintColor = 0xFFFFFFFFu;
        public float cornerRadius = 8.0f;
        public float borderThickness = 1.0f;
        public float fontSize = 16.0f;
        public float fadeDuration = 0.10f;
        public float pressedScale = 0.985f;
        public float hoverStrength = 1.0f;
        public bool preserveImageAspect = true;
        public bool maskable = true;

        private float _hoverAmount;
        private float _pressAmount;

        public event Action OnClick;

        public Button()
        {
        }

        public Button(Rect rect, string text)
        {
            this.rect = rect;
            this.text = text ?? "Gomb";
        }

        public bool Draw()
        {
            if (_controlId == 0)
                _controlId = UI.AcquireControlId();

            UI.InteractionState interaction = UI.ResolveControlInteraction(_controlId,
                                                                           rect,
                                                                           hitTestOrder,
                                                                           consumeInput);
            bool hovered = interaction.hovered;
            bool pressed = interaction.pressed && interactable;
            bool clicked = interaction.clicked && interactable;

            float delta = Time.deltaTime;
            if (delta <= 0.0f)
                delta = 1.0f / 60.0f;

            float blend = fadeDuration <= 0.0f ? 1.0f : UI.Clamp01(delta / fadeDuration);
            float hoverTarget = (hovered && interactable) ? 1.0f : 0.0f;
            float pressTarget = (pressed && interactable) ? 1.0f : 0.0f;
            _hoverAmount += (hoverTarget - _hoverAmount) * blend;
            _pressAmount += (pressTarget - _pressAmount) * blend;

            uint hoverColorBlended = UI.LerpColor(normalColor, highlightedColor, _hoverAmount * UI.Clamp01(hoverStrength));
            uint transitionColor = UI.LerpColor(hoverColorBlended, pressedColor, _pressAmount);
            uint backgroundColor = interactable ? transitionColor : disabledColor;

            float scale = 1.0f - (1.0f - pressedScale) * _pressAmount;
            Rect animatedRect = UI.ScaleFromCenter(rect, scale, scale);

            UI.DrawFilledRoundedRect(animatedRect, backgroundColor, cornerRadius);

            if (!string.IsNullOrEmpty(sourceImage))
            {
                UI.DrawImage(animatedRect,
                             sourceImage,
                             preserveImageAspect,
                             interactable ? imageTintColor : UI.LerpColor(imageTintColor, disabledColor, 0.55f),
                             maskable,
                             true,
                             cornerRadius);
            }

            UI.DrawRoundedRect(animatedRect, borderColor, cornerRadius, borderThickness);
            UI.DrawLabel(animatedRect, text, textColor, fontSize, true, TextAlign.Center);

            if (clicked)
            {
                Action handler = OnClick;
                if (handler != null)
                    handler();
            }

            return clicked;
        }

        private int _controlId;
    }

    public sealed class ProgressBar
    {
        public Rect rect;
        public uint trackColor = 0x0D1511D9u;
        public uint fillColor = 0x2DDB70FFu;
        public float smoothing = 10.0f;

        private float _targetValue = 0.0f;
        private float _displayValue = 0.0f;

        public ProgressBar()
        {
        }

        public ProgressBar(Rect rect)
        {
            this.rect = rect;
        }

        public float Value01
        {
            get
            {
                return _targetValue;
            }
            set
            {
                _targetValue = UI.Clamp01(value);
            }
        }

        public void Draw()
        {
            float delta = Time.deltaTime;
            if (delta <= 0.0f)
                delta = 1.0f / 60.0f;

            float lerpFactor = smoothing <= 0.0f ? 1.0f : UI.Clamp01(smoothing * delta);
            _displayValue += (_targetValue - _displayValue) * lerpFactor;
            _displayValue = UI.Clamp01(_displayValue);

            UI.DrawFilledRoundedRect(rect, trackColor, 0.0f);

            Rect fill = new Rect(rect.x, rect.y, rect.width * _displayValue, rect.height);
            UI.DrawFilledRoundedRect(fill, fillColor, 0.0f);
        }
    }

    public sealed class TextInput
    {
        private static int _focusedControlId;
        private static float _caretBlinkTimer;

        public Rect rect;
        public string text = string.Empty;
        public string placeholder = string.Empty;
        public int maxLength = 256;
        public bool readOnly;
        public bool consumeInput = true;
        public int hitTestOrder = 0;

        public uint normalColor = 0x1B2230FFu;
        public uint focusedColor = 0x273248FFu;
        public uint hoveredColor = 0x202B3DFFu;
        public uint borderColor = 0x61728BFFu;
        public uint textColor = 0xEAF1FCFFu;
        public uint placeholderColor = 0x9FAABFFFu;
        public uint caretColor = 0xF0F5FFFFu;

        public float cornerRadius = 6.0f;
        public float borderThickness = 1.0f;
        public float fontSize = 13.0f;

        public bool IsFocused => _controlId != 0 && _focusedControlId == _controlId;

        public bool Draw()
        {
            if (_controlId == 0)
                _controlId = UI.AcquireControlId();

            UI.InteractionState interaction = UI.ResolveControlInteraction(_controlId,
                                                                           rect,
                                                                           hitTestOrder,
                                                                           consumeInput);

            if (interaction.clicked)
            {
                _focusedControlId = _controlId;
                _caretIndex = (text ?? string.Empty).Length;
                _caretBlinkTimer = 0.0f;
            }
            else if (Input.GetMouseButtonDown(MouseButton.Left) && _focusedControlId == _controlId && !rect.Contains(Input.GetMousePosX(), Input.GetMousePosY()))
            {
                _focusedControlId = 0;
            }

            bool changed = false;
            bool focused = IsFocused;

            if (focused && !readOnly)
            {
                changed |= ApplyTextInputCharacters();
                changed |= ApplyTextEditingKeys();
            }

            if (focused && Input.GetKeyDown(KeyCode.Escape))
                _focusedControlId = 0;

            uint background = normalColor;
            if (focused)
                background = focusedColor;
            else if (interaction.hovered)
                background = hoveredColor;

            UI.DrawFilledRoundedRect(rect, background, cornerRadius);
            UI.DrawRoundedRect(rect, borderColor, cornerRadius, borderThickness);

            string displayText = text ?? string.Empty;
            bool hasText = displayText.Length > 0;
            string drawText = hasText ? displayText : (placeholder ?? string.Empty);
            uint drawColor = hasText ? textColor : placeholderColor;

            Rect textRect = UI.Inset(rect, 8.0f);
            UI.DrawLabel(textRect, drawText, drawColor, fontSize, false, TextAlign.Left);

            if (focused)
            {
                float dt = Time.deltaTime;
                if (dt <= 0.0f)
                    dt = 1.0f / 60.0f;

                _caretBlinkTimer += dt;
                bool caretVisible = (_caretBlinkTimer % 1.0f) < 0.5f;
                if (caretVisible)
                    DrawCaret(textRect, displayText);
            }

            return changed;
        }

        public static void ClearFocus()
        {
            _focusedControlId = 0;
        }

        private bool ApplyTextInputCharacters()
        {
            string incoming = Input.GetTextInput() ?? string.Empty;
            if (incoming.Length == 0)
                return false;

            bool changed = false;
            for (int i = 0; i < incoming.Length; ++i)
            {
                char c = incoming[i];
                if (c == '\r' || c == '\n')
                    continue;

                string current = text ?? string.Empty;
                if (maxLength > 0 && current.Length >= maxLength)
                    break;

                if (_caretIndex < 0)
                    _caretIndex = 0;
                if (_caretIndex > current.Length)
                    _caretIndex = current.Length;

                text = current.Insert(_caretIndex, c.ToString());
                _caretIndex += 1;
                changed = true;
            }

            return changed;
        }

        private bool ApplyTextEditingKeys()
        {
            string current = text ?? string.Empty;
            bool changed = false;

            if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                if (_caretIndex > 0)
                    _caretIndex -= 1;
            }

            if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                if (_caretIndex < current.Length)
                    _caretIndex += 1;
            }

            if (Input.GetKeyDown(KeyCode.Home))
                _caretIndex = 0;

            if (Input.GetKeyDown(KeyCode.End))
                _caretIndex = current.Length;

            if (Input.GetKeyDown(KeyCode.Backspace) && _caretIndex > 0 && current.Length > 0)
            {
                text = current.Remove(_caretIndex - 1, 1);
                _caretIndex -= 1;
                changed = true;
                current = text ?? string.Empty;
            }

            if (Input.GetKeyDown(KeyCode.Delete) && _caretIndex >= 0 && _caretIndex < current.Length)
            {
                text = current.Remove(_caretIndex, 1);
                changed = true;
            }

            return changed;
        }

        private void DrawCaret(Rect textRect, string value)
        {
            int caret = _caretIndex;
            if (caret < 0)
                caret = 0;
            if (caret > value.Length)
                caret = value.Length;

            string beforeCaret = caret <= 0 ? string.Empty : value.Substring(0, caret);
            float textWidth;
            float textHeight;
            if (!UI.MeasureText(beforeCaret, fontSize, false, out textWidth, out textHeight))
            {
                textWidth = beforeCaret.Length * Math.Max(6.0f, fontSize * 0.5f);
                textHeight = Math.Max(fontSize, 8.0f);
            }

            float x = textRect.x + textWidth;
            float y = textRect.y + (textRect.height - textHeight) * 0.5f;
            Rect caretRect = new Rect(x, y, 1.5f, textHeight);
            UI.DrawFilledRoundedRect(caretRect, caretColor, 0.0f);
        }

        private int _controlId;
        private int _caretIndex;
    }

    public static class UI
    {
        internal struct InteractionState
        {
            public bool hovered;
            public bool pressed;
            public bool clicked;
        }

        private static int _nextControlId = 1;
        private static int _drawSerial;
        private static bool _collectingPressCandidates;
        private static int _pressBestOrder = int.MinValue;
        private static int _pressBestSerial = int.MinValue;
        private static int _activePointerControlId;
        private static bool _releaseSeen;

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern float GetDisplayWidthInternal();

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern float GetDisplayHeightInternal();

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void DrawFilledRoundedRectInternal(float x,
                                                                 float y,
                                                                 float width,
                                                                 float height,
                                                                 float radius,
                                                                 uint rgbaColor);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void DrawRoundedRectInternal(float x,
                                                           float y,
                                                           float width,
                                                           float height,
                                                           float radius,
                                                           uint rgbaColor,
                                                           float thickness);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void DrawTextInternal(string text,
                                                    float x,
                                                    float y,
                                                    float fontSize,
                                                    uint rgbaColor,
                                                    bool bold);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool MeasureTextInternal(string text,
                                                       float fontSize,
                                                       bool bold,
                                                       out float width,
                                                       out float height);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void DrawImageInternal(string path,
                                                     float x,
                                                     float y,
                                                     float width,
                                                     float height,
                                                     bool keepAspect,
                                                     uint rgbaTint,
                                                     bool maskable,
                                                     bool sliced,
                                                     float cornerRadius);

        public static float GetDisplayWidth()
        {
            float width = GetDisplayWidthInternal();
            return width > 0.0f ? width : 1280.0f;
        }

        public static float GetDisplayHeight()
        {
            float height = GetDisplayHeightInternal();
            return height > 0.0f ? height : 720.0f;
        }

        public static float Clamp01(float value)
        {
            if (value < 0.0f)
                return 0.0f;
            if (value > 1.0f)
                return 1.0f;
            return value;
        }

        internal static int AcquireControlId()
        {
            if (_nextControlId <= 0 || _nextControlId == int.MaxValue)
                _nextControlId = 1;

            return _nextControlId++;
        }

        internal static InteractionState ResolveControlInteraction(int controlId,
                                                                   Rect rect,
                                                                   int hitTestOrder,
                                                                   bool consumeInput)
        {
            float mouseX = Input.GetMousePosX();
            float mouseY = Input.GetMousePosY();
            bool hovered = rect.Contains(mouseX, mouseY);

            bool mouseDown = Input.GetMouseButton(MouseButton.Left);
            bool mousePressed = Input.GetMouseButtonDown(MouseButton.Left);
            bool mouseReleased = Input.GetMouseButtonUp(MouseButton.Left);

            if (!consumeInput)
            {
                InteractionState passthroughState;
                passthroughState.hovered = hovered;
                passthroughState.pressed = hovered && mouseDown;
                passthroughState.clicked = hovered && mouseReleased;
                return passthroughState;
            }

            int serial = ++_drawSerial;
            if (_drawSerial == int.MaxValue)
                _drawSerial = 1;

            if (mousePressed && !_collectingPressCandidates)
            {
                _collectingPressCandidates = true;
                _pressBestOrder = int.MinValue;
                _pressBestSerial = int.MinValue;
                _activePointerControlId = 0;
            }
            else if (!mousePressed)
            {
                _collectingPressCandidates = false;
            }

            if (mousePressed && hovered)
            {
                bool outranksCurrent = hitTestOrder > _pressBestOrder
                                       || (hitTestOrder == _pressBestOrder && serial >= _pressBestSerial);
                if (outranksCurrent)
                {
                    _pressBestOrder = hitTestOrder;
                    _pressBestSerial = serial;
                    _activePointerControlId = controlId;
                }
            }

            bool isActive = _activePointerControlId == controlId;

            InteractionState state;
            if (mouseDown)
                state.hovered = isActive && hovered;
            else
                state.hovered = hovered;

            state.pressed = mouseDown && isActive;
            state.clicked = mouseReleased && isActive && hovered;

            if (mouseReleased)
            {
                _releaseSeen = true;
                if (isActive)
                    _activePointerControlId = 0;
            }
            else if (_releaseSeen)
            {
                // Release edge is one frame long; clear stale capture on the next frame.
                _releaseSeen = false;
                _activePointerControlId = 0;
            }

            return state;
        }

        public static void ResetInputCapture()
        {
            _collectingPressCandidates = false;
            _pressBestOrder = int.MinValue;
            _pressBestSerial = int.MinValue;
            _activePointerControlId = 0;
            _releaseSeen = false;
        }

        public static Rect ResolveRect(Rect parent, RectTransform2D transform)
        {
            if (transform == null)
                return parent;

            return transform.Resolve(parent);
        }

        public static Rect ResolveHierarchy(Rect rootRect, RectTransform2D transform)
        {
            if (transform == null)
                return rootRect;

            return transform.ResolveHierarchy(rootRect);
        }

        public static Rect ResolveToCanvas(RectTransform2D transform)
        {
            Canvas canvas = new Canvas();
            if (transform == null)
                return canvas.Bounds;

            return transform.ResolveHierarchy(canvas.Bounds);
        }

        public static Rect Inset(Rect rect, float padding)
        {
            return new Rect(rect.x + padding,
                            rect.y + padding,
                            Math.Max(0.0f, rect.width - padding * 2.0f),
                            Math.Max(0.0f, rect.height - padding * 2.0f));
        }

        public static Rect CenterRect(Rect container, float width, float height)
        {
            return new Rect(container.x + (container.width - width) * 0.5f,
                            container.y + (container.height - height) * 0.5f,
                            width,
                            height);
        }

        public static Rect ScaleFromCenter(Rect rect, float scaleX, float scaleY)
        {
            float safeScaleX = scaleX < 0.0f ? 0.0f : scaleX;
            float safeScaleY = scaleY < 0.0f ? 0.0f : scaleY;
            float nextWidth = rect.width * safeScaleX;
            float nextHeight = rect.height * safeScaleY;
            float nextX = rect.CenterX - nextWidth * 0.5f;
            float nextY = rect.CenterY - nextHeight * 0.5f;
            return new Rect(nextX, nextY, nextWidth, nextHeight);
        }

        public static uint LerpColor(uint fromRgba, uint toRgba, float t)
        {
            float blend = Clamp01(t);

            int fromR = (int)((fromRgba >> 24) & 0xFFu);
            int fromG = (int)((fromRgba >> 16) & 0xFFu);
            int fromB = (int)((fromRgba >> 8) & 0xFFu);
            int fromA = (int)(fromRgba & 0xFFu);

            int toR = (int)((toRgba >> 24) & 0xFFu);
            int toG = (int)((toRgba >> 16) & 0xFFu);
            int toB = (int)((toRgba >> 8) & 0xFFu);
            int toA = (int)(toRgba & 0xFFu);

            int outR = fromR + (int)Math.Round((toR - fromR) * blend);
            int outG = fromG + (int)Math.Round((toG - fromG) * blend);
            int outB = fromB + (int)Math.Round((toB - fromB) * blend);
            int outA = fromA + (int)Math.Round((toA - fromA) * blend);

            return (uint)((outR << 24) | (outG << 16) | (outB << 8) | outA);
        }

        public static uint Rgba(byte r, byte g, byte b, byte a = 255)
        {
            return (uint)((r << 24) | (g << 16) | (b << 8) | a);
        }

        public static Rect BottomProgressRect(float height = 4.0f)
        {
            float safeHeight = height <= 0.0f ? 4.0f : height;
            return new Rect(0.0f, GetDisplayHeight() - safeHeight, GetDisplayWidth(), safeHeight);
        }

        public static void DrawFilledRoundedRect(Rect rect, uint color, float radius = 0.0f)
        {
            DrawFilledRoundedRectInternal(rect.x, rect.y, rect.width, rect.height, radius, color);
        }

        public static void DrawRoundedRect(Rect rect, uint color, float radius = 0.0f, float thickness = 1.0f)
        {
            DrawRoundedRectInternal(rect.x, rect.y, rect.width, rect.height, radius, color, thickness);
        }

        public static void DrawImage(Rect rect,
                                     string texturePath,
                                     bool keepAspect = true,
                                     uint color = 0xFFFFFFFFu,
                                     bool maskable = true,
                                     bool sliced = false,
                                     float cornerRadius = 0.0f)
        {
            DrawImageInternal(texturePath ?? string.Empty,
                              rect.x,
                              rect.y,
                              rect.width,
                              rect.height,
                              keepAspect,
                              color,
                              maskable,
                              sliced,
                              cornerRadius);
        }

        public static void DrawLabel(Rect rect,
                                     string text,
                                     uint color = 0xFFFFFFFFu,
                                     float fontSize = 14.0f,
                                     bool bold = false,
                                     TextAlign align = TextAlign.Left)
        {
            string content = text ?? string.Empty;
            if (content.Length == 0)
                return;

            float textWidth;
            float textHeight;
            if (!MeasureTextInternal(content, fontSize, bold, out textWidth, out textHeight))
            {
                textWidth = content.Length * Math.Max(6.0f, fontSize * 0.5f);
                textHeight = Math.Max(8.0f, fontSize);
            }

            float drawX = rect.x;
            if (align == TextAlign.Center)
                drawX = rect.x + (rect.width - textWidth) * 0.5f;
            else if (align == TextAlign.Right)
                drawX = rect.Right - textWidth;

            float drawY = rect.y + (rect.height - textHeight) * 0.5f;
            DrawTextInternal(content, drawX, drawY, fontSize, color, bold);
        }

        public static bool MeasureText(string text,
                                       float fontSize,
                                       bool bold,
                                       out float width,
                                       out float height)
        {
            string value = text ?? string.Empty;
            return MeasureTextInternal(value, fontSize, bold, out width, out height);
        }

        public static Rect[] HorizontalStack(Rect container, int elementCount, float spacing = 8.0f)
        {
            if (elementCount <= 0)
                return new Rect[0];

            float totalSpacing = spacing * (elementCount - 1);
            float width = (container.width - totalSpacing) / elementCount;
            if (width < 0.0f)
                width = 0.0f;

            Rect[] rects = new Rect[elementCount];
            float x = container.x;
            for (int i = 0; i < elementCount; i++)
            {
                rects[i] = new Rect(x, container.y, width, container.height);
                x += width + spacing;
            }

            return rects;
        }

        public static Rect[] VerticalStack(Rect container, int elementCount, float spacing = 8.0f)
        {
            if (elementCount <= 0)
                return new Rect[0];

            float totalSpacing = spacing * (elementCount - 1);
            float height = (container.height - totalSpacing) / elementCount;
            if (height < 0.0f)
                height = 0.0f;

            Rect[] rects = new Rect[elementCount];
            float y = container.y;
            for (int i = 0; i < elementCount; i++)
            {
                rects[i] = new Rect(container.x, y, container.width, height);
                y += height + spacing;
            }

            return rects;
        }
    }
}
