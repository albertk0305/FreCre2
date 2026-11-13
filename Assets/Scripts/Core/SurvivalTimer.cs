using UnityEngine;

/// <summary>
/// 현재 플레이의 생존시간을 측정한다. 생존시간의 유일한 원본이다.
/// - GameManager가 StartGame()에서 ResetTimer() → StartTimer(), EndGame()에서 StopTimer()를 호출한다.
///   따라서 측정 구간은 GameState.Playing 구간과 같다.
/// - 측정 중일 때만 Time.deltaTime을 누적한다. 정지 후에는 값이 그대로 유지된다(결과/기록용).
/// - DifficultyManager(난이도), 이후 Stage의 PlayHUD/결과 처리가 ElapsedTime을 읽기만 한다.
/// Time.timeScale은 변경하지 않는다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-50)] // 같은 프레임에서 MissileSpawner(기본 0)가 최신 시간을 읽도록 먼저 갱신
public class SurvivalTimer : MonoBehaviour
{
    /// <summary>현재 생존시간(초).</summary>
    public float ElapsedTime { get; private set; }

    /// <summary>시간 측정 중인지.</summary>
    public bool IsRunning { get; private set; }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    /// <summary>측정 중이면 deltaTime만큼 시간을 누적한다. 테스트에서 직접 호출할 수 있다.</summary>
    public void Tick(float deltaTime)
    {
        if (!IsRunning || deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
        {
            return;
        }
        ElapsedTime += deltaTime;
    }

    /// <summary>측정을 시작(재개)한다. 경과시간은 바꾸지 않는다. 이미 측정 중이면 아무것도 하지 않는다.</summary>
    public void StartTimer()
    {
        IsRunning = true;
    }

    /// <summary>측정을 즉시 멈춘다. 경과시간은 유지된다.</summary>
    public void StopTimer()
    {
        IsRunning = false;
    }

    /// <summary>경과시간을 0으로 되돌린다. 측정 여부는 바꾸지 않는다.</summary>
    public void ResetTimer()
    {
        ElapsedTime = 0f;
    }
}
