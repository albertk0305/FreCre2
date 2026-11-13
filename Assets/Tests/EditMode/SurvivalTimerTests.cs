using NUnit.Framework;
using UnityEngine;

public class SurvivalTimerTests
{
    private GameObject go;
    private SurvivalTimer timer;

    [SetUp]
    public void SetUp()
    {
        go = new GameObject("SurvivalTimer");
        timer = go.AddComponent<SurvivalTimer>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(go);
    }

    [Test]
    public void Initial_ZeroAndNotRunning()
    {
        Assert.AreEqual(0f, timer.ElapsedTime);
        Assert.IsFalse(timer.IsRunning);
    }

    [Test]
    public void Tick_BeforeStart_DoesNotAccumulate()
    {
        timer.Tick(1f);
        Assert.AreEqual(0f, timer.ElapsedTime);
    }

    [Test]
    public void StartTimer_AccumulatesDeltaTime()
    {
        timer.StartTimer();
        Assert.IsTrue(timer.IsRunning);
        for (int i = 0; i < 50; i++) timer.Tick(0.02f);
        Assert.AreEqual(1.0f, timer.ElapsedTime, 1e-4f);
    }

    [Test]
    public void StopTimer_FreezesElapsedTime()
    {
        timer.StartTimer();
        timer.Tick(2.5f);
        timer.StopTimer();
        Assert.IsFalse(timer.IsRunning);

        for (int i = 0; i < 100; i++) timer.Tick(0.1f);
        Assert.AreEqual(2.5f, timer.ElapsedTime, 1e-6f, "정지 후 시간이 증가하지 않음");
    }

    [Test]
    public void ResetTimer_SetsZero_KeepsRunningState()
    {
        timer.StartTimer();
        timer.Tick(3f);
        timer.ResetTimer();
        Assert.AreEqual(0f, timer.ElapsedTime);
        Assert.IsTrue(timer.IsRunning);

        timer.StopTimer();
        timer.Tick(1f);
        timer.ResetTimer();
        Assert.AreEqual(0f, timer.ElapsedTime);
        Assert.IsFalse(timer.IsRunning);
    }

    [Test]
    public void StartTimer_Twice_DoesNotResetOrDoubleCount()
    {
        timer.StartTimer();
        timer.Tick(1f);
        timer.StartTimer();
        timer.Tick(1f);
        Assert.AreEqual(2f, timer.ElapsedTime, 1e-6f);
    }

    [Test]
    public void Restart_ResetThenStart_MeasuresFromZero()
    {
        timer.StartTimer();
        timer.Tick(7f);
        timer.StopTimer();

        timer.ResetTimer();
        timer.StartTimer();
        Assert.AreEqual(0f, timer.ElapsedTime);
        timer.Tick(0.5f);
        Assert.AreEqual(0.5f, timer.ElapsedTime, 1e-6f);
    }

    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void Tick_InvalidDelta_Ignored(float delta)
    {
        timer.StartTimer();
        timer.Tick(1f);
        timer.Tick(delta);
        Assert.AreEqual(1f, timer.ElapsedTime, 1e-6f);
    }
}
