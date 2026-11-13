using UnityEngine;

/// <summary>
/// 최고 생존기록(초)을 PlayerPrefs에 저장하고 불러온다. 프로젝트에서 기록용 PlayerPrefs에 접근하는 유일한 곳이다.
/// 씬 전환/재시작/앱 재실행과 무관하게 유지되며, GameManager가 미사일 피격 게임 오버 때만 TryUpdateRecord()를 호출한다.
/// </summary>
public static class RecordStore
{
    public const string BestTimeKey = "FreCre2_BestTime";

    /// <summary>저장된 최고 생존기록(초). 없거나 잘못된 값이면 0.</summary>
    public static float GetBestTime()
    {
        float stored = PlayerPrefs.GetFloat(BestTimeKey, 0f);
        return IsValidTime(stored) ? stored : 0f;
    }

    /// <summary>
    /// survivalTime이 기존 최고기록을 초과하면 저장(PlayerPrefs.Save 포함)하고 true를 반환한다.
    /// 기존 기록 이하(같은 값 포함)이거나 잘못된 값(NaN/음수/무한대)이면 저장하지 않고 false.
    /// </summary>
    public static bool TryUpdateRecord(float survivalTime)
    {
        if (!IsValidTime(survivalTime) || survivalTime <= GetBestTime())
        {
            return false;
        }

        PlayerPrefs.SetFloat(BestTimeKey, survivalTime);
        PlayerPrefs.Save();
        return true;
    }

    private static bool IsValidTime(float time)
    {
        return !float.IsNaN(time) && !float.IsInfinity(time) && time >= 0f;
    }
}
