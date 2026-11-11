using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ecaty.Agents.UI;
using Ecaty.Flecs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Ecaty
{
    [DefaultExecutionOrder(-460)]
    public sealed class WorkshopDock : MonoBehaviour
    {
        private AgentChatPanel chat;
        private RuntimeExperience experience;
        private FlecsWorld world;
        private VisualElement panel, backdrop;
        private string blankWorld;
        private WorkshopSeed seed;
        private WorkshopRadialMenu menu;
        private WorkshopIcon? pressedIcon;
        private WorkshopIcon pending;
        private Vector2 seedOrigin;
        private bool seedPending, seedWasDown, seedRecalled, diagnostics, escapeWasDown;
        private int seedPointerFrame = -1;
        private int escapeFrame = -1;
        public bool ConsumedEscape => escapeFrame == Time.frameCount;
        public bool SettingsOpen => page == Page.Settings;
        private enum Page
        {
            Closed, Settings, Seed, Confirm, Environment
        }
        private Page page;

        public bool OwnsSeedPointer => menu && menu.IsOpen || seedPointerFrame == Time.frameCount;
        public bool HasWorld => world && world.Session != null && world.Session.Lookup("diorama") != 0;

        public void Initialize(AgentChatPanel agent, RuntimeExperience runtime, bool settingsOnly = false)
        {
            chat = agent;
            experience = runtime;
            world = GetComponent<FlecsWorld>();
            if (!settingsOnly)
            {
                var blank = JObject.Parse(runtime.Capture());
                var sources = (JArray)blank["sources"];
                var baseSource = sources.OfType<JObject>().Single(s => (string)s["path"] == "World");
                baseSource["code"] = Resources.Load<TextAsset>("Runtime/Examples/World").text;
                blankWorld = blank.ToString(Formatting.None);
                seed = gameObject.AddComponent<WorkshopSeed>();
                seed.Initialize(world);
                menu = gameObject.AddComponent<WorkshopRadialMenu>();
                menu.Initialize(runtime.Camera, seed.View);
                RefreshSeed();
            }
            AudioListener.volume = 1f;

            panel = new VisualElement { name = "workshopPopover" };
            panel.AddToClassList("workshop-popover");
            panel.styleSheets.Add(Resources.Load<StyleSheet>("Workshop/UI/WorkshopDock"));
            backdrop = new VisualElement { name = "workshopDismiss" };
            backdrop.style.position = Position.Absolute;
            backdrop.style.left = backdrop.style.top = backdrop.style.right = backdrop.style.bottom = 0;
            backdrop.RegisterCallback<ClickEvent>(e =>
            {
                Close();
                chat.SetExpanded(false);
                e.StopImmediatePropagation();
            });
            var settings = new Button(() => { if (page == Page.Settings) Close(); else ShowSettings(); })
            {
                name = "workshopSettings",
                tooltip = "Settings",
                focusable = false
            };
            var glyph = new Label("\ue8b8") { pickingMode = PickingMode.Ignore, enableRichText = false };
            glyph.style.unityFontDefinition = FontDefinition.FromFont(Resources.Load<Font>("MaterialSymbolsRounded"));
            glyph.style.fontSize = 20;
            glyph.style.unityTextAlign = TextAnchor.MiddleCenter;
            settings.Add(glyph);
            chat.AttachAccountLauncher(settings, panel, backdrop);
            if (settingsOnly)
                settings.style.display = DisplayStyle.None;
            Close();
        }

        public void StepSeedPointer(Vector2 point, bool pressed, bool cancel = false)
        {
            bool started = pressed && !seedWasDown;
            bool released = !pressed && seedWasDown;
            seedWasDown = pressed;
            if (!seed)
                return;
            if (cancel || !seed.Visible || experience.Navigation && experience.Navigation.IsNavigating)
            {
                seedPending = false;
                seed.Highlighted = false;
                return;
            }
            if (menu.IsOpen)
            {
                StepMenu(point, started, released);
                return;
            }
            bool available = page == Page.Closed && !chat.Expanded && !experience.WorkshopOwnsPointer &&
                !experience.WorldDragActive && !experience.CoversWorldPointer(point);
            seed.Highlighted = available && HitsSeed(point);
            if (started)
            {
                seedOrigin = point;
                seedPending = seed.Highlighted;
            }
            if (!seedPending)
                return;
            if (!available || (point - seedOrigin).sqrMagnitude > 81)
            {
                seedPending = false;
                return;
            }
            seedPointerFrame = Time.frameCount;
            if (released)
            {
                seedPending = false;
                if (!seed.Highlighted)
                    return;
                ulong anchor = world.Session.Lookup("WorldSeed");
                experience.SendPointer(anchor, point, 1);
                experience.SendPointer(anchor, point, 0);
            }
        }

        private void StepMenu(Vector2 point, bool started, bool released)
        {
            if (experience.CoversWorldPointer(point))
            {
                seedPending = false;
                menu.Highlight(null);
                return;
            }
            var icon = menu.Pick(point);
            menu.Highlight(icon);
            if (started)
            {
                seedOrigin = point;
                seedPending = true;
                pressedIcon = icon;
            }
            if (!released || !seedPending)
                return;
            seedPending = false;
            seedPointerFrame = Time.frameCount;
            if (icon != pressedIcon || (point - seedOrigin).sqrMagnitude > 81)
                return;
            if (icon.HasValue)
                Activate(icon.Value);
            else if (page != Page.Seed && HitsSeed(point))
                ShowSeed();
            else
                Close();
        }

        private bool HitsSeed(Vector2 point)
        {
            var ray = experience.Camera.ScreenPointToRay(point);
            if (!seed.Hit(ray, out float distance))
                return false;
            return !experience.TryWorldPoint(ray, out Vector3 hit) ||
                (hit - ray.origin).sqrMagnitude >= distance * distance - .001f;
        }

        AgentUiPalette palette;
        public void ApplyTheme(AgentUiPalette value)
        {
            palette = value;
            palette?.Apply(panel);
            if (panel != null)
                panel.style.backgroundColor = value.Surface;
        }

        public void CloseSettings()
        {
            if (SettingsOpen)
                Close();
        }

        public void ShowSettings()
        {
            Close();
            chat.SetExpanded(false);
            page = Page.Settings;
            panel.Clear();
            var title = new Label("Settings");
            title.AddToClassList("workshop-popover__title");
            panel.Add(title);
            var button = new Button(() => { Close(); chat.Controller.ShowAccounts(); })
            {
                name = "workshopAI",
                text = "AI connection",
                focusable = false
            };
            button.AddToClassList("workshop-popover__action");
            panel.Add(button);
            panel.style.display = DisplayStyle.Flex;
            backdrop.style.display = DisplayStyle.Flex;
            ApplyTheme(palette ?? EcatyTheme.Default.UI);
        }

        private void ShowSeed()
        {
            Close();
            page = Page.Seed;
            var choices = new List<WorkshopIcon> { WorkshopIcon.NewWorld, WorkshopIcon.Bell, WorkshopIcon.Playground };
            if (File.Exists(RuntimeExperience.SavePath))
                choices.Insert(1, WorkshopIcon.OpenWorld);
            if (HasWorld)
            {
                choices.Add(WorkshopIcon.Environment);
                choices.Add(WorkshopIcon.SaveWorld);
            }
            menu.Show(choices);
        }

        private void Activate(WorkshopIcon icon)
        {
            if (chat.Controller.IsBusy)
                return;
            try
            {
                switch (icon)
                {
                    case WorkshopIcon.Confirm:
                        Open(pending);
                        break;
                    case WorkshopIcon.SaveWorld:
                        experience.Save();
                        ShowSeed();
                        break;
                    case WorkshopIcon.Environment:
                        page = Page.Environment;
                        menu.Show(new[] { WorkshopIcon.Paper, WorkshopIcon.Honey, WorkshopIcon.Twilight, WorkshopIcon.Neutral });
                        break;
                    case WorkshopIcon.Paper:
                    case WorkshopIcon.Honey:
                    case WorkshopIcon.Twilight:
                    case WorkshopIcon.Neutral:
                        ApplyLook(icon);
                        break;
                    default:
                        if (!HasWorld)
                            Open(icon);
                        else if (page != Page.Confirm)
                        {
                            pending = icon;
                            page = Page.Confirm;
                            menu.Show(new[] { icon, WorkshopIcon.Confirm });
                        }
                        else
                            Open(pending);
                        break;
                }
            }
            catch (Exception error) { Debug.LogException(error); }
        }

        private void ApplyLook(WorkshopIcon icon)
        {
            var session = world.Session;
            if (icon == WorkshopIcon.Neutral)
            {
                if (session.ManagedScriptSource("WorkshopWorldLook") != null)
                    session.SetManagedScript("WorkshopWorldLook", "// Default rendering");
                return;
            }
            if (session.ManagedScriptSource("WorkshopWorldLooks") == null)
                session.SetManagedScript("WorkshopWorldLooks", Resources.Load<TextAsset>("Workshop/Runtime/WorkshopWorldLooks").text);
            session.SetManagedScript("WorkshopWorldLook",
                "const look = WorkshopLooks." + icon + "[CapPostProcessing]\n" +
                "WorkshopWorldLookBinding { CapBinding: {target: diorama, enabled: true, revision: 1}; CapPostProcessing: $look }");
        }

        private void Open(WorkshopIcon icon)
        {
            switch (icon)
            {
                case WorkshopIcon.NewWorld:
                    StartBlankWorld();
                    break;
                case WorkshopIcon.OpenWorld:
                    StartSavedWorld();
                    break;
                case WorkshopIcon.Bell:
                    StartBellAndGate();
                    break;
                case WorkshopIcon.Playground:
                    StartCreaturePlayground();
                    break;
            }
        }

        public bool StartBlankWorld() => LoadWorld(blankWorld);
        public bool StartSavedWorld(string path = null) => LoadWorld(File.ReadAllText(path ?? RuntimeExperience.SavePath));
        public bool StartBellAndGate() => LoadWorld(blankWorld, WorkshopExamples.BellAndGate);
        public bool StartCreaturePlayground() => LoadWorld(blankWorld, WorkshopExamples.CreaturePlayground);
        public bool EnsureWorld() => HasWorld || StartBlankWorld();

        private bool LoadWorld(string source, Action<RuntimeExperience> populate = null)
        {
            if (chat.Controller.IsBusy)
                return false;
            experience.Load(source);
            seed.BindSession();
            world.SyncViews();
            populate?.Invoke(experience);
            seedRecalled = false;
            RefreshSeed();
            chat.Controller.ResetConversation();
            experience.Navigation?.ResetView();
            Close();
            return true;
        }

        public void RecallSeed()
        {
            if (HasWorld)
                seedRecalled = !seedRecalled;
            RefreshSeed();
            if (seed.Visible)
                experience.Navigation?.FrameSeed(seed.FocusPosition);
            else
                experience.Navigation?.ResetView();
        }

        private void RefreshSeed()
        {
            bool activeWorld = HasWorld;
            seed.Visible = !activeWorld || seedRecalled;
            seed.Lift = activeWorld ? 1.7f : 0f;
        }

        private void Close()
        {
            page = Page.Closed;
            seedPending = false;
            if (menu)
                menu.Close();
            panel.style.display = DisplayStyle.None;
            backdrop.style.display = DisplayStyle.None;
        }

        private void Update()
        {
            if (!chat || world.Session == null || world.Session.IsDisposed)
                return;
            if (seed)
                seed.BindSession();
            if (seed && seed.ConsumeActivation() && page == Page.Closed && !chat.Expanded)
                ShowSeed();
#if ECATY_CAP_ALINE && (UNITY_EDITOR || DEVELOPMENT_BUILD)
            if (!chat.IsEditingText && Keyboard.current?.f8Key.wasPressedThisFrame == true)
                diagnostics = !diagnostics;
#endif
            bool escapeDown = Keyboard.current?.escapeKey.isPressed == true;
            bool escapePressed = escapeDown && !escapeWasDown;
            escapeWasDown = escapeDown;
            if (escapePressed && !chat.IsEditingText)
            {
                if (page != Page.Closed || chat.Expanded)
                {
                    escapeFrame = Time.frameCount;
                    Close();
                    chat.SetExpanded(false);
                }
                else if (seed && !experience.WorkshopOwnsPointer && !experience.WorldDragActive)
                    RecallSeed();
            }
            if (page != Page.Closed && chat.Expanded)
                Close();
            if (page == Page.Closed)
                backdrop.style.display = chat.Expanded ? DisplayStyle.Flex : DisplayStyle.None;
            if (seed)
                RefreshSeed();
        }

#if ECATY_CAP_ALINE && (UNITY_EDITOR || DEVELOPMENT_BUILD)
        private void LateUpdate()
        {
            if (!diagnostics || !experience.Navigation)
                return;
            Vector3 point = experience.Navigation.Center;
            float size = Mathf.Clamp(experience.Camera.orthographicSize * .06f, .12f, 1.4f);
            Drawing.Draw.ingame.Line(point - Vector3.right * size, point + Vector3.right * size, new Color(1f, .47f, .38f));
            Drawing.Draw.ingame.Line(point - Vector3.up * size, point + Vector3.up * size, new Color(.52f, .92f, .51f));
            Drawing.Draw.ingame.Line(point - Vector3.forward * size, point + Vector3.forward * size, new Color(.45f, .75f, 1f));
        }
#endif
    }
}

