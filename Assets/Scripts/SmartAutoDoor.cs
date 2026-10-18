using UnityEngine;

/// <summary>
/// Script Pintu Otomatis (Smart Environment)
/// Pasang script ini di objek induk pintu (misal: Horizontal_Doors_Kit).
/// Jangan lupa tambahkan Box Collider dengan "Is Trigger" tercentang di objek yang sama.
/// </summary>
public class SmartAutoDoor : MonoBehaviour
{
    [Header("Referensi Pintu (Drag & Drop dari Hierarchy)")]
    public Transform leftDoor;
    public Transform rightDoor;

    [Header("Pengaturan Geser")]
    [Tooltip("Arah dan jarak bergesernya pintu kiri. Ubah angka X, Y, atau Z jika arahnya salah.")]
    public Vector3 leftDoorOffset = new Vector3(-1.5f, 0, 0);
    [Tooltip("Arah dan jarak bergesernya pintu kanan.")]
    public Vector3 rightDoorOffset = new Vector3(1.5f, 0, 0);
    
    [Tooltip("Seberapa cepat pintu membuka dan menutup.")]
    public float moveSpeed = 5f;

    // Posisi awal (tertutup) dan target (terbuka)
    private Vector3 leftClosedPos;
    private Vector3 rightClosedPos;
    
    private Vector3 leftOpenPos;
    private Vector3 rightOpenPos;
    
    private bool isPlayerNear = false;

    void Start()
    {
        // Menyimpan posisi awal pintu saat game dimulai sebagai posisi "Tertutup"
        if (leftDoor != null)
        {
            leftClosedPos = leftDoor.localPosition;
            leftOpenPos = leftClosedPos + leftDoorOffset;
        }

        if (rightDoor != null)
        {
            rightClosedPos = rightDoor.localPosition;
            rightOpenPos = rightClosedPos + rightDoorOffset;
        }
    }

    void Update()
    {
        // Mengerakkan pintu kiri secara mulus (Lerp) menuju target
        if (leftDoor != null)
        {
            Vector3 targetPos = isPlayerNear ? leftOpenPos : leftClosedPos;
            leftDoor.localPosition = Vector3.Lerp(leftDoor.localPosition, targetPos, Time.deltaTime * moveSpeed);
        }

        // Mengerakkan pintu kanan secara mulus (Lerp) menuju target
        if (rightDoor != null)
        {
            Vector3 targetPos = isPlayerNear ? rightOpenPos : rightClosedPos;
            rightDoor.localPosition = Vector3.Lerp(rightDoor.localPosition, targetPos, Time.deltaTime * moveSpeed);
        }
    }

    // Fungsi ini dipanggil otomatis oleh Unity saat ada objek menyentuh Box Collider (Trigger)
    private void OnTriggerEnter(Collider other)
    {
        // Pastikan yang menyentuh memiliki Tag "Player" (yaitu Jammo)
        if (other.CompareTag("Player"))
        {
            isPlayerNear = true;
        }
    }

    // Fungsi ini dipanggil saat objek menjauh/keluar dari Box Collider
    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerNear = false;
        }
    }
}
