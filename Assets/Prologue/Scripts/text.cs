using System.Collections;
using System.Runtime.CompilerServices;
using TMPro;
using Unity.AppUI.UI;
using UnityEngine;

public class text : MonoBehaviour
{

    [SerializeField] private TextMeshProUGUI txtSleep;
    [SerializeField] private GameObject introCanvas;
    [SerializeField] private TextMeshProUGUI txtIntro;

    string textSleep = "it's getting late... I should go to sleep.";
    string textIntro = "I found something in the old warehouse at work...\r\nIt looks like a device but... \r\nI don't know what it is for...\r\n                        \r\nI took it back home with me.";

    void Start()
    {
        StartCoroutine(SleepTextAppear());
    }

    private IEnumerator SleepTextAppear()
    {
        introCanvas.gameObject.SetActive(true);
        txtIntro.text = "";
        foreach (char c in textIntro)
        {
            txtIntro.text += c;
            yield return new WaitForSeconds(0.05f);
        }

        yield return new WaitForSeconds(8);
        introCanvas.gameObject.SetActive(false);

        txtSleep.text = "";
        foreach (char c in textSleep)
        {
            txtSleep.text += c;
            yield return new WaitForSeconds(0.15f);
        }

        yield return new WaitForSeconds(5);
        txtSleep.gameObject.SetActive(false);
    }

}
