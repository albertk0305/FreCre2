using System.Collections;
using UnityEngine;

/// <summary>
/// Play 씬 로드 후 실제 게임 시작까지 기다리는 공용 헬퍼.
/// 시작 카운트다운(StartCountdownController)이 있으면 Ready 동안 기다렸다가 Playing이 되면 반환하고,
/// Stretching 시작 연출이 남아 있으면 끝날 때까지 기다린다. 카운트다운이 없으면 즉시 반환한다.
/// </summary>
internal static class PlaySceneTestUtil
{
    public static IEnumerator WaitForGameStart(float timeoutSeconds = 8f)
    {
        var gm = Object.FindFirstObjectByType<GameManager>();
        if (gm == null)
        {
            yield break;
        }

        float t0 = Time.time;
        while (gm != null && gm.CurrentState == GameState.Ready && Time.time - t0 < timeoutSeconds)
        {
            yield return null;
        }

        var anim = Object.FindFirstObjectByType<PlayerAnimationController>();
        while (anim != null && anim.IsPlayingStretching && Time.time - t0 < timeoutSeconds)
        {
            yield return null;
        }
    }
}
