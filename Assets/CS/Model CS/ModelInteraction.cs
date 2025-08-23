using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class ModelInteraction : MonoBehaviour, IPointerClickHandler, IDragHandler
{
    private Material originalMaterial;
    private Material glowMaterial;
    private bool isGlowing = false;
    private Camera mainCamera;
    // �Ω�즲�ɰO����l����
    private Vector3 dragOffset;

    void Start()
    {
        mainCamera = Camera.main;
        // ���]�ҫ��ܤ֦��@�� Renderer
        Renderer renderer = GetComponentInChildren<Renderer>();
        if (renderer != null)
        {
            originalMaterial = renderer.material;
            glowMaterial = new Material(originalMaterial);
            // �ҥεo���ĪG�]�ݽT�{ shader �䴩 emission�^
            glowMaterial.EnableKeyword("_EMISSION");
            glowMaterial.SetColor("_EmissionColor", Color.yellow);
        }
    }

    /// <summary>
    /// �I���ҫ��ɤ����o���ĪG
    /// </summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        ToggleGlow();
    }

    private void ToggleGlow()
    {
        Renderer renderer = GetComponentInChildren<Renderer>();
        if (renderer == null) return;

        if (!isGlowing)
        {
            renderer.material = glowMaterial;
            isGlowing = true;
        }
        else
        {
            renderer.material = originalMaterial;
            isGlowing = false;
        }
    }

    /// <summary>
    /// �즲�ɧ�s�ҫ���m�P����]²��ܽd�G�̾ڤ����ưʱ���B�즲���ʡ^
    /// </summary>
    public void OnDrag(PointerEventData eventData)
    {
        // �̾ڷưʶq����]���B�ȥH X �b�����ưʧ@������Ѽơ^
        float rotationSpeed = 0.2f;
        float rotationY = eventData.delta.x * rotationSpeed;
        transform.Rotate(Vector3.up, rotationY, Space.World);

        // ��s�����m
        Ray ray = mainCamera.ScreenPointToRay(eventData.position);
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            // �p��즲�᪺�첾�]�i�ھڻݨD�վ�^
            Vector3 newPos = hit.point;
            transform.position = newPos;
        }
    }
}

