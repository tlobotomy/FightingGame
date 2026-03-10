using System.Runtime.ExceptionServices;
using UnityEngine;
using UnityEngine.InputSystem;

public class FighterController : MonoBehaviour {
    //MOVEMENT PARAMETERS
    public float walkSpeed = 5f;
    public float backWalkSpeed = 2f;
    public float jumpForce = 10f;
    public float gravity = -30f;

    float verticalVelocity;
    Vector2 moveInput;


    public Transform opponent;
    bool facingRight = true;
    bool isGrounded = true;


    public void OnMove(InputValue value) {
        moveInput = value.Get<Vector2>();
    }

    /*public void OnJump(InputValue value) {
        if(value.isPressed && isGrounded) {
            StartJump();
        }
    }*/

    //Face the opponent
    void UpdateFacing() {
        if (opponent.position.x > transform.position.x)
            facingRight = true;
        else
            facingRight = false;

        transform.localScale = new Vector3(facingRight ? 1 : -1, 1, 1); //what's going on here?
    }

    float GetForwardInput() { //means that +1 is forward and -1 is back no matter what side youre on
        return facingRight ? moveInput.x : -moveInput.x;
    }
    //STATES
    public enum FighterState { //Different possible characterstates
        Idle,
        Crouch,
        WalkForward,
        WalkBackward,
        Jump,
        Attack,
        Hitstun,
        Block,
        Knockdown
    }

    public FighterState currentState;

    //Jumping stuff so it's not detected every frame
    public float fallMultiplier = 3f;
    public float airSpeed = 10f;
    bool jumpPressedLastFrame;
    void HandleJumpInput() {
        bool jumpPressed = moveInput.y > 0.5f;
        if (jumpPressed && !jumpPressedLastFrame && isGrounded) {
            StartJump();
        }
        jumpPressedLastFrame = jumpPressed;
    }

    float jumpDirection;
    void Idle() {
        float forwardInput = GetForwardInput();
        if (moveInput.y < -0.5f) {
            currentState = FighterState.Crouch;
        }
        if (forwardInput > 0.1f) {
            currentState = FighterState.WalkForward;
        }
        else if (forwardInput < -0.1f) {
            currentState = FighterState.WalkBackward;
        }
    }
    void Crouch() {
        //crouch code

        //transition
        if (moveInput.y > -0.2f) {
            currentState = FighterState.Idle;
        }

        //crouching attacks go here
    }

    void Attack() {

    }
    void Hitstun() {

    }
    void Block() {

    }
    void Knockdown() {

    }
    void WalkForward() { //Walks towards opponent, not necessarily the direction right
        float direction = facingRight ? 1 : -1;
        transform.position += new Vector3(direction * walkSpeed * Time.deltaTime, 0, 0);
        if (GetForwardInput() <= 0) {
            currentState = FighterState.Idle;
        }
        if (moveInput.y < -0.5f) {
            currentState = FighterState.Crouch;
        }
    }

    void WalkBackward() {
        float direction = facingRight ? -1 : 1;
        transform.position += new Vector3(direction * backWalkSpeed * Time.deltaTime, 0, 0);
        if (GetForwardInput() >= 0) {
            currentState = FighterState.Idle;
        }
        if (moveInput.y < -0.5f) {
            currentState = FighterState.Crouch;
        }
    }

    void StartJump() {
        verticalVelocity = jumpForce;
        isGrounded = false;
        jumpDirection = moveInput.x;
        currentState = FighterState.Jump;
    }
    void Jump() {
        float appliedGravity = gravity;

        if (verticalVelocity < 0) {
            appliedGravity *= fallMultiplier;
        }
        verticalVelocity += appliedGravity * Time.deltaTime;
        transform.position += new Vector3(jumpDirection * airSpeed * Time.deltaTime, verticalVelocity * Time.deltaTime, 0);

        if (transform.position.y <= groundY) {
            Land();
        }
    }

    float groundY;
    void Land() {
        Vector3 pos = transform.position;
        pos.y = groundY;

        transform.position = pos;

        verticalVelocity = 0;
        isGrounded = true;
        currentState = FighterState.Idle;
    }

        //INPUTS

        /*public InputActionAsset InputActions; //Stores input actions


        private void OnEnable() { //Enables player input map
            InputActions.FindActionMap("Player").Enable();
        }

        private void OnDisable() { //Disables player input map
            InputActions.FindActionMap("Player").Disable();
        }


        private void Awake() {
            m_moveAction = InputSystem.actions.FindAction("Move");

        }*/

    void HandleState() {
        switch (currentState) {
            case FighterState.Idle:
                Idle();
                break;
            case FighterState.WalkForward:
                WalkForward();
                break;
            case FighterState.WalkBackward:
                WalkBackward();
                break;
            case FighterState.Jump:
                Jump();
                break;
            case FighterState.Crouch:
                Crouch();
                break;
            case FighterState.Attack:
                Attack();
                break;
            case FighterState.Block:
                Block();
                break;
            case FighterState.Hitstun:
                Hitstun();
                break;
            case FighterState.Knockdown:
                Knockdown();
                break;
        }
    }
    void Start() {
        groundY = transform.position.y;
    }
    void FixedUpdate() {
        UpdateFacing();
        HandleJumpInput();
        HandleState();
        
    }
}
