using UnityEngine;

using UnityEngine.SceneManagement;

public class MainMenuController : MonoBehaviour {
    public void OnStart() {
        SceneManager.LoadScene("CharacterSelect");
    }

    public void OnQuit() {
        Application.Quit();
    }
}