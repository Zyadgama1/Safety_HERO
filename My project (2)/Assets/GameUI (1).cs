using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Drop this file anywhere inside Assets and press Play. No setup needed.
/// - Finds the "start" and "Restart" buttons in the scene (by their name or label text).
/// - Start: begins the game (the car can't move before it is pressed) and hides the button.
/// - Restart: puts the car back at its starting point and shows the start button again.
/// - Restart button is pinned to the top-left corner of the screen (also when Game view is maximized).
/// </summary>
public class GameUI : MonoBehaviour
{
    public static bool WaitingForStart;   // read by CarSignSystem

    [Tooltip("Distance of the Restart button from the top-left corner (UI units)")]
    public float margin = 20f;

    Button startButton, restartButton;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoSetup()
    {
        WaitingForStart = false;
        if (FindObjectOfType<GameUI>() != null) return;
        new GameObject("GameUI").AddComponent<GameUI>();
    }

    void Awake()
    {
        // uGUI buttons need an EventSystem to receive clicks
        if (FindObjectOfType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        foreach (Button b in FindObjectsOfType<Button>(true))
        {
            string label = (b.name + " " + ReadText(b)).ToLower();
            if (label.Contains("restart"))
            {
                if (restartButton == null) restartButton = b;
            }
            else if (label.Contains("start") && startButton == null)
            {
                startButton = b;
            }
        }

        if (restartButton != null)
        {
            PinTopLeft(restartButton);
            restartButton.onClick.AddListener(OnRestart);
            DisableNavigation(restartButton);
        }
        else Debug.LogWarning("GameUI: no button named/labelled 'Restart' was found.");

        if (startButton != null)
        {
            DisableNavigation(startButton);
            startButton.onClick.AddListener(OnStart);
        }
        else Debug.LogWarning("GameUI: no button named/labelled 'start' was found.");

        if (startButton == null)
        {
            WaitingForStart = false;
            if (startButton != null) startButton.gameObject.SetActive(false);
        }
        else
        {
            WaitingForStart = true;
            startButton.gameObject.SetActive(true);
        }
    }

    void Update()
    {
        // Stop Space / Enter from "pressing" a selected button (Space is the brake!)
        EventSystem es = EventSystem.current;
        if (es == null) return;
        GameObject sel = es.currentSelectedGameObject;
        if (sel == null) return;
        if ((startButton != null && sel == startButton.gameObject) ||
            (restartButton != null && sel == restartButton.gameObject))
            es.SetSelectedGameObject(null);
    }

    void OnStart()
    {
        WaitingForStart = false;
        if (startButton != null) startButton.gameObject.SetActive(false);
        Deselect();
    }

    void OnRestart()
    {
        Deselect();
        Time.timeScale = 1f;

        // Put the car back exactly where it was when the game first started
        if (CarSignSystem.Instance != null) CarSignSystem.Instance.ResetToStart();

        // Back to the start screen, same as the very first launch
        if (startButton != null)
        {
            WaitingForStart = true;
            startButton.gameObject.SetActive(true);
        }
        else WaitingForStart = false;
    }

    static void Deselect()
    {
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    static void DisableNavigation(Button b)
    {
        Navigation nav = b.navigation;
        nav.mode = Navigation.Mode.None;
        b.navigation = nav;
    }

    void PinTopLeft(Button b)
    {
        RectTransform rt = b.GetComponent<RectTransform>();
        if (rt == null) return;

        // Make sure it sits directly under the root canvas so "top-left" means the screen's corner
        Canvas canvas = b.GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            Canvas root = canvas.rootCanvas;
            if (rt.parent != root.transform) rt.SetParent(root.transform, false);
        }

        Vector2 size = rt.rect.size;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = size;
        rt.anchoredPosition = new Vector2(margin, -margin);
        rt.SetAsLastSibling();
    }

    // Reads the label of a button (works for legacy Text and TextMeshPro without needing TMP installed)
    static string ReadText(Button b)
    {
        foreach (Component c in b.GetComponentsInChildren<Component>(true))
        {
            if (c == null) continue;
            System.Type t = c.GetType();
            if (!t.Name.Contains("Text")) continue;
            PropertyInfo p = t.GetProperty("text");
            if (p == null) continue;
            string s = p.GetValue(c, null) as string;
            if (!string.IsNullOrEmpty(s)) return s;
        }
        return "";
    }
}
