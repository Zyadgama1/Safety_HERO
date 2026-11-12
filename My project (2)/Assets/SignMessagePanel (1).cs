using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The message panel shown on every sign: dark panel, sign picture on the left,
/// the sentence in the middle and a red Close button. Built automatically at runtime.
/// Used by CarSignSystem - no setup needed.
/// </summary>
public class SignMessagePanel : MonoBehaviour
{
    static SignMessagePanel instance;

    GameObject panel;
    Text label;
    RawImage icon;
    Button closeButton;
    Action onClose;

    public static SignMessagePanel Get()
    {
        if (instance == null)
        {
            var go = new GameObject("SignMessagePanel");
            instance = go.AddComponent<SignMessagePanel>();
            instance.Build();
        }
        return instance;
    }

    public void Show(string text, Texture iconTexture, Action closeCallback)
    {
        onClose = closeCallback;
        label.text = text;
        icon.texture = iconTexture;
        icon.gameObject.SetActive(iconTexture != null);
        panel.SetActive(true);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    public void Hide()
    {
        if (panel != null) panel.SetActive(false);
    }

    void OnCloseClicked()
    {
        Hide();
        if (onClose != null) onClose();
    }

    void Update()
    {
        // Space (brake) / Enter must not "press" the Close button by accident
        EventSystem es = EventSystem.current;
        if (es != null && closeButton != null && es.currentSelectedGameObject == closeButton.gameObject)
            es.SetSelectedGameObject(null);
    }

    // ------------------------------------------------------------------
    void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<GraphicRaycaster>();

        if (FindObjectOfType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", 32);

        // ---- panel ----
        panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(transform, false);
        var prt = (RectTransform)panel.transform;
        prt.anchorMin = new Vector2(0.08f, 0f);
        prt.anchorMax = new Vector2(0.92f, 0f);
        prt.pivot = new Vector2(0.5f, 0f);
        prt.anchoredPosition = new Vector2(0f, 40f);
        prt.sizeDelta = new Vector2(0f, 230f);
        panel.GetComponent<Image>().color = new Color(0.03f, 0.03f, 0.05f, 0.92f);

        // ---- sign picture (left) ----
        var iconGO = new GameObject("SignPicture", typeof(RectTransform), typeof(RawImage));
        iconGO.transform.SetParent(panel.transform, false);
        var irt = (RectTransform)iconGO.transform;
        irt.anchorMin = new Vector2(0f, 0.5f);
        irt.anchorMax = new Vector2(0f, 0.5f);
        irt.pivot = new Vector2(0f, 0.5f);
        irt.anchoredPosition = new Vector2(25f, 0f);
        irt.sizeDelta = new Vector2(180f, 180f);
        icon = iconGO.GetComponent<RawImage>();
        icon.raycastTarget = false;

        // ---- sentence (middle) ----
        var tGO = new GameObject("Sentence", typeof(RectTransform), typeof(Text));
        tGO.transform.SetParent(panel.transform, false);
        var trt = (RectTransform)tGO.transform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(230f, 78f);
        trt.offsetMax = new Vector2(-30f, -15f);
        label = tGO.GetComponent<Text>();
        label.font = font;
        label.fontSize = 38;
        label.fontStyle = FontStyle.Bold;
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 14;
        label.resizeTextMaxSize = 38;
        label.alignment = TextAnchor.MiddleLeft;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        label.color = Color.white;
        label.raycastTarget = false;

        // ---- Close button (bottom right) ----
        var bGO = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
        bGO.transform.SetParent(panel.transform, false);
        var brt = (RectTransform)bGO.transform;
        brt.anchorMin = new Vector2(1f, 0f);
        brt.anchorMax = new Vector2(1f, 0f);
        brt.pivot = new Vector2(1f, 0f);
        brt.anchoredPosition = new Vector2(-30f, 18f);
        brt.sizeDelta = new Vector2(170f, 56f);
        var bImg = bGO.GetComponent<Image>();
        bImg.color = new Color(0.85f, 0.1f, 0.1f, 1f);
        closeButton = bGO.GetComponent<Button>();
        closeButton.targetGraphic = bImg;
        var nav = closeButton.navigation;
        nav.mode = Navigation.Mode.None;
        closeButton.navigation = nav;
        closeButton.onClick.AddListener(OnCloseClicked);

        var cGO = new GameObject("Text", typeof(RectTransform), typeof(Text));
        cGO.transform.SetParent(bGO.transform, false);
        var crt = (RectTransform)cGO.transform;
        crt.anchorMin = Vector2.zero;
        crt.anchorMax = Vector2.one;
        crt.offsetMin = Vector2.zero;
        crt.offsetMax = Vector2.zero;
        var cText = cGO.GetComponent<Text>();
        cText.font = font;
        cText.fontSize = 32;
        cText.fontStyle = FontStyle.Bold;
        cText.alignment = TextAnchor.MiddleCenter;
        cText.color = Color.white;
        cText.text = "Close";
        cText.raycastTarget = false;

        panel.SetActive(false);
    }
}
