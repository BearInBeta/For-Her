using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Yarn.Unity;
using System;
using UnityEngine.Timeline;

public class GameController : DialogueViewBase
{
    [SerializeField] TMP_InputField m_command, m_result;
    [SerializeField] float typewriterWait;
    [SerializeField] DialogueRunner m_runner;
    [SerializeField] MarkupPalette m_palette;
    IEnumerator typewriter;
    string textToType;
    DialogueOption[] dialogueOptions;
    Action<int> onOptionSelected;
    Action onDialogueLineFinished;
    bool isTyping;
    bool hasLine;
    
    // Start is called before the first frame update
    void Awake()
    {
        textToType = "";
        CommandDeselect();

    }
    public static string PaletteMarkedUpText(Yarn.Markup.MarkupParseResult line, MarkupPalette palette, bool applyLineBreaks = true)
    {
        string lineOfText = line.Text;
        line.Attributes.Sort((a, b) => (b.Position.CompareTo(a.Position)));
        foreach (var attribute in line.Attributes)
        {
            // we have a colour that matches the current marker
            Color markerColour;
            if(attribute.Name == "i")
            {
                lineOfText = lineOfText.Insert(attribute.Position + attribute.Length, "</i>");
                lineOfText = lineOfText.Insert(attribute.Position, $"<i>");
            }if (attribute.Name == "b")
            {
                lineOfText = lineOfText.Insert(attribute.Position + attribute.Length, "</b>");
                lineOfText = lineOfText.Insert(attribute.Position, $"<b>");
            }
            else if (palette.ColorForMarker(attribute.Name, out markerColour))
            {
                // we use the range on the marker to insert the TMP <color> tags
                // not the best approach but will work ok for this use case
                lineOfText = lineOfText.Insert(attribute.Position + attribute.Length, "</color>");
                lineOfText = lineOfText.Insert(attribute.Position, $"<color=#{ColorUtility.ToHtmlStringRGB(markerColour)}>");
            }

            if (applyLineBreaks && attribute.Name == "br")
            {
                lineOfText = lineOfText.Insert(attribute.Position, "<br>");
            }
        }
        return lineOfText;
    }
    public override void RunLine(LocalizedLine dialogueLine, Action onDialogueLineFinished)
    {
        Yarn.Markup.MarkupParseResult text = dialogueLine.Text;
        string output = PaletteMarkedUpText(text, m_palette, true);
        hasLine = true;
        if(textToType != "")
        {
            textToType += "\n";
        }
        textToType += output;
        this.onDialogueLineFinished = (Action) onDialogueLineFinished.Clone();
        if(!isTyping)
            Typewriter(output);
    }
    // Update is called once per frame
    void Update()
    {
        if(Input.GetKeyDown(KeyCode.Return)){
            SubmitCommand();
        }
    }
    public override void RunOptions(DialogueOption[] dialogueOptions, Action<int> onOptionSelected)
    {
        this.dialogueOptions = dialogueOptions;
        this.onOptionSelected = onOptionSelected;
        CommandSelect();
        
    }
    public void Typewriter(string text)
    {
        typewriter = WriteText(text);
        StartCoroutine(typewriter);
    }
    public void ErrorWriter(string text)
    {
        typewriter = WriteText(text);
        StartCoroutine(typewriter);
    }
    public void SkipText()
    {
        StopCoroutine(typewriter);
        while (dialogueOptions == null && hasLine == true)
        {
            hasLine = false;
            onDialogueLineFinished();
        }
        m_result.text = textToType;
        textToType = "";
        isTyping = false;
        hasLine = false;
    }
    public void SubmitCommand()
    {
        if (hasLine)
        {
            if (isTyping)
            {
                SkipText();
            }
            else
            {
                hasLine = false;
                onDialogueLineFinished();
            }
        }if (dialogueOptions != null && dialogueOptions.Length > 0)
        {
            if (m_command.text != "")
            {
                bool optionAvailable = false;
                foreach (var option in dialogueOptions)
                {
                    if (m_command.text.Equals(option.Line.RawText))
                    {
                        m_result.text = "";
                        onOptionSelected(option.DialogueOptionID);
                        dialogueOptions = null;
                        CommandDeselect();
                        optionAvailable = true;
                    }
                    
                }
                if (!optionAvailable)
                {
                    Typewriter("Option " + m_command.text + " is not a valid option");
                }
            }
        }
        
        
        
        
    }
    public void CommandSelect()
    {
        m_command.interactable = true;
        m_command.Select();
        m_command.ActivateInputField();
    }

    public void CommandDeselect()
    {
        m_command.interactable = false;

    }
    IEnumerator WriteText(string text)
    {
        isTyping = true;

        m_command.text = "";
        if (m_result.text != "")
            m_result.text += "\n";
        foreach (char letter in text)
        {
            m_result.text += letter;
            yield return new WaitForSeconds(typewriterWait);
        }


        isTyping = false;
        if (hasLine)
        {
            hasLine = false;
            onDialogueLineFinished();
        }
    }

   

}
