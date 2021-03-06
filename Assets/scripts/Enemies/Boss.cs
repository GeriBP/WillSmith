using System;
using System.Collections.Generic;
using System.Linq;
using FluentBehaviourTree;
using UnityEngine;
using UnityEngine.Assertions;
using System.Collections;
using FMODUnity;

/// <summary>
/// The final boss of the game
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(StudioEventEmitter))]
public class Boss : MonoBehaviour, IEnemy
{
    public GameObject canvas;
    [SerializeField]
    private float startingHP = 4f;

    [SerializeField]
    private GameObject[] minionPrefab;

    [SerializeField]
    private GameObject pixelExplosion, bigExplosion;

	/// <summary>
	/// Prefab to spawn for the projectile
	/// </summary>
	[SerializeField]
	private Projectile projectilePrefab;

    /// <summary>
    /// Time between each bullet
    /// </summary>
    [SerializeField]
    private float timeBetweenEachShot = 0.5f;

    /// <summary>
    /// Duration of each volley of bullets when the boss is attacking
    /// </summary>
    [SerializeField]
    private float fireDuration = 2f;

    /// <summary>
    /// How long to wait before firing a round of shots
    /// </summary>
    [SerializeField]
    private float fireWindupDuration = 1.5f;

	[SerializeField]
	private float projectileSpeed = 5f;

    [SerializeField]
    private float finalCooldownDuration = 3f;

    /// <summary>
    /// Can't take damage while the shield is active.
    /// </summary>
    private bool shieldActive = false;

    /// <summary>
    /// Our current hit point. At or below 0 will result in death.
    /// </summary>
    private float currentHP;

    private new Rigidbody2D rigidbody;

    private StudioEventEmitter gunSound;
    private StudioEventEmitter shieldDownSound;

    private BossHealthUI ui;

    private IBehaviourTreeNode behaviour;

    private GameObject shield;

    #region state management
    private bool alreadySpawnedMinions = false;
    private float timeAttackStarted;
    private int numberOfAttacksCompleted = 0;
    private int numberOfAttacksWoundUp = 0;
    private int currentAttack = -1;
    private int nextAttack = 0;
    private float windupStartTime;
    private float timeLastFired;
    private int initialNumberOfEnemies;
    private bool startedEndSegment = false;
    private float finalSectionStartTime;
    private float targetRotation = 720f;
    #endregion

    void Awake()
    {
        rigidbody = GetComponent<Rigidbody2D>();
        Assert.IsNotNull(rigidbody);

        var shieldTransform = GetComponentsInChildren<Transform>()
            .Where(t => t.name == "Shield")
            .FirstOrDefault();
        Assert.IsNotNull(shieldTransform);
        shield = shieldTransform.gameObject;

        Assert.IsNotNull(projectilePrefab);
        Assert.IsNotNull(pixelExplosion);

        ui = FindObjectOfType<BossHealthUI>();
        Assert.IsNotNull(ui);

		var soundEmitters = GetComponents<StudioEventEmitter>();
		Assert.IsTrue(soundEmitters.Length == 2, "Boss requires two sound emitters");
		gunSound = soundEmitters[0];
		shieldDownSound = soundEmitters[1];
    }

    void Start()
    {
        currentHP = startingHP;

        Reset(new TimeData(Time.deltaTime));

        // Show health UI
        ui.SetHealthBarVisible(true);
        ui.SetHealthBarValue(1f);

        behaviour = new BehaviourTreeBuilder()
            .Sequence("Main behaviour")
                .Sequence("Spawn minions")
                    .Selector("Spawn")
                        .Condition("Already spawned", t => alreadySpawnedMinions)
                        .Sequence("Enable shield and spawn")
                            .Do("Enable shield", t => SetShieldEnabled(true))
                            .Do("Spawn", SpawnMinions)
                        .End()
                    .End()
                    .Do("Wait until all minions defeated", WaitUntilMinionsDefeated)
                    .Do("Disable shield", t => SetShieldEnabled(false))
                .End()
                .Sequence("Attack")
                    .Selector("Set up attack")
                        .Condition("Already set up", t => currentAttack >= nextAttack)
                        .Do("Set up", t =>
                        {
                            Debug.Log("Setting up attack");
                            currentAttack++;
                            windupStartTime = Time.time;
                            return BehaviourTreeStatus.Success;
                        })
                    .End()
                    .Do("Wait", t => Time.time >= windupStartTime + fireWindupDuration ?
                        BehaviourTreeStatus.Success : BehaviourTreeStatus.Running)
                    .Selector("Fire or wait")
                        .Condition("Timeout", t => Time.time >= windupStartTime + fireWindupDuration + fireDuration)
                        .Parallel("Fire and rotate", 3, 3)
                            .Sequence("Fire")
                                .Condition("Time", t => Time.time >= windupStartTime + fireWindupDuration + 1f)
                                .Do("Wait", t => Time.time >= timeLastFired + timeBetweenEachShot ? 
                                    BehaviourTreeStatus.Success : BehaviourTreeStatus.Running)
                                .Do("Fire", Fire)
                            .End()
                            .Do("Rotate", t => 
                            {
                                var time = Mathf.Clamp01((Time.time - (windupStartTime + fireWindupDuration)) / fireDuration);
                                time = 1f + Mathf.Sin((1.5f * Mathf.PI) + time * Mathf.PI * 0.5f);
                                transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, targetRotation, time));
                                return BehaviourTreeStatus.Running;
                            })
                        .End()
                    .End()
                .End()
                .Sequence("Start ending then wait")
                    .Selector("Start or wait")
                        .Condition("Already started", t => startedEndSegment)
                        .Do("Start ending", t => 
                        { 
                            startedEndSegment = true;
                            finalSectionStartTime = Time.time;

                            return BehaviourTreeStatus.Success;
                        })
                    .End()
                    .Do("Wait", t => Time.time >= finalSectionStartTime + finalCooldownDuration ? 
                        BehaviourTreeStatus.Success : BehaviourTreeStatus.Running)
                    .Do("Reset", Reset)
                .End()
            .End()
            .Build();
    }

    void Update()
    {
        behaviour.Tick(new TimeData(Time.deltaTime));
    }

    private BehaviourTreeStatus SpawnMinions(TimeData t)
    {
        foreach (var spawnPoint in GetComponentsInChildren<Transform>())
        {
            if (spawnPoint.name == "SpawnPoint")
            {
                Instantiate(minionPrefab[UnityEngine.Random.Range(0, minionPrefab.Length)], spawnPoint.transform.position, Quaternion.identity);
                gameManager.enemiesAlive++;
            }
        }
        alreadySpawnedMinions = true;

        return BehaviourTreeStatus.Success;
    }

    private BehaviourTreeStatus WaitUntilMinionsDefeated(TimeData t)
    {
        return gameManager.enemiesAlive <= initialNumberOfEnemies ? 
            BehaviourTreeStatus.Success :
            BehaviourTreeStatus.Running;
    }

    private BehaviourTreeStatus SetShieldEnabled(bool enabled)
    {
        // Early out if the state of the shield already matches
        if (shieldActive == enabled)
        {
            return BehaviourTreeStatus.Success;
        }

        shield.SetActive(enabled);
        shieldActive = enabled;

        if (!shieldActive)
        {
            shieldDownSound.Play();
        }

        return BehaviourTreeStatus.Success;
    }

	private BehaviourTreeStatus Fire(TimeData t)
	{
		SpawnProjectile(transform.rotation * new Vector2(1, 1).normalized);
		SpawnProjectile(transform.rotation * new Vector2(1, -1).normalized);
		SpawnProjectile(transform.rotation * new Vector2(-1, -1).normalized);
		SpawnProjectile(transform.rotation * new Vector2(-1, 1).normalized);

        gunSound.Play();

		timeLastFired = Time.time;
		return BehaviourTreeStatus.Success;
	}

    private void SpawnProjectile(Vector2 direction)
    {   
        var projectile = Instantiate(projectilePrefab, (Vector2)transform.position + direction * 2f, Quaternion.identity);
		projectile.MovementSpeed = projectileSpeed;
        projectile.Direction = direction;
    }

    private BehaviourTreeStatus Reset(TimeData t)
    {
        Debug.Log("Boss resetting");
        alreadySpawnedMinions = false;
        numberOfAttacksCompleted = 0;
        numberOfAttacksWoundUp = 0;
        currentAttack = -1;
        nextAttack = 0;
        initialNumberOfEnemies = gameManager.enemiesAlive;
        startedEndSegment = false;
        targetRotation *= -1f;

        return BehaviourTreeStatus.Success;
    }

    public void TakeHit(float damage)
    {
        if (shieldActive)
        {
            return;
        }

        currentHP -= damage;
        Debug.Log("Taking " + damage + " HP of damage. Remaining HP " + currentHP);

        ui.SetHealthBarValue(Mathf.Max(0f, currentHP / startingHP));

        if (currentHP <= 0f)
        {
            gameManager.enemiesAlive--;
            gameManager.bossDead = true;
            canvas.SetActive(false);
            Instantiate(pixelExplosion, transform.position, Quaternion.identity);
            Instantiate(pixelExplosion, transform.position, Quaternion.identity);
            Instantiate(pixelExplosion, transform.position, Quaternion.identity);
            Instantiate(pixelExplosion, transform.position, Quaternion.identity);
            Instantiate(bigExplosion, transform.position, Quaternion.identity);
            Instantiate(bigExplosion, transform.position, Quaternion.identity);
            Instantiate(pixelExplosion, transform.position, Quaternion.identity);
            Instantiate(bigExplosion, transform.position, Quaternion.identity);
            Instantiate(bigExplosion, transform.position, Quaternion.identity);
            Destroy(gameObject);
        }
    }

    void OnDestroy()
    {
        ui.SetHealthBarVisible(false);
    }
}