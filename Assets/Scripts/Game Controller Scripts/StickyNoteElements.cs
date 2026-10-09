using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class StickyNoteElements : MonoBehaviour
{
    public Image stickyNoteImage;
    public TMP_Text placeholder;
    public TMP_Text text;
    public Image exitImage;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void DestroyNote()
    {
        Destroy(gameObject);
    }
}
