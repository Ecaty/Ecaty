#if CAP_FLEXALON
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using Runtime.Flecs;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Game.Editor
{
    /// <summary>
    /// Takes a gallery save through the whole app life: file on disk, Play starts and reads it, Play stops and the app writes it.
    /// Every other check puts a file back into a running gallery; this one is the only one that reads at start and looks at the
    /// write at exit (a save that failed to restore used to be replaced by the defaults without any test noticing).
    ///
    /// The app cannot be pointed at another folder, so the real save files are used and protected:
    /// - Before case A, every file of Application.persistentDataPath/GalleryPrototype plus Experiences/current.json and
    ///   Clay/creation.json (those that exist) are copied to Library/SaveRoundTripBackup (ignored by git, survives a crash).
    ///   manifest.json (names, sizes, SHA-256, the full gallery file list) is written LAST: a manifest means a complete backup.
    /// - At the end, on any error and on any timeout the files are restored, extra files in the gallery folder deleted, the
    ///   hashes compared with the manifest (the last check of the report) and only then the backup folder removed. When the
    ///   comparison fails the backup is kept.
    /// - [InitializeOnLoadMethod] restores from a manifest whenever no run is in progress (Editor restart, crash, and Play that
    ///   finally stopped after a timeout) and the Editor is not playing.
    /// - Start() restores from a manifest left by an unfinished run before it begins.
    ///
    /// Runner (Tests/Run.ps1): it reads the gallery save bytes before the first suite and writes them back in its finally block
    /// (and, when Play was running at the start, calls RunnerSupport.RestoreGalleryArrangement on the live gallery, in memory
    /// only). It never touches the Extended verifiers' files in between and stops Play before each editor verifier starts (the
    /// stop saves the gallery). So at Start() the files are what the runner leaves for the next verifier, my backup records
    /// exactly that, and my restore puts exactly that back before the report is written; the runner's own restore then comes
    /// last and writes the file it captured at the very start. The two do not fight. Run alone, only my protection applies.
    ///
    /// State lives in SessionState because Play mode reloads the domain (twice per case). Report:
    /// Verification/gallery-save-round-trip-checks.json (testedUtc, passed, checks, failedChecks, error). Files are never
    /// logged or reported: names, sizes, counts and hashes only.
    /// </summary>
    public static class GallerySaveRoundTripVerification
    {
        const string K = "Game.GallerySaveRoundTrip.";
        const string KActive = K + "Active", KPhase = K + "Phase", KCase = K + "Case", KDeadline = K + "Deadline",
            KChecks = K + "Checks", KErrors = K + "Errors", KResume = K + "Resume", KFrame = K + "Frame", KObs = K + "Obs", KStarted = K + "Started";
        const string ReportFile = "Verification/gallery-save-round-trip-checks.json";
        const int Begin = 0, EnterPlay = 1, WaitGallery = 2, Settle = 3, ExitPlay = 4, Aborting = 5;
        const int SettleFrames = 60, Cases = 4;
        const string ResumeText = "could not resume";
        static readonly string[] ExtraSaves = { "Experiences/current.json", "Clay/creation.json" };
        static readonly string[] CaseNames = { "A", "B", "C", "D" };

        static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        static string BackupDir => Path.Combine(ProjectRoot, "Library", "SaveRoundTripBackup");
        static string ManifestPath => Path.Combine(BackupDir, "manifest.json");
        static string FilesDir => Path.Combine(BackupDir, "files");
        static string WorkDir => Path.Combine(BackupDir, "work");
        static string GoldenPath => Path.Combine(ProjectRoot, "Tests", "Fixtures", "Saves", "gallery-arrangement-v1.json");
        static string ReportPath => Path.Combine(ProjectRoot, ReportFile);
        static bool Active => SessionState.GetBool(KActive, false);
        static string NativePath(string relative) => relative.Replace('/', Path.DirectorySeparatorChar);

        // One line per event, so a run can be followed in the Editor log (names and counts only).
        static void Trace(string text) => Debug.Log("Gallery save round trip [trace]: " + text + " | active=" + Active + " phase=" + SessionState.GetInt(KPhase, -1) + " case=" + SessionState.GetInt(KCase, -1)
            + " playing=" + EditorApplication.isPlaying + " willChange=" + EditorApplication.isPlayingOrWillChangePlaymode + " backup=" + Directory.Exists(BackupDir) + " manifest=" + File.Exists(ManifestPath));

        [InitializeOnLoadMethod]
        static void Initialize()
        {
            Trace("domain loaded");
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            Application.logMessageReceived -= OnLog;
            EditorApplication.playModeStateChanged += OnPlayMode;
            if (Active)
            {
                // The domain was reloaded in the middle of a run (Play started or stopped): carry on.
                EditorApplication.update += Tick;
                Application.logMessageReceived += OnLog;
                return;
            }
            // No run in progress (the flag is gone after an Editor restart): a manifest means a run did not finish.
            Recover();
        }

        static void OnPlayMode(PlayModeStateChange change)
        {
            Trace("play mode " + change);
            // A Play that would not stop during a timed-out run ends later: restore then.
            if (change == PlayModeStateChange.EnteredEditMode && !Active)
                Recover();
        }

        static void Recover()
        {
            try
            {
                if (Active || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(ManifestPath))
                    return;
                var problems = Restore();
                Debug.LogWarning("Gallery save round trip: restored the saves from the backup of a run that did not finish" + (problems.Count == 0 ? "." : " with " + problems.Count + " problem(s); the backup is kept in Library/SaveRoundTripBackup."));
                WriteReport(new JObject
                {
                    ["testedUtc"] = DateTime.UtcNow.ToString("O"),
                    ["passed"] = false,
                    ["error"] = "A run of this check did not finish; the saves were restored from its backup" + (problems.Count == 0 ? "." : " but " + string.Join("; ", problems)),
                    ["checks"] = new JArray(),
                    ["failedChecks"] = new JArray()
                });
            }
            catch (Exception error) { Debug.LogWarning("Gallery save round trip: the backup could not be restored (" + error.GetType().Name + "); it is kept in Library/SaveRoundTripBackup."); }
        }

        public static bool Start()
        {
            if (Active)
            {
                Debug.LogWarning("Gallery save round trip: a run is already in progress.");
                return false;
            }
            string refusal = null;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                refusal = "The Editor is in Play mode; stop it before starting this check.";
            else if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                refusal = "The Editor is compiling or importing; try again when it is idle.";
            else if (AnySceneDirty())
                refusal = "A scene has unsaved changes; save or revert it before starting this check.";
            else if (!File.Exists(GoldenPath))
                refusal = "The golden arrangement Tests/Fixtures/Saves/gallery-arrangement-v1.json is missing.";
            if (refusal == null)
            {
                try
                {
                    if (File.Exists(ManifestPath))
                    {
                        // A previous run did not finish: put its files back first.
                        var old = Restore();
                        if (old.Count > 0)
                            refusal = "The backup of an earlier unfinished run could not be restored completely (" + string.Join("; ", old) + "); nothing was started.";
                    }
                    if (refusal == null)
                        Backup();
                }
                catch (Exception error)
                {
                    refusal = "The backup step failed (" + error.GetType().Name + ": " + error.Message + ").";
                    // A manifest is complete by construction; without one the folder is a half-written copy that nothing needs.
                    try { if (!File.Exists(ManifestPath) && Directory.Exists(BackupDir)) Directory.Delete(BackupDir, true); } catch (Exception) { }
                }
            }
            if (refusal != null)
            {
                WriteReport(new JObject { ["testedUtc"] = DateTime.UtcNow.ToString("O"), ["passed"] = false, ["error"] = refusal, ["checks"] = new JArray(), ["failedChecks"] = new JArray() });
                Debug.LogWarning("Gallery save round trip: " + refusal);
                return true; // the runner reads the report; the call itself did what it could
            }
            SessionState.SetString(KStarted, DateTime.UtcNow.ToString("O"));
            SessionState.SetString(KChecks, "[]");
            SessionState.SetString(KErrors, "[]");
            SessionState.SetInt(KCase, 0);
            SessionState.SetInt(KResume, 0);
            SessionState.SetInt(KPhase, Begin);
            SessionState.SetBool(KActive, true);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            return true;
        }

        static bool AnySceneDirty()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && scene.isDirty)
                    return true;
            }
            return false;
        }

        // ---- State machine ----

        static void Tick()
        {
            if (!Active)
            {
                EditorApplication.update -= Tick;
                return;
            }
            try { Step(); }
            catch (Exception error)
            {
                if (SessionState.GetInt(KPhase, 0) == Aborting)
                {
                    // The restore itself threw: stop here and leave the backup (and its manifest) for the next load to restore.
                    try { Defer(); } catch (Exception) { }
                    Clear();
                    return;
                }
                Fail("Unexpected error in step " + SessionState.GetInt(KPhase, 0) + ": " + error.GetType().Name + ": " + error.Message);
            }
        }

        static void Step()
        {
            switch (SessionState.GetInt(KPhase, Begin))
            {
                case Begin:
                    BeginCase();
                    break;
                case EnterPlay:
                    if (EditorApplication.isPlaying)
                    {
                        SetPhase(WaitGallery, 90);
                    }
                    else if (Expired())
                        Fail("Case " + CaseName + ": Play mode did not start in time.");
                    break;
                case WaitGallery:
                    if (!EditorApplication.isPlaying)
                    {
                        Fail("Case " + CaseName + ": Play mode ended before the gallery appeared.");
                        break;
                    }
                    Application.runInBackground = true;
                    var gallery = Object.FindAnyObjectByType<CrystalWorldGallery>();
                    var world = Object.FindAnyObjectByType<FlecsWorld>();
                    if (gallery && world && world.Session != null && !world.Session.IsDisposed && gallery.Items.Count > 0)
                    {
                        // The gallery finished its start (items exist, the reveal frame comes four frames later); settle for 60 more.
                        SessionState.SetInt(KFrame, Time.frameCount);
                        SetPhase(Settle, 90);
                    }
                    else if (Expired())
                        Fail("Case " + CaseName + ": the gallery did not appear in time.");
                    break;
                case Settle:
                    if (!EditorApplication.isPlaying)
                    {
                        Fail("Case " + CaseName + ": Play mode ended before the gallery settled.");
                        break;
                    }
                    Application.runInBackground = true;
                    if (Time.frameCount >= SessionState.GetInt(KFrame, 0) + SettleFrames)
                    {
                        Observe();
                        EditorApplication.isPlaying = false;
                        SetPhase(ExitPlay, 120);
                    }
                    else if (Expired())
                        Fail("Case " + CaseName + ": Play frames did not advance in time.");
                    break;
                case ExitPlay:
                    if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
                        Evaluate();
                    else if (Expired())
                        Fail("Case " + CaseName + ": Play mode did not stop in time.");
                    break;
                case Aborting:
                    if (EditorApplication.isPlaying)
                        EditorApplication.isPlaying = false;
                    if (!EditorApplication.isPlayingOrWillChangePlaymode)
                        Finish();
                    else if (Expired())
                        Defer();
                    break;
            }
        }

        static string CaseName => CaseNames[Math.Min(SessionState.GetInt(KCase, 0), Cases - 1)];

        static void SetPhase(int phase, int seconds)
        {
            Trace("phase -> " + phase);
            SessionState.SetInt(KPhase, phase);
            SessionState.SetString(KDeadline, DateTime.UtcNow.AddSeconds(seconds).Ticks.ToString());
        }

        static bool Expired() => long.TryParse(SessionState.GetString(KDeadline, ""), out long ticks) && DateTime.UtcNow.Ticks > ticks;

        // Records the error, stops Play if needed and restores; every error and timeout comes here.
        static void Fail(string message)
        {
            var errors = JArray.Parse(SessionState.GetString(KErrors, "[]"));
            errors.Add(message);
            SessionState.SetString(KErrors, errors.ToString(Newtonsoft.Json.Formatting.None));
            if (EditorApplication.isPlaying)
                EditorApplication.isPlaying = false;
            SetPhase(Aborting, 120);
        }

        static void AddCheck(bool passed, string text)
        {
            var checks = JArray.Parse(SessionState.GetString(KChecks, "[]"));
            checks.Add(new JArray(text, passed));
            SessionState.SetString(KChecks, checks.ToString(Newtonsoft.Json.Formatting.None));
        }

        static void OnLog(string text, string stack, LogType type)
        {
            if (!Active || string.IsNullOrEmpty(text))
                return;
            int phase = SessionState.GetInt(KPhase, Begin);
            if (phase >= EnterPlay && phase <= ExitPlay && text.IndexOf(ResumeText, StringComparison.Ordinal) >= 0)
                SessionState.SetInt(KResume, SessionState.GetInt(KResume, 0) + 1);
        }

        // ---- Cases ----

        static string GalleryDir => Path.Combine(Application.persistentDataPath, "GalleryPrototype");
        static string SavePath => CrystalWorldGallery.SavePath;

        static void BeginCase()
        {
            int index = SessionState.GetInt(KCase, 0);
            if (index >= Cases)
            {
                Finish();
                return;
            }
            byte[] input = InputFor(index);
            if (input == null)
            {
                AddCheck(false, "Case " + CaseNames[index] + ": the version 2 file written in case A exists to be read again.");
                SessionState.SetInt(KCase, index + 1);
                return;
            }
            Directory.CreateDirectory(WorkDir);
            File.WriteAllBytes(Path.Combine(WorkDir, "input.bin"), input);
            Directory.CreateDirectory(GalleryDir);
            foreach (string file in Directory.GetFiles(GalleryDir))
            {
                string name = Path.GetFileName(file);
                if (name == "tree-arrangement.v1.json" || (name.StartsWith("tree-arrangement.unread-", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal)))
                    File.Delete(file);
            }
            File.WriteAllBytes(SavePath, input);
            SessionState.SetInt(KResume, 0);
            SessionState.SetString(KObs, "{}");
            EditorApplication.isPlaying = true;
            SetPhase(EnterPlay, 180);
        }

        static byte[] InputFor(int index)
        {
            switch (index)
            {
                case 0:
                    return File.ReadAllBytes(GoldenPath);
                case 1:
                    {
                        // The older shape of the same file: the portal's world as plain text. Built in memory, no fixture file.
                        var golden = JObject.Parse(File.ReadAllText(GoldenPath));
                        golden["portal"]["world"] = (string)golden["portal"]["world"]?["world"] ?? "";
                        return new UTF8Encoding(false).GetBytes(golden.ToString(Newtonsoft.Json.Formatting.None));
                    }
                case 2:
                    {
                        string output = Path.Combine(WorkDir, "case-a-output.bin");
                        return File.Exists(output) ? File.ReadAllBytes(output) : null;
                    }
                default:
                    return Encoding.UTF8.GetBytes("{\"version\":1,\"entries\":\"not a list\"}");
            }
        }

        // The saved worlds of the golden file that have objects (a world without objects is not saved as a world).
        static void Golden(out int entries, out List<string> worldIds)
        {
            var golden = JObject.Parse(File.ReadAllText(GoldenPath));
            entries = ((JArray)golden["entries"]).Count;
            worldIds = ((JArray)golden["worlds"]).Where(w => JArray.Parse((string)w["record"]["objects"]).Count > 0).Select(w => (string)w["id"]).ToList();
        }

        // In Play: what the running gallery shows.
        static void Observe()
        {
            var gallery = Object.FindAnyObjectByType<CrystalWorldGallery>();
            var obs = new JObject();
            if (gallery)
            {
                Golden(out _, out var ids);
                obs["items"] = gallery.Items.Count;
                obs["hasAll"] = ids.Count > 0 && ids.All(id => gallery.Experiences.Has(id));
                obs["worldsKnown"] = ids.Count(id => gallery.Experiences.Has(id));
            }
            SessionState.SetString(KObs, obs.ToString(Newtonsoft.Json.Formatting.None));
        }

        static JObject TryRead(string path)
        {
            try { return File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : null; }
            catch (Exception) { return null; } // not parseable: reported as a failed check, never with the message (it could quote content)
        }

        static string[] UnreadCopies() => Directory.Exists(GalleryDir)
            ? Directory.GetFiles(GalleryDir, "tree-arrangement.unread-*.json")
            : new string[0];

        // Play has stopped: read what the app wrote.
        static void Evaluate()
        {
            Trace("evaluate");
            int index = SessionState.GetInt(KCase, 0);
            string name = CaseNames[index];
            var obs = JObject.Parse(SessionState.GetString(KObs, "{}"));
            int resumeLines = SessionState.GetInt(KResume, 0);
            byte[] input = File.ReadAllBytes(Path.Combine(WorkDir, "input.bin"));
            Golden(out int goldenEntries, out var goldenIds);
            bool fileExists = File.Exists(SavePath);
            var written = TryRead(SavePath);
            var entries = written?["entries"] as JArray;
            var rows = written?["worlds"] as JArray;
            bool version2 = written != null && (int?)written["version"] == 2;
            bool worldRowsRight = rows != null && rows.Count == goldenIds.Count && rows.Select(r => (string)r["id"]).OrderBy(s => s, StringComparer.Ordinal).SequenceEqual(goldenIds.OrderBy(s => s, StringComparer.Ordinal))
                && rows.All(r => r["document"] is JObject d && (int?)d["format"] == 2);
            bool showsAll = (int?)obs["items"] == goldenEntries && (bool?)obs["hasAll"] == true;

            if (index == 0 || index == 1)
            {
                string shape = index == 0 ? "the committed version 1 golden arrangement" : "the golden arrangement with the portal's world as plain text";
                AddCheck(resumeLines == 0 && showsAll, "Case " + name + ": " + shape + " starts without a \"" + ResumeText + "\" warning and the running gallery shows all " + goldenEntries + " entries and every saved world that has objects.");
                AddCheck(fileExists && version2 && entries != null && entries.Count == goldenEntries && worldRowsRight,
                    "Case " + name + ": after Play stops tree-arrangement.json is version 2 with " + goldenEntries + " entries and one format 2 world document for each of the " + goldenIds.Count + " saved worlds.");
                string v1 = Path.Combine(GalleryDir, "tree-arrangement.v1.json");
                AddCheck(File.Exists(v1) && File.ReadAllBytes(v1).SequenceEqual(input), "Case " + name + ": the input file is kept as tree-arrangement.v1.json, byte-identical to what was put there.");
                if (index == 0 && fileExists)
                    File.Copy(SavePath, Path.Combine(WorkDir, "case-a-output.bin"), true);
            }
            else if (index == 2)
            {
                var before = JObject.Parse(Encoding.UTF8.GetString(input));
                var beforeEntries = ((JArray)before["entries"]).Select(e => (string)e["id"]).ToList();
                var beforeRows = ((JArray)before["worlds"]).Select(r => (string)r["id"]).OrderBy(s => s, StringComparer.Ordinal).ToList();
                AddCheck(resumeLines == 0 && showsAll, "Case " + name + ": the version 2 file written in case A starts without a \"" + ResumeText + "\" warning and the running gallery shows all " + goldenEntries + " entries and every saved world that has objects.");
                AddCheck(fileExists && version2 && entries != null && entries.Select(e => (string)e["id"]).SequenceEqual(beforeEntries)
                    && rows != null && rows.Select(r => (string)r["id"]).OrderBy(s => s, StringComparer.Ordinal).SequenceEqual(beforeRows) && worldRowsRight,
                    "Case " + name + ": the second round trip writes version 2 again with the same entries and the same " + beforeRows.Count + " format 2 world rows as the file it read.");
            }
            else
            {
                AddCheck(resumeLines == 1, "Case " + name + ": a file the reader refuses produces exactly one \"" + ResumeText + "\" warning.");
                var copies = UnreadCopies();
                AddCheck(copies.Length > 0 && copies.Any(c => File.ReadAllBytes(c).SequenceEqual(input)),
                    "Case " + name + ": the file that could not be resumed is kept as tree-arrangement.unread-*.json, byte-identical to the input.");
            }
            var changed = ChangedExtraSaves();
            AddCheck(changed.Count == 0, "Case " + name + ": Experiences/current.json and Clay/creation.json are unchanged by the Play session" + (changed.Count == 0 ? "." : " (changed: " + string.Join(", ", changed) + ")."));
            SessionState.SetInt(KCase, index + 1);
            SessionState.SetInt(KPhase, Begin);
        }

        // The two other save files must not change: the gallery is the only thing that saves on exit.
        static List<string> ChangedExtraSaves()
        {
            var changed = new List<string>();
            var manifest = JObject.Parse(File.ReadAllText(ManifestPath));
            string root = (string)manifest["root"];
            foreach (string rel in ExtraSaves)
            {
                string path = Path.Combine(root, NativePath(rel));
                var saved = ((JArray)manifest["files"]).FirstOrDefault(f => (string)f["path"] == rel);
                bool same = saved == null ? !File.Exists(path) : File.Exists(path) && Sha(path) == (string)saved["sha256"];
                if (!same)
                    changed.Add(rel);
            }
            return changed;
        }

        // ---- Finish ----

        static void Finish()
        {
            var problems = new List<string>();
            var errors = JArray.Parse(SessionState.GetString(KErrors, "[]"));
            try { problems = Restore(); }
            catch (Exception error)
            {
                problems.Add("restore failed: " + error.GetType().Name);
                Debug.LogWarning("Gallery save round trip: restore failed (" + error.GetType().Name + "); the backup is kept in Library/SaveRoundTripBackup.");
            }
            AddCheck(problems.Count == 0, "The user's saves are byte-identical after the run.");
            var checks = JArray.Parse(SessionState.GetString(KChecks, "[]"));
            var report = new JObject
            {
                ["testedUtc"] = SessionState.GetString(KStarted, DateTime.UtcNow.ToString("O")),
                ["passed"] = errors.Count == 0 && checks.All(c => (bool)c[1]),
                ["checks"] = new JArray(checks.Where(c => (bool)c[1]).Select(c => (string)c[0])),
                ["failedChecks"] = new JArray(checks.Where(c => !(bool)c[1]).Select(c => (string)c[0])),
                ["restoreProblems"] = new JArray(problems),
                ["limits"] = new JArray("Windows Editor; the scene that is open must hold the gallery", "only the gallery folder and the two named files are protected; a crash is repaired the next time the Editor loads")
            };
            if (errors.Count > 0)
                report["error"] = string.Join(" | ", errors.Select(e => (string)e));
            else if (!(bool)report["passed"])
                report["error"] = "Failed: " + string.Join(" | ", (JArray)report["failedChecks"]);
            WriteReport(report);
            Clear();
        }

        // Play would not stop: leave the backup and the manifest; the EnteredEditMode handler restores when it finally stops.
        static void Defer()
        {
            var errors = JArray.Parse(SessionState.GetString(KErrors, "[]"));
            errors.Add("Play mode did not stop; the saves are restored from Library/SaveRoundTripBackup when it does.");
            WriteReport(new JObject
            {
                ["testedUtc"] = SessionState.GetString(KStarted, DateTime.UtcNow.ToString("O")),
                ["passed"] = false,
                ["error"] = string.Join(" | ", errors.Select(e => (string)e)),
                ["checks"] = new JArray(),
                ["failedChecks"] = new JArray()
            });
            Clear();
        }

        static void Clear()
        {
            Trace("clear");
            SessionState.EraseBool(KActive);
            SessionState.EraseInt(KPhase);
            SessionState.EraseInt(KCase);
            SessionState.EraseInt(KResume);
            SessionState.EraseInt(KFrame);
            SessionState.EraseString(KDeadline);
            SessionState.EraseString(KChecks);
            SessionState.EraseString(KErrors);
            SessionState.EraseString(KObs);
            SessionState.EraseString(KStarted);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
        }

        static void WriteReport(JObject report)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
                File.WriteAllText(ReportPath, report.ToString());
            }
            catch (Exception error) { Debug.LogWarning("Gallery save round trip: the report could not be written (" + error.GetType().Name + ")."); }
        }

        // ---- Backup and restore ----

        static string Sha(string path)
        {
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        static void CopyWithRetry(string from, string to)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    File.Copy(from, to, true);
                    return;
                }
                catch (IOException) when (attempt < 5) { Thread.Sleep(200); }
                catch (UnauthorizedAccessException) when (attempt < 5) { Thread.Sleep(200); }
            }
        }

        static void Backup()
        {
            Trace("backup begins");
            string root = Application.persistentDataPath;
            if (Directory.Exists(BackupDir))
                Directory.Delete(BackupDir, true); // no manifest: a half-written copy from a crash during the backup, nothing was changed yet
            Directory.CreateDirectory(FilesDir);
            string gallery = Path.Combine(root, "GalleryPrototype");
            bool folderExisted = Directory.Exists(gallery);
            var relatives = new List<string>();
            var galleryNames = new JArray();
            if (folderExisted)
                foreach (string file in Directory.GetFiles(gallery))
                {
                    string name = Path.GetFileName(file);
                    galleryNames.Add(name);
                    relatives.Add("GalleryPrototype/" + name);
                }
            var absent = new JArray();
            foreach (string rel in ExtraSaves)
                if (File.Exists(Path.Combine(root, NativePath(rel))))
                    relatives.Add(rel);
                else
                    absent.Add(rel);
            var files = new JArray();
            foreach (string rel in relatives)
            {
                string source = Path.Combine(root, NativePath(rel));
                string copy = Path.Combine(FilesDir, NativePath(rel));
                Directory.CreateDirectory(Path.GetDirectoryName(copy));
                CopyWithRetry(source, copy);
                string hash = Sha(copy);
                if (hash != Sha(source))
                    throw new IOException("The copy of " + rel + " differs from the original.");
                files.Add(new JObject { ["path"] = rel, ["size"] = new FileInfo(copy).Length, ["sha256"] = hash });
            }
            var manifest = new JObject { ["version"] = 1, ["createdUtc"] = DateTime.UtcNow.ToString("O"), ["root"] = root, ["galleryFolderExisted"] = folderExisted, ["galleryFiles"] = galleryNames, ["absent"] = absent, ["files"] = files };
            // Written last, by rename: a manifest on disk means the backup above is complete.
            string temp = ManifestPath + ".tmp";
            File.WriteAllText(temp, manifest.ToString());
            File.Move(temp, ManifestPath);
        }

        // Puts the backed-up state back and compares it with the manifest. Returns the problems (names only); the backup folder is
        // removed only when there are none.
        static List<string> Restore()
        {
            Trace("restore begins, called from " + new System.Diagnostics.StackTrace(1, false).GetFrame(0)?.GetMethod()?.Name);
            var problems = new List<string>();
            var manifest = JObject.Parse(File.ReadAllText(ManifestPath));
            string root = (string)manifest["root"];
            var files = (JArray)manifest["files"];
            var absent = (JArray)manifest["absent"];
            var galleryNames = new HashSet<string>(((JArray)manifest["galleryFiles"]).Select(n => (string)n), StringComparer.OrdinalIgnoreCase);
            bool folderExisted = (bool)manifest["galleryFolderExisted"];
            string gallery = Path.Combine(root, "GalleryPrototype");

            foreach (var file in files)
            {
                string rel = (string)file["path"];
                try
                {
                    string copy = Path.Combine(FilesDir, NativePath(rel));
                    if (!File.Exists(copy))
                    {
                        problems.Add("backup copy missing: " + rel);
                        continue;
                    }
                    string target = Path.Combine(root, NativePath(rel));
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    CopyWithRetry(copy, target);
                }
                catch (Exception error) { problems.Add("could not restore " + rel + " (" + error.GetType().Name + ")"); }
            }
            foreach (var token in absent)
            {
                string rel = (string)token;
                try
                {
                    string target = Path.Combine(root, NativePath(rel));
                    if (File.Exists(target))
                        File.Delete(target);
                }
                catch (Exception error) { problems.Add("could not remove " + rel + " (" + error.GetType().Name + ")"); }
            }
            if (Directory.Exists(gallery))
            {
                foreach (string file in Directory.GetFiles(gallery))
                {
                    string name = Path.GetFileName(file);
                    if (galleryNames.Contains(name))
                        continue;
                    try { File.Delete(file); }
                    catch (Exception error) { problems.Add("could not delete extra file " + name + " (" + error.GetType().Name + ")"); }
                }
                if (!folderExisted)
                    try { if (Directory.GetFileSystemEntries(gallery).Length == 0) Directory.Delete(gallery); } catch (Exception) { }
            }

            // The comparison: hashes of every backed-up file, the gallery folder's file list, files that must stay absent.
            foreach (var file in files)
            {
                string rel = (string)file["path"];
                string target = Path.Combine(root, NativePath(rel));
                try
                {
                    if (!File.Exists(target))
                        problems.Add("missing after restore: " + rel);
                    else if (new FileInfo(target).Length != (long)file["size"] || Sha(target) != (string)file["sha256"])
                        problems.Add("differs after restore: " + rel);
                }
                catch (Exception error) { problems.Add("could not verify " + rel + " (" + error.GetType().Name + ")"); }
            }
            foreach (var token in absent)
                if (File.Exists(Path.Combine(root, NativePath((string)token))))
                    problems.Add("should not exist after restore: " + (string)token);
            if (Directory.Exists(gallery))
            {
                var names = Directory.GetFiles(gallery).Select(n => Path.GetFileName(n)).ToList();
                foreach (string extra in names.Where(n => !galleryNames.Contains(n)))
                    problems.Add("extra file in the gallery folder: " + extra);
                if (!folderExisted && names.Count > 0)
                    problems.Add("the gallery folder did not exist before");
            }
            else if (folderExisted && galleryNames.Count > 0)
                problems.Add("the gallery folder is missing");

            if (problems.Count == 0)
            {
                try
                {
                    File.Delete(ManifestPath);
                    Directory.Delete(BackupDir, true);
                }
                catch (Exception error) { Debug.LogWarning("Gallery save round trip: the backup folder could not be removed (" + error.GetType().Name + "); the saves are restored."); }
            }
            return problems;
        }
    }
}
#endif
