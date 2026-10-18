
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

//This script requires you to have setup your animator with 3 parameters, "InputMagnitude", "InputX", "InputZ"
//With a blend tree to control the inputmagnitude and allow blending between animations.
[RequireComponent(typeof(CharacterController))]
public class MovementInput : MonoBehaviour {

    public float Velocity;
    [Space]

	public float InputX;
	public float InputZ;
	public Vector3 desiredMoveDirection;
	public bool blockRotationPlayer;
	public float desiredRotationSpeed = 0.1f;
	public Animator anim;
	public float Speed;
	public float allowPlayerRotation = 0.1f;
	public Camera cam;
	public CharacterController controller;
	public bool isGrounded;

    [Header("Animation Smoothing")]
    [Range(0, 1f)]
    public float HorizontalAnimSmoothTime = 0.2f;
    [Range(0, 1f)]
    public float VerticalAnimTime = 0.2f;
    [Range(0,1f)]
    public float StartAnimTime = 0.3f;
    [Range(0, 1f)]
    public float StopAnimTime = 0.15f;

    public float verticalVel;
    private Vector3 moveVector;
    private Vector3 horizontalMove;
    private float currentMoveSpeedMultiplier = 0.5f;
    private float rotationVelocity;
    private ThirdPersonCameraAAA aaaCamera;

	// Use this for initialization
	void Start () {
		anim = this.GetComponent<Animator> ();
        if (anim != null) anim.applyRootMotion = false;
		cam = Camera.main;
		controller = this.GetComponent<CharacterController> ();

        SetupAAACamera();
	}

    void SetupAAACamera()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        // Otomatis pasang dan aktifkan ThirdPersonCameraAAA di Main Camera
        aaaCamera = cam.GetComponent<ThirdPersonCameraAAA>();
        if (aaaCamera == null)
        {
            aaaCamera = cam.gameObject.AddComponent<ThirdPersonCameraAAA>();
        }
        aaaCamera.target = this.transform;
        aaaCamera.DisableConflictingCinemachine();

        // Otomatis pasang StarSparrowControllerAAA & Lighting Rig pada prefab StarSparrow21 di scene
        GameObject shipObj = GameObject.Find("StarSparrow21");
        if (shipObj == null)
        {
            // Fallback jika nama memiliki nomor atau akhiran seperti StarSparrow21 (1)
            Transform[] allTransforms = FindObjectsByType<Transform>(FindObjectsSortMode.None);
            for (int i = 0; i < allTransforms.Length; i++)
            {
                if (allTransforms[i] != null && allTransforms[i].parent == null && allTransforms[i].name.StartsWith("StarSparrow"))
                {
                    shipObj = allTransforms[i].gameObject;
                    break;
                }
            }
        }

        if (shipObj != null)
        {
            StarSparrowControllerAAA shipCtrl = shipObj.GetComponent<StarSparrowControllerAAA>();
            if (shipCtrl == null)
            {
                shipCtrl = shipObj.AddComponent<StarSparrowControllerAAA>();
            }
            shipCtrl.characterPlayer = this;
            shipCtrl.aaaCamera = aaaCamera;
            shipCtrl.EnsureShipLightingRig();
        }
    }
	
    private bool wasGrounded;
    private float lastAirVelocity;

	float GetHorizontalInput()
	{
		float val = 0;
		if (Keyboard.current != null)
		{
			if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) val -= 1;
			if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) val += 1;
		}
		if (Gamepad.current != null)
		{
			val += Gamepad.current.leftStick.x.ReadValue();
		}
		return Mathf.Clamp(val, -1f, 1f);
	}

	float GetVerticalInput()
	{
		float val = 0;
		if (Keyboard.current != null)
		{
			if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) val -= 1;
			if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) val += 1;
		}
		if (Gamepad.current != null)
		{
			val += Gamepad.current.leftStick.y.ReadValue();
		}
		return Mathf.Clamp(val, -1f, 1f);
	}

	// Update is called once per frame
	void Update () {
        horizontalMove = Vector3.zero;
		InputMagnitude ();

        isGrounded = controller.isGrounded;

        // Cek momen pendaratan setelah jatuh
        if (isGrounded && !wasGrounded)
        {
            // Efek pantulan halus pada kamera saat mendarat
            if (aaaCamera != null && lastAirVelocity < -3f)
            {
                aaaCamera.TriggerLandingImpact(Mathf.Abs(lastAirVelocity) * 0.08f);
            }

            // Hanya putar animasi "Falling To Landing" jika jatuh dari tempat tinggi 
            if (lastAirVelocity < -10f) 
            {
                anim.SetTrigger("landing"); 
            }
        }

        if (!isGrounded)
        {
            lastAirVelocity = verticalVel; // Catat kecepatan jatuh sebelum mendarat
        }

        if (isGrounded)
        {
            // Reset velocity saat menyentuh tanah agar tidak terus minus
            if (verticalVel < 0) verticalVel = -2f; 
            
            // Logika Lompat (Tekan Spasi di Keyboard atau Tombol 'A' di Xbox)
            bool jumpPressed = (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) || 
                               (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);

            if (jumpPressed)
            {
                verticalVel = 25f; // Kekuatan lompat selaras dengan durasi animasi Jump
                anim.SetTrigger("jump"); // Trigger animasi lompat
            }

            // --- TAMBAHAN UNTUK ANIMASI KEMENANGAN ---
            // Tombol V (Keyboard) atau Tombol 'X' (Xbox Controller)
            bool victory1Pressed = (Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame) || 
                                   (Gamepad.current != null && Gamepad.current.buttonWest.wasPressedThisFrame);
            if (victory1Pressed)
            {
                anim.SetTrigger("victory1"); // Trigger untuk Fist Pump
            }

            // Tombol B (Keyboard) atau Tombol 'Y' (Xbox Controller)
            bool victory2Pressed = (Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame) || 
                                   (Gamepad.current != null && Gamepad.current.buttonNorth.wasPressedThisFrame);
            if (victory2Pressed)
            {
                anim.SetTrigger("victory2"); // Trigger untuk Victory Idle
            }
            // ------------------------------------------
        }
        else
        {
            // Gravitasi saat di udara (Frame-rate independent, durasi ~0.77s sesuai animasi)
            verticalVel -= 65f * Time.deltaTime; 
        }

        // Gabungkan gerakan horizontal dan vertikal dalam 1 kali panggilan controller.Move agar bebas jitter
        moveVector = horizontalMove + new Vector3(0, verticalVel * 0.2f, 0);
        controller.Move(moveVector * Time.deltaTime);

        wasGrounded = isGrounded; // Simpan status mendarat untuk frame berikutnya
    }

    void PlayerMoveAndRotation() {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

		var forward = cam.transform.forward;
		var right = cam.transform.right;

		forward.y = 0f;
		right.y = 0f;

		forward.Normalize ();
		right.Normalize ();

        Vector2 clampedInput = Vector2.ClampMagnitude(new Vector2(InputX, InputZ), 1f);
		desiredMoveDirection = forward * clampedInput.y + right * clampedInput.x;

		if (blockRotationPlayer == false && desiredMoveDirection.sqrMagnitude > 0.001f) {
            // Rotasi karakter halus dan konsisten di semua FPS (Frame-rate independent)
            float targetAngle = Mathf.Atan2(desiredMoveDirection.x, desiredMoveDirection.z) * Mathf.Rad2Deg;
            float smoothTime = Mathf.Clamp(0.015f / Mathf.Max(0.01f, desiredRotationSpeed), 0.04f, 0.25f);
            float smoothedAngle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref rotationVelocity, smoothTime);
            transform.rotation = Quaternion.Euler(0f, smoothedAngle, 0f);

            // Sesuaikan kecepatan fisik dengan status Walk (0.55x) vs Sprint (1.0x) secara mulus
            float targetMultiplier = IsSprinting() ? 1.0f : 0.55f;
            currentMoveSpeedMultiplier = Mathf.Lerp(currentMoveSpeedMultiplier, targetMultiplier, 1f - Mathf.Exp(-10f * Time.deltaTime));

            horizontalMove = desiredMoveDirection * (Velocity * currentMoveSpeedMultiplier);
		}
	}

    public void LookAt(Vector3 pos)
    {
        float rotFactor = 1f - Mathf.Exp(-12f * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(pos), rotFactor);
    }

    public void RotateToCamera(Transform t)
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        var forward = cam.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude > 0.001f)
        {
            desiredMoveDirection = forward.normalized;
            float rotFactor = 1f - Mathf.Exp(-12f * Time.deltaTime);
            t.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(desiredMoveDirection), rotFactor);
        }
    }

	bool IsSprinting()
	{
		// Cek Keyboard Shift
		if (Keyboard.current != null && Keyboard.current.shiftKey.isPressed) return true;
		
		// Cek Tombol RT di Controller (Gamepad.rightTrigger)
		if (Gamepad.current != null && Gamepad.current.rightTrigger.isPressed) return true;
		
		return false;
	}

    public bool IsSprintingActive()
    {
        return IsSprinting() && Speed > 0.55f;
    }

	void InputMagnitude() {
		//Calculate Input Vectors
		InputX = GetHorizontalInput();
		InputZ = GetVerticalInput();

        Vector2 clampedInput = Vector2.ClampMagnitude(new Vector2(InputX, InputZ), 1f);

		//Calculate the Input Magnitude
		Speed = clampedInput.magnitude;

        // Batasi kecepatan jika pemain TIDAK menekan tombol sprint (Shift atau RT)
        if (!IsSprinting()) 
        {
            Speed = Mathf.Clamp(Speed, 0f, 0.5f);
        }

        //Physically move player
		if (Speed > allowPlayerRotation) {
			anim.SetFloat ("Blend", Speed, StartAnimTime, Time.deltaTime);
			PlayerMoveAndRotation ();
		} else if (Speed < allowPlayerRotation) {
			anim.SetFloat ("Blend", 0f, StopAnimTime, Time.deltaTime);
		}
	}
}
