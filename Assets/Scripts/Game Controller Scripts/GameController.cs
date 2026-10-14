using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
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

    [Header("Session Loading Screen")]
    public SessionLoadingScreen loadingScreen;

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
    [SerializeField] private List<TagColor> tagColorList = new List<TagColor>();

    [Header("Defined Values")]
    [SerializeField]
    private string[] fluff =
    {
        "the", "and", "is", "in", "at", "of", "a", "to"
    };
    [SerializeField] private string systemName;

    [Header("Cursors")]
    [SerializeField] private Texture2D defaultCursor;
    [SerializeField] private Texture2D clickableCursor;

    private string playingTrackName;

    private class TypewriterJob
    {
        public string Text;
        public bool Skip;
        public bool Unskippable;
        public Action OnComplete;
        public int CharactersWritten;
        public int ResumeAt;
        public bool IsYarnLine;
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
        Cursor.SetCursor(defaultCursor, Vector2.zero, CursorMode.Auto);

        tagColorMap = new Dictionary<string, Color>();
        foreach (TagColor entry in tagColorList)
        {
            if (string.IsNullOrEmpty(entry.tag) || tagColorMap.ContainsKey(entry.tag))
                continue;
            tagColorMap.Add(entry.tag, entry.color);
        }

        rooms = new Stack<string>();
        if (m_command != null)
        {
            m_command.lineType = TMP_InputField.LineType.SingleLine;
            m_command.onSubmit.AddListener(OnCommandSubmitted);
        }
        CommandDeselect();
    }

    private void Start()
    {
        words = CSVReader.LoadCSVOneColumn("words.csv");
        commands = CSVReader.LoadCSV("data.csv");
        spellChecker = new SpellChecker(words);
        specialChecker = new SpellChecker(FlattenListArray(commands));

        // The restore coroutine MUST be hosted by GameSaveSystem, NOT by this
        // DialogueViewBase: DialogueRunner.StartDialogue calls StopAllCoroutines
        // on every dialogue view when changing the node.
        GameSaveSystem saves = GetComponent<GameSaveSystem>();
        if (saves == null)
            saves = gameObject.AddComponent<GameSaveSystem>();
        saves.BeginSession(this, RestoreSavedSession());
    }

    private void Update()
    {
        HandleDialogueSkipInput();
        HandleLinkInteraction();
        UpdateLocationText();
        TryRestoreDeferredCaret();
        // Loading overlay animation/fade now runs in SessionLoadingScreen.Update().
    }

    private void HandleDialogueSkipInput()
    {
        if (!isRevealingText) return;
        if (m_command != null && m_command.interactable) return;
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            SkipText();
    }

    private void HandleLinkInteraction()
    {
        bool isTopmost = IsTopmostUnderPointer();
        if (isTopmost)
            CheckLinkHover();
        else if (cursorOverLink)
        {
            cursorOverLink = false;
            Cursor.SetCursor(defaultCursor, Vector2.zero, CursorMode.Auto);
        }

        if (Input.GetMouseButtonDown(0) && isTopmost)
            mouseDownPos = Input.mousePosition;

        if (Input.GetMouseButtonUp(0) && isTopmost)
        {
            float distance = Vector2.Distance(mouseDownPos, Input.mousePosition);
            if (distance < dragThreshold)
                CheckLinkClick();
        }
    }

    private void UpdateLocationText()
    {
        if (m_locationText == null) return;
        string location = rooms != null && rooms.Count > 0 ? rooms.Peek() : "Not Connected";
        m_locationText.text = $"{(CheckRoom() == "" ? "NOT CONNECTED" : CheckRoom())}";
    }

    private void OnDisable()
    {
        if (loadingScreen != null)
            loadingScreen.HideImmediately();

        if (cursorOverLink)
        {
            cursorOverLink = false;
            Cursor.SetCursor(defaultCursor, Vector2.zero, CursorMode.Auto);
        }
    }

    private void OnDestroy()
    {
        if (m_command != null)
            m_command.onSubmit.RemoveListener(OnCommandSubmitted);
    }

    private void OnCommandSubmitted(string value)
    {
        if (m_command == null || !m_command.interactable || !awaitingOptions ||
            isTyping || typewriterQueueRunning)
            return;
        SubmitCommand();
    }

    public void SubmitCommand()
    {
        if (m_command == null || !m_command.interactable || !awaitingOptions ||
            isTyping || typewriterQueueRunning)
            return;

        string commandText = m_command.text.Trim();
        if (string.IsNullOrEmpty(commandText))
        {
            RequestCommandFocus();
            return;
        }

        CommandDeselect();
        m_command.SetTextWithoutNotify("");
        Typewriter($"<command>>{commandText}</command>", true);

        if (dialogueOptions != null && dialogueOptions.Length > 0)
            HandleDialogueCommand(commandText);
        else
            TryEnableCommandInput();
    }

    private void RequestCommandFocus()
    {
        if (focusCoroutine != null)
            StopCoroutine(focusCoroutine);
        focusCoroutine = StartCoroutine(RestoreCommandFocus());
    }

    private IEnumerator RestoreCommandFocus()
    {
        yield return null;
        focusCoroutine = null;
        if (m_command == null || !m_command.interactable ||
            !m_command.gameObject.activeInHierarchy)
            yield break;

        if (EventSystem.current != null)
        {
            GameObject selected = EventSystem.current.currentSelectedGameObject;
            if (selected != null && selected != m_command.gameObject)
                yield break;
            EventSystem.current.SetSelectedGameObject(m_command.gameObject);
        }
        m_command.ActivateInputField();
    }

    private void TryEnableCommandInput()
    {
        if (!awaitingOptions || restartingDialogue || isTyping ||
            typewriterQueueRunning || typewriterQueue.Count > 0)
            return;
        CommandSelect();
    }

    [YarnCommand("playsfx")]
    public void PlaySFX(string sfxName)
    {
        AudioClip clip = Resources.Load<AudioClip>($"SFX/{sfxName}");
        if (clip != null)
        {
            if (m_gameSFX != null)
                m_gameSFX.PlayOneShot(clip);
        }
        else
            Debug.LogWarning($"SFX '{sfxName}' not found in Resources/SFX/");
    }

    private string CheckRoom()
    {
        if (m_runner == null) return "";
        string currentRoom = m_runner.CurrentNodeName;
        if (string.IsNullOrEmpty(currentRoom)) return "";
        if (string.Equals(currentRoom, "start", StringComparison.OrdinalIgnoreCase))
            return "";

        rooms.Push(currentRoom);
        string[] splitWords = currentRoom.Trim().Split(
            new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
        string toType = "";
        string roomName = "";
        for (int i = 1; i < splitWords.Length; i++)
            roomName += splitWords[i] + " ";

        roomName = roomName == "" ? currentRoom : roomName.Trim();

        switch (splitWords[0])
        {
            case "location": toType = $"CURRENT LOCATION: {roomName}"; break;
            case "person": toType = $"SPEAKING TO: {roomName}"; break;
            case "object": toType = $"INSPECTING: {roomName}"; break;
            default: toType = $"{roomName}"; break;
        }
        return toType;
    }

    private void TypeRoom()
    {
        string room = CheckRoom();
        if (room != "")
            Typewriter($"<system>{systemName}: " + room + "</system>");
    }

    public override void RunLine(LocalizedLine dialogueLine, Action onDialogueLineFinished)
    {
        // Index relative to latest selected Yarn option; needed by save replay.
        lineOrdinalSinceChoice++;
        if (restoringSession)
        {
            if (restorePendingYarnLine && pendingRestoreLineCallback == null &&
                replayedChoiceCount == restoreChoiceCount &&
                lineOrdinalSinceChoice == restoreTargetLineOrdinal &&
                (string.IsNullOrEmpty(restoreTargetNode) || m_runner == null ||
                 m_runner.CurrentNodeName == restoreTargetNode))
            {
                // Keep the original Yarn completion callback for the resumed line.
                pendingRestoreLineCallback = onDialogueLineFinished;
                return;
            }
            onDialogueLineFinished?.Invoke();
            return;
        }

        string charName = dialogueLine.CharacterName;
        Yarn.Markup.MarkupParseResult text = dialogueLine.Text;
        bool nocharacter = dialogueLine.Metadata?.Contains("nocharacter") == true;
        bool nochevron = dialogueLine.Metadata?.Contains("nochevron") == true;
        bool unskippable = dialogueLine.Metadata?.Contains("unskippable") == true;
        string output;

        if (!string.IsNullOrEmpty(charName) && !nocharacter)
        {
            if (charName == systemName)
            {
                output = $"<system>" + $"{(!nochevron ? "> " : "")}" +
                    $"{PaletteMarkedUpText(text, m_palette, true)}" + $"</system>";
            }
            else
            {
                output = $"<dialogue>" + $"{(!nochevron ? "> " : "")}" +
                    $"{PaletteMarkedUpText(text, m_palette, true)}" + $"</dialogue>";
            }
        }
        else
        {
            output = $"<info>" + $"{(!nochevron ? "> " : "")}" +
                $"{PaletteMarkedUpText(text, m_palette, true)}" + $"</info>";
        }

        Typewriter(output, false, unskippable, onDialogueLineFinished, true);
    }

    public override void RunOptions(DialogueOption[] dialogueOptions, Action<int> onOptionSelected)
    {
        awaitingOptions = true;
        this.dialogueOptions = dialogueOptions;
        this.onOptionSelected = onOptionSelected;
        if (!restoringSession)
            TryEnableCommandInput();
    }

    public override void DialogueComplete()
    {
        if (restoringSession || suppressDialogueComplete || restartingDialogue ||
            awaitingOptions || automaticRestartCoroutine != null)
            return;
        automaticRestartCoroutine = StartCoroutine(RestartRoomNextFrame());
    }

    public string PaletteMarkedUpText(
        Yarn.Markup.MarkupParseResult line,
        MarkupPalette palette,
        bool applyLineBreaks = true)
    {
        string lineOfText = line.Text;
        line.Attributes.Sort((a, b) => b.Position.CompareTo(a.Position));

        foreach (var attribute in line.Attributes)
        {
            Color markerColour;
            if (attribute.Name == "i")
            {
                lineOfText = lineOfText
                    .Insert(attribute.Position + attribute.Length, "</i>")
                    .Insert(attribute.Position, "<i>");
            }
            else if (attribute.Name == "b")
            {
                lineOfText = lineOfText
                    .Insert(attribute.Position + attribute.Length, "</b>")
                    .Insert(attribute.Position, "<b>");
            }
            else if (palette != null &&
                palette.ColorForMarker(attribute.Name, out markerColour))
            {
                string htmlColor = ColorUtility.ToHtmlStringRGB(markerColour);
                lineOfText = lineOfText
                    .Insert(attribute.Position + attribute.Length, "</color>")
                    .Insert(attribute.Position, $"<color=#{htmlColor}>");
            }
            else if (applyLineBreaks && attribute.Name == "br")
                lineOfText = lineOfText.Insert(attribute.Position, "<br>");
        }
        return lineOfText;
    }

    public string ReplaceTagsWithColors(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        input = WrapInteractables(input);
        foreach (KeyValuePair<string, Color> kvp in tagColorMap)
        {
            string tag = kvp.Key;
            string htmlColor = ColorUtility.ToHtmlStringRGB(kvp.Value);
            input = ReplaceNestedTags(input, tag, $"<color=#{htmlColor}>", "</color>");
        }
        return input;
    }

    private string ReplaceNestedTags(
        string input, string tag, string openReplacement, string closeReplacement)
    {
        string escapedTag = Regex.Escape(tag);
        string pattern = $@"<{escapedTag}>(.*?)</{escapedTag}>";
        while (Regex.IsMatch(input, pattern, RegexOptions.Singleline))
        {
            input = Regex.Replace(input, pattern, match =>
            {
                string innerContent = match.Groups[1].Value;
                innerContent = ReplaceNestedTags(
                    innerContent, tag, openReplacement, closeReplacement);
                return $"{openReplacement}" + $"{innerContent}" + $"{closeReplacement}";
            }, RegexOptions.Singleline);
        }
        return input;
    }

    private string WrapInteractables(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        string pattern = @"(?<!<interactable>)(<link=([^>]+)>.*?<\/link>)(?!<\/interactable>)";
        return Regex.Replace(
            input, pattern, "<interactable>$1</interactable>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
    }

    private void HandleDialogueCommand(string commandText)
    {
        string originalCommand = commandText.ToLowerInvariant().Trim();
        string command = originalCommand;
        int waitingInputOption = GetOptionID("waitinginput");

        if (waitingInputOption != -1)
        {
            if (commandText.Length > 50 || commandText.Length < 1 ||
                Regex.IsMatch(commandText, @"[\d\W]"))
            {
                Typewriter($"<error>INVALID INPUT: {commandText}</error>");
                return;
            }
            m_runner.VariableStorage.SetValue("$inputVariable", commandText);
            SelectDialogueOption(waitingInputOption);
            return;
        }

        int exactResult = GetOptionID(command);
        if (exactResult != -1)
        {
            SelectDialogueOption(exactResult);
            return;
        }

        int oneWordResult = CheckOption(command, "");
        if (oneWordResult != -1)
        {
            SelectDialogueOption(oneWordResult);
            return;
        }

        bool blockFreeInput = false;
        m_runner.VariableStorage.TryGetValue<bool>("$blockFreeInput", out blockFreeInput);
        if (blockFreeInput)
        {
            Typewriter($"<error>INVALID INPUT: {commandText}</error>");
            return;
        }

        if (command == "location")
        {
            string currentRoom = rooms != null && rooms.Count > 0
                ? rooms.Peek() : "Not Connected";
            string roomName = currentRoom;
            if (currentRoom.StartsWith("location_", StringComparison.OrdinalIgnoreCase))
                roomName = currentRoom.Substring(9);
            roomName = roomName.Replace("_", " ");
            Typewriter($"<system>{systemName}: CURRENT LOCATION: {roomName}</system>");
            return;
        }

        if (command == "go back")
        {
            if (rooms == null || rooms.Count <= 1)
            {
                Typewriter("<error>Nothing to go back to...</error>");
                return;
            }
            rooms.Pop();
            Typewriter($"<system>{systemName}: CURRENT LOCATION: {rooms.Peek()}</system>");
            RestartRoom();
            return;
        }

        string[] fluffSplitWords = command.Trim().Split(
            new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        command = RemoveFluffWords(command);
        string[] splitWords = command.Trim().Split(
            new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        if (splitWords.Length == 0)
        {
            Typewriter($"<error>Option '{originalCommand}' is not a valid option</error>");
            return;
        }
        if (splitWords.Length > 8)
        {
            Typewriter($"<error>'{command}' is not a valid option</error>");
            return;
        }

        if (splitWords[0] == "play")
        {
            string track = "";
            for (int i = 1; i < fluffSplitWords.Length; i++)
                track += fluffSplitWords[i] + " ";
            track = track.Trim();
            StartCoroutine(PlayTrack(track));
            return;
        }

        if (splitWords.Length == 1)
        {
            oneWordResult = CheckOption(splitWords[0], "");
            if (oneWordResult != -1)
            {
                SelectDialogueOption(oneWordResult);
                return;
            }
        }

        for (int i = 1; i < splitWords.Length; i++)
        {
            string cmd = string.Join(" ", splitWords.Take(i));
            string subject = string.Join(" ", splitWords.Skip(i));
            int commandResult = CheckOption(cmd, subject);
            if (commandResult != -1)
            {
                SelectDialogueOption(commandResult);
                return;
            }
        }
        Typewriter($"<error>Option '{originalCommand}' is not a valid option</error>");
    }

    private IEnumerator PlayTrack(string trackName)
    {
        foreach (MusicTrack musicTrack in musicTracks)
        {
            if (musicTrack.name.Equals(trackName, StringComparison.OrdinalIgnoreCase))
            {
                if (m_gameMusic != null)
                {
                    m_gameMusic.clip = musicTrack.audioClip;
                    m_gameMusic.Play();
                    playingTrackName = trackName;
                }
                if (notificationCoroutine != null)
                    StopCoroutine(notificationCoroutine);
                notificationCoroutine = StartCoroutine(ShowMusicNotification(trackName));
                yield break;
            }
        }
        Typewriter($"<error>There is no track named '{trackName}'</error>");
    }

    private IEnumerator ShowMusicNotification(string trackName)
    {
        if (notificationUI == null || notificationText == null ||
            notificationRect == null || notificationCanvasGroup == null)
            yield break;

        notificationText.text = trackName;
        notificationCanvasGroup.alpha = 0f;
        notificationUI.SetActive(true);
        yield return null;
        Canvas.ForceUpdateCanvases();

        Vector2 visiblePosition = notificationRect.anchoredPosition;
        Vector2 hiddenPosition = visiblePosition +
            Vector2.right * notificationSlideDistance;
        notificationRect.anchoredPosition = hiddenPosition;
        notificationCanvasGroup.alpha = 0f;
        yield return null;

        float elapsed = 0f;
        while (elapsed < notificationAnimTime)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / notificationAnimTime);
            float eased = Mathf.SmoothStep(0f, 1f, t);
            notificationRect.anchoredPosition = Vector2.Lerp(
                hiddenPosition, visiblePosition, eased);
            notificationCanvasGroup.alpha = eased;
            yield return null;
        }

        notificationRect.anchoredPosition = visiblePosition;
        notificationCanvasGroup.alpha = 1f;
        yield return new WaitForSecondsRealtime(notificationTime);

        elapsed = 0f;
        while (elapsed < notificationAnimTime)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / notificationAnimTime);
            float eased = Mathf.SmoothStep(0f, 1f, t);
            notificationRect.anchoredPosition = Vector2.Lerp(
                visiblePosition, hiddenPosition, eased);
            notificationCanvasGroup.alpha = 1f - eased;
            yield return null;
        }

        notificationCanvasGroup.alpha = 0f;
        notificationRect.anchoredPosition = visiblePosition;
        notificationUI.SetActive(false);
        notificationCoroutine = null;
    }

    private void SelectDialogueOption(int optionID)
    {
        Action<int> callback = onOptionSelected;
        if (callback == null) return;

        // Yarn Spinner does not publicly serialize its VM instruction pointer.
        // Record the actual chosen option path so we can rebuild that pointer.
        if (!restoringSession)
        {
            if (choiceHistory.Count >= 512)
            {
                historyStartNode = m_runner != null
                    ? m_runner.CurrentNodeName : historyStartNode;
                choiceHistory.Clear();
            }
            string input = "";
            if (m_runner != null && m_runner.VariableStorage != null)
                m_runner.VariableStorage.TryGetValue<string>("$inputVariable", out input);
            choiceHistory.Add(new GameSaveSystem.ChoiceState
            {
                node = m_runner != null ? m_runner.CurrentNodeName : "",
                optionId = optionID,
                inputValue = input
            });
        }

        lineOrdinalSinceChoice = 0;
        if (restoringSession) replayedChoiceCount++;
        ResetCommandState();
        callback.Invoke(optionID);
    }

    private int CheckOption(string cmd, string subject)
    {
        string command = $"{cmd} {subject}".Trim();
        int basicInput = GetOptionID(command);
        if (basicInput != -1) return basicInput;
        if (spellChecker == null) return -1;

        string correctedCommand = spellChecker.GetBestCorrection(cmd.Trim());
        string commandWord = GetBasicCommand(correctedCommand);
        string subjectWord = string.IsNullOrWhiteSpace(subject)
            ? "" : spellChecker.GetBestCorrection(subject.Trim());
        command = $"{commandWord} {subjectWord}".Trim();
        return GetOptionID(command);
    }

    private void RestartDialogue(string nodeName)
    {
        if (string.IsNullOrEmpty(nodeName))
        {
            Debug.LogWarning("Cannot restart dialogue because node name is empty.");
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
        restartCoroutine = StartCoroutine(RestartDialogueCoroutine(nodeName));
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

        // Yarn's StartDialogue stops ALL coroutines on dialogue views.
        typewriterQueue.Clear();
        activeTypewriterJob = null;
        typewriterQueueRunning = false;
        isTyping = false;
        isRevealingText = false;
        skipRequested = false;
        suppressDialogueComplete = false;
        restartingDialogue = false;
        restartCoroutine = null;
        m_runner.StartDialogue(nodeName);
    }

    private void ResetCommandState()
    {
        dialogueOptions = null;
        onOptionSelected = null;
        awaitingOptions = false;
        CommandDeselect();
        if (m_command != null)
            m_command.SetTextWithoutNotify("");
    }

    private void RestartRoom()
    {
        string currentRoom = null;
        if (rooms != null && rooms.Count > 0)
            currentRoom = rooms.Peek();
        else if (m_runner != null && !string.IsNullOrEmpty(m_runner.CurrentNodeName))
            currentRoom = m_runner.CurrentNodeName;

        if (string.IsNullOrEmpty(currentRoom))
        {
            Debug.LogWarning("Cannot restart room because no current Yarn node exists.");
            return;
        }
        RestartDialogue(currentRoom);
    }

    private IEnumerator RestartRoomNextFrame()
    {
        yield return null;
        automaticRestartCoroutine = null;
        if (restartingDialogue || awaitingOptions) yield break;
        RestartRoom();
    }

    public void Typewriter(
        string text,
        bool skip = false,
        bool unskippable = false,
        Action onComplete = null,
        bool yarnLine = false)
    {
        if (restoringSession)
        {
            onComplete?.Invoke();
            return;
        }
        string processedText = ReplaceTagsWithColors(text) + "\n";
        TypewriterJob job = new TypewriterJob
        {
            Text = processedText,
            Skip = skip,
            Unskippable = unskippable,
            OnComplete = onComplete,
            IsYarnLine = yarnLine
        };
        typewriterQueue.Enqueue(job);
        if (!typewriterQueueRunning)
        {
            typewriterQueueRunning = true;
            StartCoroutine(ProcessTypewriterQueue());
        }
    }

    private IEnumerator ProcessTypewriterQueue()
    {
        while (typewriterQueue.Count > 0)
        {
            TypewriterJob job = typewriterQueue.Dequeue();
            activeTypewriterJob = job;
            isTyping = true;
            isRevealingText = !job.Skip;
            skipRequested = false;

            int resumeAt = Mathf.Clamp(job.ResumeAt, 0, job.Text.Length);
            job.CharactersWritten = resumeAt;
            string finalText = m_result.text + job.Text.Substring(resumeAt);
            if (job.Skip)
            {
                m_result.text = finalText;
                job.CharactersWritten = job.Text.Length;
                ScrollToBottom();
            }
            else
            {
                bool insideMarkupTag = false;
                for (int i = resumeAt; i < job.Text.Length; i++)
                {
                    char letter = job.Text[i];
                    if (skipRequested && !job.Unskippable)
                    {
                        m_result.text = finalText;
                        job.CharactersWritten = job.Text.Length;
                        ScrollToBottom();
                        break;
                    }

                    if (letter == '<' || letter == '>' || insideMarkupTag)
                    {
                        if (letter == '<') insideMarkupTag = true;
                        else if (letter == '>') insideMarkupTag = false;
                        m_result.text += letter;
                        job.CharactersWritten = i + 1;
                        continue;
                    }

                    if (m_typingSFX != null && typingSFXClip != null)
                        m_typingSFX.PlayOneShot(typingSFXClip);

                    m_result.text += letter;
                    job.CharactersWritten = i + 1;
                    ScrollToBottom();
                    yield return new WaitForSeconds(typewriterWait);
                }
            }

            isRevealingText = false;
            yield return new WaitForSeconds(typewriterLineWait);
            isTyping = false;
            skipRequested = false;
            activeTypewriterJob = null;
            Action completionCallback = job.OnComplete;
            completionCallback?.Invoke();
        }
        typewriterQueueRunning = false;
        isTyping = false;
        isRevealingText = false;
        TryEnableCommandInput();
    }

    public void SkipText()
    {
        if (activeTypewriterJob == null || !isRevealingText ||
            activeTypewriterJob.Unskippable || skipRequested)
            return;
        skipRequested = true;
        if (m_typingSFX != null && typingSkipClip != null)
            m_typingSFX.PlayOneShot(typingSkipClip);
    }

    private void ScrollToBottom()
    {
        if (m_scrollRect == null) return;
        Canvas.ForceUpdateCanvases();
        m_scrollRect.verticalNormalizedPosition = 0f;
    }

    private bool IsTopmostUnderPointer()
    {
        if (targetWindowRoot == null || EventSystem.current == null)
            return false;
        PointerEventData pointer = new PointerEventData(EventSystem.current);
        pointer.position = Input.mousePosition;
        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, results);
        if (results.Count == 0) return false;
        GameObject topHit = results[0].gameObject;
        return topHit.transform == targetWindowRoot ||
               topHit.transform.IsChildOf(targetWindowRoot);
    }

    private void CheckLinkHover()
    {
        if (m_result == null) return;
        m_result.ForceMeshUpdate();
        Camera cam = GetCanvasCamera();
        int linkIndex = TMP_TextUtilities.FindIntersectingLink(
            m_result, Input.mousePosition, cam);
        if (linkIndex != -1)
        {
            if (!cursorOverLink)
            {
                cursorOverLink = true;
                Cursor.SetCursor(clickableCursor, Vector2.zero, CursorMode.Auto);
            }
        }
        else if (cursorOverLink)
        {
            cursorOverLink = false;
            Cursor.SetCursor(defaultCursor, Vector2.zero, CursorMode.Auto);
        }
    }

    private void CheckLinkClick()
    {
        if (m_result == null) return;
        m_result.ForceMeshUpdate();
        Camera cam = GetCanvasCamera();
        int linkIndex = TMP_TextUtilities.FindIntersectingLink(
            m_result, Input.mousePosition, cam);
        if (linkIndex == -1) return;
        TMP_LinkInfo linkInfo = m_result.textInfo.linkInfo[linkIndex];
        string linkId = linkInfo.GetLinkID().Trim();
        Sprite sprite = Resources.Load<Sprite>($"Images/{linkId}");
        if (sprite != null)
            OpenImagePopup(sprite);
        else
            Debug.LogError($"Sprite '{linkId}' not found in Resources/Images/");
    }

    private void OpenImagePopup(Sprite sprite)
    {
        if (sprite == null)
        {
            Debug.LogError("[IMAGE POPUP] Cannot open image popup — sprite was null.");
            return;
        }
        if (m_image == null)
        {
            Debug.LogError("[IMAGE POPUP] Image component is not assigned.");
            return;
        }
        if (m_ImageWindowController == null)
        {
            Debug.LogError("[IMAGE POPUP] WindowController is not assigned.");
            return;
        }
        m_image.sprite = sprite;
        m_ImageWindowController.Maximize();
    }

    private Camera GetCanvasCamera()
    {
        if (m_result == null) return null;
        Canvas canvas = m_result.canvas;
        if (canvas == null) return null;
        if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            return null;
        if (canvas.worldCamera != null)
            return canvas.worldCamera;
        return Camera.main;
    }

    private int GetOptionID(string command)
    {
        if (dialogueOptions == null || string.IsNullOrEmpty(command))
            return -1;
        foreach (DialogueOption option in dialogueOptions)
        {
            if (command.Equals(option.Line.RawText, StringComparison.OrdinalIgnoreCase))
                return option.DialogueOptionID;
        }
        return -1;
    }

    private string GetBasicCommand(string command)
    {
        if (string.IsNullOrEmpty(command) || commands == null)
            return command;
        foreach (List<string> cmds in commands)
        {
            if (cmds == null) continue;
            if (cmds.Contains(command)) return cmds.First();
        }
        return command;
    }

    public string RemoveFluffWords(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        List<string> inputWords = input.Split(
            new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        inputWords.RemoveAll(word => fluff.Contains(word.ToLowerInvariant()));
        return string.Join(" ", inputWords);
    }

    public string CorrectSentence(string sentence)
    {
        if (string.IsNullOrEmpty(sentence)) return sentence;
        IEnumerable<string> correctedWords = sentence.Split(' ').Select(word =>
        {
            string special = specialChecker.GetBestCorrection(word);
            string corrected = spellChecker.GetBestCorrection(special);
            return corrected ?? word;
        });
        return string.Join(" ", correctedWords);
    }

    public string[] FlattenListArray(List<string>[] listOfLists)
    {
        if (listOfLists == null) return Array.Empty<string>();
        return listOfLists.Where(list => list != null)
            .SelectMany(list => list).ToArray();
    }

    public string HighlightWords(string[] options, string s)
    {
        if (options == null || string.IsNullOrEmpty(s)) return s;
        if (!tagColorMap.ContainsKey("interactable")) return s;
        string colorTag =
            $"<color=#{ColorUtility.ToHtmlStringRGB(tagColorMap["interactable"])}>";
        string closeTag = "</color>";
        foreach (string option in options)
        {
            if (string.IsNullOrEmpty(option)) continue;
            string pattern =
                $@"(?<!{Regex.Escape(colorTag)})\b{Regex.Escape(option)}\b(?!{Regex.Escape(closeTag)})";
            s = Regex.Replace(s, pattern, match => colorTag + match.Value + closeTag);
        }
        return s;
    }

    public static string[] ProcessDialogueOptions(DialogueOption[] dialogueOptions)
    {
        if (dialogueOptions == null) return Array.Empty<string>();
        return dialogueOptions.Select(option =>
        {
            string[] optionWords = option.Line.RawText.Split(
                new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            return optionWords.Length > 1 ? optionWords[1].Trim() : "";
        }).ToArray();
    }

    public void CommandSelect()
    {
        if (m_command == null) return;
        m_command.interactable = true;
        if (!m_command.gameObject.activeInHierarchy) return;
        RequestCommandFocus();
    }

    public void CommandDeselect()
    {
        if (m_command == null) return;
        if (focusCoroutine != null)
        {
            StopCoroutine(focusCoroutine);
            focusCoroutine = null;
        }
        m_command.interactable = false;
        if (EventSystem.current != null &&
            EventSystem.current.currentSelectedGameObject == m_command.gameObject)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    public void QuitGame()
    {
        Application.Quit();
    }

    // === Session restoration ===
    // The IEnumerator is executed by GameSaveSystem, which Yarn does NOT treat
    // as a dialogue view. Never StartCoroutine it on this class.
    private readonly List<GameSaveSystem.ChoiceState> choiceHistory =
        new List<GameSaveSystem.ChoiceState>();
    private string historyStartNode;
    private bool restoringSession;
    private int lineOrdinalSinceChoice;
    private int replayedChoiceCount;
    private int restoreChoiceCount;
    private int restoreTargetLineOrdinal;
    private string restoreTargetNode;
    private bool restorePendingYarnLine;
    private Action pendingRestoreLineCallback;
    private int pendingCaretPosition = -1;

    // Keep this compatibility method because an existing GameSaveSystem may
    // invoke it. The overlay now animates itself in SessionLoadingScreen.Update().
    internal void TickLoadingScreen(float deltaTime)
    {
    }

    private void ResetInterruptedTyping()
    {
        // Yarn stops all view coroutines in StartDialogue, but leaves these
        // flags and queue contents behind unless they are reset explicitly.
        typewriterQueue.Clear();
        activeTypewriterJob = null;
        typewriterQueueRunning = false;
        isTyping = false;
        isRevealingText = false;
        skipRequested = false;
        if (focusCoroutine != null)
        {
            StopCoroutine(focusCoroutine);
            focusCoroutine = null;
        }
        if (restartCoroutine != null)
        {
            StopCoroutine(restartCoroutine);
            restartCoroutine = null;
        }
        if (automaticRestartCoroutine != null)
        {
            StopCoroutine(automaticRestartCoroutine);
            automaticRestartCoroutine = null;
        }
        restartingDialogue = false;
    }

    private IEnumerator RestoreSavedSession()
    {
        if (loadingScreen != null)
            loadingScreen.Show();
        else
            Debug.LogWarning("Session Loading Screen is not assigned in GameController.");

        // Crucially: hosted by GameSaveSystem, not this DialogueViewBase.
        // Wait for the runner's automatic Start and the UI's initialization.
        yield return null;
        yield return null;

        GameSaveSystem.SaveData saved = null;
        try { saved = GameSaveSystem.Load(); }
        catch (Exception ex) { Debug.LogError("Save file load failed: " + ex); }

        if (saved == null && m_runner != null)
        {
            StartCoroutine(PlayTrack("smoke and wood"));
            historyStartNode = m_runner.startNode;
        }
        if (saved != null)
        {
            restoringSession = true;
            suppressDialogueComplete = true;
            restoreChoiceCount = saved.version >= 2 && saved.choices != null
                ? Mathf.Min(saved.choices.Count, 512) : 0;
            replayedChoiceCount = 0;
            restorePendingYarnLine = saved.version >= 3 &&
                saved.typingJobs != null &&
                saved.typingJobs.Any(j => j != null && j.isYarnLine);
            restoreTargetLineOrdinal = saved.pendingLineOrdinal;
            restoreTargetNode = saved.pendingLineNode;
            pendingRestoreLineCallback = null;
            try
            {
                ResetInterruptedTyping();
                if (m_runner != null)
                {
                    m_runner.Stop();
                    dialogueOptions = null;
                    onOptionSelected = null;
                    awaitingOptions = false;
                    // Saved flags affect available options. Reapply after replay.
                    RestoreYarnVariables(saved);
                    // Starting dialogue stops coroutines only on the view;
                    // GameSaveSystem still runs this enumerator.
                    historyStartNode = saved.version >= 2 &&
                        !string.IsNullOrEmpty(saved.historyStartNode)
                        ? saved.historyStartNode : saved.node;
                    if (string.IsNullOrEmpty(historyStartNode) ||
                        !m_runner.NodeExists(historyStartNode))
                        historyStartNode = m_runner.startNode;
                    lineOrdinalSinceChoice = 0;
                    m_runner.StartDialogue(historyStartNode);
                    ResetInterruptedTyping();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("Yarn restore initialization failed: " + ex);
            }

            // Fast-forward prior selections. Bound time/size for corrupt saves.
            float deadline = Time.realtimeSinceStartup + 6f;
            int choiceIndex = 0;
            int choiceCount = saved.version >= 2 && saved.choices != null
                ? Mathf.Min(saved.choices.Count, 512) : 0;
            while (choiceIndex < choiceCount && Time.realtimeSinceStartup < deadline)
            {
                if (!awaitingOptions || onOptionSelected == null)
                {
                    yield return null;
                    continue;
                }
                GameSaveSystem.ChoiceState chosen = saved.choices[choiceIndex];
                bool matching = dialogueOptions != null && dialogueOptions.Any(o =>
                    o.DialogueOptionID == chosen.optionId) &&
                    (string.IsNullOrEmpty(chosen.node) || m_runner == null ||
                     m_runner.CurrentNodeName == chosen.node);
                if (!matching)
                {
                    Debug.LogWarning("Yarn options changed since the save; using the saved node instead.");
                    break;
                }
                try
                {
                    if (m_runner != null && m_runner.VariableStorage != null &&
                        chosen.inputValue != null)
                        m_runner.VariableStorage.SetValue("$inputVariable", chosen.inputValue);
                    SelectDialogueOption(chosen.optionId);
                    choiceIndex++;
                }
                catch (Exception ex)
                {
                    Debug.LogError("Yarn option replay failed: " + ex);
                    break;
                }
                yield return null;
            }

            // For old saves or invalid choice histories, use saved node.
            bool usedFallback = choiceIndex < choiceCount;
            if (usedFallback && m_runner != null &&
                !string.IsNullOrEmpty(saved.node) && m_runner.NodeExists(saved.node))
            {
                try
                {
                    m_runner.Stop();
                    dialogueOptions = null;
                    onOptionSelected = null;
                    awaitingOptions = false;
                    lineOrdinalSinceChoice = 0;
                    m_runner.StartDialogue(saved.node);
                    ResetInterruptedTyping();
                    historyStartNode = saved.node;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("Yarn fallback failed: " + ex.Message);
                }
            }

            // Prompt wait is bounded, never required to dismiss overlay.
            deadline = Time.realtimeSinceStartup + 3f;
            while (m_runner != null && m_runner.IsDialogueRunning &&
                   !awaitingOptions && pendingRestoreLineCallback == null &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;

            if (!awaitingOptions && pendingRestoreLineCallback == null &&
                m_runner != null && !string.IsNullOrEmpty(saved.node) &&
                m_runner.NodeExists(saved.node) && saved.node != historyStartNode)
            {
                try
                {
                    m_runner.Stop();
                    dialogueOptions = null;
                    onOptionSelected = null;
                    awaitingOptions = false;
                    lineOrdinalSinceChoice = 0;
                    m_runner.StartDialogue(saved.node);
                    ResetInterruptedTyping();
                    usedFallback = true;
                    historyStartNode = saved.node;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("Prompt recovery failed: " + ex.Message);
                }
                deadline = Time.realtimeSinceStartup + 2f;
                while (m_runner.IsDialogueRunning && !awaitingOptions &&
                       pendingRestoreLineCallback == null &&
                       Time.realtimeSinceStartup < deadline)
                    yield return null;
            }

            if (!awaitingOptions && pendingRestoreLineCallback == null &&
                m_runner != null && !string.IsNullOrEmpty(m_runner.startNode) &&
                m_runner.NodeExists(m_runner.startNode))
            {
                // Final recovery: don't leave command input permanently frozen.
                try
                {
                    m_runner.Stop();
                    dialogueOptions = null;
                    onOptionSelected = null;
                    awaitingOptions = false;
                    lineOrdinalSinceChoice = 0;
                    m_runner.StartDialogue(m_runner.startNode);
                    ResetInterruptedTyping();
                    usedFallback = true;
                    historyStartNode = m_runner.startNode;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("Start node recovery failed: " + ex.Message);
                }
                deadline = Time.realtimeSinceStartup + 2f;
                while (m_runner.IsDialogueRunning && !awaitingOptions &&
                       pendingRestoreLineCallback == null &&
                       Time.realtimeSinceStartup < deadline)
                    yield return null;
            }

            if (!awaitingOptions && pendingRestoreLineCallback == null)
                Debug.LogWarning(
                    "Saved Yarn dialogue did not produce an options prompt or a resumable line.");

            try
            {
                RestoreGameState(saved);
                GameSaveSystem.RestoreScene(saved);
                Canvas.ForceUpdateCanvases();
            }
            catch (Exception ex)
            {
                Debug.LogError("Session state restore failed: " + ex);
            }

            // Keep replay history for next save and resume terminal text.
            choiceHistory.Clear();
            if (!usedFallback && saved.choices != null)
                choiceHistory.AddRange(saved.choices);
            ResetInterruptedTyping();
            restoringSession = false;
            suppressDialogueComplete = false;
            ResumeSavedTyping(saved);
            if (awaitingOptions && onOptionSelected != null)
                TryEnableCommandInput();
            yield return null;
            if (m_scrollRect != null)
                m_scrollRect.normalizedPosition = saved.scroll;
        }

        // Closed windows stay closed; ensure no invisible raycast blockers.
        GameSaveSystem.MinimizeAllWindows();
        yield return new WaitForSeconds(2);
        if (loadingScreen != null)
            loadingScreen.Dismiss();
    }

    public void MinimizeAll()
    {
        foreach (WindowController window in
            Resources.FindObjectsOfTypeAll<WindowController>())
        {
            window.Close();
        }
    }

    private void ResumeSavedTyping(GameSaveSystem.SaveData saved)
    {
        if (saved.version < 3 || saved.typingJobs == null) return;
        bool hasYarnJob = saved.typingJobs.Any(j => j != null && j.isYarnLine);
        if (hasYarnJob && pendingRestoreLineCallback == null)
        {
            Debug.LogWarning(
                "Could not locate the saved Yarn line during replay; " +
                "skipping its unfinished text to avoid freezing the dialogue.");
            return;
        }

        foreach (var state in saved.typingJobs)
        {
            if (state == null || string.IsNullOrEmpty(state.text)) continue;
            var job = new TypewriterJob
            {
                Text = state.text,
                ResumeAt = Mathf.Clamp(state.charactersWritten, 0, state.text.Length),
                CharactersWritten = Mathf.Clamp(state.charactersWritten, 0, state.text.Length),
                Skip = state.skip,
                Unskippable = state.unskippable,
                IsYarnLine = state.isYarnLine
            };
            if (state.isYarnLine)
            {
                job.OnComplete = () =>
                {
                    Action callback = pendingRestoreLineCallback;
                    pendingRestoreLineCallback = null;
                    callback?.Invoke();
                };
            }
            typewriterQueue.Enqueue(job);
        }
        if (typewriterQueue.Count > 0)
        {
            typewriterQueueRunning = true;
            StartCoroutine(ProcessTypewriterQueue());
        }
    }

    private void TryRestoreDeferredCaret()
    {
        if (pendingCaretPosition < 0 || restoringSession || m_command == null)
            return;
        if (!m_command.isActiveAndEnabled ||
            !m_command.gameObject.activeInHierarchy ||
            !m_command.interactable || !m_command.isFocused ||
            m_command.textComponent == null)
            return;
        try
        {
            m_command.ForceLabelUpdate();
            m_command.textComponent.ForceMeshUpdate();
            var info = m_command.textComponent.textInfo;
            if (info == null || info.characterInfo == null ||
                info.characterInfo.Length == 0)
                return;
            m_command.caretPosition = Mathf.Clamp(
                pendingCaretPosition, 0, m_command.text.Length);
            pendingCaretPosition = -1;
        }
        catch (NullReferenceException)
        {
            // TMP layout may need another active frame.
        }
    }

    internal void CaptureGameState(GameSaveSystem.SaveData data)
    {
        data.terminal = m_result != null ? m_result.text : "";
        data.pendingLineOrdinal = lineOrdinalSinceChoice;
        data.pendingLineNode = m_runner != null ? m_runner.CurrentNodeName : "";
        // Snapshot typing offset on Unity's main thread. Workers write JSON only.
        if (activeTypewriterJob != null)
            data.typingJobs.Add(CaptureTypingJob(activeTypewriterJob));
        foreach (TypewriterJob job in typewriterQueue)
            data.typingJobs.Add(CaptureTypingJob(job));

        data.command = m_command != null ? m_command.text : "";
        data.commandInteractable = m_command != null && m_command.interactable;
        data.commandCaret = pendingCaretPosition >= 0 ? pendingCaretPosition :
            (m_command != null && m_command.isActiveAndEnabled &&
             m_command.textComponent != null &&
             m_command.textComponent.textInfo != null ? m_command.caretPosition : 0);
        data.scroll = m_scrollRect != null
            ? m_scrollRect.normalizedPosition : Vector2.zero;
        data.rooms = rooms != null ? rooms.Reverse().ToArray() : new string[0];
        data.node = m_runner != null ? m_runner.CurrentNodeName : "";
        data.waitingForCommand = awaitingOptions;
        data.choices = new List<GameSaveSystem.ChoiceState>(choiceHistory);
        data.historyStartNode = !string.IsNullOrEmpty(historyStartNode)
            ? historyStartNode : (m_runner != null ? m_runner.startNode : "");
        if (m_image != null && m_image.sprite != null)
            data.image = m_image.sprite.name;
        if (m_gameMusic != null && m_gameMusic.clip != null)
        {
            data.music = playingTrackName;
            data.musicTime = m_gameMusic.time;
            data.musicPlaying = m_gameMusic.isPlaying;
            data.musicVolume = m_gameMusic.volume;
        }
        if (m_runner != null && m_runner.VariableStorage != null)
        {
            var values = m_runner.VariableStorage.GetAllVariables();
            foreach (var v in values.FloatVariables)
                data.floats.Add(new GameSaveSystem.FloatValue
                {
                    key = v.Key,
                    value = v.Value
                });
            foreach (var v in values.StringVariables)
                data.strings.Add(new GameSaveSystem.StringValue
                {
                    key = v.Key,
                    value = v.Value
                });
            foreach (var v in values.BoolVariables)
                data.bools.Add(new GameSaveSystem.BoolValue
                {
                    key = v.Key,
                    value = v.Value
                });
        }
    }

    private static GameSaveSystem.TypingState CaptureTypingJob(TypewriterJob job)
    {
        return new GameSaveSystem.TypingState
        {
            text = job.Text,
            charactersWritten = job.CharactersWritten,
            skip = job.Skip,
            unskippable = job.Unskippable,
            isYarnLine = job.IsYarnLine
        };
    }

    private void RestoreYarnVariables(GameSaveSystem.SaveData data)
    {
        if (m_runner == null || m_runner.VariableStorage == null) return;
        var floats = new Dictionary<string, float>();
        var strings = new Dictionary<string, string>();
        var bools = new Dictionary<string, bool>();
        if (data.floats != null)
            foreach (var v in data.floats) floats[v.key] = v.value;
        if (data.strings != null)
            foreach (var v in data.strings) strings[v.key] = v.value;
        if (data.bools != null)
            foreach (var v in data.bools) bools[v.key] = v.value;
        m_runner.VariableStorage.SetAllVariables(floats, strings, bools);
    }

    private void RestoreGameState(GameSaveSystem.SaveData data)
    {
        rooms = new Stack<string>(data.rooms ?? new string[0]);
        RestoreYarnVariables(data);
        if (m_result != null)
            m_result.text = data.terminal ?? "";
        if (m_command != null)
        {
            m_command.SetTextWithoutNotify(data.command ?? "");
            pendingCaretPosition = Mathf.Clamp(
                data.commandCaret, 0, m_command.text.Length);
            // Only Yarn's actual callback can make input operational.
            m_command.interactable = awaitingOptions && onOptionSelected != null;
        }
        if (m_scrollRect != null)
            m_scrollRect.normalizedPosition = data.scroll;
        if (m_image != null && !string.IsNullOrEmpty(data.image))
        {
            Sprite sprite = Resources.Load<Sprite>(data.image);
            if (sprite != null)
                m_image.sprite = sprite;
        }
        if (m_gameMusic != null && !string.IsNullOrEmpty(data.music))
        {
            if (data.music != "")
                StartCoroutine(PlayTrack(data.music));
        }
    }

    /// <summary>Intentionally not connected to any UI or terminal command.</summary>
    public void DeleteCurrentSave()
    {
        GameSaveSystem.DeleteCurrentSave();
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
