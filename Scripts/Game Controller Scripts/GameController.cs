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
    [SerializeField] string[] fluff = { "the", "and", "is", "in", "at", "of", "a", "to" };
    IEnumerator typewriter;
    string textToType, finalText;
    DialogueOption[] dialogueOptions;
    Action<int> onOptionSelected;
    Action onDialogueLineFinished;
    bool isTyping;
    bool awaitingOptions;
    List<string>[] commands;
    List<string> words;
    SpellChecker spellChecker, specialChecker;
    /* Standard Monobehaviour*/
    void Awake()
    {
        textToType = "";
        CommandDeselect();

    }
    private void Start()
    {
        words = CSVReader.LoadCSVOneColumn("words.csv");
        commands = CSVReader.LoadCSV("data.csv");
        spellChecker = new SpellChecker(words);
        specialChecker = new SpellChecker(FlattenListArray(commands));
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Return))
        {
            SubmitCommand();
        }
    }

    /* Standard Monobehaviour End*/



    /* DialogueViewBase */
    public override void RunLine(LocalizedLine dialogueLine, Action onDialogueLineFinished)
    {
        Yarn.Markup.MarkupParseResult text = dialogueLine.Text;
        string output = PaletteMarkedUpText(text, m_palette, true);
        textToType = output + "\n";
        this.onDialogueLineFinished = (Action)onDialogueLineFinished.Clone();
        Typewriter(textToType);

    }
    public override void RunOptions(DialogueOption[] dialogueOptions, Action<int> onOptionSelected)
    {
        awaitingOptions = true;
        this.dialogueOptions = dialogueOptions;
        //m_result.text = HighlightWords(ProcessDialogueOptions(dialogueOptions), m_result.text);
        this.onOptionSelected = onOptionSelected;
        CommandSelect();

    }

    public string PaletteMarkedUpText(Yarn.Markup.MarkupParseResult line, MarkupPalette palette, bool applyLineBreaks = true)
    {
        string lineOfText = line.Text;
        line.Attributes.Sort((a, b) => (b.Position.CompareTo(a.Position)));
        foreach (var attribute in line.Attributes)
        {
            // we have a colour that matches the current marker
            Color markerColour;
            if (attribute.Name == "i")
            {
                lineOfText = lineOfText.Insert(attribute.Position + attribute.Length, "</i>");
                lineOfText = lineOfText.Insert(attribute.Position, $"<i>");
            }
            if (attribute.Name == "b")
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

    void FinishDialogue(Action action)
    {
        if (!awaitingOptions)
        {
            action();
        }
    }
    /* End DialogueViewBase */


    /* Interaction */
    public void SubmitCommand()
    {
        if (isTyping)
        {
            SkipText();
        }
        else if (dialogueOptions != null && dialogueOptions.Length > 0)
        {
            if (m_command.text != "")
            {
                string command = m_command.text.ToLower();
                    
                command = RemoveFluffWords(command);

                string commandWord = CorrectSentence(SplitLastWord(command).Item1.Trim());
                string subjectWord = spellChecker.GetBestCorrection(SplitLastWord(command).Item2.Trim());
                command = commandWord + " " + subjectWord;
                m_result.text += "\n" + "<color=#" + ColorUtility.ToHtmlStringRGB(m_commandColor) + ">>" + m_command.text + "</color>\n";
                bool optionAvailable = false;
                foreach (var option in dialogueOptions)
                {
                    if (command.Equals(option.Line.RawText))
                    {
                        onOptionSelected(option.DialogueOptionID);
                        dialogueOptions = null;
                        CommandDeselect();
                        optionAvailable = true;
                    }

                }
                if (!optionAvailable)
                {
                    Typewriter("Option " + command + " is not a valid option");
                }
            }
        }
        else
        {
            FinishDialogue(onDialogueLineFinished);
        }




    }
    public void SkipText()
    {
        m_typingSFX.PlayOneShot(typingSkipClip);
        StopCoroutine(typewriter);

        m_result.text = finalText;
        textToType = "";
        isTyping = false;
        FinishDialogue(onDialogueLineFinished);

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
    IEnumerator WriteText(string text)
    {
        isTyping = true;
        finalText = m_result.text + text;
        m_command.text = "";
        bool awaitingClosing = false;

        foreach (char letter in text)
        {
            if(letter == '<' || letter == '>' || awaitingClosing)
            {
                if(letter == '<')
                {
                    awaitingClosing = true;
                }else if(letter == '>')
                {
                    awaitingClosing = false;
                }
                
                m_result.text += letter;
                continue;
                
            }

            m_typingSFX.PlayOneShot(typingSFXClip);
            m_result.text += letter;
            m_scrollRect.verticalNormalizedPosition = 0f;
            yield return new WaitForSeconds(typewriterWait);
        }


        isTyping = false;
        FinishDialogue(onDialogueLineFinished);
    }

    /* End Interaction */

    /*Helpful*/
    public string RemoveFluffWords(string input)
    {
        List<string> words = input.Split(' ').ToList();
        words.RemoveAll(word => fluff.Contains(word.ToLower()));
        return string.Join(" ", words);
    }
    public static (string, string) SplitLastWord(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return (input, "");
        }

        var words = input.Trim().Split(' ');
        if (words.Length == 1)
        {
            return (words[0], "");
        }

        string lastWord = words[^1];
        string remaining = string.Join(" ", words[..^1]);
        return (remaining, lastWord);
    }

    public string CorrectSentence(string sentence)
    {
        var words = sentence.Split(' ');
        var correctedWords = words.Select(word => spellChecker.GetBestCorrection(specialChecker.GetBestCorrection(word)) ?? word);
        return string.Join(" ", correctedWords);
    }
    public string[] FlattenListArray(List<string>[] listOfLists)
    {
        return listOfLists.SelectMany(list => list).ToArray();
    }
    /*End helpful*/

    /*unsed*/

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


    public static string[] ProcessDialogueOptions(DialogueOption[] dialogueOptions)
    {
        return dialogueOptions.Select(option =>
        {
            string[] words = option.Line.RawText.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            return words.Length > 1 ? words[1].Trim() : "";
        }).ToArray();
    }
    /*End unsed*/
}
