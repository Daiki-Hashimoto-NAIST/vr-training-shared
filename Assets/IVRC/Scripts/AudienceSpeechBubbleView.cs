using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace IVRC
{
    /// <summary>
    /// Owns the reusable World Space audience speech-bubble prefab layout.
    /// The layout group and fitter resize the image around the TMP label;
    /// this component supplies the label's bounded preferred size so longer
    /// calls wrap rather than growing across the participant's view.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudienceSpeechBubbleView : MonoBehaviour
    {

        [Header("Prefab References")]
        [SerializeField] private Canvas _canvas;
        [SerializeField] private Image _wordBubble;
        [SerializeField] private HorizontalLayoutGroup _layoutGroup;
        [SerializeField] private ContentSizeFitter _contentSizeFitter;
        [SerializeField] private TextMeshProUGUI _label;
        [SerializeField] private LayoutElement _labelLayout;

        [Header("Text Bounds")]
        [Tooltip(
            "Maximum label width in Canvas units. Longer calls wrap and let " +
            "the bubble grow vertically.")]
        [SerializeField, Min(1f)] private float _maximumTextWidth = 430f;
        [SerializeField, Min(1f)] private float _minimumTextWidth = 92f;

        public Canvas Canvas => _canvas;

        public void Configure(
            string text,
            TMP_FontAsset font,
            float fontSize,
            Color textColor,
            Sprite sprite,
            Color bubbleColor,
            Camera viewerCamera)
        {
            ResolveReferences();
            if (_label == null || _labelLayout == null)
            {
                return;
            }

            _label.font = font == null ? TMP_Settings.defaultFontAsset : font;
            _label.fontSize = Mathf.Max(1f, fontSize);
            _label.color = textColor;
            _label.text = text ?? string.Empty;
            _label.enableWordWrapping = true;
            _label.textWrappingMode = TextWrappingModes.Normal;

            if (_wordBubble != null)
            {
                _wordBubble.sprite = sprite;
                _wordBubble.color = bubbleColor;
                _wordBubble.type = sprite != null &&
                                   sprite.border.sqrMagnitude > 0.0001f
                    ? Image.Type.Sliced
                    : Image.Type.Simple;
            }

            if (_canvas != null)
            {
                _canvas.renderMode = RenderMode.WorldSpace;
                _canvas.worldCamera = viewerCamera;
            }

            ApplyLabelSize();
        }

        private void OnValidate()
        {
            _maximumTextWidth = Mathf.Max(1f, _maximumTextWidth);
            _minimumTextWidth = Mathf.Clamp(
                _minimumTextWidth,
                1f,
                _maximumTextWidth);
            ResolveReferences();
        }

        private void ApplyLabelSize()
        {
            var unconstrainedWidth = _label.GetPreferredValues(
                _label.text,
                Mathf.Infinity,
                Mathf.Infinity).x;
            var labelWidth = Mathf.Clamp(
                unconstrainedWidth,
                _minimumTextWidth,
                _maximumTextWidth);
            var labelHeight = _label.GetPreferredValues(
                _label.text,
                labelWidth,
                Mathf.Infinity).y;

            _labelLayout.minWidth = 0f;
            _labelLayout.flexibleWidth = 0f;
            _labelLayout.preferredWidth = labelWidth;
            _labelLayout.minHeight = 0f;
            _labelLayout.flexibleHeight = 0f;
            _labelLayout.preferredHeight = labelHeight;

            if (_layoutGroup != null)
            {
                _layoutGroup.childControlWidth = true;
                _layoutGroup.childControlHeight = true;
                _layoutGroup.childForceExpandWidth = false;
                _layoutGroup.childForceExpandHeight = false;
            }

            if (_contentSizeFitter != null)
            {
                _contentSizeFitter.horizontalFit =
                    ContentSizeFitter.FitMode.PreferredSize;
                _contentSizeFitter.verticalFit =
                    ContentSizeFitter.FitMode.PreferredSize;
            }

            Canvas.ForceUpdateCanvases();
            if (_wordBubble != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(
                    _wordBubble.rectTransform);
            }
        }

        private void ResolveReferences()
        {
            if (_canvas == null)
            {
                _canvas = GetComponent<Canvas>();
            }

            if (_wordBubble == null)
            {
                _wordBubble = GetComponentInChildren<Image>(true);
            }

            if (_layoutGroup == null && _wordBubble != null)
            {
                _layoutGroup = _wordBubble.GetComponent<
                    HorizontalLayoutGroup>();
            }

            if (_contentSizeFitter == null && _wordBubble != null)
            {
                _contentSizeFitter = _wordBubble.GetComponent<
                    ContentSizeFitter>();
            }

            if (_label == null)
            {
                _label = GetComponentInChildren<TextMeshProUGUI>(true);
            }

            if (_labelLayout == null && _label != null)
            {
                _labelLayout = _label.GetComponent<LayoutElement>();
            }
        }
    }
}
