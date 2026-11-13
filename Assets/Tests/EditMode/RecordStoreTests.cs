using NUnit.Framework;
using UnityEngine;

public class RecordStoreTests
{
    private BestTimeBackup backup;

    [SetUp]
    public void SetUp()
    {
        backup = BestTimeBackup.Capture();
        BestTimeBackup.ClearForTest();
    }

    [TearDown]
    public void TearDown()
    {
        backup.Restore();
    }

    [Test]
    public void Key_IsSpecValue()
    {
        Assert.AreEqual("FreCre2_BestTime", RecordStore.BestTimeKey);
    }

    [Test]
    public void InitialBestTime_IsZero()
    {
        Assert.AreEqual(0f, RecordStore.GetBestTime());
    }

    [Test]
    public void Sequence_30_20_50()
    {
        Assert.IsTrue(RecordStore.TryUpdateRecord(30f), "첫 기록 30초는 신기록");
        Assert.AreEqual(30f, RecordStore.GetBestTime());

        Assert.IsFalse(RecordStore.TryUpdateRecord(20f), "20초는 기존보다 낮음");
        Assert.AreEqual(30f, RecordStore.GetBestTime());

        Assert.IsTrue(RecordStore.TryUpdateRecord(50f));
        Assert.AreEqual(50f, RecordStore.GetBestTime());
    }

    [Test]
    public void EqualRecord_IsNotNewRecord()
    {
        Assert.IsTrue(RecordStore.TryUpdateRecord(42.5f));
        Assert.IsFalse(RecordStore.TryUpdateRecord(42.5f));
        Assert.AreEqual(42.5f, RecordStore.GetBestTime());
    }

    [Test]
    public void SavedRecord_ReadsBackSameValue_FromPlayerPrefs()
    {
        RecordStore.TryUpdateRecord(73.125f);
        Assert.AreEqual(73.125f, RecordStore.GetBestTime());
        Assert.AreEqual(73.125f, RecordStore.GetBestTime(), "여러 번 읽어도 같음");
        Assert.IsTrue(PlayerPrefs.HasKey(RecordStore.BestTimeKey));
        Assert.AreEqual(73.125f, PlayerPrefs.GetFloat(RecordStore.BestTimeKey), "같은 키로 저장됨");
    }

    [Test]
    public void ZeroRecord_IsNotNewRecord()
    {
        Assert.IsFalse(RecordStore.TryUpdateRecord(0f));
        Assert.IsFalse(PlayerPrefs.HasKey(RecordStore.BestTimeKey), "저장하지 않음");
    }

    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void InvalidRecord_Rejected(float value)
    {
        RecordStore.TryUpdateRecord(10f);
        Assert.IsFalse(RecordStore.TryUpdateRecord(value));
        Assert.AreEqual(10f, RecordStore.GetBestTime());
    }

    [Test]
    public void CorruptedStoredValue_ReadsAsZero()
    {
        PlayerPrefs.SetFloat(RecordStore.BestTimeKey, -5f);
        Assert.AreEqual(0f, RecordStore.GetBestTime());
        Assert.IsTrue(RecordStore.TryUpdateRecord(1f));
        Assert.AreEqual(1f, RecordStore.GetBestTime());
    }
}
