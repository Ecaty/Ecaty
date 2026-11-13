using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Runtime.Flecs;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// Runs the Tests/Run.ps1 suites without blocking the Editor, shows the latest summary
    /// and lists live capability bindings (CapBinding/CapState) while in Play mode.
    /// </summary>
    public sealed class DevConsoleWindow : EditorWindow
    {
        private const string RunnerPidKey = "DevConsole.RunnerPid";
        private const string RunnerSuiteKey = "DevConsole.RunnerSuite";
        private const double RefreshSeconds = 0.5;

        private struct CapRow
        {
            public string Path;
            public string Target;
            public bool Enabled;
            public int Revision;
            public int Phase;
            public int AppliedRevision;
            public int Generation;
            public string Message;
        }

        private readonly List<CapRow> rows = new List<CapRow>();
        private string summary = "";
        private string capError = "";
        private Vector2 summaryScroll, rowScroll;
        private double nextRefresh;

        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        private static string ResultsDirectory => System.IO.Path.Combine(ProjectRoot, "Tests", "Results");
        private static string LatestSummary => System.IO.Path.Combine(ResultsDirectory, "latest.txt");

        [MenuItem("Tools/Dev Console")]
        private static void Open() => GetWindow<DevConsoleWindow>("Dev Console");

        private void OnEnable() => EditorApplication.update += Tick;

        private void OnDisable() => EditorApplication.update -= Tick;

        private void Tick()
        {
            if (EditorApplication.timeSinceStartup < nextRefresh)
                return;
            nextRefresh = EditorApplication.timeSinceStartup + RefreshSeconds;
            summary = File.Exists(LatestSummary) ? ReadShared(LatestSummary) : "No run yet.";
            RefreshCapabilities();
            Repaint();
        }

        private static string ReadShared(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                    return reader.ReadToEnd();
            }
            catch (IOException)
            {
                return "";
            }
        }

        private static bool RunnerActive()
        {
            int pid = SessionState.GetInt(RunnerPidKey, 0);
            if (pid == 0)
                return false;
            try
            {
                return !Process.GetProcessById(pid).HasExited;
            }
            catch (ArgumentException)
            {
                SessionState.EraseInt(RunnerPidKey);
                return false;
            }
        }

        private static void Launch(string suite)
        {
            string pwsh = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe");
            string shell = File.Exists(pwsh) ? pwsh : "powershell";
            string script = System.IO.Path.Combine(ProjectRoot, "Tests", "Run.ps1");
            var info = new ProcessStartInfo(shell, "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" -Suite " + suite)
            {
                WorkingDirectory = ProjectRoot,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var process = Process.Start(info);
            SessionState.SetInt(RunnerPidKey, process.Id);
            SessionState.SetString(RunnerSuiteKey, suite);
        }

        private void RefreshCapabilities()
        {
            rows.Clear();
            capError = "";
            if (!Application.isPlaying)
                return;
            var world = FindAnyObjectByType<FlecsWorld>();
            if (!world || world.Session == null || world.Session.IsDisposed)
            {
                capError = "No running Flecs world.";
                return;
            }
            try
            {
                var session = world.Session;
                foreach (var result in JObject.Parse(session.Query("CapBinding, CapState"))["results"])
                {
                    string parent = (string)result["parent"] ?? "";
                    string path = parent.Length > 0 ? parent + "." + (string)result["name"] : (string)result["name"];
                    ulong entity = session.Lookup(path);
                    var binding = JObject.Parse(session.ComponentJson(entity, "CapBinding"));
                    var state = JObject.Parse(session.ComponentJson(entity, "CapState"));
                    rows.Add(new CapRow
                    {
                        Path = path,
                        Target = (string)binding["target"] ?? "",
                        Enabled = (bool?)binding["enabled"] ?? false,
                        Revision = (int?)binding["revision"] ?? 0,
                        Phase = (int?)state["phase"] ?? 0,
                        AppliedRevision = (int?)state["appliedRevision"] ?? 0,
                        Generation = (int?)state["generation"] ?? 0,
                        Message = (string)state["message"] ?? ""
                    });
                }
            }
            catch (Exception exception)
            {
                capError = exception.Message;
            }
        }

        private void OnGUI()
        {
            bool running = RunnerActive();
            EditorGUILayout.LabelField("Test runner", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(running))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Run All"))
                    Launch("All");
                if (GUILayout.Button("Gallery"))
                    Launch("Gallery");
                if (GUILayout.Button("Extended"))
                    Launch("Extended");
            }
            if (running)
                EditorGUILayout.HelpBox("Running " + SessionState.GetString(RunnerSuiteKey, "") + "...", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open report"))
                {
                    string latest = System.IO.Path.Combine(ResultsDirectory, "Latest.html");
                    if (File.Exists(latest))
                        Application.OpenURL(new Uri(latest).AbsoluteUri);
                }
                if (GUILayout.Button("Reveal results"))
                    EditorUtility.RevealInFinder(ResultsDirectory);
            }

            EditorGUILayout.LabelField("Latest run", EditorStyles.boldLabel);
            summaryScroll = EditorGUILayout.BeginScrollView(summaryScroll, GUILayout.MaxHeight(180));
            EditorGUILayout.SelectableLabel(summary, EditorStyles.textArea, GUILayout.ExpandHeight(true), GUILayout.MinHeight(100));
            EditorGUILayout.EndScrollView();

            EditorGUILayout.LabelField("Capability bindings", EditorStyles.boldLabel);
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play mode to list CapBinding / CapState rows.", MessageType.None);
                return;
            }
            if (capError.Length > 0)
                EditorGUILayout.HelpBox(capError, MessageType.Warning);
            rowScroll = EditorGUILayout.BeginScrollView(rowScroll);
            foreach (var row in rows)
            {
                EditorGUILayout.LabelField(row.Path + (row.Enabled ? "" : "  (disabled)"), EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("target " + row.Target + "  rev " + row.AppliedRevision + "/" + row.Revision +
                    "  phase " + row.Phase + "  gen " + row.Generation);
                if (row.Message.Length > 0)
                    EditorGUILayout.LabelField(row.Message, EditorStyles.miniLabel);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
