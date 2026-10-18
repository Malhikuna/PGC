using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Third-Person Character Controller 3D (Standar Industri).
/// Menggunakan New Input System dan CharacterController.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController3D : MonoBehaviour
{
    [Header("Movement Settings")]
    [Tooltip("Kecepatan jalan normal karakter.")]
    [SerializeField] private float moveSpeed = 4.0f;
    [Tooltip("Kecepatan saat berlari (sprint).")]
    [SerializeField] private float sprintSpeed = 7.0f;
    [Tooltip("Seberapa cepat karakter berputar menghadap arah pergerakan. Nilai lebih kecil = lebih cepat.")]
    [Range(0.0f, 0.3f)]
    [SerializeField] private float rotationSmoothTime = 0.12f;
    [Tooltip("Akselerasi dan deselerasi karakter (transisi antara diam, jalan, dan lari).")]
    [SerializeField] private float speedChangeRate = 10.0f;

    [Header("Jump & Gravity")]
    [Tooltip("Tinggi lompatan maksimal karakter.")]
    [SerializeField] private float jumpHeight = 1.2f;
    [Tooltip("Kekuatan gravitasi. Standar bumi adalah -9.81, tetapi di game biasanya lebih berat.")]
    [SerializeField] private float gravity = -15.0f;
    [Tooltip("Batas waktu sebelum karakter mulai jatuh setelah melewati ujung tebing (Coyote Time mini).")]
    [SerializeField] private float fallTimeout = 0.15f;

    [Header("Ground Detection")]
    [Tooltip("Apakah karakter saat ini berada di atas tanah?")]
    [SerializeField] private bool isGrounded = true;
    [Tooltip("Offset untuk posisi deteksi tanah relatif terhadap posisi transform karakter.")]
    [SerializeField] private float groundOffset = -0.14f;
    [Tooltip("Radius dari bola (sphere) yang mendeteksi tanah.")]
    [SerializeField] private float groundRadius = 0.28f;
    [Tooltip("Layer mana saja yang dihitung sebagai permukaan tanah padat.")]
    [SerializeField] private LayerMask groundLayers;

    [Header("Input Actions (New Input System)")]
    [Tooltip("Action untuk bergerak (Vector2).")]
    [SerializeField] private InputActionReference moveAction;
    [Tooltip("Action untuk melompat (Button).")]
    [SerializeField] private InputActionReference jumpAction;
    [Tooltip("Action untuk berlari (Button/Pass-through).")]
    [SerializeField] private InputActionReference sprintAction;

    [Header("References")]
    [Tooltip("Referensi ke Main Camera untuk kalkulasi arah gerakan.")]
    [SerializeField] private Transform mainCamera;

    // Komponen internal
    private CharacterController controller;

    // Variabel state untuk perhitungan
    private float currentSpeed;
    private float targetRotation = 0.0f;
    private float rotationVelocity;
    private float verticalVelocity;
    private float terminalVelocity = 53.0f;
    private float fallTimeoutDelta;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        // Otomatis mencari Main Camera jika belum diatur di Inspector
        if (mainCamera == null && Camera.main != null)
        {
            mainCamera = Camera.main.transform;
        }
    }

    private void OnEnable()
    {
        // Mengaktifkan semua input action saat script aktif
        if (moveAction != null) moveAction.action.Enable();
        if (jumpAction != null) jumpAction.action.Enable();
        if (sprintAction != null) sprintAction.action.Enable();
    }

    private void OnDisable()
    {
        // Menonaktifkan input action saat script nonaktif
        if (moveAction != null) moveAction.action.Disable();
        if (jumpAction != null) jumpAction.action.Disable();
        if (sprintAction != null) sprintAction.action.Disable();
    }

    private void Start()
    {
        fallTimeoutDelta = fallTimeout;
    }

    private void Update()
    {
        GroundedCheck();
        JumpAndGravity();
        Move();
    }

    private void GroundedCheck()
    {
        // Posisi sphere untuk mendeteksi tanah diletakkan di telapak kaki
        Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y + groundOffset, transform.position.z);
        // Cek tabrakan sphere dengan layer yang dianggap tanah
        isGrounded = Physics.CheckSphere(spherePosition, groundRadius, groundLayers, QueryTriggerInteraction.Ignore);
    }

    private void JumpAndGravity()
    {
        if (isGrounded)
        {
            // Reset timer jatuh saat menyentuh tanah
            fallTimeoutDelta = fallTimeout;

            // Mencegah gravitasi terus menumpuk saat diam di tanah
            if (verticalVelocity < 0.0f)
            {
                verticalVelocity = -2f; // Memberi gaya tekan ringan agar karakter tidak melayang di jalan menurun (slope)
            }

            // Lompat
            bool isJumping = jumpAction != null && jumpAction.action.WasPressedThisFrame();
            if (isJumping)
            {
                // Rumus fisika standar untuk melompat ke ketinggian tertentu: v = sqrt(2 * g * h)
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
        }
        else
        {
            // Pengurangan fall timeout (Coyote Time / batas jeda animasi jatuh)
            if (fallTimeoutDelta >= 0.0f)
            {
                fallTimeoutDelta -= Time.deltaTime;
            }
            
            // Mencegah input lompat tambahan saat sudah di udara
            // (kecuali Anda ingin menambahkan double jump di sini)
        }

        // Menerapkan gravitasi secara terus-menerus selama batas kecepatan jatuh (terminal velocity) belum tercapai
        if (verticalVelocity < terminalVelocity)
        {
            verticalVelocity += gravity * Time.deltaTime;
        }
    }

    private void Move()
    {
        // Mengambil input (misal: WASD, Analog Stick Kiri)
        Vector2 inputVector = moveAction != null ? moveAction.action.ReadValue<Vector2>() : Vector2.zero;
        Vector3 inputDir = new Vector3(inputVector.x, 0f, inputVector.y).normalized;

        // Cek apakah tombol lari ditekan
        bool isSprinting = sprintAction != null && sprintAction.action.IsPressed();
        float targetSpeed = isSprinting ? sprintSpeed : moveSpeed;

        // Jika tidak ada input arah, target kecepatan adalah 0
        if (inputVector == Vector2.zero) targetSpeed = 0.0f;

        // Mengambil kecepatan horizontal karakter saat ini
        float currentHorizontalSpeed = new Vector3(controller.velocity.x, 0.0f, controller.velocity.z).magnitude;
        
        // Toleransi perbedaan kecepatan sebelum mulai interpolasi (mengurangi jitter)
        float speedOffset = 0.1f;
        if (currentHorizontalSpeed < targetSpeed - speedOffset || currentHorizontalSpeed > targetSpeed + speedOffset)
        {
            // Melakukan interpolasi kecepatan secara smooth (Linear Interpolation)
            currentSpeed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed, Time.deltaTime * speedChangeRate);
            // Membulatkan nilai untuk mencegah perhitungan desimal mikroskopis (floating point imprecision)
            currentSpeed = Mathf.Round(currentSpeed * 1000f) / 1000f;
        }
        else
        {
            currentSpeed = targetSpeed;
        }

        // Kalkulasi rotasi menghadap arah gerak (Relatif terhadap kamera)
        if (inputDir != Vector3.zero && mainCamera != null)
        {
            // Mencari sudut (Atan2) berdasarkan input dan menambahkan arah hadap Y kamera
            targetRotation = Mathf.Atan2(inputDir.x, inputDir.z) * Mathf.Rad2Deg + mainCamera.eulerAngles.y;
            
            // Melakukan rotasi karakter dengan halus
            float rotation = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetRotation, ref rotationVelocity, rotationSmoothTime);
            transform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);
        }

        // Mendapatkan vektor arah maju akhir setelah rotasi kalkulasi di atas
        Vector3 targetDirection = Quaternion.Euler(0.0f, targetRotation, 0.0f) * Vector3.forward;

        // Eksekusi pergerakan ke komponen CharacterController:
        // (Pergerakan Horizontal) + (Pergerakan Vertikal / Gravitasi)
        Vector3 movement = targetDirection.normalized * (currentSpeed * Time.deltaTime) + new Vector3(0.0f, verticalVelocity, 0.0f) * Time.deltaTime;
        
        controller.Move(movement);
    }

    // Menggambar sphere deteksi tanah di Scene View Unity untuk mempermudah debugging
    private void OnDrawGizmosSelected()
    {
        Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
        Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);

        Gizmos.color = isGrounded ? transparentGreen : transparentRed;

        // Menyesuaikan posisi dengan offset
        Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y + groundOffset, transform.position.z);
        Gizmos.DrawSphere(spherePosition, groundRadius);
    }
}
