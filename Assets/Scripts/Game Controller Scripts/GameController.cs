
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Yarn;
using Yarn.Unity;

public class GameController : DialogueViewBase
{
    [Serializable]
    public class TagColor
    {
        public string tag;
        public Color color;
    }

    [Serializable]
    public class MusicTrack
    {
        public string name;
        public AudioClip audioClip;
    }

    [Header("Audio")]
    [SerializeField] private AudioSource m_typingSFX;
    [SerializeField] private AudioSource m_gameSFX;
    [SerializeField] private AudioSource m_gameMusic;
    [SerializeField] private AudioClip typingSFXClip;
    [SerializeField] private AudioClip typingSkipClip;
    [SerializeField] private MusicTrack[] musicTracks;

    [Header("UI References")]
    [SerializeField] private TMP_InputField m_command;
    [SerializeField] private ScrollRect m_scrollRect;
    [SerializeField] private TMP_Text m_result;
    [SerializeField] private TMP_Text m_locationText;
    [SerializeField] private Image m_image;
    [SerializeField] private WindowController m_ImageWindowController;
    [SerializeField] private RectTransform targetWindowRoot;

    [Header("Music Notification")]
    [SerializeField] private GameObject notificationUI;
    [SerializeField] private TMP_Text notificationText;
    [SerializeField] private float notificationTime = 0.8f;
    [SerializeField] private CanvasGroup notificationCanvasGroup;
    [SerializeField] private RectTransform notificationRect;
    [SerializeField] private float notificationAnimTime = 0.25f;
    [SerializeField] private float notificationSlideDistance = 100f;

    [Header("Dialogue")]
    [SerializeField] private DialogueRunner m_runner;
    [SerializeField] private MarkupPalette m_palette;
    [SerializeField] private float typewriterWait = 0.02f;
    [SerializeField] private float typewriterLineWait = 0.2f;

    [Header("Colors")]
    [SerializeField]
    private List<TagColor> tagColorList =
        new List<TagColor>();

    [Header("Defined Values")]
    [SerializeField]
    private string[] fluff =
    {
        "the",
        "and",
        "is",
        "in",
        "at",
        "of",
        "a",
        "to"
    };

    [SerializeField] private string systemName;

    [Header("Cursors")]
    [SerializeField] private Texture2D defaultCursor;
    [SerializeField] private Texture2D clickableCursor;

    private class TypewriterJob
    {
        public string Text;
        public bool Skip;
        public bool Unskippable;
        public Action OnComplete;
    }

    private readonly Queue<TypewriterJob> typewriterQueue =
        new Queue<TypewriterJob>();

    private TypewriterJob activeTypewriterJob;

    private bool typewriterQueueRunning;
    private bool skipRequested;
    private bool isTyping;
    private bool isRevealingText;

    private DialogueOption[] dialogueOptions;
    private Action<int> onOptionSelected;
    private bool awaitingOptions;

    private List<string>[] commands;
    private List<string> words;

    private SpellChecker spellChecker;
    private SpellChecker specialChecker;

    private Stack<string> rooms;
    private Dictionary<string, Color> tagColorMap;

    private bool cursorOverLink;
    private Vector2 mouseDownPos;

    private const float dragThreshold = 10f;

    private Coroutine notificationCoroutine;
    private Coroutine focusCoroutine;
    private Coroutine restartCoroutine;
    private Coroutine automaticRestartCoroutine;

    private bool restartingDialogue;
    private bool suppressDialogueComplete;

    private void Awake()
    {
        Cursor.SetCursor(
            defaultCursor,
            Vector2.zero,
            CursorMode.Auto
        );

        tagColorMap = new Dictionary<string, Color>();

        foreach (TagColor entry in tagColorList)
        {
            if (string.IsNullOrEmpty(entry.tag))
                continue;

            if (tagColorMap.ContainsKey(entry.tag))
                continue;

            tagColorMap.Add(entry.tag, entry.color);
        }

        rooms = new Stack<string>();

        if (m_command != null)
        {
            m_command.lineType =
                TMP_InputField.LineType.SingleLine;

            m_command.onSubmit.AddListener(
                OnCommandSubmitted
            );
        }

        CommandDeselect();
    }

    private void Start()
    {
        words = CSVReader.LoadCSVOneColumn("words.csv");
        commands = CSVReader.LoadCSV("data.csv");

        spellChecker = new SpellChecker(words);

        specialChecker = new SpellChecker(
            FlattenListArray(commands)
        );

        StartCoroutine(
            PlayTrack("smoke and wood")
        );
    }

    private void Update()
    {
        HandleDialogueSkipInput();
        HandleLinkInteraction();
        UpdateLocationText();
    }

    private void HandleDialogueSkipInput()
    {
        if (!isRevealingText)
            return;

        if (m_command != null &&
            m_command.interactable)
            return;

        if (Input.GetKeyDown(KeyCode.Return) ||
            Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            SkipText();
        }
    }

    private void HandleLinkInteraction()
    {
        bool isTopmost = IsTopmostUnderPointer();

        if (isTopmost)
        {
            CheckLinkHover();
        }
        else if (cursorOverLink)
        {
            cursorOverLink = false;

            Cursor.SetCursor(
                defaultCursor,
                Vector2.zero,
                CursorMode.Auto
            );
        }

        if (Input.GetMouseButtonDown(0) && isTopmost)
        {
            mouseDownPos = Input.mousePosition;
        }

        if (Input.GetMouseButtonUp(0) && isTopmost)
        {
            float distance = Vector2.Distance(
                mouseDownPos,
                Input.mousePosition
            );

            if (distance < dragThreshold)
            {
                CheckLinkClick();
            }
        }
    }

    private void UpdateLocationText()
    {
        if (m_locationText == null)
            return;

        string location =
            rooms != null && rooms.Count > 0
                ? rooms.Peek()
                : "Not Connected";

        m_locationText.text =
            $"LOCATION: {location}";
    }

    private void OnDisable()
    {
        if (cursorOverLink)
        {
            cursorOverLink = false;

            Cursor.SetCursor(
                defaultCursor,
                Vector2.zero,
                CursorMode.Auto
            );
        }
    }

    private void OnDestroy()
    {
        if (m_command != null)
        {
            m_command.onSubmit.RemoveListener(
                OnCommandSubmitted
            );
        }
    }

    private void OnCommandSubmitted(string value)
    {
        if (m_command == null)
            return;

        if (!m_command.interactable)
            return;

        if (!awaitingOptions)
            return;

        if (isTyping || typewriterQueueRunning)
            return;

        SubmitCommand();
    }

    public void SubmitCommand()
    {
        if (m_command == null)
            return;

        if (!m_command.interactable)
            return;

        if (!awaitingOptions)
            return;

        if (isTyping || typewriterQueueRunning)
            return;

        string commandText =
            m_command.text.Trim();

        if (string.IsNullOrEmpty(commandText))
        {
            RequestCommandFocus();
            return;
        }

        CommandDeselect();

        m_command.SetTextWithoutNotify("");

        Typewriter(
            $"<command>>{commandText}</command>",
            true
        );

        if (dialogueOptions != null &&
            dialogueOptions.Length > 0)
        {
            HandleDialogueCommand(commandText);
        }
        else
        {
            TryEnableCommandInput();
        }
    }

    private void RequestCommandFocus()
    {
        if (focusCoroutine != null)
        {
            StopCoroutine(focusCoroutine);
        }

        focusCoroutine = StartCoroutine(
            RestoreCommandFocus()
        );
    }

    private IEnumerator RestoreCommandFocus()
    {
        yield return null;

        focusCoroutine = null;

        if (m_command == null)
            yield break;

        if (!m_command.interactable)
            yield break;

        if (!m_command.gameObject.activeInHierarchy)
            yield break;

        if (EventSystem.current != null)
        {
            GameObject selected =
                EventSystem.current.currentSelectedGameObject;

            if (selected != null &&
                selected != m_command.gameObject)
            {
                yield break;
            }

            EventSystem.current.SetSelectedGameObject(
                m_command.gameObject
            );
        }

        m_command.ActivateInputField();
    }

    private void TryEnableCommandInput()
    {
        if (!awaitingOptions)
            return;

        if (restartingDialogue)
            return;

        if (isTyping || typewriterQueueRunning)
            return;

        if (typewriterQueue.Count > 0)
            return;

        CommandSelect();
    }

    [YarnCommand("playsfx")]
    public void PlaySFX(string sfxName)
    {
        AudioClip clip =
            Resources.Load<AudioClip>(
                $"SFX/{sfxName}"
            );

        if (clip != null)
        {
            if (m_gameSFX != null)
            {
                m_gameSFX.PlayOneShot(clip);
            }
        }
        else
        {
            Debug.LogWarning(
                $"SFX '{sfxName}' not found in Resources/SFX/"
            );
        }
    }

    private void CheckRoom()
    {
        if (m_runner == null)
            return;

        string currentRoom =
            m_runner.CurrentNodeName;

        if (string.IsNullOrEmpty(currentRoom))
            return;

        if (string.Equals(
                currentRoom,
                "start",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (rooms.Contains(currentRoom))
            return;

        rooms.Push(currentRoom);

        string[] splitWords =
            currentRoom
                .Trim()
                .Split(
                    new[] { '_' },
                    StringSplitOptions.RemoveEmptyEntries
                );

        string toType = "";
        string roomName = "";

        for (int i = 1; i < splitWords.Length; i++)
        {
            roomName += splitWords[i] + " ";
        }

        if (roomName == "")
        {
            roomName = currentRoom;
        }
        else
        {
            roomName = roomName.Trim();
        }

        switch (splitWords[0])
        {
            case "location":
                toType =
                    $"<system>{systemName}: CURRENT LOCATION: {roomName}</system>";
                break;

            case "person":
                toType =
                    $"<system>{systemName}: SPEAKING TO: {roomName}</system>";
                break;

            case "object":
                toType =
                    $"<system>{systemName}: INSPECTING: {roomName}</system>";
                break;

            default:
                toType =
                    $"<system>{systemName}: {roomName}</system>";
                break;
        }

        Typewriter(toType);
    }

    public override void RunLine(
        LocalizedLine dialogueLine,
        Action onDialogueLineFinished)
    {
        CheckRoom();

        string charName =
            dialogueLine.CharacterName;

        Yarn.Markup.MarkupParseResult text =
            dialogueLine.Text;

        bool nocharacter =
            dialogueLine.Metadata?.Contains("nocharacter") == true;

        bool nochevron =
            dialogueLine.Metadata?.Contains("nochevron") == true;

        bool unskippable =
            dialogueLine.Metadata?.Contains("unskippable") == true;

        string output;

        if (!string.IsNullOrEmpty(charName) &&
            !nocharacter)
        {
            if (charName == systemName)
            {
                output =
                    $"<system>" +
                    $"{(!nochevron ? "> " : "")}" +
                    $"{PaletteMarkedUpText(text, m_palette, true)}" +
                    $"</system>";
            }
            else
            {
                output =
                    $"<dialogue>" +
                    $"{(!nochevron ? "> " : "")}" +
                    $"{PaletteMarkedUpText(text, m_palette, true)}" +
                    $"</dialogue>";
            }
        }
        else
        {
            output =
                $"<info>" +
                $"{(!nochevron ? "> " : "")}" +
                $"{PaletteMarkedUpText(text, m_palette, true)}" +
                $"</info>";
        }

        Typewriter(
            output,
            false,
            unskippable,
            onDialogueLineFinished
        );
    }

    public override void RunOptions(
        DialogueOption[] dialogueOptions,
        Action<int> onOptionSelected)
    {
        awaitingOptions = true;

        this.dialogueOptions =
            dialogueOptions;

        this.onOptionSelected =
            onOptionSelected;

        CheckRoom();

        TryEnableCommandInput();
    }

    public override void DialogueComplete()
    {
        if (suppressDialogueComplete)
            return;

        if (restartingDialogue)
            return;

        if (awaitingOptions)
            return;

        if (automaticRestartCoroutine != null)
            return;

        automaticRestartCoroutine =
            StartCoroutine(
                RestartRoomNextFrame()
            );
    }

    public string PaletteMarkedUpText(
        Yarn.Markup.MarkupParseResult line,
        MarkupPalette palette,
        bool applyLineBreaks = true)
    {
        string lineOfText =
            line.Text;

        line.Attributes.Sort(
            (a, b) =>
                b.Position.CompareTo(a.Position)
        );

        foreach (var attribute in line.Attributes)
        {
            Color markerColour;

            if (attribute.Name == "i")
            {
                lineOfText =
                    lineOfText
                        .Insert(
                            attribute.Position + attribute.Length,
                            "</i>"
                        )
                        .Insert(
                            attribute.Position,
                            "<i>"
                        );
            }
            else if (attribute.Name == "b")
            {
                lineOfText =
                    lineOfText
                        .Insert(
                            attribute.Position + attribute.Length,
                            "</b>"
                        )
                        .Insert(
                            attribute.Position,
                            "<b>"
                        );
            }
            else if (
                palette != null &&
                palette.ColorForMarker(
                    attribute.Name,
                    out markerColour
                ))
            {
                string htmlColor =
                    ColorUtility.ToHtmlStringRGB(
                        markerColour
                    );

                lineOfText =
                    lineOfText
                        .Insert(
                            attribute.Position + attribute.Length,
                            "</color>"
                        )
                        .Insert(
                            attribute.Position,
                            $"<color=#{htmlColor}>"
                        );
            }
            else if (
                applyLineBreaks &&
                attribute.Name == "br")
            {
                lineOfText =
                    lineOfText.Insert(
                        attribute.Position,
                        "<br>"
                    );
            }
        }

        return lineOfText;
    }

    public string ReplaceTagsWithColors(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        input = WrapInteractables(input);

        foreach (
            KeyValuePair<string, Color> kvp
            in tagColorMap)
        {
            string tag = kvp.Key;

            string htmlColor =
                ColorUtility.ToHtmlStringRGB(
                    kvp.Value
                );

            input = ReplaceNestedTags(
                input,
                tag,
                $"<color=#{htmlColor}>",
                "</color>"
            );
        }

        return input;
    }

    private string ReplaceNestedTags(
        string input,
        string tag,
        string openReplacement,
        string closeReplacement)
    {
        string escapedTag =
            Regex.Escape(tag);

        string pattern =
            $@"<{escapedTag}>(.*?)</{escapedTag}>";

        while (
            Regex.IsMatch(
                input,
                pattern,
                RegexOptions.Singleline
            ))
        {
            input = Regex.Replace(
                input,
                pattern,
                match =>
                {
                    string innerContent =
                        match.Groups[1].Value;

                    innerContent =
                        ReplaceNestedTags(
                            innerContent,
                            tag,
                            openReplacement,
                            closeReplacement
                        );

                    return
                        $"{openReplacement}" +
                        $"{innerContent}" +
                        $"{closeReplacement}";
                },
                RegexOptions.Singleline
            );
        }

        return input;
    }

    private string WrapInteractables(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        string pattern =
            @"(?<!<interactable>)(<link=([^>]+)>.*?<\/link>)(?!<\/interactable>)";

        return Regex.Replace(
            input,
            pattern,
            "<interactable>$1</interactable>",
            RegexOptions.Singleline |
            RegexOptions.IgnoreCase
        );
    }

    private void HandleDialogueCommand(
        string commandText)
    {
        string originalCommand =
            commandText.ToLowerInvariant().Trim();

        string command =
            originalCommand;

        int waitingInputOption =
            GetOptionID("waitinginput");

        if (waitingInputOption != -1)
        {
            if (
                commandText.Length > 50 ||
                commandText.Length < 1 ||
                Regex.IsMatch(
                    commandText,
                    @"[\d\W]"
                ))
            {
                Typewriter(
                    $"<error>INVALID INPUT: {commandText}</error>"
                );

                return;
            }

            m_runner.VariableStorage.SetValue(
                "$inputVariable",
                commandText
            );

            SelectDialogueOption(
                waitingInputOption
            );

            return;
        }

        int exactResult =
            GetOptionID(command);

        if (exactResult != -1)
        {
            SelectDialogueOption(
                exactResult
            );

            return;
        }

        int oneWordResult =
            CheckOption(
                command,
                ""
            );

        if (oneWordResult != -1)
        {
            SelectDialogueOption(
                oneWordResult
            );

            return;
        }

        bool blockFreeInput = false;

        m_runner.VariableStorage.TryGetValue<bool>(
            "$blockFreeInput",
            out blockFreeInput
        );

        if (blockFreeInput)
        {
            Typewriter(
                $"<error>INVALID INPUT: {commandText}</error>"
            );

            return;
        }


        if (command == "location")
        {
            string currentRoom =
                rooms != null && rooms.Count > 0
                    ? rooms.Peek()
                    : "Not Connected";

            string roomName = currentRoom;

            if (currentRoom.StartsWith("location_",
                StringComparison.OrdinalIgnoreCase))
            {
                roomName = currentRoom.Substring(9);
            }

            roomName = roomName.Replace("_", " ");

            Typewriter(
                $"<system>{systemName}: CURRENT LOCATION: {roomName}</system>"
            );

            return;
        }


        if (command == "go back")
        {
            if (
                rooms == null ||
                rooms.Count <= 1)
            {
                Typewriter(
                    "<error>Nothing to go back to...</error>"
                );

                return;
            }

            rooms.Pop();

            Typewriter(
                $"<system>{systemName}: CURRENT LOCATION: {rooms.Peek()}</system>"
            );

            RestartRoom();

            return;
        }

        string[] fluffSplitWords =
            command
                .Trim()
                .Split(
                    new[] { ' ' },
                    StringSplitOptions.RemoveEmptyEntries
                );

        command =
            RemoveFluffWords(command);

        string[] splitWords =
            command
                .Trim()
                .Split(
                    new[] { ' ' },
                    StringSplitOptions.RemoveEmptyEntries
                );

        if (splitWords.Length == 0)
        {
            Typewriter(
                $"<error>Option '{originalCommand}' is not a valid option</error>"
            );

            return;
        }

        if (splitWords.Length > 8)
        {
            Typewriter(
                $"<error>'{command}' is not a valid option</error>"
            );

            return;
        }

        if (splitWords[0] == "play")
        {
            string track = "";

            for (int i = 1; i < fluffSplitWords.Length; i++)
            {
                track += fluffSplitWords[i] + " ";
            }

            track = track.Trim();

            StartCoroutine(
                PlayTrack(track)
            );

            return;
        }

        if (splitWords.Length == 1)
        {
            oneWordResult =
                CheckOption(
                    splitWords[0],
                    ""
                );

            if (oneWordResult != -1)
            {
                SelectDialogueOption(
                    oneWordResult
                );

                return;
            }
        }

        for (int i = 1; i < splitWords.Length; i++)
        {
            string cmd =
                string.Join(
                    " ",
                    splitWords.Take(i)
                );

            string subject =
                string.Join(
                    " ",
                    splitWords.Skip(i)
                );

            int commandResult =
                CheckOption(
                    cmd,
                    subject
                );

            if (commandResult != -1)
            {
                SelectDialogueOption(
                    commandResult
                );

                return;
            }
        }

        Typewriter(
            $"<error>Option '{originalCommand}' is not a valid option</error>"
        );
    }

    private IEnumerator PlayTrack(string trackName)
    {
        foreach (MusicTrack musicTrack in musicTracks)
        {
            if (musicTrack.name.Equals(
                    trackName,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (m_gameMusic != null)
                {
                    m_gameMusic.clip =
                        musicTrack.audioClip;

                    m_gameMusic.Play();
                }

                if (notificationCoroutine != null)
                {
                    StopCoroutine(notificationCoroutine);
                }

                notificationCoroutine =
                    StartCoroutine(
                        ShowMusicNotification(trackName)
                    );

                yield break;
            }
        }

        Typewriter(
            $"<error>There is no track named '{trackName}'</error>"
        );
    }

    private IEnumerator ShowMusicNotification(string trackName)
    {
        if (notificationUI == null ||
            notificationText == null ||
            notificationRect == null ||
            notificationCanvasGroup == null)
        {
            yield break;
        }

        notificationText.text = trackName;

        notificationCanvasGroup.alpha = 0f;

        notificationUI.SetActive(true);

        yield return null;

        Canvas.ForceUpdateCanvases();

        Vector2 visiblePosition =
            notificationRect.anchoredPosition;

        Vector2 hiddenPosition =
            visiblePosition +
            Vector2.right * notificationSlideDistance;

        notificationRect.anchoredPosition =
            hiddenPosition;

        notificationCanvasGroup.alpha = 0f;

        yield return null;

        float elapsed = 0f;

        while (elapsed < notificationAnimTime)
        {
            elapsed += Time.unscaledDeltaTime;

            float t =
                Mathf.Clamp01(
                    elapsed / notificationAnimTime
                );

            float eased =
                Mathf.SmoothStep(
                    0f,
                    1f,
                    t
                );

            notificationRect.anchoredPosition =
                Vector2.Lerp(
                    hiddenPosition,
                    visiblePosition,
                    eased
                );

            notificationCanvasGroup.alpha =
                eased;

            yield return null;
        }

        notificationRect.anchoredPosition =
            visiblePosition;

        notificationCanvasGroup.alpha = 1f;

        yield return new WaitForSecondsRealtime(
            notificationTime
        );

        elapsed = 0f;

        while (elapsed < notificationAnimTime)
        {
            elapsed += Time.unscaledDeltaTime;

            float t =
                Mathf.Clamp01(
                    elapsed / notificationAnimTime
                );

            float eased =
                Mathf.SmoothStep(
                    0f,
                    1f,
                    t
                );

            notificationRect.anchoredPosition =
                Vector2.Lerp(
                    visiblePosition,
                    hiddenPosition,
                    eased
                );

            notificationCanvasGroup.alpha =
                1f - eased;

            yield return null;
        }

        notificationCanvasGroup.alpha = 0f;

        notificationRect.anchoredPosition =
            visiblePosition;

        notificationUI.SetActive(false);

        notificationCoroutine = null;
    }

    private void SelectDialogueOption(int optionID)
    {
        Action<int> callback =
            onOptionSelected;

        ResetCommandState();

        callback?.Invoke(optionID);
    }

    private int CheckOption(
        string cmd,
        string subject)
    {
        string command =
            $"{cmd} {subject}".Trim();

        int basicInput =
            GetOptionID(command);

        if (basicInput != -1)
        {
            return basicInput;
        }

        if (spellChecker == null)
            return -1;

        string correctedCommand =
            spellChecker.GetBestCorrection(
                cmd.Trim()
            );

        string commandWord =
            GetBasicCommand(
                correctedCommand
            );

        string subjectWord;

        if (string.IsNullOrWhiteSpace(subject))
        {
            subjectWord = "";
        }
        else
        {
            subjectWord =
                spellChecker.GetBestCorrection(
                    subject.Trim()
                );
        }

        command =
            $"{commandWord} {subjectWord}".Trim();

        return GetOptionID(command);
    }

    private void RestartDialogue(string nodeName)
    {
        if (string.IsNullOrEmpty(nodeName))
        {
            Debug.LogWarning(
                "Cannot restart dialogue because node name is empty."
            );

            return;
        }

        if (automaticRestartCoroutine != null)
        {
            StopCoroutine(automaticRestartCoroutine);
            automaticRestartCoroutine = null;
        }

        if (restartCoroutine != null)
        {
            StopCoroutine(restartCoroutine);
            restartCoroutine = null;
        }

        dialogueOptions = null;
        onOptionSelected = null;
        awaitingOptions = false;

        CommandDeselect();

        restartingDialogue = true;

        restartCoroutine =
            StartCoroutine(
                RestartDialogueCoroutine(nodeName)
            );
    }

    private IEnumerator RestartDialogueCoroutine(string nodeName)
    {
        if (m_runner == null)
        {
            restartingDialogue = false;
            restartCoroutine = null;
            yield break;
        }

        suppressDialogueComplete = true;

        m_runner.Stop();

        yield return null;

        m_runner.StartDialogue(nodeName);

        suppressDialogueComplete = false;
        restartingDialogue = false;
        restartCoroutine = null;

        TryEnableCommandInput();
    }

    private void ResetCommandState()
    {
        dialogueOptions = null;
        onOptionSelected = null;

        awaitingOptions = false;

        CommandDeselect();

        if (m_command != null)
        {
            m_command.SetTextWithoutNotify("");
        }
    }

    private void RestartRoom()
    {
        string currentRoom = null;

        if (rooms != null && rooms.Count > 0)
        {
            currentRoom = rooms.Peek();
        }
        else if (
            m_runner != null &&
            !string.IsNullOrEmpty(
                m_runner.CurrentNodeName))
        {
            currentRoom =
                m_runner.CurrentNodeName;
        }

        if (string.IsNullOrEmpty(currentRoom))
        {
            Debug.LogWarning(
                "Cannot restart room because no current Yarn node exists."
            );

            return;
        }

        RestartDialogue(currentRoom);
    }

    private IEnumerator RestartRoomNextFrame()
    {
        yield return null;

        automaticRestartCoroutine = null;

        if (restartingDialogue)
            yield break;

        if (awaitingOptions)
            yield break;

        RestartRoom();
    }

    public void Typewriter(
        string text,
        bool skip = false,
        bool unskippable = false,
        Action onComplete = null)
    {
        string processedText =
            ReplaceTagsWithColors(text) +
            "\n";

        TypewriterJob job =
            new TypewriterJob
            {
                Text = processedText,
                Skip = skip,
                Unskippable = unskippable,
                OnComplete = onComplete
            };

        typewriterQueue.Enqueue(job);

        if (!typewriterQueueRunning)
        {
            typewriterQueueRunning = true;

            StartCoroutine(
                ProcessTypewriterQueue()
            );
        }
    }

    private IEnumerator ProcessTypewriterQueue()
    {
        while (typewriterQueue.Count > 0)
        {
            TypewriterJob job =
                typewriterQueue.Dequeue();

            activeTypewriterJob = job;

            isTyping = true;
            isRevealingText = !job.Skip;
            skipRequested = false;

            string finalText =
                m_result.text +
                job.Text;

            if (job.Skip)
            {
                m_result.text = finalText;

                ScrollToBottom();
            }
            else
            {
                bool insideMarkupTag = false;

                foreach (char letter in job.Text)
                {
                    if (
                        skipRequested &&
                        !job.Unskippable)
                    {
                        m_result.text = finalText;

                        ScrollToBottom();

                        break;
                    }

                    if (
                        letter == '<' ||
                        letter == '>' ||
                        insideMarkupTag)
                    {
                        if (letter == '<')
                        {
                            insideMarkupTag = true;
                        }
                        else if (letter == '>')
                        {
                            insideMarkupTag = false;
                        }

                        m_result.text += letter;

                        continue;
                    }

                    if (
                        m_typingSFX != null &&
                        typingSFXClip != null)
                    {
                        m_typingSFX.PlayOneShot(
                            typingSFXClip
                        );
                    }

                    m_result.text += letter;

                    ScrollToBottom();

                    yield return new WaitForSeconds(
                        typewriterWait
                    );
                }
            }

            isRevealingText = false;

            yield return new WaitForSeconds(
                typewriterLineWait
            );

            isTyping = false;
            skipRequested = false;
            activeTypewriterJob = null;

            Action completionCallback =
                job.OnComplete;

            completionCallback?.Invoke();
        }

        typewriterQueueRunning = false;
        isTyping = false;
        isRevealingText = false;

        TryEnableCommandInput();
    }

    public void SkipText()
    {
        if (activeTypewriterJob == null)
            return;

        if (!isRevealingText)
            return;

        if (activeTypewriterJob.Unskippable)
            return;

        if (skipRequested)
            return;

        skipRequested = true;

        if (
            m_typingSFX != null &&
            typingSkipClip != null)
        {
            m_typingSFX.PlayOneShot(
                typingSkipClip
            );
        }
    }

    private void ScrollToBottom()
    {
        if (m_scrollRect == null)
            return;

        Canvas.ForceUpdateCanvases();

        m_scrollRect.verticalNormalizedPosition = 0f;
    }

    private bool IsTopmostUnderPointer()
    {
        if (targetWindowRoot == null)
            return false;

        if (EventSystem.current == null)
            return false;

        PointerEventData pointer =
            new PointerEventData(
                EventSystem.current
            );

        pointer.position =
            Input.mousePosition;

        List<RaycastResult> results =
            new List<RaycastResult>();

        EventSystem.current.RaycastAll(
            pointer,
            results
        );

        if (results.Count == 0)
            return false;

        GameObject topHit =
            results[0].gameObject;

        return
            topHit.transform ==
            targetWindowRoot ||
            topHit.transform.IsChildOf(
                targetWindowRoot
            );
    }

    private void CheckLinkHover()
    {
        if (m_result == null)
            return;

        m_result.ForceMeshUpdate();

        Camera cam =
            GetCanvasCamera();

        int linkIndex =
            TMP_TextUtilities.FindIntersectingLink(
                m_result,
                Input.mousePosition,
                cam
            );

        if (linkIndex != -1)
        {
            if (!cursorOverLink)
            {
                cursorOverLink = true;

                Cursor.SetCursor(
                    clickableCursor,
                    Vector2.zero,
                    CursorMode.Auto
                );
            }
        }
        else if (cursorOverLink)
        {
            cursorOverLink = false;

            Cursor.SetCursor(
                defaultCursor,
                Vector2.zero,
                CursorMode.Auto
            );
        }
    }

    private void CheckLinkClick()
    {
        if (m_result == null)
            return;

        m_result.ForceMeshUpdate();

        Camera cam =
            GetCanvasCamera();

        int linkIndex =
            TMP_TextUtilities.FindIntersectingLink(
                m_result,
                Input.mousePosition,
                cam
            );

        if (linkIndex == -1)
            return;

        TMP_LinkInfo linkInfo =
            m_result
                .textInfo
                .linkInfo[linkIndex];

        string linkId =
            linkInfo
                .GetLinkID()
                .Trim();

        Sprite sprite =
            Resources.Load<Sprite>(
                $"Images/{linkId}"
            );

        if (sprite != null)
        {
            OpenImagePopup(sprite);
        }
        else
        {
            Debug.LogError(
                $"Sprite '{linkId}' not found in Resources/Images/"
            );
        }
    }

    private void OpenImagePopup(Sprite sprite)
    {
        if (sprite == null)
        {
            Debug.LogError(
                "[IMAGE POPUP] Cannot open image popup — sprite was null."
            );

            return;
        }

        if (m_image == null)
        {
            Debug.LogError(
                "[IMAGE POPUP] Image component is not assigned."
            );

            return;
        }

        if (m_ImageWindowController == null)
        {
            Debug.LogError(
                "[IMAGE POPUP] WindowController is not assigned."
            );

            return;
        }

        m_image.sprite = sprite;

        m_ImageWindowController.Maximize();
    }

    private Camera GetCanvasCamera()
    {
        if (m_result == null)
            return null;

        Canvas canvas =
            m_result.canvas;

        if (canvas == null)
            return null;

        if (
            canvas.renderMode ==
            RenderMode.ScreenSpaceOverlay)
        {
            return null;
        }

        if (canvas.worldCamera != null)
        {
            return canvas.worldCamera;
        }

        return Camera.main;
    }

    private int GetOptionID(string command)
    {
        if (dialogueOptions == null)
            return -1;

        if (string.IsNullOrEmpty(command))
            return -1;

        foreach (
            DialogueOption option
            in dialogueOptions)
        {
            if (
                command.Equals(
                    option.Line.RawText,
                    StringComparison.OrdinalIgnoreCase
                ))
            {
                return option.DialogueOptionID;
            }
        }

        return -1;
    }

    private string GetBasicCommand(string command)
    {
        if (string.IsNullOrEmpty(command))
            return command;

        if (commands == null)
            return command;

        foreach (List<string> cmds in commands)
        {
            if (cmds == null)
                continue;

            if (cmds.Contains(command))
            {
                return cmds.First();
            }
        }

        return command;
    }

    public string RemoveFluffWords(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        List<string> inputWords =
            input
                .Split(
                    new[] { ' ' },
                    StringSplitOptions.RemoveEmptyEntries
                )
                .ToList();

        inputWords.RemoveAll(
            word =>
                fluff.Contains(
                    word.ToLowerInvariant()
                )
        );

        return string.Join(
            " ",
            inputWords
        );
    }

    public string CorrectSentence(string sentence)
    {
        if (string.IsNullOrEmpty(sentence))
            return sentence;

        IEnumerable<string> correctedWords =
            sentence
                .Split(' ')
                .Select(
                    word =>
                    {
                        string special =
                            specialChecker
                                .GetBestCorrection(
                                    word
                                );

                        string corrected =
                            spellChecker
                                .GetBestCorrection(
                                    special
                                );

                        return corrected ?? word;
                    }
                );

        return string.Join(
            " ",
            correctedWords
        );
    }

    public string[] FlattenListArray(
        List<string>[] listOfLists)
    {
        if (listOfLists == null)
        {
            return Array.Empty<string>();
        }

        return listOfLists
            .Where(
                list => list != null
            )
            .SelectMany(
                list => list
            )
            .ToArray();
    }

    public string HighlightWords(
        string[] options,
        string s)
    {
        if (
            options == null ||
            string.IsNullOrEmpty(s))
        {
            return s;
        }

        if (
            !tagColorMap.ContainsKey(
                "interactable"))
        {
            return s;
        }

        string colorTag =
            $"<color=#{ColorUtility.ToHtmlStringRGB(tagColorMap["interactable"])}>";

        string closeTag =
            "</color>";

        foreach (string option in options)
        {
            if (string.IsNullOrEmpty(option))
                continue;

            string pattern =
                $@"(?<!{Regex.Escape(colorTag)})\b{Regex.Escape(option)}\b(?!{Regex.Escape(closeTag)})";

            s = Regex.Replace(
                s,
                pattern,
                match =>
                    colorTag +
                    match.Value +
                    closeTag
            );
        }

        return s;
    }

    public static string[] ProcessDialogueOptions(
        DialogueOption[] dialogueOptions)
    {
        if (dialogueOptions == null)
        {
            return Array.Empty<string>();
        }

        return dialogueOptions
            .Select(
                option =>
                {
                    string[] optionWords =
                        option.Line.RawText.Split(
                            new[] { ' ' },
                            2,
                            StringSplitOptions.RemoveEmptyEntries
                        );

                    return
                        optionWords.Length > 1
                            ? optionWords[1].Trim()
                            : "";
                }
            )
            .ToArray();
    }

    public void CommandSelect()
    {
        if (m_command == null)
            return;

        m_command.interactable = true;

        if (!m_command.gameObject.activeInHierarchy)
            return;

        RequestCommandFocus();
    }

    public void CommandDeselect()
    {
        if (m_command == null)
            return;

        if (focusCoroutine != null)
        {
            StopCoroutine(focusCoroutine);
            focusCoroutine = null;
        }

        m_command.interactable = false;

        if (EventSystem.current != null &&
            EventSystem.current.currentSelectedGameObject ==
            m_command.gameObject)
        {
            EventSystem.current.SetSelectedGameObject(
                null
            );
        }
    }
}
