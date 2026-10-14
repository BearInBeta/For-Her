using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using static GameController;
using static Unity.Burst.Intrinsics.X86;

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
    [SerializeField] private float offsetChanger = 0.2f;
    private float offset = 0;
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
        newNote.transform.position = new Vector2(offset, offset);
        offset += offsetChanger;
    }

    public List<GameSaveSystem.NoteState> CaptureNotes()
    {
        var result = new List<GameSaveSystem.NoteState>();
        // On a fresh game the notes holder (or individual note UI references) can
        // be absent or not yet initialized. An invalid note must not stop saves.
        if (stickyNotesHolder == null) return result;

        foreach (Transform child in stickyNotesHolder)
        {
            if (child == null || !child.gameObject.activeSelf || child.gameObject.GetComponent<SavedElement>()) continue;
            var note = child.GetComponent<StickyNoteElements>();
            if (note == null) continue;

            var rect = child as RectTransform;
            var input = child.GetComponentInChildren<TMPro.TMP_InputField>(true);
            result.Add(new GameSaveSystem.NoteState
            {
                text = input != null ? input.text : (note.text != null ? note.text.text : ""),
                position = rect != null ? rect.anchoredPosition : Vector2.zero,
                size = rect != null ? rect.sizeDelta : Vector2.zero,
                color = note.stickyNoteImage != null ? note.stickyNoteImage.color : Color.white,
                textColor = note.text != null ? note.text.color : Color.black,
                placeholderColor = note.placeholder != null ? note.placeholder.color : Color.gray,
                exitColor = note.exitImage != null ? note.exitImage.color : Color.white,
                sibling = child.GetSiblingIndex()
            });
        }
        return result;
    }

    public void RestoreNotes(List<GameSaveSystem.NoteState> states)
    {
        if (stickyNotesHolder == null || stickyNote == null || states == null) return;
        // Disabled objects must also be removed. Destroy is completed by Unity
        // at end of frame; the save driver waits a frame before autosaving.
        foreach (var note in stickyNotesHolder.GetComponentsInChildren<StickyNoteElements>(true))
        {
            if (!note.gameObject.GetComponent<SavedElement>()) { 
                note.gameObject.SetActive(false);
                Destroy(note.gameObject);
            }
        }
        foreach (var state in states)
        {
            if (state == null) continue;
            var go = Instantiate(stickyNote, stickyNotesHolder);
            var note = go.GetComponent<StickyNoteElements>();
            if (note == null)
            {
                Debug.LogWarning("Sticky note prefab is missing StickyNoteElements.", go);
                Destroy(go);
                continue;
            }
            if (note.stickyNoteImage != null) note.stickyNoteImage.color = state.color;
            if (note.text != null) note.text.color = state.textColor;
            if (note.placeholder != null) note.placeholder.color = state.placeholderColor;
            if (note.exitImage != null) note.exitImage.color = state.exitColor;
            var input = go.GetComponentInChildren<TMPro.TMP_InputField>(true);
            if (input != null) input.SetTextWithoutNotify(state.text ?? "");
            else if (note.text != null) note.text.text = state.text ?? "";
            var rect = go.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchoredPosition = state.position;
                rect.sizeDelta = state.size;
                go.transform.SetSiblingIndex(Mathf.Clamp(state.sibling, 0, stickyNotesHolder.childCount - 1));
            }
        }
    }

    public void RecenterNotes()
    {
        offset = 0;
        foreach(StickyNoteElements sne in stickyNotesHolder.GetComponentsInChildren<StickyNoteElements>())
        {
            sne.gameObject.transform.position = new Vector2(offset, offset);
            offset += offsetChanger;
        }
    }


}
