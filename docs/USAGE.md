# Using PulseKey

## Quick action

1. Start PulseKey and open the app you want to target.
2. Choose the app from **Target window**. The list contains visible top-level windows. Use **Refresh list** after opening a new app. The Windows desktop shell and PulseKey itself are omitted.
3. Choose **Keyboard** or **Mouse**. Click the keyboard field and press the desired key, or select Left, Right, Middle, X1, or X2 for mouse input. `F8` and `F9` are reserved for PulseKey controls.
4. Choose **Hold** to keep the input down while the selected window is active, or **Repeat** to send separate presses/clicks. Enter an interval from 50 to 10,000 milliseconds for Repeat. For example, 100 ms is approximately 10 actions per second.
5. Press **Start** or `F8`. PulseKey becomes **Armed**. Bring the target window to the front to become **Active**. Press **Stop** or `F9` to release and stop.

The action pauses if another window comes to the front. In Hold mode, PulseKey releases the held input on focus loss. If the chosen window closes, the action stops. Reopen or refresh the target list before starting again.

## Macro studio

Macro studio uses the same selected target as Quick action. Select a target before running a sequence.

1. Enter a macro name.
2. Use **Press a key**, **Click mouse**, or **Wait** to add steps.
3. Each key or click step has a delay *after* the action. A Wait step contributes its own pause. Timings are clamped to 50–10,000 ms.
4. Use **Edit** to change an input or timing, **Up** and **Down** to reorder, and **Remove** to delete a step.
5. Choose **Run once**. PulseKey waits for the selected target to be in front before each step. `F9` cancels playback.

Macro studio is a manual editor; it does not record your input. The current macro remains in the editor until you close the app. Save it to a `.pulse.xml` file if you want to keep it. Load opens a saved macro.

### Macro file format

The file is XML with a name and ordered steps. This is an illustrative example:

```xml
<?xml version="1.0" encoding="utf-8"?>
<MacroFile>
  <Name>Example</Name>
  <Steps>
    <MacroStep><Kind>Key</Kind><Value>Space</Value><DelayMs>100</DelayMs></MacroStep>
    <MacroStep><Kind>Delay</Kind><Value>Wait</Value><DelayMs>500</DelayMs></MacroStep>
    <MacroStep><Kind>Click</Kind><Value>Left</Value><DelayMs>100</DelayMs></MacroStep>
  </Steps>
</MacroFile>
```

Supported kinds are `Key`, `Click`, and `Delay`. Mouse values are `Left`, `Right`, `Middle`, `X1`, and `X2`. Key values are Windows Forms `Keys` names. The loader rejects files over 256 KB, disallows DTD processing, and limits a macro to 500 steps. Treat macros from other people as untrusted input even though the format contains data rather than executable code.

## Appearance

Open **Appearance** and select a preview tile. There is no extra theme dropdown. The gradient and accents update across the whole app. Your selected theme is saved to `%LOCALAPPDATA%\PulseKey\settings.txt`; a legacy `Starry Night` setting maps to **Blue Eclipse**.

## Keyboard shortcuts and state

| State | Meaning |
| --- | --- |
| Idle | Nothing is armed or playing. |
| Armed | A quick action is started and waiting for the target window. |
| Active | Input is being sent while the selected target is in front. |

`F8` toggles the quick action. `F9` stops quick actions and macro playback. If another program has already registered either hotkey, PulseKey shows a warning; the on-screen controls still work.
