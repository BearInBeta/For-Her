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
using static Unity.Burst.Intrinsics.X86.Avx;
using System.Xml.Linq;

public class GameController : DialogueViewBase
{
    [Serializable]
    public class TagColor
    {
        public string tag;
        public Color color;
    }

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
    [SerializeField] private TMP_Text m_locationText;

    [Header("Dialogue")]
    [SerializeField] private DialogueRunner m_runner;
    [SerializeField] private MarkupPalette m_palette;
    [SerializeField] private float typewriterWait = 0.02f;

    [Header("Colors")]
    [SerializeField] private List<TagColor> tagColorList = new List<TagColor>();

    [Header("Defined Values")]
    [SerializeField] private string[] fluff = { "the", "and", "is", "in", "at", "of", "a", "to" };
    [SerializeField] private string systemName;

    [Header("Cursors")]
    [SerializeField] private Texture2D defaultCursor;
    [SerializeField] private Texture2D clickableCursor;
    /* ===============================
     * 🧠 Private Variables
     * =============================== */
    private IEnumerator typewriter;
    private string textToType, finalText;
    private DialogueOption[] dialogueOptions;
    private Action<int> onOptionSelected;
    private Action onDialogueLineFinished;

    private bool isTyping;
    private bool awaitingOptions;
    private bool unskippable;

    private List<string>[] commands;
    private List<string> words;
    private SpellChecker spellChecker, specialChecker;
    private Stack<string> rooms;
    private Dictionary<string, Color> tagColorMap;
    private bool cursorOverLink;
    private Vector2 mouseDownPos;
    private float dragThreshold = 10f;
    /* ===============================
     * 🧱 Unity Lifecycle Methods
     * =============================== */
    private void Awake()
    {
        Cursor.SetCursor(defaultCursor, Vector2.zero, CursorMode.Auto);


        // Convert list to dictionary for quick lookup
        tagColorMap = new Dictionary<string, Color>();
        foreach (var entry in tagColorList)
        {
            if (!string.IsNullOrEmpty(entry.tag) && !tagColorMap.ContainsKey(entry.tag))
                tagColorMap.Add(entry.tag, entry.color);
        }


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

        // Always do hover logic
        CheckLinkHover();

        // Record mouse down
        if (Input.GetMouseButtonDown(0))
            mouseDownPos = Input.mousePosition;

        // On release: check movement distance
        if (Input.GetMouseButtonUp(0))
        {
            if (Vector2.Distance(mouseDownPos, Input.mousePosition) < dragThreshold)
            {
                // A "real" click → process link
                CheckLinkClick();
            }
        }

        m_locationText.text = $"LOCATION: {(rooms?.Count != 0 ? rooms.Peek() : "Connecting...")}";
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

    private void CheckRoom()
    {
        if (m_runner.CurrentNodeName.ToLower() != "start")
        {
            string currentRoom = m_runner.CurrentNodeName;

            if (!rooms.Contains(currentRoom))
            {
                rooms.Push(currentRoom);
                Typewriter($"<system>{systemName}: CURRENT LOCATION: {currentRoom}</system>");
            }
        }
    }
    public override void RunLine(LocalizedLine dialogueLine, Action onDialogueLineFinished)
    {
        // Track visited rooms based on Yarn node name
        CheckRoom();

        // Parse and color Yarn text using palette
        string charName = dialogueLine.CharacterName;
        Yarn.Markup.MarkupParseResult text = dialogueLine.Text;
        string output;
        bool nocharacter = dialogueLine.Metadata?.Contains("nocharacter") == true;
        bool nochevron = dialogueLine.Metadata?.Contains("nochevron") == true;
        if (charName != null && charName != "" && !nocharacter)
        {
            if(charName == systemName)
               output = $"<system>{(!nochevron ? "> " : "")}{PaletteMarkedUpText(text, m_palette, true)}</system>";
            else
                output = $"<dialogue>{(!nochevron ? "> " : "")}{PaletteMarkedUpText(text, m_palette, true)}</dialogue>";

        }
        else
        {
            output = $"<info>{(!nochevron ? "> " : "")}{PaletteMarkedUpText(text, m_palette, true)}</info>";
        }
        textToType = output;

        this.onDialogueLineFinished = (Action)onDialogueLineFinished.Clone();

        unskippable = dialogueLine.Metadata?.Contains("unskippable") == true;
        Typewriter(textToType, false, unskippable);

    }

    public override void RunOptions(DialogueOption[] dialogueOptions, Action<int> onOptionSelected)
    {
        // Track visited rooms based on Yarn node name
       
        awaitingOptions = true;
        this.dialogueOptions = dialogueOptions;
        this.onOptionSelected = onOptionSelected;

        CheckRoom();

        CommandSelect();
    }

    private void FinishDialogue(Action action)
    {
        if (!awaitingOptions)
            action?.Invoke();
    }
    public override void DialogueComplete()
    {
        // If we are not waiting for input or options, restart the room
        if (!awaitingOptions)
        {
            StartCoroutine(RestartRoomNextFrame());
        }
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

    /// Replaces special tags (like <interactable>...</interactable>) with rich text color tags.
    public string ReplaceTagsWithColors(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        input = WrapInteractables(input);
        foreach (var kvp in tagColorMap)
        {
            string tag = kvp.Key;
            string htmlColor = ColorUtility.ToHtmlStringRGB(kvp.Value);
            string openPattern = $"<{tag}>";
            string closePattern = $"</{tag}>";

            input = ReplaceNestedTags(input, tag, $"<color=#{htmlColor}>", "</color>");
        }

        return input;
    }

    /// Recursively replaces nested occurrences of a tag with a replacement.
    private string ReplaceNestedTags(string input, string tag, string openReplacement, string closeReplacement)
    {
        string pattern = $@"<{tag}>(.*?)</{tag}>";
        while (Regex.IsMatch(input, pattern, RegexOptions.Singleline))
        {
            input = Regex.Replace(input, pattern, match =>
            {
                string innerContent = match.Groups[1].Value;
                // Recurse for inner tags of the same type
                innerContent = ReplaceNestedTags(innerContent, tag, openReplacement, closeReplacement);
                return $"{openReplacement}{innerContent}{closeReplacement}";
            }, RegexOptions.Singleline);
        }
        return input;
    }

    private string WrapInteractables(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // Don't wrap if already inside <interactable>
        string pattern = @"(?<!<interactable>)(<link=([^>]+)>.*?<\/link>)(?!<\/interactable>)";

        return Regex.Replace(
            input,
            pattern,
            "<interactable>$1</interactable>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase
        );
    }

    /* ===============================
     * 💻 Command Input Handling
     * =============================== */

    public void SubmitCommand()
    {

        // Skip typing effect if mid-type
        if (isTyping)
        {
            SkipText();
            return;
        }
        string commandText = m_command.text.Trim();
        // Print the player's input in color
        if (commandText != "")
        {
            Typewriter($"<command>>{commandText}</command>", true);

        }
        else
        {
            if(!isTyping)
                CommandSelect();
            return;
        }
        // Handle selectable dialogue options
        if (dialogueOptions != null && dialogueOptions.Length > 0)
        {
            HandleDialogueCommand(commandText);
        }
        else
        {
            FinishDialogue(onDialogueLineFinished);
        }
    }

    private void HandleDialogueCommand(string commandText)
    {
        string ogcommand = commandText.ToLower();
        string command = ogcommand;

        // Handle special "text input" case
        if (GetOptionID("waitinginput") != -1)
        {
            if (commandText.Length > 50 || commandText.Length < 1 || Regex.IsMatch(commandText, @"[\d\W]"))
            {
                Typewriter($"<error>INVALID INPUT: {commandText}</error>");
                return;
            }

            m_runner.VariableStorage.SetValue("$inputVariable", commandText);
            int commandResult = GetOptionID("waitinginput");

            onOptionSelected?.Invoke(commandResult);
            ResetCommandState();
            FinishDialogue(onDialogueLineFinished);
            return;
        }

        // Restart current node
        if (command == "location")
        {
            RestartRoom();
            return;
        }

        // Go back one room
        if (command == "go back")
        {
            if (rooms.Count <= 1)
            {
                Typewriter("<error>Nothing to go back to...</error>");
                return;
            }

            rooms.Pop();
            Typewriter($"<system>{systemName}: CURRENT LOCATION: {rooms.Peek()}");
            RestartRoom();
            return;
        }

        // Clean and correct command text
        command = RemoveFluffWords(command);

        string[] splitWords = command.Trim().Split(' ');

        if(splitWords.Length > 8)
        {
            Typewriter($"<error>'{command}' is not a valid option</error>");
            return;
        }
        for (int i = 0; i < splitWords.Length; i++)
        {
            string cmd = "";
            string subject = "";
            for(int j = 0; j < i; j++)
            {
                cmd += splitWords[j] + " ";
            }
            cmd = cmd.Trim();

            for (int j = i; j < splitWords.Length; j++)
            {
                subject += splitWords[j] + " ";
            }
            subject = subject.Trim();


            int commandResult = checkOption(cmd, subject);
            if (commandResult != -1)
            {
                ResetCommandState();
                onOptionSelected?.Invoke(commandResult);
                return;
            }
        }
        Typewriter($"<error>Option '{ogcommand}' is not a valid option</error>");
    }

    private int checkOption(string cmd, string subject)
    {
        string command = $"{cmd} {subject}".Trim();
        int basicInput = GetOptionID(command);
        if (basicInput != -1)
        {
            return basicInput;
        }
        string commandWord = GetBasicCommand(spellChecker.GetBestCorrection(cmd.Trim()));
        string subjectWord = spellChecker.GetBestCorrection(subject.Trim());
        command = $"{commandWord} {subjectWord}".Trim();

        return GetOptionID(command);
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
    private void RestartRoom()
    {
        string currentRoom = rooms.Peek();
        RestartDialogue(currentRoom);
    }

    private IEnumerator RestartRoomNextFrame()
    {
        yield return null; // Let Yarn fully shut down

        RestartRoom();
    }
    /* ===============================
     * ✍️ Typewriter Effect
     * =============================== */

    public void Typewriter(string text, bool skip = false, bool unskippable = false)
    {
        this.unskippable = unskippable;
        text = ReplaceTagsWithColors(text) + "\n";
        typewriter = WriteText(text, skip);
        StartCoroutine(typewriter);
    }

    public void SkipText()
    {
        if (!unskippable)
        {
            m_typingSFX.PlayOneShot(typingSkipClip);
            StopCoroutine(typewriter);

            m_result.text = finalText;
            textToType = "";
            isTyping = false;
            FinishDialogue(onDialogueLineFinished);
        }
    }

    private IEnumerator WriteText(string text, bool skip)
    {
        while (isTyping)
        {
            yield return new WaitForEndOfFrame();
        }
        isTyping = true;
        finalText = m_result.text + text;
        m_command.text = "";
        bool awaitingClosing = false;
        if (skip)
        {
            SkipText();
            yield return null;
        }
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
        FinishDialogue(onDialogueLineFinished);
    }

    /* ===============================
     * 🧮 Helper Methods
     * =============================== */
    private void CheckLinkHover()
    {
        m_result.ForceMeshUpdate();
        Camera cam = GetCanvasCamera();

        int linkIndex = TMP_TextUtilities.FindIntersectingLink(
            m_result,
            Input.mousePosition,
            cam
        );

        if (linkIndex != -1)
        {
            if (!cursorOverLink)
            {
                cursorOverLink = true;
                Cursor.SetCursor(clickableCursor, Vector2.zero, CursorMode.Auto);
            }
        }
        else
        {
            if (cursorOverLink)
            {
                cursorOverLink = false;
                Cursor.SetCursor(defaultCursor, Vector2.zero, CursorMode.Auto);
            }
        }
    }

    private void CheckLinkClick()
    {
        m_result.ForceMeshUpdate();
        Camera cam = GetCanvasCamera();

        int linkIndex = TMP_TextUtilities.FindIntersectingLink(
            m_result,
            Input.mousePosition,
            cam
        );

        if (linkIndex == -1)
            return;

        TMP_LinkInfo linkInfo = m_result.textInfo.linkInfo[linkIndex];
        string linkId = linkInfo.GetLinkID().Trim();

        Sprite sprite = Resources.Load<Sprite>($"Images/{linkId}");

        if (sprite != null)
            OpenImagePopup(sprite);
        else
        {
            Debug.LogError($"Sprite '{linkId}' not found in Resources/Images/");
            OpenImagePopup(null);
        }
    }



    private void OpenImagePopup(Sprite sprite)
    {
        if (sprite == null)
        {
            Debug.LogError("[IMAGE POPUP] Cannot open image popup — sprite was null.");
            return;
        }

        Debug.Log($"[IMAGE POPUP] Opening popup for sprite: {sprite.name}");

        // TODO: Show popup UI, assign sprite to Image component, etc.
    }

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
    private Camera GetCanvasCamera()
    {
        Canvas canvas = m_result.canvas;

        if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            return null;

        return canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
    }


    /* ===============================
     * 💤 Unused or Debug Helpers
     * =============================== */

    public string HighlightWords(string[] options, string s)
    {
        string colorTag = $"<color=#{ColorUtility.ToHtmlStringRGB(tagColorMap["interactable"])}>";
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
