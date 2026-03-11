using System.Runtime.ExceptionServices;
using UnityEngine;
using UnityEngine.InputSystem;


public class FighterController : MonoBehaviour {
    //PARAMETERS
    public int health = 100;
    public int supermeter = 0;
    public float walkSpeed = 5f;
    public float backWalkSpeed = 2f;
    public float jumpForce = 10f; //vertical force of your jump
    public float gravity = -30f;
    public float fallMultiplier = 3f; //fall faster after apex
    public float airSpeed = 10f; //horizontal jump force
    bool jumpPressedLastFrame;
    float verticalVelocity;
    float jumpDirection;
    float groundY;
    Vector2 moveInput;
    Animator animator;
    SpriteRenderer sprite;

    //FACING OPPONENT
    public Transform opponent;
    bool facingRight = true;
    bool isGrounded = true;
    void UpdateFacing() {
        if (opponent.position.x > transform.position.x)
            facingRight = true;
        else
            facingRight = false;

        transform.localScale = new Vector3(facingRight ? 1 : -1, 1, 1); //It evaluates if facing right, sets to 1 or -1 based on the bool. the other parameters are the y,z cord
        if (facingRight == true && isGrounded == true) {
            sprite.flipX = true;
        }
        if (facingRight == false && isGrounded == true) {
            sprite.flipX = true;
        }
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


    //GAME FUNCTIONS
    void HandleState() { //Changes the gamestate, coordinates state to function
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

    //Data gathering functions
    public void OnMove(InputValue value) { //Anytime player moves we store the x,y coordinates
        moveInput = value.Get<Vector2>();

    }

    float GetForwardInput() { //means that +1 is forward and -1 is back no matter what side youre on
        return facingRight ? moveInput.x : -moveInput.x;
    }


    //Stationary functions
    void Idle() {
        float forwardInput = GetForwardInput();
        animator.Play("idle");
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

    //Walking functions
    void WalkForward() { //Walks towards opponent, not necessarily the direction right
        float direction = facingRight ? 1 : -1;
        transform.position += new Vector3(direction * walkSpeed * Time.deltaTime, 0, 0);
        animator.Play("walkf");
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

    //Jumping functions
    void HandleJumpInput() {
        bool jumpPressed = moveInput.y > 0.5f;
        if (jumpPressed && !jumpPressedLastFrame && isGrounded) {
            StartJump();
        }
        jumpPressedLastFrame = jumpPressed;
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
    void Land() {
        Vector3 pos = transform.position;
        pos.y = groundY;

        transform.position = pos; //Do I need this line?

        verticalVelocity = 0;
        isGrounded = true;
        currentState = FighterState.Idle;
    }

    void Attack() {

    }
    void Hitstun() {

    }
    void Block() {

    }
    void Knockdown() {

    }


    //Game Functions

    private void Awake() {
        if (animator == null) {
            animator = GetComponentInChildren<Animator>();
            sprite = GetComponentInChildren<SpriteRenderer>();
        }
    }
    void Start() {
        groundY = -34;
    }
    void FixedUpdate() {
        UpdateFacing();
        HandleJumpInput();
        HandleState();
        
    }
    void LateUpdate() { //Clamps x movement (boundaries)
        Vector3 pos = transform.position;
        pos.x = Mathf.Clamp(pos.x, -180f, 180f); // stage width
        transform.position = pos;
    }
}
