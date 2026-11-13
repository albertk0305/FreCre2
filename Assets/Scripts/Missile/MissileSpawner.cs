using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 미사일 생성 시점, 생성 위치, 이동 방향, 속도 및 활성 미사일 목록을 관리한다.
/// - 생성 위치: ArenaBounds.Center + (cosθ, sinθ) × SpawnRadius, θ ∈ [0°, 360°) 균등 무작위. (카메라 위치는 사용하지 않음)
/// - 이동 방향: 생성점 → Center ± AimTargetHalfSize 정사각형 안의 무작위 목표점, 정규화. 8방향 양자화 없음.
/// - 제거 범위: 카메라 가시 영역 + DespawnMargin. 판정은 Missile이 담당한다.
/// Stage 3에서는 Spawn On Start로 자동 시작한다. Stage 4에서 GameManager가 Start/StopSpawning, StopAllMissiles를 호출한다.
/// Stage 5에서 DifficultyManager가 SetSpawnSettings 등으로 간격/상한/속도를 넘겨줄 수 있다.
/// </summary>
[DisallowMultipleComponent]
public class MissileSpawner : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Missile 컴포넌트가 붙은 미사일 프리팹.")]
    [SerializeField] private Missile missilePrefab;
    [SerializeField] private ArenaBounds arenaBounds;
    [Tooltip("제거 범위 계산용 카메라. 비우면 Camera.main을 한 번 찾는다. 생성 좌표의 기준이 아니다.")]
    [SerializeField] private Camera targetCamera;
    [Tooltip("생성된 미사일의 부모(Hierarchy 정리용). 비우면 이 오브젝트 아래에 생성.")]
    [SerializeField] private Transform missileParent;

    [Header("Spawn Position / Direction")]
    [Min(0f)] [SerializeField] private float spawnRadius = 5.0f;
    [Min(0f)] [SerializeField] private float aimTargetHalfSize = 1.15f;

    [Header("Speed (unit/s)")]
    [Min(0f)] [SerializeField] private float minMissileSpeed = 1.67f;
    [Min(0f)] [SerializeField] private float maxMissileSpeed = 2.33f;

    [Header("Timing / Count (Stage 3 고정 시험값)")]
    [Min(0.01f)] [SerializeField] private float spawnInterval = 1.80f;
    [Min(0)] [SerializeField] private int maxActiveMissiles = 5;

    [Header("Despawn")]
    [Min(0f)] [SerializeField] private float despawnMargin = 0.33f;

    [Header("Start")]
    [Tooltip("Stage 3 단독 테스트용. Stage 4에서 GameManager가 시작을 제어하면 끈다.")]
    [SerializeField] private bool spawnOnStart = true;

    private readonly List<Missile> activeMissiles = new List<Missile>();
    private float spawnTimer;
    private bool isSpawning;
    private bool cameraResolved;

    public IReadOnlyList<Missile> ActiveMissiles
    {
        get
        {
            PruneDestroyed();
            return activeMissiles;
        }
    }

    public int ActiveMissileCount
    {
        get
        {
            PruneDestroyed();
            return activeMissiles.Count;
        }
    }

    public bool IsSpawning => isSpawning;

    /// <summary>Start()에서 자동으로 생성을 시작할지. Stage 4부터 GameManager가 Awake에서 끈다.</summary>
    public bool SpawnOnStart
    {
        get => spawnOnStart;
        set => spawnOnStart = value;
    }

    public float SpawnTimer => spawnTimer;
    public float SpawnRadius => spawnRadius;
    public float AimTargetHalfSize => aimTargetHalfSize;
    public float MinMissileSpeed => minMissileSpeed;
    public float MaxMissileSpeed => maxMissileSpeed;
    public float SpawnInterval => spawnInterval;
    public int MaxActiveMissiles => maxActiveMissiles;
    public float DespawnMargin => despawnMargin;

    /// <summary>생성 기준 중심 (ArenaBounds.Center). ArenaBounds가 없으면 이 오브젝트 위치.</summary>
    public Vector2 SpawnCenter => arenaBounds != null ? arenaBounds.Center : (Vector2)transform.position;

    private void Start()
    {
        if (missilePrefab == null)
        {
            Debug.LogWarning($"{name}: Missile Prefab이 연결되지 않아 미사일을 생성할 수 없습니다.", this);
        }
        if (arenaBounds == null)
        {
            Debug.LogWarning($"{name}: ArenaBounds가 연결되지 않아 이 오브젝트 위치를 생성 중심으로 사용합니다.", this);
        }

        if (spawnOnStart)
        {
            StartSpawning();
        }
    }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    private void OnValidate()
    {
        if (maxMissileSpeed < minMissileSpeed)
        {
            maxMissileSpeed = minMissileSpeed;
        }
    }

    private void OnDestroy()
    {
        foreach (Missile missile in activeMissiles)
        {
            if (missile != null)
            {
                missile.OnDespawned -= HandleMissileDespawn;
            }
        }
        activeMissiles.Clear();
    }

    /// <summary>생성 타이머 진행. 시작 후 SpawnInterval마다 1회 생성을 시도한다. 테스트에서 직접 호출할 수 있다.</summary>
    public void Tick(float deltaTime)
    {
        if (!isSpawning || deltaTime <= 0f)
        {
            return;
        }

        spawnTimer += deltaTime;
        if (spawnTimer < spawnInterval)
        {
            return;
        }

        // 한 번에 한 번만 시도한다. 프레임 지연으로 여러 간격이 쌓여도 몰아서 생성하지 않는다.
        spawnTimer -= spawnInterval;
        if (spawnTimer >= spawnInterval)
        {
            spawnTimer = 0f;
        }

        SpawnMissile(); // 상한 도달 시 내부에서 건너뜀
    }

    /// <summary>생성을 시작한다. 첫 미사일은 SpawnInterval 후 생성된다.</summary>
    public void StartSpawning()
    {
        spawnTimer = 0f;
        isSpawning = true;
    }

    /// <summary>새 미사일 생성을 중지한다. 이미 생성된 미사일은 그대로 움직인다.</summary>
    public void StopSpawning()
    {
        isSpawning = false;
    }

    /// <summary>현재 활성 미사일의 이동을 모두 멈춘다 (Stage 4 게임 오버용).</summary>
    public void StopAllMissiles()
    {
        PruneDestroyed();
        foreach (Missile missile in activeMissiles)
        {
            missile.StopMovement();
        }
    }

    /// <summary>미사일 1개를 생성한다. 활성 수가 상한이면 생성하지 않고 null을 반환한다.</summary>
    public Missile SpawnMissile()
    {
        if (missilePrefab == null)
        {
            return null;
        }

        PruneDestroyed();
        if (activeMissiles.Count >= maxActiveMissiles)
        {
            return null;
        }

        Vector2 spawnPosition = CalculateSpawnPosition();
        Vector2 direction = CalculateDirection(spawnPosition, out Vector2 aimTarget);
        float speed = CalculateSpeed();
        Bounds despawnBounds = CalculateDespawnBounds();

        Transform parent = missileParent != null ? missileParent : transform;
        Missile missile = Instantiate(missilePrefab, new Vector3(spawnPosition.x, spawnPosition.y, 0f), Quaternion.identity, parent);
        missile.Initialize(spawnPosition, direction, speed, aimTarget, despawnBounds);

        missile.OnDespawned -= HandleMissileDespawn; // 중복 구독 방지
        missile.OnDespawned += HandleMissileDespawn;
        activeMissiles.Add(missile);
        return missile;
    }

    /// <summary>ArenaBounds.Center 기준 반지름 SpawnRadius 원 위의 균등 무작위 좌표.</summary>
    public Vector2 CalculateSpawnPosition()
    {
        float angle = Random.value * 360f % 360f; // [0, 360)
        return SpawnPositionFromAngle(SpawnCenter, spawnRadius, angle);
    }

    /// <summary>생성점에서 경기장 내부 무작위 목표점(Center ± AimTargetHalfSize)으로 향하는 정규화 방향.</summary>
    public Vector2 CalculateDirection(Vector2 spawnPosition)
    {
        return CalculateDirection(spawnPosition, out _);
    }

    public Vector2 CalculateDirection(Vector2 spawnPosition, out Vector2 aimTarget)
    {
        Vector2 center = SpawnCenter;
        aimTarget = RandomPointInSquare(center, aimTargetHalfSize);
        Vector2 direction = DirectionToTarget(spawnPosition, aimTarget);
        if (direction == Vector2.zero)
        {
            // 생성점과 목표점이 같을 때(SpawnRadius≈0 등) 남쪽으로 진행.
            aimTarget = spawnPosition;
            direction = Vector2.down;
        }
        return direction;
    }

    public float CalculateSpeed()
    {
        return Random.Range(minMissileSpeed, Mathf.Max(minMissileSpeed, maxMissileSpeed));
    }

    /// <summary>카메라 가시 영역 + DespawnMargin. 카메라가 없거나 Orthographic이 아니면 생성 원 기준으로 대체.</summary>
    public Bounds CalculateDespawnBounds()
    {
        Camera cam = ResolveCamera();
        if (cam != null && cam.orthographic)
        {
            return CalculateCameraBounds(cam.transform.position, cam.orthographicSize, cam.aspect, despawnMargin);
        }

        float half = spawnRadius + despawnMargin;
        Vector2 c = SpawnCenter;
        return new Bounds(new Vector3(c.x, c.y, 0f), new Vector3(half * 2f, half * 2f, 0f));
    }

    private void HandleMissileDespawn(Missile missile)
    {
        if (missile != null)
        {
            missile.OnDespawned -= HandleMissileDespawn;
        }
        activeMissiles.Remove(missile);
    }

    private void PruneDestroyed()
    {
        // 외부에서 Destroy된 미사일(Unity null)이 목록에 남지 않도록 정리한다.
        activeMissiles.RemoveAll(m => m == null || m.IsDespawned);
    }

    private Camera ResolveCamera()
    {
        if (targetCamera == null && !cameraResolved)
        {
            cameraResolved = true;
            targetCamera = Camera.main;
        }
        return targetCamera;
    }

    // ---------------- 순수 계산 (EditMode 테스트 대상) ----------------

    /// <summary>center + (cosθ, sinθ) × radius. angleDegrees는 East=0°, 반시계 방향.</summary>
    public static Vector2 SpawnPositionFromAngle(Vector2 center, float radius, float angleDegrees)
    {
        float rad = angleDegrees * Mathf.Deg2Rad;
        return center + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * radius;
    }

    public static Vector2 RandomPointInSquare(Vector2 center, float halfSize)
    {
        halfSize = Mathf.Max(0f, halfSize);
        return new Vector2(
            center.x + Random.Range(-halfSize, halfSize),
            center.y + Random.Range(-halfSize, halfSize));
    }

    /// <summary>from → target 정규화 벡터. 두 점이 같으면 Vector2.zero.</summary>
    public static Vector2 DirectionToTarget(Vector2 from, Vector2 target)
    {
        Vector2 delta = target - from;
        return delta.sqrMagnitude > 1e-12f ? delta.normalized : Vector2.zero;
    }

    /// <summary>Orthographic 카메라 가시 영역(높이 2×size, 너비 2×size×aspect)에 margin을 더한 범위.</summary>
    public static Bounds CalculateCameraBounds(Vector2 cameraPosition, float orthographicSize, float aspect, float margin)
    {
        float halfHeight = Mathf.Max(0f, orthographicSize) + Mathf.Max(0f, margin);
        float halfWidth = Mathf.Max(0f, orthographicSize) * Mathf.Max(0f, aspect) + Mathf.Max(0f, margin);
        return new Bounds(
            new Vector3(cameraPosition.x, cameraPosition.y, 0f),
            new Vector3(halfWidth * 2f, halfHeight * 2f, 0f));
    }
}
