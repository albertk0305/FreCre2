// 여러 시스템이 공유하는 공통 데이터 타입.
// Stage 2: ControlMode, Stage 4: GameState, Stage 5: DifficultySnapshot, Stage 6: GameResult.

/// <summary>게임 진행 상태. GameManager가 소유하고 변경한다.</summary>
public enum GameState
{
    /// <summary>게임 시작 전 준비.</summary>
    Ready = 0,

    /// <summary>게임 진행 중.</summary>
    Playing = 1,

    /// <summary>게임 종료.</summary>
    GameOver = 2,
}

/// <summary>플레이어 조작 방식.</summary>
public enum ControlMode
{
    /// <summary>키보드 입력 (WASD/방향키 + Space).</summary>
    PC = 0,

    /// <summary>가상 조이스틱 및 점프 버튼 (Stage 10에서 구현).</summary>
    Mobile = 1,
}

/// <summary>
/// 특정 생존시간의 난이도 계산 결과. DifficultyManager가 만들고 MissileSpawner가 생성 시도마다 받아 적용한다.
/// 원본 설정값(8개)은 DifficultyManager가 관리하며, 이 구조체는 계산 결과 4개만 담는다.
/// </summary>
public readonly struct DifficultySnapshot
{
    /// <summary>미사일 생성 간격(초).</summary>
    public float SpawnInterval { get; }

    /// <summary>동시에 존재할 수 있는 최대 미사일 수.</summary>
    public int MaxActiveMissiles { get; }

    /// <summary>미사일 최소 속도(unit/s). 생존시간과 무관하게 고정.</summary>
    public float MinMissileSpeed { get; }

    /// <summary>미사일 최대 속도(unit/s). 생존시간과 무관하게 고정.</summary>
    public float MaxMissileSpeed { get; }

    public DifficultySnapshot(float spawnInterval, int maxActiveMissiles, float minMissileSpeed, float maxMissileSpeed)
    {
        SpawnInterval = spawnInterval;
        MaxActiveMissiles = maxActiveMissiles;
        MinMissileSpeed = minMissileSpeed;
        MaxMissileSpeed = maxMissileSpeed;
    }

    public override string ToString()
    {
        return $"Interval {SpawnInterval:0.###}s, MaxActive {MaxActiveMissiles}, Speed {MinMissileSpeed:0.##}~{MaxMissileSpeed:0.##}";
    }
}

/// <summary>
/// 게임 오버(미사일 피격) 시 GameManager가 만드는 결과 데이터. Stage 9 GameOverUI가 표시에 사용한다.
/// </summary>
public readonly struct GameResult
{
    /// <summary>이번 플레이의 생존시간(초).</summary>
    public float SurvivalTime { get; }

    /// <summary>이번 결과까지 반영된 최고 생존기록(초).</summary>
    public float BestTime { get; }

    /// <summary>이번 생존시간이 기존 최고기록을 초과해 갱신되었는지. 같은 기록은 false.</summary>
    public bool IsNewRecord { get; }

    public GameResult(float survivalTime, float bestTime, bool isNewRecord)
    {
        SurvivalTime = survivalTime;
        BestTime = bestTime;
        IsNewRecord = isNewRecord;
    }

    public override string ToString()
    {
        return $"Survival {SurvivalTime:0.00}s, Best {BestTime:0.00}s, NewRecord {IsNewRecord}";
    }
}
