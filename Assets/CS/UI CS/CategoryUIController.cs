using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CategoryUIController : MonoBehaviour
{
    [Header("�W�� 4 �ӫ��s")]
    public Button chairBtn;
    public Button sofaBtn;
    public Button deskBtn;
    public Button drawerBtn;

    [Header("�U�� 4 �Ӯe��(Panels)")]
    public GameObject chairPanel;
    public GameObject sofaPanel;
    public GameObject deskPanel;
    public GameObject drawerPanel;

    private Button[] allButtons;
    private GameObject[] allPanels;
    private int currentIndex = 0; // 0=Chair,1=Sofa,2=Desk,3=Drawer

    void Start()
    {
        // �������s��}�C��K�B�z
        allButtons = new Button[] { chairBtn, sofaBtn, deskBtn, drawerBtn };
        allPanels = new GameObject[] { chairPanel, sofaPanel, deskPanel, drawerPanel };

        // ���U���s�ƥ�
        chairBtn.onClick.AddListener(() => OnClickCategory(0));
        sofaBtn.onClick.AddListener(() => OnClickCategory(1));
        deskBtn.onClick.AddListener(() => OnClickCategory(2));
        drawerBtn.onClick.AddListener(() => OnClickCategory(3));

        // �@�}�l�N��ܲ�0��(Chair)�A���è�L
        ShowPanel(0);
        UpdateButtonStyle(0);
    }

    private void OnClickCategory(int index)
    {
        currentIndex = index;
        ShowPanel(index);
        UpdateButtonStyle(index);
    }

    private void ShowPanel(int index)
    {
        // ��������
        for (int i = 0; i < allPanels.Length; i++)
        {
            allPanels[i].SetActive(false);
        }
        // ��ܫ��w
        allPanels[index].SetActive(true);
    }

    private void UpdateButtonStyle(int selectedIndex)
    {
        // 4 �ӫ��s (0=Chair,1=Sofa,2=Desk,3=Drawer)
        for (int i = 0; i < allButtons.Length; i++)
        {
            var rt = allButtons[i].GetComponent<RectTransform>();
            var img = allButtons[i].GetComponent<Image>();
            var txt = allButtons[i].GetComponentInChildren<TextMeshProUGUI>();

            if (i == selectedIndex)
            {
                // �Q�襤
                rt.sizeDelta = new Vector2(400, 180);
                img.color = new Color32(46, 0, 149, 255);
                txt.color = Color.white;
            }
            else
            {
                // ���Q�襤
                rt.sizeDelta = new Vector2(350, 150);
                img.color = Color.white;
                txt.color = Color.black;
            }
        }
    }
}

