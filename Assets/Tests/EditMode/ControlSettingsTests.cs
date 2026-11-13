using NUnit.Framework;
using UnityEngine;

public class ControlSettingsTests
{
    private bool hadValue;
    private int savedValue;

    [SetUp]
    public void SetUp()
    {
        // 에디터의 실제 PlayerPrefs를 건드리므로 기존 값을 백업한다.
        hadValue = PlayerPrefs.HasKey(ControlSettings.PlayerPrefsKey);
        savedValue = PlayerPrefs.GetInt(ControlSettings.PlayerPrefsKey, 0);
        PlayerPrefs.DeleteKey(ControlSettings.PlayerPrefsKey);
    }

    [TearDown]
    public void TearDown()
    {
        if (hadValue) PlayerPrefs.SetInt(ControlSettings.PlayerPrefsKey, savedValue);
        else PlayerPrefs.DeleteKey(ControlSettings.PlayerPrefsKey);
        PlayerPrefs.Save();
    }

    [Test]
    public void Key_IsFreCre2ControlMode()
    {
        Assert.AreEqual("FreCre2_ControlMode", ControlSettings.PlayerPrefsKey);
    }

    [Test]
    public void Default_IsPC()
    {
        Assert.AreEqual(ControlMode.PC, ControlSettings.GetControlMode());
    }

    [Test]
    public void SetThenGet_RoundTrips()
    {
        ControlSettings.SetControlMode(ControlMode.Mobile);
        Assert.AreEqual(ControlMode.Mobile, ControlSettings.GetControlMode());
        ControlSettings.SetControlMode(ControlMode.PC);
        Assert.AreEqual(ControlMode.PC, ControlSettings.GetControlMode());
    }

    [Test]
    public void InvalidStoredValue_FallsBackToPC()
    {
        PlayerPrefs.SetInt(ControlSettings.PlayerPrefsKey, 99);
        Assert.AreEqual(ControlMode.PC, ControlSettings.GetControlMode());
    }
}
