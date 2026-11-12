using UnityEngine;

/// <summary>
/// Waiting / turn sound (loop): plays while the car turns left or right.
/// Drop this file anywhere inside Assets and press Play. It attaches itself to "main car".
/// </summary>
public class TurnSound : MonoBehaviour
{
    [Header("Sound")]
    [Tooltip("Drag the sound here, or leave empty and put the file in a 'Resources' folder")]
    public AudioClip clip;
    [Range(0f, 1f)] public float volume = 0.6f;

    AudioSource src;
    CarSignSystem car;

    // Adds itself to the car ("main car") when the scene starts
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoSetup()
    {
        if (FindObjectOfType<TurnSound>() != null) return;
        foreach (Transform t in FindObjectsOfType<Transform>())
        {
            string n = t.name.Replace(" ", "").Replace("_", "").ToLower();
            if (n == "maincar") { t.gameObject.AddComponent<TurnSound>(); return; }
        }
    }

    void Awake()
    {
        if (clip == null) clip = FindClip();

        src = gameObject.AddComponent<AudioSource>();
        src.clip = clip;
        src.loop = true;
        src.playOnAwake = false;
        src.spatialBlend = 0f;   // 2D sound
        src.volume = 0f;

        if (clip == null)
            Debug.LogWarning("TurnSound: sound not found. Drag it into 'Clip', or put the file (name contains 'carturn') inside a Resources folder.");
    }

    // Looks for the file in any "Resources" folder by its name
    AudioClip FindClip()
    {
        string[] keys = { "carturn", "turn", "wait", "blink" };
        foreach (AudioClip c in Resources.LoadAll<AudioClip>(""))
        {
            string n = c.name.ToLower();
            foreach (string k in keys)
                if (n.Contains(k)) return c;
        }
        return null;
    }

    bool HasCar()
    {
        if (car == null) car = CarSignSystem.Instance;
        return car != null;
    }

    [Header("Turn")]
    [Tooltip("The sound stays a little after you release the arrow (seconds)")]
    public float linger = 0.35f;
    float timer;


    void Update()
    {
        if (src == null || clip == null || !HasCar()) return;

        float dt = Time.unscaledDeltaTime;
        bool steering = !car.IsPaused && Mathf.Abs(car.SpeedMs) > 1f && car.SteerInput != 0;

        if (steering) timer = linger;
        else timer -= dt;

        bool on = !car.IsPaused && timer > 0f;
        src.volume = Mathf.MoveTowards(src.volume, on ? volume : 0f, 6f * dt);

        if (on && !src.isPlaying) src.Play();
        else if (!on && src.volume <= 0.001f && src.isPlaying) src.Stop();
    }
}
