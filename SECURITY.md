# Security model

TextReveal reads what is on your screen, so its attack surface is deliberately small and auditable.
This page lists what the application can and cannot do, and how the code is hardened where it
touches Windows.

## Runs with your rights only

- The executable runs `asInvoker` with `uiAccess="false"`: no elevation prompt, no service, no
  driver, no scheduled task. TextReveal can never do more than the user who started it.
- The install page copies the executable into `%LOCALAPPDATA%\Programs\TextReveal`, never into
  `Program Files`: installing needs no administrator rights either.
- One process at a time, guarded by a per-session named mutex. Starting the executable again only
  signals the running instance to show its settings window.
- No third-party package: the only dependencies are .NET and Windows itself, so there is no NuGet
  supply chain to audit.

## Nothing goes out, nothing is persisted

- No networking API is referenced anywhere: no `HttpClient`, no socket, no telemetry.
- The captured screen lives in memory, is never encoded and never written to disk; closing the
  overlay releases it.
- The clipboard is only *written*, and only when the copy button is clicked or `Ctrl + C` is
  pressed. TextReveal never reads the clipboard.
- Files it may create:
  - `%LOCALAPPDATA%\TextReveal\config.json` - the settings;
  - `%LOCALAPPDATA%\TextReveal\errors.log` - exception type, message and stack trace after an
    unexpected error (never screen content, never clipboard content);
  - `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\TextReveal` - only if the startup checkbox
    is ticked, and removable from the settings window;
  - `%LOCALAPPDATA%\Programs\TextReveal\` with one `.lnk` on the desktop and one in the Start menu,
    plus an `HKCU\...\Uninstall\TextReveal` entry - only if you accept the install page, and the
    uninstall entry removes all of it.

## Hardened where it touches the system

| Surface | Measure |
| --- | --- |
| Screen | One GDI capture of the monitor under the cursor, in memory only |
| OCR | The engine shipped with Windows, called in-process; no image is handed to another process |
| Configuration | Strict JSON parsing (string-only enums, no type-name resolution), size-capped before reading, and validated: a shortcut needs at least one real modifier and a defined key, so a crafted `config.json` can never register a bare key as a global hotkey |
| Writes | Atomic: a unique temporary file in the target folder, then replace - no predictable temporary path to redirect |
| Registry | A single `HKCU\...\Run` value, written only from a fully qualified `.exe` path without anything appended |
| Installer | Copies the running executable into the user profile, creates two `.lnk` files through the Windows Script Host and one `HKCU\...\Uninstall` entry; every path is derived from `%LOCALAPPDATA%`, never from user input |
| Native calls | `[LibraryImport]` only (no hand-written marshalling), and the assembly restricts the DLL search path to `System32`, so a DLL dropped next to the executable is never loaded |
| Robustness | Unexpected exceptions are logged and the application keeps running; a busy clipboard never takes it down |

## Reporting

Found something? Please open an issue describing the scenario. Security reports are welcome.
