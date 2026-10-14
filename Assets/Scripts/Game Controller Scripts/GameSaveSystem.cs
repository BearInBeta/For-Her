using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Owns restoration and autosaving. This is deliberately NOT a Yarn dialogue view:
/// DialogueRunner.StartDialogue stops coroutines on its views, including GameController.
/// Unity objects are only accessed on the main thread. Disk writes run on a worker.
/// </summary>
public sealed class GameSaveSystem : MonoBehaviour
{
    [Serializable] public class FloatValue { public string key; public float value; }
    [Serializable] public class StringValue { public string key; public string value; }
    [Serializable] public class BoolValue { public string key; public bool value; }
    [Serializable] public class ChoiceState
    {
        public string node;
        public int optionId;
        public string inputValue;
    }
    [Serializable] public class NoteState
    {
        public string text;
        public Vector2 position, size;
        public Color color, textColor, placeholderColor, exitColor;
        public int sibling;
    }
    [Serializable] public class WindowState
    {
        public string path;
        public Vector2 anchoredPosition, sizeDelta, anchorMin, anchorMax, pivot;
        public Vector3 localScale;
        public Quaternion localRotation;
        public int sibling;
        public bool active;
        public int state;
    }
    [Serializable] public class TypingState
    {
        public string text;
        public int charactersWritten;
        public bool skip, unskippable, isYarnLine;
    }
    [Serializable] public class SaveData
    {
        // Version 1 saves from the previous patches are still readable.
        public int version = 4;
        public string terminal, command, node, historyStartNode, image, music;
        public bool commandInteractable, waitingForCommand, musicPlaying;
        public int commandCaret;
        public int pendingLineOrdinal;
        public string pendingLineNode;
        public List<TypingState> typingJobs = new List<TypingState>();
        public float musicTime, musicVolume;
        public Vector2 scroll;
        public string[] rooms;
        public List<ChoiceState> choices = new List<ChoiceState>();
        public List<FloatValue> floats = new List<FloatValue>();
        public List<StringValue> strings = new List<StringValue>();
        public List<BoolValue> bools = new List<BoolValue>();
        public List<WindowState> windows = new List<WindowState>();
        public List<NoteState> notes = new List<NoteState>();
        public List<SavedElement.State> elements = new List<SavedElement.State>();
    }

    private const float SaveInterval = 0.25f;
    private const string FileName = "session.save.json";
    private static readonly object FileLock = new object();
    private static readonly ConcurrentQueue<string> workerErrors = new ConcurrentQueue<string>();
    private static string savePath;
    private static int generation;
    private GameController controller;
    private Coroutine sessionCoroutine;
    private bool sessionStarted;
    private bool readyToSave;
    private string lastJson;
    private Task pending = Task.CompletedTask;

    private void Awake()
    {
        // This Unity API is main-thread only. Never resolve it inside a Task.
        if (savePath == null)
            savePath = Path.Combine(Application.persistentDataPath, FileName);
    }

    public void BeginSession(GameController owner, IEnumerator restore)
    {
        if (sessionStarted) return;
        controller = owner;
        sessionStarted = true;
        sessionCoroutine = StartCoroutine(RunSession(restore));
    }

    private IEnumerator RunSession(IEnumerator restore)
    {
        // Unlike a coroutine on GameController, Yarn cannot cancel this driver.
        try
        {
            yield return restore;
        }
        finally
        {
            if (controller != null) controller.loadingScreen.Dismiss();
            readyToSave = isActiveAndEnabled;
            sessionCoroutine = null;
        }
        // One post-layout frame so note deletion/replacement can finish.
        yield return null;
        SaveNow();
        while (true)
        {
            yield return new WaitForSecondsRealtime(SaveInterval);
            SaveNow();
        }
    }

    private void Update()
    {
        if (controller != null) controller.TickLoadingScreen(Time.unscaledDeltaTime);
        string error;
        while (workerErrors.TryDequeue(out error)) Debug.LogWarning(error);
    }

    private void OnDisable()
    {
        if (sessionCoroutine != null) StopCoroutine(sessionCoroutine);
        if (controller != null) controller.loadingScreen.Dismiss();
        if (readyToSave) SaveNow();
    }
    private void OnApplicationPause(bool paused) { if (paused) SaveNow(); }
    private void OnApplicationFocus(bool focused) { if (!focused) SaveNow(); }
    private void OnApplicationQuit()
    {
        SaveNow();
        try { pending.Wait(1500); }
        catch (Exception ex) { Debug.LogWarning("Final save write failed: " + ex.Message); }
    }

    private void SaveNow()
    {
        if (!readyToSave || controller == null || !controller.isActiveAndEnabled) return;
        try
        {
            SaveData state = new SaveData();
            controller.CaptureGameState(state);
            CaptureScene(state);
            string json = JsonUtility.ToJson(state);
            if (json == lastJson) return;
            lastJson = json;
            int ticket = generation;
            string path = savePath; // Immutable main-thread snapshot passed to worker.
            pending = pending.ContinueWith(_ => Write(json, path, ticket), TaskScheduler.Default);
        }
        catch (Exception ex) { Debug.LogWarning("Autosave snapshot failed: " + ex); }
    }

    private static void Write(string json, string path, int ticket)
    {
        try
        {
            lock (FileLock)
            {
                if (ticket != generation || string.IsNullOrEmpty(path)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temp = path + ".tmp";
                File.WriteAllText(temp, json);
                if (File.Exists(path))
                {
                    File.Copy(path, path + ".bak", true);
                    File.Delete(path);
                }
                File.Move(temp, path);
            }
        }
        catch (Exception ex)
        {
            // No Unity API calls on the worker thread.
            workerErrors.Enqueue("Autosave write failed: " + ex);
        }
    }

    public static SaveData Load()
    {
        // Called on the main thread after the GameSaveSystem Awake.
        if (string.IsNullOrEmpty(savePath))
            savePath = Path.Combine(Application.persistentDataPath, FileName);
        lock (FileLock)
        {
            foreach (string path in new[] { savePath, savePath + ".bak" })
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    SaveData data = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
                    if (data != null && (data.version == 1 || data.version == 2 || data.version == 3 || data.version == 4)) return data;
                }
                catch (Exception ex) { Debug.LogWarning("Cannot read save: " + ex.Message); }
            }
        }
        return null;
    }

    /// <summary>Not connected to UI. Stops future autosaves until next startup.</summary>
    public static void DeleteCurrentSave()
    {
        foreach (GameSaveSystem saver in FindObjectsOfType<GameSaveSystem>())
        {
            saver.readyToSave = false;
            saver.enabled = false;
        }
        if (string.IsNullOrEmpty(savePath))
            savePath = Path.Combine(Application.persistentDataPath, FileName);
        lock (FileLock)
        {
            generation++;
            foreach (string path in new[] { savePath, savePath + ".bak", savePath + ".tmp" })
                if (File.Exists(path)) File.Delete(path);
        }
    }

    // Stable window identities: sibling index among IDENTICALLY NAMED children
    // rather than among all siblings (which changes when windows are raised).
    private static string StablePath(Transform transform)
    {
        List<string> parts = new List<string>();
        while (transform != null)
        {
            int identicalBefore = 0;
            if (transform.parent != null)
                foreach (Transform sibling in transform.parent)
                {
                    if (sibling == transform) break;
                    if (sibling.name == transform.name) identicalBefore++;
                }
            parts.Add(transform.name + "[" + identicalBefore + "]");
            transform = transform.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    // Prior patch's legacy path format. Used to restore existing V1 saves.
    private static string LegacyPath(Transform transform)
    {
        List<string> parts = new List<string>();
        while (transform != null)
        {
            parts.Add(transform.name + "#" + transform.GetSiblingIndex());
            transform = transform.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static StickyNotes FindSceneNotes()
    {
        return Resources.FindObjectsOfTypeAll<StickyNotes>().FirstOrDefault(n =>
            n.gameObject.scene.IsValid() && n.gameObject.scene.isLoaded);
    }

    private static SavedElement[] FindSavedElements()
    {
        // Includes inactive scene objects, not prefabs or unloaded scenes.
        return Resources.FindObjectsOfTypeAll<SavedElement>()
            .Where(e => e != null && e.gameObject.scene.IsValid() &&
                        e.gameObject.scene.isLoaded).ToArray();
    }

    private static string ElementKey(SavedElement element)
    {
        return string.IsNullOrEmpty(element.SaveKey)
            ? "path:" + StablePath(element.transform)
            : "id:" + element.SaveKey;
    }

    public static void CaptureScene(SaveData data)
    {
        // Presentation state is OPT-IN. Untagged transforms, windows, canvas
        // groups, graphics and animators are deliberately not serialized.
        var seen = new HashSet<string>();
        foreach (SavedElement element in FindSavedElements())
        {
            string key = ElementKey(element);
            if (!seen.Add(key))
            {
                Debug.LogWarning("Duplicate SavedElement identity: " + key, element);
                continue;
            }
            try { data.elements.Add(element.Capture(key)); }
            catch (Exception ex) { Debug.LogWarning("Cannot save " + key + ": " + ex, element); }
        }
        // Sticky notes are runtime-created game content, not generic
        // presentation state. Their existing dedicated serialization remains.
        StickyNotes notes = FindSceneNotes();
        if (notes != null) data.notes = notes.CaptureNotes();
    }

    /// <summary>Start with all surviving windows minimized and non-interactive.</summary>
    public static void MinimizeAllWindows()
    {
        foreach (WindowController window in Resources.FindObjectsOfTypeAll<WindowController>())
        {
            if (!window.gameObject.scene.IsValid() || !window.gameObject.scene.isLoaded) continue;
            if (window.State != WindowController.WindowState.Closed)
                window.RestoreState(WindowController.WindowState.Minimized, false);
        }
    }

    public static void RestoreScene(SaveData data)
    {
        StickyNotes notes = FindSceneNotes();
        if (notes != null && data.notes != null) notes.RestoreNotes(data.notes);

        // Existing saves from earlier versions still restore window geometry.
        // New saves only restore objects explicitly marked SavedElement.
        if (data.version <= 3)
        {
            RestoreLegacyWindows(data);
            return;
        }
        if (data.elements == null) return;

        var elementsByKey = new Dictionary<string, SavedElement>();
        foreach (SavedElement element in FindSavedElements())
        {
            string key = ElementKey(element);
            if (!elementsByKey.ContainsKey(key)) elementsByKey.Add(key, element);
            else Debug.LogWarning("Duplicate SavedElement identity: " + key, element);
        }
        foreach (SavedElement.State state in data.elements)
        {
            if (state == null || string.IsNullOrEmpty(state.key)) continue;
            SavedElement element;
            if (!elementsByKey.TryGetValue(state.key, out element)) continue;
            try { element.Restore(state); }
            catch (Exception ex) { Debug.LogWarning("Cannot restore " + state.key + ": " + ex, element); }
        }
    }

    private static void RestoreLegacyWindows(SaveData data)
    {
        var windows = Resources.FindObjectsOfTypeAll<WindowController>()
            .Where(w => w.gameObject.scene.IsValid() && w.gameObject.scene.isLoaded).ToArray();
        var byPath = new Dictionary<string, WindowController>();
        foreach (WindowController window in windows)
        {
            byPath[StablePath(window.transform)] = window;
            if (data.version == 1) byPath[LegacyPath(window.transform)] = window;
        }
        if (data.windows == null) return;
        foreach (WindowState state in data.windows.OrderBy(w => w.sibling))
        {
            WindowController window;
            if (state == null || string.IsNullOrEmpty(state.path) ||
                !byPath.TryGetValue(state.path, out window)) continue;
            RectTransform rect = window.GetComponent<RectTransform>();
            if (rect == null) continue;
            rect.anchorMin = state.anchorMin;
            rect.anchorMax = state.anchorMax;
            rect.pivot = state.pivot;
            rect.sizeDelta = state.sizeDelta;
            rect.anchoredPosition = state.anchoredPosition;
            rect.localScale = state.localScale;
            rect.localRotation = state.localRotation;
            if (rect.parent != null)
                rect.SetSiblingIndex(Mathf.Clamp(state.sibling, 0, rect.parent.childCount - 1));
            window.RestoreState((WindowController.WindowState)state.state, state.active);
        }
    }
}
