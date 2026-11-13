using UnityEngine;

/// <summary>
/// 테스트가 에디터의 실제 최고기록(PlayerPrefs "FreCre2_BestTime")을 오염시키지 않도록 백업/복원한다.
/// SetUp에서 Capture() 후 ClearForTest(), TearDown에서 Restore()를 호출한다.
/// </summary>
internal sealed class BestTimeBackup
{
    private readonly bool hadValue;
    private readonly float value;

    private BestTimeBackup(bool hadValue, float value)
    {
        this.hadValue = hadValue;
        this.value = value;
    }

    public static BestTimeBackup Capture()
    {
        bool had = PlayerPrefs.HasKey(RecordStore.BestTimeKey);
        return new BestTimeBackup(had, had ? PlayerPrefs.GetFloat(RecordStore.BestTimeKey) : 0f);
    }

    /// <summary>테스트 시작 상태: 저장된 기록 없음.</summary>
    public static void ClearForTest()
    {
        PlayerPrefs.DeleteKey(RecordStore.BestTimeKey);
    }

    public void Restore()
    {
        if (hadValue)
        {
            PlayerPrefs.SetFloat(RecordStore.BestTimeKey, value);
        }
        else
        {
            PlayerPrefs.DeleteKey(RecordStore.BestTimeKey);
        }
        PlayerPrefs.Save();
    }
}
