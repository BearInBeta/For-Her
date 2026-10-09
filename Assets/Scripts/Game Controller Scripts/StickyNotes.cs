using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using static GameController;

public class StickyNotes : MonoBehaviour
{
    [Serializable]
    public class NoteColor
    {
        public Color color;
        public Color textColor;
        public Color placeholderColor;
        public Color exitColor;
    }

    [SerializeField] private List<NoteColor> noteColors = new List<NoteColor>();
    [SerializeField] private GameObject stickyNoteCreator;
    [SerializeField] private GameObject stickyNote;
    [SerializeField] private Transform stickyNotesLayout;
    [SerializeField] private Transform stickyNotesHolder;

    // Start is called before the first frame update
    void Start()
    {
        foreach (NoteColor note in noteColors)
        {
            GameObject newNote = Instantiate(stickyNoteCreator, stickyNotesLayout);
            newNote.GetComponent<StickyNoteCreator>().stickyNotes = this;
            newNote.GetComponent<StickyNoteCreator>().color = note;
            newNote.GetComponent<Image>().color = note.color;
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void CreateNote(NoteColor color)
    {
        GameObject newNote = Instantiate(stickyNote, stickyNotesHolder);
        newNote.GetComponent<StickyNoteElements>().stickyNoteImage.color = color.color;
        newNote.GetComponent<StickyNoteElements>().exitImage.color = color.exitColor;
        newNote.GetComponent<StickyNoteElements>().placeholder.color = color.placeholderColor;
        newNote.GetComponent<StickyNoteElements>().text.color = color.textColor;

    }


}
