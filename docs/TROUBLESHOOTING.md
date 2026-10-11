# Troubleshooting

## The target window is missing

Open the target app, then click **Refresh list**. PulseKey lists visible top-level windows with a usable title; hidden windows, windows without a title, and the Windows desktop shell are not included. A protected app may use a window or input path PulseKey cannot control.

## Armed but not Active

Bring the selected target window to the foreground. **Armed** means PulseKey is waiting; it does not mean input is currently being delivered. The selected window must still exist. If it was closed, choose it again after reopening.

## Active but no effect in another app

PulseKey sends synthetic Windows input through `SendInput`. Some apps reject synthetic input, and higher-integrity or protected processes can refuse input from a normal process. Try an ordinary text editor to isolate whether PulseKey is working. PulseKey does not bypass anti-cheat or guarantee compatibility with games.

## F8 or F9 does not work

Another program may already have registered the key. PulseKey warns at startup if registration fails. Use the on-screen controls or close the conflicting app and restart PulseKey.

## Full screen looks wrong or the title bar is missing

Use `F11` or **Exit full screen** to return to the prior window state. The native maximize button keeps the title bar; `F11` fills the whole display and intentionally hides it. Version 1.0.2 also enlarges the cards, controls, and text to match the display. If only the background grows, install the latest release.

## Shift does not appear in a macro

Choose **Press a key**, then press Shift by itself in the capture dialog. The timeline displays **Press Shift**. A macro key step sends a press and release; use Quick action's **Hold** mode if you need a sustained Shift input.

## A held input seems stuck

Press `F9`, click **Stop**, or close PulseKey. The app sends a release when stopped, when focus leaves the target, and during normal shutdown. If the target or Windows has stopped responding, reconnecting the input device or ending the target app may be necessary.

## Theme or interval did not persist

Check that Windows allows PulseKey to write `%LOCALAPPDATA%\PulseKey\settings.txt`. The app clamps intervals to 50–10,000 ms. Macro files are stored separately wherever you chose to save them.

## Windows warns about the executable

The current release is unsigned. Review the source, download only from the repository's own Releases page, and verify the published SHA-256 checksum. Do not disable Windows security protections just to run PulseKey.
