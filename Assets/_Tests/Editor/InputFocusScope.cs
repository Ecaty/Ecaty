using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ecaty.Editor
{
    /// <summary>
    /// Makes synthetic Input System devices work while the Editor is not the foreground app.
    /// Swaps in a temporary copy of the settings (background devices stay enabled and all device input
    /// goes to the Game view) and restores the original asset afterwards. The asset is never modified or saved.
    /// </summary>
    public sealed class InputFocusScope : IDisposable
    {
        private readonly InputSettings original;
        private readonly InputSettings temporary;

        private InputFocusScope()
        {
            original = InputSystem.settings;
            temporary = original ? UnityEngine.Object.Instantiate(original) : ScriptableObject.CreateInstance<InputSettings>();
            temporary.hideFlags = HideFlags.HideAndDontSave;
            temporary.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            temporary.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = temporary;
        }

        public static InputFocusScope Begin() => new InputFocusScope();

        public void Dispose()
        {
            if (original)
                InputSystem.settings = original;
            if (temporary)
                UnityEngine.Object.DestroyImmediate(temporary);
        }
    }
}
