using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

public class CardInteraction : MonoBehaviour
{
    public static event Action<CardInteraction, bool> SelectionChanged;

    // khoa tuong tac card theo trang thai UI overlay
    public static bool IsGlobalInteractionLocked { get; private set; }

    private Vector3 originalPos;
    private Vector3 originalUp;

    public float hoverOffset = 0.5f;
    public float duration = 0.2f;

    private bool isHovered = false;
    private bool isSelected = false;

    public bool IsSelected => isSelected;

    public GameObject border;

    private Tween moveTween;

    public static void SetGlobalInteractionLocked(bool isLocked)
    {
        IsGlobalInteractionLocked = isLocked;
    }

    void Start()
    {
        originalPos = transform.position;
        originalUp = transform.up;

        if (border != null)
            border.SetActive(false);
    }

    void OnMouseEnter()
    {
        if (IsGlobalInteractionLocked)
        {
            return;
        }

        isHovered = true;

        if (!isSelected)
            MoveTo(originalPos + originalUp * hoverOffset);
    }

    void OnMouseExit()
    {
        if (IsGlobalInteractionLocked)
        {
            return;
        }

        isHovered = false;

        if (!isSelected)
            MoveTo(originalPos);
    }

    void OnMouseDown()
    {
        if (IsGlobalInteractionLocked)
        {
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            ToggleSelect();
        }
    }

    void ToggleSelect()
    {
        SetSelected(!isSelected);
    }

    public void SetSelected(bool selected)
    {
        if (IsGlobalInteractionLocked && selected)
        {
            return;
        }

        if (isSelected == selected)
        {
            return;
        }

        isSelected = selected;

        if (border != null)
            border.SetActive(isSelected);

        if (isSelected)
        {
            MoveTo(originalPos + originalUp * hoverOffset);
        }
        else
        {
            if (!isHovered)
                MoveTo(originalPos);
        }

        SelectionChanged?.Invoke(this, isSelected);
    }

    public void UpdateLayoutPose(Vector3 position, Quaternion rotation)
    {
        originalPos = position;
        originalUp = rotation * Vector3.up;

        if (moveTween != null && moveTween.IsActive())
            moveTween.Kill();

        transform.SetPositionAndRotation(position, rotation);

        if (isSelected || isHovered)
        {
            transform.position = originalPos + originalUp * hoverOffset;
        }
    }

    void MoveTo(Vector3 target)
    {
        // Kill tween cũ để tránh bug giật
        if (moveTween != null && moveTween.IsActive())
            moveTween.Kill();

        moveTween = transform.DOMove(target, duration)
            .SetEase(Ease.OutQuad);
    }
}
