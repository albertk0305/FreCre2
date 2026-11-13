using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 씬 전환을 한곳에서 관리한다. 씬 이름은 이 클래스에만 둔다(실제 씬 파일: Assets/Scenes/Main.unity, Play.unity).
/// Play 씬을 새로 로드하면 씬 오브젝트가 모두 새로 만들어지고 GameManager.Start()가 새 게임을 시작하므로
/// GameState/플레이어/미사일/생성 타이머/생존시간/난이도는 별도 초기화 코드 없이 초기 상태가 된다.
/// 최고기록(RecordStore, PlayerPrefs)은 씬과 무관하므로 유지된다.
/// 씬 전환은 게임 오버를 발생시키지 않으며 기록을 저장하지 않는다.
/// </summary>
public static class SceneNavigator
{
    public const string MainSceneName = "Main";
    public const string PlaySceneName = "Play";

    /// <summary>메인화면으로 이동한다.</summary>
    public static void LoadMainScene()
    {
        Load(MainSceneName);
    }

    /// <summary>게임플레이 씬으로 이동한다. 진입 시 새 게임이 자동으로 시작된다.</summary>
    public static void LoadPlayScene()
    {
        Load(PlaySceneName);
    }

    /// <summary>Play 씬을 다시 로드해 생존시간 0초부터 새 게임을 시작한다.</summary>
    public static void RestartPlayScene()
    {
        Load(PlaySceneName);
    }

    private static void Load(string sceneName)
    {
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"SceneNavigator: '{sceneName}' 씬을 로드할 수 없습니다. Build Profiles의 Scene List에 추가되어 있는지 확인하세요.");
            return;
        }
        SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
    }
}
