using System.Reflection;
using UnityEngine;

/// <summary>
/// Drop this file anywhere inside Assets and press Play. No setup needed.
/// - Finds the pedestrian character by itself (the object with an Animator named like "FreeSample_male...").
/// - Finds the blue crosswalk sign ahead of the car and places the crossing there.
/// - When the car gets close, the pedestrian walks across the road.
/// - If the car hits him: he goes back, the car returns to a point before the crossing,
///   and the message is shown again.
/// Works with CarSignSystem + GameUI (reads them, changes nothing in them).
/// </summary>
public class PedestrianCrossing : MonoBehaviour
{
    enum State { Waiting, Walking, Finished }

    [Header("Optional manual points (leave empty = automatic at the crosswalk sign)")]
    public Transform startPoint;
    public Transform endPoint;
    public Transform triggerPoint;

    [Header("Automatic placement")]
    [Tooltip("Empty = he starts exactly where you placed him in the scene and crosses the road from there. Or drag a sign here to start next to it instead.")]
    public Transform crosswalkSign;
    [Tooltip("How far he walks across the road (meters)")]
    public float roadWidth = 9f;
    [Tooltip("He starts this far behind the sign (away from the road)")]
    public float startBack = 0.5f;

    [Header("Walking")]
    public float walkSpeed = 1.5f;
    [Tooltip("He starts walking when the car is this close to the crosswalk")]
    public float triggerRadius = 10f;
    [Tooltip("Car position is saved when it gets this close (retry point after a hit)")]
    public float saveRadius = 35f;
    public float modelYawOffset = 0f;

    [Header("Hit detection")]
    public float bodyRadius = 0.4f;
    public float bodyHeight = 1.8f;
    [Range(0.5f, 1f)] public float carHitScale = 0.9f;

    [Header("Animation (state names in the Animator)")]
    public string idleState = "Idle";
    public string walkState = "Walk";
    public float crossFade = 0.15f;

    [Header("Hit message")]
    public string hitTitle = "PEDESTRIAN";
    [TextArea]
    public string hitMessage =
        "WRONG MOVE: You hit the pedestrian.\n" +
        "Brake and wait until he has crossed, then drive on.\n" +
        "You were sent back before the crossing.";

    // ------------------------------------------------------------------
    State state = State.Waiting;
    Animator anim;
    CarSignSystem car;
    bool initDone, wasWaitingForStart, hasSave, hitHandled;
    Vector3 sPos, ePos, tPos, savedPos;
    float savedYaw;

    // car access by reflection, so this works with any version of CarSignSystem
    static FieldInfo yawField;
    static MethodInfo teleportMethod, messageMethod;

    // Auto-attach to the character when the scene starts.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoSetup()
    {
        if (FindObjectOfType<PedestrianCrossing>() != null) return;
        foreach (Animator a in FindObjectsOfType<Animator>())
        {
            if (a.GetComponentInParent<CarSignSystem>() != null) continue;
            string n = a.gameObject.name.ToLower();
            if (n.Contains("freesample") || n.Contains("male") || n.Contains("pedestrian") || n.Contains("people") || n.Contains("character"))
            {
                a.gameObject.AddComponent<PedestrianCrossing>();
                return;
            }
        }
        Debug.LogWarning("PedestrianCrossing: no character found. Add this script to your character manually.");
    }

    Vector3 scenePos;   // where you put the character in the scene

    void Awake()
    {
        scenePos = transform.position;
        anim = GetComponentInChildren<Animator>();
        if (anim != null) anim.applyRootMotion = false;   // we move him by code

        // The car must pass through him; the hit is judged by this script
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.isTrigger = true;
        foreach (CharacterController cc in GetComponentsInChildren<CharacterController>()) cc.enabled = false;
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }

        if (yawField == null)
        {
            const BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            System.Type t = typeof(CarSignSystem);
            yawField = t.GetField("yaw", f);
            teleportMethod = t.GetMethod("TeleportTo", f);
            messageMethod = t.GetMethod("ShowMessage", f);
        }
    }

    void Start()
    {
        wasWaitingForStart = GameUI.WaitingForStart;
    }

    // ------------------------------------------------------------------
    void Update()
    {
        bool waiting = GameUI.WaitingForStart;

        // Restart button was pressed
        if (waiting && !wasWaitingForStart && initDone)
        {
            hasSave = false;
            hitHandled = false;
            ResetPedestrian();
        }
        wasWaitingForStart = waiting;

        if (car == null) car = FindObjectOfType<CarSignSystem>();
        if (car == null) return;
        if (!initDone && !Init()) return;   // placed right away, before Start is pressed
        if (waiting) return;

        if (state == State.Waiting) UpdateWaiting();
        else if (state == State.Walking) UpdateWalking();
    }

    void LateUpdate()
    {
        if (!initDone || car == null) return;
        if (GameUI.WaitingForStart || Time.timeScale <= 0f) return;
        if (state != State.Walking || hitHandled) return;

        if (CarTouches()) OnHit();
    }

    // ------------------------------------------------------------------
    bool Init()
    {
        if (startPoint != null && endPoint != null)
        {
            sPos = startPoint.position;
            ePos = endPoint.position;
            tPos = triggerPoint != null ? triggerPoint.position : sPos;
        }
        else
        {
            Vector3 cp = car.transform.position;
            Vector3 fwd = Vector3.ProjectOnPlane(car.transform.rotation * car.localForward, Vector3.up);
            fwd = fwd.sqrMagnitude > 0.0001f ? fwd.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);

            // base point: his place in the scene (or next to the chosen sign)
            Vector3 sp = crosswalkSign != null ? GroundPos(crosswalkSign) : scenePos;
            Vector3 off = sp - cp; off.y = 0f;
            float lateral = Vector3.Dot(off, right);
            Vector3 cross = lateral >= 0f ? -right : right;   // from his side toward the road

            sPos = crosswalkSign != null ? sp - cross * startBack : sp;
            ePos = sp + cross * roadWidth;
            tPos = sp;
            Debug.Log("PedestrianCrossing: crossing at " + sp + ", " + Mathf.RoundToInt(Flat(cp, sp)) +
                      " m from the car. Wrong place? Move the character in the scene, or set Start/End Point.");
        }

        initDone = true;
        ResetPedestrian();
        return true;
    }

    static Vector3 GroundPos(Transform sign)
    {
        Vector3 p = sign.position;
        Renderer[] rs = sign.GetComponentsInChildren<Renderer>();
        if (rs.Length > 0)
        {
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            p.y = b.min.y;   // bottom of the sign post = ground
        }
        return p;
    }

    // ------------------------------------------------------------------
    void UpdateWaiting()
    {
        float dist = Flat(car.transform.position, tPos);

        // remember where the car was before reaching the crossing
        if (!hasSave && dist <= Mathf.Max(saveRadius, triggerRadius))
        {
            savedPos = car.transform.position;
            savedYaw = ReadYaw();
            hasSave = true;
        }

        if (dist <= triggerRadius)
        {
            state = State.Walking;
            hitHandled = false;
            Play(walkState);
        }
    }

    void UpdateWalking()
    {
        transform.position = Vector3.MoveTowards(transform.position, ePos, walkSpeed * Time.deltaTime);
        if ((transform.position - ePos).sqrMagnitude < 0.0001f)
        {
            state = State.Finished;
            Play(idleState);
        }
    }

    // ------------------------------------------------------------------
    bool CarTouches()
    {
        Renderer[] rs = car.GetComponentsInChildren<Renderer>();
        bool any = false;
        Bounds b = new Bounds(car.transform.position, Vector3.zero);
        foreach (Renderer r in rs)
        {
            if (r == null || !r.enabled) continue;
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        if (!any) return false;

        Vector3 e = b.extents;
        e.x *= carHitScale;
        e.z *= carHitScale;
        b.extents = e;

        Vector3 p = transform.position;
        if (p.y + bodyHeight < b.min.y || p.y > b.max.y) return false;

        float dx = Mathf.Max(b.min.x - p.x, 0f, p.x - b.max.x);
        float dz = Mathf.Max(b.min.z - p.z, 0f, p.z - b.max.z);
        return dx * dx + dz * dz < bodyRadius * bodyRadius;
    }

    void OnHit()
    {
        hitHandled = true;
        ResetPedestrian();

        Vector3 pos = hasSave ? savedPos : car.transform.position;
        float yaw = hasSave ? savedYaw : ReadYaw();
        TeleportCar(pos, yaw);

        if (messageMethod != null) messageMethod.Invoke(car, new object[] { hitTitle, hitMessage });
        else Debug.LogWarning("PedestrianCrossing: hit! (CarSignSystem.ShowMessage not found)");
    }

    void ResetPedestrian()
    {
        state = State.Waiting;
        transform.position = sPos;

        Vector3 dir = ePos - sPos; dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(dir.normalized) * Quaternion.Euler(0f, modelYawOffset, 0f);

        Play(idleState);
    }

    // ------------------------------------------------------------------
    float ReadYaw()
    {
        return yawField != null ? (float)yawField.GetValue(car) : 0f;
    }

    void TeleportCar(Vector3 pos, float yaw)
    {
        if (teleportMethod != null) teleportMethod.Invoke(car, new object[] { pos, yaw });
        else car.transform.position = pos;
    }

    void Play(string stateName)
    {
        if (anim == null || anim.runtimeAnimatorController == null || string.IsNullOrEmpty(stateName)) return;
        if (!anim.HasState(0, Animator.StringToHash(stateName)))
        {
            Debug.LogWarning("PedestrianCrossing: Animator state '" + stateName + "' not found. Change its name in the Inspector.");
            return;
        }
        anim.CrossFade(stateName, state == State.Waiting && stateName == idleState ? 0f : crossFade, 0);
    }

    static float Flat(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    void OnDrawGizmos()
    {
        if (!initDone) return;
        Gizmos.color = Color.green;
        Gizmos.DrawLine(sPos, ePos);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(tPos, triggerRadius);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(sPos, 0.4f);
        Gizmos.DrawWireSphere(ePos, 0.4f);
    }
}
