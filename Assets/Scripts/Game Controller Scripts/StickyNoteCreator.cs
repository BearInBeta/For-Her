using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using static StickyNotes;

public class StickyNoteCreator : MonoBehaviour
{
    public StickyNotes stickyNotes;
    public NoteColor color;
    private Image image;
    // Start is called before the first frame update
    void Start()
    {
        image = GetComponent<Image>();
    }

    // Update is called once per frame
    void Update()
    {

    }
    public void CreateNote()
    {
        stickyNotes.CreateNote(color);
    }
}
