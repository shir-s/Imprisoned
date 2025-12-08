// FILEPATH: Assets/Scripts/Traps/CrusherTrap.cs
using System.Collections.Generic;
using JellyGame.GamePlay.Enemy;
using JellyGame.GamePlay.Player;
using JellyGame.GamePlay.Traps;
using UnityEngine;

/// <summary>
/// Moves two parts of a crusher trap in and out in a constant rhythm,
/// and can crush objects that are simultaneously touching both sides.
/// After a successful crush the trap closes fully and stays closed.
/// </summary>
public class CrusherTrap : MonoBehaviour
{
    [Header("Parts")]
    [SerializeField] private Transform leftPart;
    [SerializeField] private Transform rightPart;

    [Header("Movement")]
    [Tooltip("Local offset from center when the trap is fully OPEN.")]
    [SerializeField] private float openOffset = 1.0f;

    [Tooltip("Local offset from center when the trap is fully CLOSED (max depth).")]
    [SerializeField] private float closedOffset = 0.2f;

    [Tooltip("Seconds for a full open→close→open cycle.")]
    [SerializeField] private float cycleDuration = 2.0f;

    [Tooltip("0..1 range within the cycle where the trap can crush.")]
    [Range(0f, 1f)]
    [SerializeField] private float dangerStart = 0.3f;

    [Range(0f, 1f)]
    [SerializeField] private float dangerEnd = 0.7f;

    [Header("Depth")]
    [Tooltip("0 = almost not closing, 1 = full closedOffset. Can be changed from other scripts.")]
    [Range(0f, 1f)]
    [SerializeField] private float depth01 = 1f;

    [Header("Crush Settings")]
    [SerializeField] private bool killPlayerInstantly = true;
    [SerializeField] private bool killEnemyInstantly = true;

    [SerializeField] private int playerDamage = 1;
    [SerializeField] private int enemyDamage = 9999;

    public bool IsDangerPhase { get; private set; }

    public float Depth01
    {
        get => depth01;
        set => depth01 = Mathf.Clamp01(value);
    }

    Vector3 leftBaseLocalPos;
    Vector3 rightBaseLocalPos;
    float currentOffset;

    // מי נוגע בכל צד כרגע
    readonly HashSet<Collider> leftContacts  = new HashSet<Collider>();
    readonly HashSet<Collider> rightContacts = new HashSet<Collider>();

    // האם המלכודת כבר "ננעלה" אחרי מחיצה
    bool isDeactivated = false;

    void Awake()
    {
        leftBaseLocalPos  = leftPart.localPosition;
        rightBaseLocalPos = rightPart.localPosition;

        currentOffset = openOffset;
        ApplyCurrentOffset();
    }

    void Update()
    {
        if (cycleDuration <= 0f || isDeactivated)
            return;

        // t: 0→1→0→1…
        float t = Mathf.PingPong(Time.time / (cycleDuration * 0.5f), 1f);

        // עומק הסגירה בפועל: בין openOffset ל-closedOffset לפי depth01
        float targetClosedOffset = Mathf.Lerp(openOffset, closedOffset, depth01);

        // האנימציה זזה בין פתוח לגובה הסגירה שנקבע ע"י depth01
        currentOffset = Mathf.Lerp(openOffset, targetClosedOffset, t);

        IsDangerPhase = (t >= dangerStart && t <= dangerEnd);

        ApplyCurrentOffset();
    }

    void ApplyCurrentOffset()
    {
        leftPart.localPosition  = leftBaseLocalPos  + Vector3.left  * currentOffset;
        rightPart.localPosition = rightBaseLocalPos + Vector3.right * currentOffset;
    }

    /// <summary>
    /// Set how deep the crusher closes (0..1).
    /// </summary>
    public void SetDepth01(float value)
    {
        Depth01 = value;
    }

    /// <summary>
    /// Called once after a successful crush: stop motion, close fully and stay closed.
    /// </summary>
    void DeactivateTrap()
    {
        if (isDeactivated)
            return;

        isDeactivated = true;

        // לא מסוכן יותר (לא אמורות להיות מחיצות נוספות)
        IsDangerPhase = false;

        // מנקים מגעים קיימים
        leftContacts.Clear();
        rightContacts.Clear();

        // סוגרים לגמרי לפי עומק
        float targetClosedOffset = Mathf.Lerp(openOffset, closedOffset, depth01);
        currentOffset = targetClosedOffset;
        ApplyCurrentOffset();

        // אם את רוצה גם להפסיק לגמרי את ה-Update:
        // enabled = false;
    }

    // נקראות מהטריגרים שבצדדים
    public void RegisterSideContact(CrusherSideTrigger.Side side, Collider col)
    {
        if (side == CrusherSideTrigger.Side.Left)
            leftContacts.Add(col);
        else
            rightContacts.Add(col);

        TryCrush(col);
    }

    public void UnregisterSideContact(CrusherSideTrigger.Side side, Collider col)
    {
        if (side == CrusherSideTrigger.Side.Left)
            leftContacts.Remove(col);
        else
            rightContacts.Remove(col);
    }

    void TryCrush(Collider col)
    {
        if (isDeactivated)
            return;

        // חייבים להיות גם בשמאל וגם בימין
        if (!leftContacts.Contains(col) || !rightContacts.Contains(col))
            return;

        if (!IsDangerPhase)
            return;

        bool crushedSomething = false;

        // מחפש קומפוננטת חיים על האובייקט או ההורה שלו
        // Check on the collider's GameObject first, then parent
        var playerHealth = col.GetComponent<PlayerHealth>();
        if (playerHealth == null)
            playerHealth = col.GetComponentInParent<PlayerHealth>();
        if (playerHealth != null && playerHealth.CurrentHealth > 0)
        {
            if (killPlayerInstantly)
                playerHealth.Kill();
            else
                playerHealth.TakeDamage(playerDamage);

            crushedSomething = true;
        }
        else
        {
            // Check on the collider's GameObject first, then parent
            var enemyHealth = col.GetComponent<EnemyHealth>();
            if (enemyHealth == null)
                enemyHealth = col.GetComponentInParent<EnemyHealth>();
            
            if (enemyHealth != null && enemyHealth.CurrentHealth > 0)
            {
                print($"[CrusherTrap] Crushing ENEMY: {col.name} -> {enemyHealth.gameObject.name}, Health: {enemyHealth.CurrentHealth}");
                if (killEnemyInstantly)
                {
                    print("[CrusherTrap] Killing enemy instantly");
                    enemyHealth.Kill();
                }
                else
                {
                    print($"[CrusherTrap] Damaging enemy: {enemyDamage}");
                    enemyHealth.TakeDamage(enemyDamage);
                }

                crushedSomething = true;
            }
            else
            {
                print($"[CrusherTrap] TryCrush: Collider {col.name} has no EnemyHealth component!");
                print($"[CrusherTrap] Collider GameObject: {col.gameObject.name}, Parent: {(col.transform.parent != null ? col.transform.parent.name : "none")}");
            }
        }

        if (crushedSomething)
        {
            DeactivateTrap();
        }
    }

    void OnDisable()
    {
        leftContacts.Clear();
        rightContacts.Clear();
    }
}
