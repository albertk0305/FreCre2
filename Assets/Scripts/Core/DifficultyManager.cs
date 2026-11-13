using UnityEngine;

/// <summary>
/// 생존시간을 기준으로 현재 미사일 생성 난이도를 계산한다. 난이도 원본 설정값 8개를 소유한다.
/// - 생성 간격: max(MinimumSpawnInterval, InitialSpawnInterval - t × SpawnIntervalDecreaseRate)
/// - 최대 동시 미사일: min(MaximumMissiles, InitialMaxMissiles + floor(t / MissileCountIncreaseInterval))
/// - 속도 범위: 생존시간과 무관하게 고정 (미사일 자체가 빨라지지 않는다)
/// 미사일을 생성/이동/참조하지 않는다. MissileSpawner가 생성 시도마다 GetCurrentDifficulty()를 호출한다.
/// </summary>
[DisallowMultipleComponent]
public class DifficultyManager : MonoBehaviour
{
    // 최초 플레이테스트용 임시 밸런스 (FreCre2 밸런스 v0.5 3.1)
    public const float DefaultInitialSpawnInterval = 1.80f;
    public const float DefaultMinimumSpawnInterval = 0.45f;
    public const float DefaultSpawnIntervalDecreaseRate = 0.0125f;
    public const int DefaultInitialMaxMissiles = 5;
    public const int DefaultMaximumMissiles = 14;
    public const float DefaultMissileCountIncreaseInterval = 15f;
    public const float DefaultMinMissileSpeed = 1.67f;
    public const float DefaultMaxMissileSpeed = 2.33f;

    /// <summary>생성 간격의 안전 하한(0 또는 음수 간격으로 매 프레임 생성되는 것을 방지).</summary>
    public const float MinimumAllowedSpawnInterval = 0.01f;

    [Header("References")]
    [Tooltip("생존시간 원본. GetCurrentDifficulty()가 이 ElapsedTime을 사용한다. 비우면 0초 기준.")]
    [SerializeField] private SurvivalTimer survivalTimer;

    [Header("Spawn Interval (s)")]
    [Min(MinimumAllowedSpawnInterval)] [SerializeField] private float initialSpawnInterval = DefaultInitialSpawnInterval;
    [Min(MinimumAllowedSpawnInterval)] [SerializeField] private float minimumSpawnInterval = DefaultMinimumSpawnInterval;
    [Tooltip("생존 1초당 생성 간격 감소량.")]
    [Min(0f)] [SerializeField] private float spawnIntervalDecreaseRate = DefaultSpawnIntervalDecreaseRate;

    [Header("Max Active Missiles")]
    [Min(0)] [SerializeField] private int initialMaxMissiles = DefaultInitialMaxMissiles;
    [Min(0)] [SerializeField] private int maximumMissiles = DefaultMaximumMissiles;
    [Tooltip("이 시간(초)마다 최대 동시 미사일 수가 1 증가한다.")]
    [Min(0.01f)] [SerializeField] private float missileCountIncreaseInterval = DefaultMissileCountIncreaseInterval;

    [Header("Missile Speed (unit/s, 시간과 무관하게 고정)")]
    [Min(0f)] [SerializeField] private float minMissileSpeed = DefaultMinMissileSpeed;
    [Min(0f)] [SerializeField] private float maxMissileSpeed = DefaultMaxMissileSpeed;

    public SurvivalTimer SurvivalTimer => survivalTimer;
    public float InitialSpawnInterval => initialSpawnInterval;
    public float MinimumSpawnInterval => minimumSpawnInterval;
    public float SpawnIntervalDecreaseRate => spawnIntervalDecreaseRate;
    public int InitialMaxMissiles => initialMaxMissiles;
    public int MaximumMissiles => maximumMissiles;
    public float MissileCountIncreaseInterval => missileCountIncreaseInterval;
    public float MinMissileSpeed => minMissileSpeed;
    public float MaxMissileSpeed => maxMissileSpeed;

    /// <summary>DifficultyManager가 없을 때(Stage 3 단독 실행/테스트) MissileSpawner가 쓰는 0초 기준 기본 난이도.</summary>
    public static DifficultySnapshot DefaultSnapshot => new DifficultySnapshot(
        DefaultInitialSpawnInterval, DefaultInitialMaxMissiles, DefaultMinMissileSpeed, DefaultMaxMissileSpeed);

    /// <summary>연결된 SurvivalTimer.ElapsedTime 기준 현재 난이도. 타이머가 없으면 0초 기준.</summary>
    public DifficultySnapshot GetCurrentDifficulty()
    {
        return GetCurrentDifficulty(survivalTimer != null ? survivalTimer.ElapsedTime : 0f);
    }

    /// <summary>생존시간(초)에 해당하는 난이도를 계산한다. 음수/NaN은 0초로 취급한다.</summary>
    public DifficultySnapshot GetCurrentDifficulty(float elapsedTime)
    {
        if (float.IsNaN(elapsedTime) || elapsedTime < 0f)
        {
            elapsedTime = 0f;
        }

        float spawnInterval = Mathf.Max(
            minimumSpawnInterval,
            initialSpawnInterval - elapsedTime * spawnIntervalDecreaseRate);
        spawnInterval = Mathf.Max(MinimumAllowedSpawnInterval, spawnInterval);

        // min(MaximumMissiles, InitialMaxMissiles + floor(t / interval)).
        // 증가 횟수를 float로 비교해 아주 큰 t에서도 int 오버플로 없이 같은 결과를 낸다.
        float steps = missileCountIncreaseInterval > 0f
            ? Mathf.Floor(elapsedTime / missileCountIncreaseInterval)
            : 0f;
        int maxActiveMissiles = steps >= maximumMissiles - initialMaxMissiles
            ? maximumMissiles
            : initialMaxMissiles + (int)steps;

        float minSpeed = Mathf.Max(0f, minMissileSpeed);
        float maxSpeed = Mathf.Max(minSpeed, maxMissileSpeed);

        return new DifficultySnapshot(spawnInterval, maxActiveMissiles, minSpeed, maxSpeed);
    }

    private void OnValidate()
    {
        if (minimumSpawnInterval > initialSpawnInterval)
        {
            minimumSpawnInterval = initialSpawnInterval;
        }
        if (maximumMissiles < initialMaxMissiles)
        {
            maximumMissiles = initialMaxMissiles;
        }
        if (maxMissileSpeed < minMissileSpeed)
        {
            maxMissileSpeed = minMissileSpeed;
        }
    }
}
