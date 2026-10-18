using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class CharacterSkinController : MonoBehaviour
{
    Animator animator;
    Renderer[] characterMaterials;

    public Texture2D[] albedoList;
    [ColorUsage(true,true)]
    public Color[] eyeColors;
    public enum EyePosition { normal, happy, angry, dead}
    public EyePosition eyeState;

    // Start is called before the first frame update
    void Start()
    {
        animator = GetComponent<Animator>();
        characterMaterials = GetComponentsInChildren<Renderer>();
        
    }

    private int currentCycleState = 0;

    // Update is called once per frame
    void Update()
    {
        if (Keyboard.current != null)
        {
            if (Keyboard.current.digit1Key.wasPressedThisFrame) SetState(0);
            if (Keyboard.current.digit2Key.wasPressedThisFrame) SetState(1);
            if (Keyboard.current.digit3Key.wasPressedThisFrame) SetState(2);
            if (Keyboard.current.digit4Key.wasPressedThisFrame) SetState(3);
        }

        // Siklus bergantian menggunakan tombol B di Xbox Controller (buttonEast)
        // atau tombol C di keyboard 
        bool cyclePressed = (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame) || 
                            (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame);
        
        if (cyclePressed)
        {
            currentCycleState++;
            if (currentCycleState > 3) currentCycleState = 0; // Kembali ke awal jika sudah maksimal
            
            SetState(currentCycleState);
        }
    }

    // Fungsi bantuan agar kode lebih rapi
    void SetState(int stateIndex)
    {
        currentCycleState = stateIndex;
        switch (stateIndex)
        {
            case 0:
                ChangeEyeOffset(EyePosition.normal);
                ChangeAnimatorIdle("normal");
                break;
            case 1:
                ChangeEyeOffset(EyePosition.angry);
                ChangeAnimatorIdle("angry");
                break;
            case 2:
                ChangeEyeOffset(EyePosition.happy);
                ChangeAnimatorIdle("happy");
                break;
            case 3:
                ChangeEyeOffset(EyePosition.dead);
                ChangeAnimatorIdle("dead");
                break;
        }
    }

    void ChangeAnimatorIdle(string trigger)
    {
        animator.SetTrigger(trigger);
    }

    void ChangeMaterialSettings(int index)
    {
        for (int i = 0; i < characterMaterials.Length; i++)
        {
            if (characterMaterials[i].transform.CompareTag("PlayerEyes"))
                characterMaterials[i].material.SetColor("_EmissionColor", eyeColors[index]);
            else
                characterMaterials[i].material.SetTexture("_MainTex",albedoList[index]);
        }
    }

    void ChangeEyeOffset(EyePosition pos)
    {
        Vector2 offset = Vector2.zero;

        switch (pos)
        {
            case EyePosition.normal:
                offset = new Vector2(0, 0);
                break;
            case EyePosition.happy:
                offset = new Vector2(.33f, 0);
                break;
            case EyePosition.angry:
                offset = new Vector2(.66f, 0);
                break;
            case EyePosition.dead:
                offset = new Vector2(.33f, .66f);
                break;
            default:
                break;
        }

        for (int i = 0; i < characterMaterials.Length; i++)
        {
            if (characterMaterials[i].transform.CompareTag("PlayerEyes"))
                characterMaterials[i].material.SetTextureOffset("_MainTex", offset);
        }
    }
}
