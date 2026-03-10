using UnityEngine;
using UnityEngine.InputSystem;


public class GameManager : MonoBehaviour
{
    public PlayerInput player1;
    public PlayerInput player2;
    public JoinUI joinUI;


    private bool p1Joined = false;
    private bool p2Joined = false;

    private void Start() {
        player1.DeactivateInput();
        player2.DeactivateInput();
    }

    private void Update() {
        if (!p1Joined) {
            TryJoinPlayer1();
        }
        else if (!p2Joined) {
            TryJoinPlayer2();
        }
    }

    void TryJoinPlayer1() {
        if (Keyboard.current.enterKey.wasPressedThisFrame) {
            AssignPlayer(player1, Keyboard.current);
            p1Joined = true;
        }

        foreach (var gamepad in Gamepad.all) {
            if (gamepad.startButton.wasPressedThisFrame) {
                AssignPlayer(player1, gamepad);
                p1Joined = true;
            }
        }
    }

    void TryJoinPlayer2() {
        if (Keyboard.current.enterKey.wasPressedThisFrame) {
            AssignPlayer(player2, Keyboard.current);
            p2Joined = true;
        }

        foreach (var gamepad in Gamepad.all) {
            if (gamepad.startButton.wasPressedThisFrame) {
                AssignPlayer(player2, gamepad);
                p2Joined = true;
            }
        }
    }

    void AssignPlayer(PlayerInput player, InputDevice device) {
        player.SwitchCurrentControlScheme(device);
        player.ActivateInput();

        if (!p1Joined) {
            joinUI.SetP1Joined();
        }
        else {
            joinUI.SetP2Joined();
        }
    }
}
