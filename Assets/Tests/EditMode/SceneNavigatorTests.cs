using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;

/// <summary>
/// 씬 이름 설정/버튼 메서드 형태 검사(EditMode). 실제 씬 전환은 SceneNavigationPlayModeTests에서 검증한다.
/// </summary>
public class SceneNavigatorTests
{
    [TestCase(SceneNavigator.MainSceneName, "Assets/Scenes/Main.unity")]
    [TestCase(SceneNavigator.PlaySceneName, "Assets/Scenes/Play.unity")]
    public void SceneNames_MatchActualSceneFiles(string sceneName, string path)
    {
        Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(path), path);
        Assert.AreEqual(sceneName, System.IO.Path.GetFileNameWithoutExtension(path));
    }

    [TestCase("Assets/Scenes/Main.unity")]
    [TestCase("Assets/Scenes/Play.unity")]
    public void Scenes_EnabledInBuildSceneList(string path)
    {
        Assert.IsTrue(EditorBuildSettings.scenes.Any(s => s.path == path && s.enabled),
            $"{path}가 Build Profiles Scene List에 활성 상태로 있어야 SceneManager.LoadScene(name)이 동작한다.");
    }

    [TestCase("OnClickPlay")]
    [TestCase("OnClickExit")]
    [TestCase("OnClickRestart")]
    public void ButtonMethods_ArePublicParameterlessVoid_ForButtonOnClick(string methodName)
    {
        MethodInfo m = typeof(SceneNavigationButtons).GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNotNull(m, methodName);
        Assert.AreEqual(typeof(void), m.ReturnType);
        Assert.AreEqual(0, m.GetParameters().Length);
    }
}
