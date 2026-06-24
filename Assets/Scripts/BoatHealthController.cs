using System;
using UnityEngine;
using UnityEngine.InputSystem;

public enum BoatDamageSource
{
    Unspecified = 0,
    Collision = 1,
    EnemyContact = 2,
    Boundary = 3,
    HullWear = 4,
    Debug = 5,
    Whirlpool = 6
}

// Owns the boat's hull health state and exposes the simple APIs other systems use.
//
// Truth updates happen immediately through TakeDamage / Heal / SetHealth.
// Visual consumers such as BoatHealthBarController can animate locally toward the
// latest target while still reading instantly correct health data and events.
public class BoatHealthController : MonoBehaviour
{
    public static event Action BoatSunk;

    [Header("Health")]

    // Maximum hull health for this boat prefab.
    [SerializeField] float maxHealth = 100f;

    // Starting hull health on scene load. Defaults to full, but can be lowered in
    // the Inspector for testing orange/red states without adding temporary hacks.
    [SerializeField] float startingHealth = 100f;

    [Header("Debug")]

    // Hold '[' to apply slow repeated damage for bar testing.
    [SerializeField] float debugDamagePerSecond = 12f;

    // Hold ']' to apply slow repeated healing for bar testing.
    [SerializeField] float debugHealPerSecond = 12f;

    public float MaxHealth => maxHealth;
    public float CurrentHealth => currentHealth;
    public float HealthFraction => maxHealth > 0f ? currentHealth / maxHealth : 0f;
    public bool IsDead => currentHealth <= 0f;

    // previousHealth, currentHealth
    public event Action<float, float> OnHealthChanged;
    public event Action<float> OnDamaged;
    public event Action<float, BoatDamageSource> OnDamagedWithSource;

    float currentHealth;
    bool hasEmittedBoatSunk;

    void Awake()
    {
        currentHealth = Mathf.Clamp(startingHealth, 0f, maxHealth);
        hasEmittedBoatSunk = currentHealth <= 0f;
    }

    void Update()
    {
        HandleDebugInput();
    }

    void OnValidate()
    {
        maxHealth = Mathf.Max(1f, maxHealth);
        startingHealth = Mathf.Clamp(startingHealth, 0f, maxHealth);
    }

    public void TakeDamage(float amount)
    {
        TakeDamage(amount, BoatDamageSource.Unspecified);
    }

    public void TakeDamage(float amount, BoatDamageSource damageSource)
    {
        if (amount <= 0f)
            return;

        SetHealth(currentHealth - amount, damageSource);
    }

    public void Heal(float amount)
    {
        if (amount <= 0f)
            return;

        SetHealth(currentHealth + amount);
    }

    public void SetHealth(float amount)
    {
        SetHealth(amount, BoatDamageSource.Unspecified);
    }

    public void SetHealth(float amount, BoatDamageSource damageSource)
    {
        float clampedHealth = Mathf.Clamp(amount, 0f, maxHealth);
        if (Mathf.Approximately(clampedHealth, currentHealth))
            return;

        float previousHealth = currentHealth;
        currentHealth = clampedHealth;
        float damageAmount = Mathf.Max(0f, previousHealth - currentHealth);
        OnHealthChanged?.Invoke(previousHealth, currentHealth);
        if (damageAmount > 0f)
        {
            OnDamaged?.Invoke(damageAmount);
            OnDamagedWithSource?.Invoke(damageAmount, damageSource);
        }
        if (!hasEmittedBoatSunk && currentHealth <= 0f)
        {
            hasEmittedBoatSunk = true;
            BoatSunk?.Invoke();
        }
    }

    void HandleDebugInput()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.leftBracketKey.isPressed)
            TakeDamage(debugDamagePerSecond * Time.deltaTime, BoatDamageSource.Debug);

        if (keyboard.rightBracketKey.isPressed)
            Heal(debugHealPerSecond * Time.deltaTime);
    }
}
