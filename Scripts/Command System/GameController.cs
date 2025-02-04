using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Yarn.Unity;
using System;
using UnityEngine.Timeline;
using System.Text.RegularExpressions;
using System.Linq;
using UnityEngine.UI;

public class GameController : DialogueViewBase
{
    [SerializeField] AudioSource m_typingSFX;
    [SerializeField] AudioClip typingSFXClip, typingSkipClip;
    [SerializeField] TMP_InputField m_command;
    [SerializeField] ScrollRect m_scrollRect;
    [SerializeField] TMP_Text m_result;
    [SerializeField] float typewriterWait;
    [SerializeField] DialogueRunner m_runner;
    [SerializeField] MarkupPalette m_palette;
    [SerializeField] Color m_interactableColor,m_commandColor;
    IEnumerator typewriter;
    string textToType, finalText;
    DialogueOption[] dialogueOptions;
    Action<int> onOptionSelected;
    Action onDialogueLineFinished;
    bool isTyping;
    
    // Start is called before the first frame update
    void Awake()
    {
        textToType = "";
        CommandDeselect();

    }
    public string HighlightWords(string[] options, string s)
    {
        string colorTag = "<color=#" + ColorUtility.ToHtmlStringRGB(m_interactableColor) + ">";
        string closeTag = "</color>";

        foreach (string option in options)
        {
            string pattern = $@"(?<!{Regex.Escape(colorTag)})\b{Regex.Escape(option)}\b(?!{Regex.Escape(closeTag)})";
            s = Regex.Replace(s, pattern, match => colorTag + match.Value + closeTag);
        }
        return s;
    }

    public string PaletteMarkedUpText(Yarn.Markup.MarkupParseResult line, MarkupPalette palette, bool applyLineBreaks = true)
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
        textToType = output + "\n";
        this.onDialogueLineFinished = (Action) onDialogueLineFinished.Clone();
        Typewriter(textToType);

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
        m_result.text = HighlightWords(ProcessDialogueOptions(dialogueOptions), m_result.text);
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
        m_typingSFX.PlayOneShot(typingSkipClip);
        StopCoroutine(typewriter);
        
        m_result.text = finalText;
        textToType = "";
        isTyping = false;
        onDialogueLineFinished();

    }
    public static string[] ProcessDialogueOptions(DialogueOption[] dialogueOptions)
    {
        return dialogueOptions.Select(option =>
        {
            string[] words = option.Line.RawText.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            return words.Length > 1 ? words[1].Trim() : "";
        }).ToArray();
    }
    public void SubmitCommand()
    {
        if (isTyping)
        {
            SkipText();
        }else if (dialogueOptions != null && dialogueOptions.Length > 0)
        {
            if (m_command.text != "")
            {
                m_result.text += "\n" + "<color=#" + ColorUtility.ToHtmlStringRGB(m_commandColor) + ">>" + m_command.text + "</color>\n";
                bool optionAvailable = false;
                foreach (var option in dialogueOptions)
                {
                    if (m_command.text.Equals(option.Line.RawText))
                    {
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
        else
        {
            onDialogueLineFinished();
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
        finalText = m_result.text + text;
        m_command.text = "";
        foreach (char letter in text)
        {
            m_typingSFX.PlayOneShot(typingSFXClip);
            m_result.text += letter;
            m_scrollRect.verticalNormalizedPosition = 0f;
            yield return new WaitForSeconds(typewriterWait);
        }


        isTyping = false;

        onDialogueLineFinished();
    }



}
