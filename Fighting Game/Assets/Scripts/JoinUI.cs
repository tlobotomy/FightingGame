using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

public class JoinUI : MonoBehaviour
{
    public TextMeshProUGUI p1Text;
    public TextMeshProUGUI p2Text;

    public GameObject joinPanel1;
    public GameObject joinPanel2;
    private bool p1Joined;
    private bool p2Joined;

    public void SetP1Joined() {
        p1Joined = true;
        p1Text.text = "PLAYER 1: READY";
        CheckBothJoined();
    }
    public void SetP2Joined() {
        p2Joined = true;
        p2Text.text = "PLAYER 2 READY";
        CheckBothJoined();
    }

    void CheckBothJoined() {
        if (p1Joined) {
            joinPanel1.SetActive(false);
        }
        if (p2Joined) {
            joinPanel2.SetActive(false);
        }
    }

}
