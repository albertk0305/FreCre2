/// <summary>
/// 생존시간/최고기록을 화면에 표시할 문자열로 바꾼다. 형식: MM:SS.ss (예: 01:05.42).
/// MainMenuUI, PlayHUD, GameOverUI가 같은 형식을 쓰도록 한곳에 둔다. 값은 내림 처리해 59.999초가 60.00으로 보이지 않는다.
/// </summary>
public static class TimeFormatter
{
    /// <summary>초 단위 시간을 MM:SS.ss로 변환한다. 음수/NaN/무한대는 00:00.00.</summary>
    public static string Format(float seconds)
    {
        int totalCentiseconds = ToCentiseconds(seconds);
        int minutes = totalCentiseconds / 6000;
        int secs = totalCentiseconds / 100 % 60;
        int centis = totalCentiseconds % 100;
        return $"{minutes:00}:{secs:00}.{centis:00}";
    }

    /// <summary>
    /// 상대 허용 오차 2^-22(≈2.4e-7, float 약 2 ulp). 소수 값을 float로 저장할 때의 반올림 오차(최대 2^-24 ≈ 6e-8)는 덮고,
    /// float로 구분 가능한 실제 차이(예: 59.999초, 1.009999초)는 그대로 내림한다.
    /// </summary>
    private const double RelativeTolerance = 1.0 / (1 << 22);

    /// <summary>
    /// 표시 단위(1/100초)로 내림한 값. 표시 갱신이 필요한지 비교할 때 사용한다.
    /// 65.42f처럼 정확한 1/100초 값이 float로 65.4199981…로 저장되어 한 단계 아래로 내려가는 것을 막기 위해,
    /// 다음 1/100초 경계와의 차이가 float 표현 오차 이내이면 그 경계 값으로 본다. 그 밖에는 일반 내림(59.999 → 59.99).
    /// </summary>
    public static int ToCentiseconds(float seconds)
    {
        if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0f)
        {
            return 0;
        }

        double centis = (double)seconds * 100d;
        double nearest = System.Math.Round(centis);
        double snapped = System.Math.Abs(centis - nearest) <= centis * RelativeTolerance ? nearest : System.Math.Floor(centis);
        return snapped >= int.MaxValue ? int.MaxValue : (int)snapped;
    }
}
