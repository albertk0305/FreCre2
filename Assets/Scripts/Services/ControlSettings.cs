using System;
using UnityEngine;

/// <summary>
/// 메인화면에서 선택한 PC / 모바일 조작 방식을 PlayerPrefs에 보관한다.
/// 기본 조작 방식은 PC.
/// </summary>
public static class ControlSettings
{
    public const string PlayerPrefsKey = "FreCre2_ControlMode";
    public const ControlMode DefaultMode = ControlMode.PC;

    public static void SetControlMode(ControlMode mode)
    {
        if (!Enum.IsDefined(typeof(ControlMode), mode))
        {
            mode = DefaultMode;
        }

        PlayerPrefs.SetInt(PlayerPrefsKey, (int)mode);
        PlayerPrefs.Save();
    }

    /// <summary>저장된 조작 방식을 반환한다. 저장값이 없거나 잘못되었으면 PC.</summary>
    public static ControlMode GetControlMode()
    {
        if (!PlayerPrefs.HasKey(PlayerPrefsKey))
        {
            return DefaultMode;
        }

        int stored = PlayerPrefs.GetInt(PlayerPrefsKey, (int)DefaultMode);
        return Enum.IsDefined(typeof(ControlMode), stored) ? (ControlMode)stored : DefaultMode;
    }
}
