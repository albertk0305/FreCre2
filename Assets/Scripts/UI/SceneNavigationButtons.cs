using UnityEngine;

/// <summary>
/// UI Button의 OnClick에 연결하는 씬 전환 요청 스크립트. 게임 로직(게임 오버, 기록 저장)은 처리하지 않는다.
/// - Main 씬 PlayButton → OnClickPlay
/// - Play 씬 ExitButton → OnClickExit, RestartButton → OnClickRestart
/// 진행 중에 Exit/Restart를 눌러도 GameManager.EndGame()을 호출하지 않으므로 현재 생존시간은 기록되지 않는다.
/// </summary>
[DisallowMultipleComponent]
public class SceneNavigationButtons : MonoBehaviour
{
    public void OnClickPlay()
    {
        SceneNavigator.LoadPlayScene();
    }

    public void OnClickExit()
    {
        SceneNavigator.LoadMainScene();
    }

    public void OnClickRestart()
    {
        SceneNavigator.RestartPlayScene();
    }
}
