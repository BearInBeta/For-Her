using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Yarn.Unity;
using Yarn;

public class GameController : DialogueViewBase
{
    /* ===============================
     * 🧩 Serialized Fields (Editor Setup)
     * =============================== */
    [Header("Audio")]
    [SerializeField] private AudioSource m_typingSFX, m_gameSFX;
    [SerializeField] private AudioClip typingSFXClip, typingSkipClip;

    [Header("UI References")]
    [SerializeField] private TMP_InputField m_command;
    [SerializeField] private ScrollRect m_scrollRect;
    [SerializeField] private TMP_Text m_result;

    [Header("Dialogue")]
    [SerializeField] private DialogueRunner m_runner;
    [SerializeField] private MarkupPalette m_palette;
    [SerializeField] private float typewriterWait = 0.02f;

    [Header("Colors")]
    [SerializeField] private Color m_interactableColor;
    [SerializeField] private Color m_commandColor;

    [Header("Language/Grammar")]
    [SerializeField] private string[] fluff = { "the", "and", "is", "in", "at", "of", "a", "to" };

    /* ===============================
     * 🧠 Private Variables
     * =============================== */
    private IEnumerator typewriter;
    private string textToType, finalText, currentYear;
    private DialogueOption[] dialogueOptions;
    private Action<int> onOptionSelected;
    private Action onDialogueLineFinished;

    private bool isTyping;
    private bool awaitingOptions;
    private bool lastLine;
    private bool textInput;

    private List<string>[] commands;
    private List<string> words;
    private SpellChecker spellChecker, specialChecker;
    private Stack<string> rooms;

    /* ===============================
     * 🧱 Unity Lifecycle Methods
     * =============================== */
    private void Awake()
    {
        currentYear = "1987";
        rooms = new Stack<string>();
        textToType = "";
        CommandDeselect();
    }

    private void Start()
    {
        // Load data for command and word recognition
        words = CSVReader.LoadCSVOneColumn("words.csv");
        commands = CSVReader.LoadCSV("data.csv");

        spellChecker = new SpellChecker(words);
        specialChecker = new SpellChecker(FlattenListArray(commands));
    }

    private void Update()
    {
        // Allow Enter key to submit commands
        if (Input.GetKeyDown(KeyCode.Return))
        {
            SubmitCommand();
        }
    }

    /* ===============================
     * 🎭 Yarn Commands
     * =============================== */
    [YarnCommand("playsfx")]
    public void PlaySFX(string sfxName)
    {
        // Load and play SFX by name from Resources/SFX/
        AudioClip clip = Resources.Load<AudioClip>($"SFX/{sfxName}");
        if (clip != null)
            m_gameSFX.PlayOneShot(clip);
        else
            Debug.LogWarning($"SFX '{sfxName}' not found in Resources/SFX/");
    }

    /* ===============================
     * 💬 DialogueViewBase Overrides
     * =============================== */

    public override void RunLine(LocalizedLine dialogueLine, Action onDialogueLineFinished)
    {
        // Track visited rooms based on Yarn node name
        if (m_runner.CurrentNodeName.ToLower() != "start")
        {
            string currentRoom = "";
            if (m_runner.CurrentNodeName.Contains("_"))
            {
                currentRoom = m_runner.CurrentNodeName.Substring(0, m_runner.CurrentNodeName.IndexOf('_') + 1);
                currentYear = m_runner.CurrentNodeName.Substring(m_runner.CurrentNodeName.IndexOf('_') + 1);
            }
            else currentRoom = m_runner.CurrentNodeName;

            if (!rooms.Contains(currentRoom))
                rooms.Push(currentRoom);
        }

        // Debug: Print visited rooms stack
        Debug.Log(string.Join("", rooms));

        // Parse and color Yarn text using palette
        Yarn.Markup.MarkupParseResult text = dialogueLine.Text;
        string output = PaletteMarkedUpText(text, m_palette, true);
        textToType = output + "\n";

        this.onDialogueLineFinished = (Action)onDialogueLineFinished.Clone();
        Typewriter(textToType);

        // Metadata handling (e.g., "lastline" or "textinput")
        lastLine = dialogueLine.Metadata?.Contains("lastline") == true;
        textInput = dialogueLine.Metadata?.Contains("textinput") == true;

        if (textInput)
            CommandSelect();
    }

    public override void RunOptions(DialogueOption[] dialogueOptions, Action<int> onOptionSelected)
    {
        awaitingOptions = true;
        this.dialogueOptions = dialogueOptions;
        this.onOptionSelected = onOptionSelected;
        CommandSelect();
    }

    private void FinishDialogue(Action action)
    {
        if (!awaitingOptions)
            action?.Invoke();
    }

    /* ===============================
     * 🧾 Text & Markup Handling
     * =============================== */
    public string PaletteMarkedUpText(Yarn.Markup.MarkupParseResult line, MarkupPalette palette, bool applyLineBreaks = true)
    {
        string lineOfText = line.Text;
        line.Attributes.Sort((a, b) => b.Position.CompareTo(a.Position));

        foreach (var attribute in line.Attributes)
        {
            Color markerColour;

            // Handle text styles
            if (attribute.Name == "i")
            {
                lineOfText = lineOfText.Insert(attribute.Position + attribute.Length, "</i>")
                                         .Insert(attribute.Position, "<i>");
            }
            else if (attribute.Name == "b")
            {
                lineOfText = lineOfText.Insert(attribute.Position + attribute.Length, "</b>")
                                         .Insert(attribute.Position, "<b>");
            }
            // Handle colored text via palette
            else if (palette.ColorForMarker(attribute.Name, out markerColour))
            {
                lineOfText = lineOfText.Insert(attribute.Position + attribute.Length, "</color>")
                                         .Insert(attribute.Position, $"<color=#{ColorUtility.ToHtmlStringRGB(markerColour)}>");
            }
            // Handle manual line breaks
            else if (applyLineBreaks && attribute.Name == "br")
            {
                lineOfText = lineOfText.Insert(attribute.Position, "<br>");
            }
        }

        return lineOfText;
    }

    /* ===============================
     * 💻 Command Input Handling
     * =============================== */

    public void SubmitCommand()
    {
        // Print the player's input in color
        if (m_command.text.Trim() != "")
            m_result.text += $"<color=#{ColorUtility.ToHtmlStringRGB(m_commandColor)}>>{m_command.text}</color>\n";

        // Skip typing effect if mid-type
        if (isTyping)
        {
            SkipText();
            return;
        }

        // Handle special "text input" case
        if (textInput)
        {
            if (m_command.text.Length > 10 || m_command.text.Length < 1 || Regex.IsMatch(m_command.text, @"[\d\W]"))
            {
                m_result.text += $"<color=#{ColorUtility.ToHtmlStringRGB(m_commandColor)}>INVALID INPUT</color>\n";
                return;
            }

            m_runner.VariableStorage.SetValue("$inputVariable", m_command.text);
            int commandResult = GetOptionID("waitinginput");

            if (commandResult != -1)
            {
                onOptionSelected?.Invoke(commandResult);
                ResetCommandState();
                return;
            }

            FinishDialogue(onDialogueLineFinished);
            return;
        }

        // Handle selectable dialogue options
        if (dialogueOptions != null && dialogueOptions.Length > 0)
        {
            HandleDialogueCommand();
        }
        else
        {
            FinishDialogue(onDialogueLineFinished);
        }
    }

    private void HandleDialogueCommand()
    {
        string command = m_command.text.ToLower();

        // Restart current node
        if (command == "look around")
        {
            RestartDialogue(m_runner.CurrentNodeName);
            return;
        }

        // Go back one room
        if (command == "go back")
        {
            if (rooms.Count <= 1)
            {
                Typewriter("Nothing to go back to...");
                return;
            }

            rooms.Pop();
            string currentRoom = rooms.Peek();
            if (currentRoom.Contains("_"))
                currentRoom += currentYear;

            RestartDialogue(currentRoom);
            return;
        }

        // Clean and correct command text
        command = RemoveFluffWords(command);
        (string cmd, string subject) = SplitLastWord(command);

        string commandWord = GetBasicCommand(spellChecker.GetBestCorrection(cmd.Trim()));
        string subjectWord = spellChecker.GetBestCorrection(subject.Trim());
        command = $"{commandWord} {subjectWord}".Trim();

        int commandResult = GetOptionID(command);
        if (commandResult != -1)
        {
            onOptionSelected?.Invoke(commandResult);
            ResetCommandState();
            return;
        }

        Typewriter($"Option '{command}' is not a valid option\n");
    }

    private void RestartDialogue(string nodeName)
    {
        dialogueOptions = null;
        CommandDeselect();
        awaitingOptions = false;
        m_runner.Stop();
        m_runner.StartDialogue(nodeName);
    }

    private void ResetCommandState()
    {
        dialogueOptions = null;
        CommandDeselect();
        awaitingOptions = false;
    }

    /* ===============================
     * ✍️ Typewriter Effect
     * =============================== */

    public void Typewriter(string text)
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
        FinishDialogue(onDialogueLineFinished);
    }

    private IEnumerator WriteText(string text)
    {
        isTyping = true;
        finalText = m_result.text + text;
        m_command.text = "";
        bool awaitingClosing = false;

        foreach (char letter in text)
        {
            // Avoid animating markup tags
            if (letter == '<' || letter == '>' || awaitingClosing)
            {
                awaitingClosing = letter switch
                {
                    '<' => true,
                    '>' => false,
                    _ => awaitingClosing
                };
                m_result.text += letter;
                continue;
            }

            // Add letter with typing sound
            m_typingSFX.PlayOneShot(typingSFXClip);
            m_result.text += letter;
            m_scrollRect.verticalNormalizedPosition = 0f;
            yield return new WaitForSeconds(typewriterWait);
        }

        isTyping = false;
        if (lastLine)
            FinishDialogue(onDialogueLineFinished);
    }

    /* ===============================
     * 🧮 Helper Methods
     * =============================== */

    private int GetOptionID(string command)
    {
        foreach (var option in dialogueOptions)
            if (command.Equals(option.Line.RawText))
                return option.DialogueOptionID;

        return -1;
    }

    private string GetBasicCommand(string command)
    {
        foreach (List<string> cmds in commands)
            if (cmds.Contains(command))
                return cmds.First();

        return command;
    }

    public string RemoveFluffWords(string input)
    {
        var words = input.Split(' ').ToList();
        words.RemoveAll(word => fluff.Contains(word.ToLower()));
        return string.Join(" ", words);
    }

    public static (string, string) SplitLastWord(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return (input, "");

        var parts = input.Trim().Split(' ');
        return parts.Length == 1
            ? (parts[0], "")
            : (string.Join(" ", parts[..^1]), parts[^1]);
    }

    public string CorrectSentence(string sentence)
    {
        var correctedWords = sentence.Split(' ')
            .Select(word => spellChecker.GetBestCorrection(specialChecker.GetBestCorrection(word)) ?? word);
        return string.Join(" ", correctedWords);
    }

    public string[] FlattenListArray(List<string>[] listOfLists)
    {
        return listOfLists.SelectMany(list => list).ToArray();
    }

    /* ===============================
     * 💤 Unused or Debug Helpers
     * =============================== */

    public string HighlightWords(string[] options, string s)
    {
        string colorTag = $"<color=#{ColorUtility.ToHtmlStringRGB(m_interactableColor)}>";
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
        return dialogueOptions
            .Select(option =>
            {
                string[] words = option.Line.RawText.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                return words.Length > 1 ? words[1].Trim() : "";
            })
            .ToArray();
    }

    /* ===============================
     * ⚙️ Command Input Focus
     * =============================== */
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
}
