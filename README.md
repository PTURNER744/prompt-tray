# Prompt Tray

Prompt Tray is a portable Windows 10/11 app for keeping everyday text and files in the notification area. It runs without an account or cloud storage.

[Product website](https://pturner744.github.io/prompt-tray/) · [Download the Windows beta](https://github.com/PTURNER744/prompt-tray/releases/tag/v0.1.0) · [Ironworks Technology](https://www.ironworkstechnology.com/)

The beta is unsigned. Windows Smart App Control may block it from launching.

## Use it

1. Extract the ZIP and open **PromptTray.exe**. The app uses the Windows .NET Framework 4.8 runtime.
2. Choose **New item**, then pick **Text**, **Link to a file**, or **Store a copy of a file**. Type the text or choose a file. Add a category if you want.
3. Search the text, filename, file path, or category. Use **↑** and **↓** to select a result from search. Text items use their first line as the list label, and file items use the filename. Existing prompts appear as text items.
4. Press **Enter** or double-click to copy text or open a file. **Copy file** lets you paste the actual file into a File Explorer folder, and **Copy path** copies the location as text. Use **Options** for categories, preview, editing, and deletion. **Ctrl+N** opens the item editor.
5. To paste text directly, put the cursor in the destination app, open Prompt Tray from its notification-area icon, and choose **Paste into previous app**. If Windows blocks activation or the paste keystroke, switch back to the app and press **Ctrl+V**. Elevated apps and apps with different paste shortcuts can require manual paste.

Press **Ctrl+Alt+P** from any app to open Prompt Tray and focus search while Prompt Tray is running. If Windows reports the shortcut is unavailable, use the tray icon or taskbar button.

Use **Options → Show on taskbar** to keep a taskbar button. With this enabled, closing the window or pressing **Esc** minimizes it to the taskbar. With it disabled, the window hides to the notification area. Click its tray icon to reopen it. To pin the app permanently, right-click its taskbar button and choose **Pin to taskbar**. Use **Options → Exit** or right-click the tray icon and choose **Exit** to quit.

## Auto-save the clipboard

Turn on **Options → Auto-save clipboard**, or use the tray menu. It starts off and remembers your choice. While enabled and running, new copies appear under **Options → Filter by category → Clipboard**. Text, including URLs, saves as plain text. Copied files save as links to the originals, and copied images save as local PNG files. Custom app formats and rich formatting aren't preserved.

Prompt Tray skips its own copies, duplicate text and file links, and items marked to stay out of clipboard history. Some apps can put private text on the clipboard without marking it. Turn auto-save off before copying sensitive material. Turning it off pauses capture. Already saved items remain in your library.

## Drag and drop

Drop files from File Explorer, selected text, or an image from an app that supports dragging it onto the Prompt Tray window. Dropped files become links to their originals. Dropped images save as local PNGs. New items go in the active category, or **Uncategorized** when viewing all categories.

Drag a saved file from the list into a File Explorer folder to copy it. Drag a text item into an app that accepts dropped text. These actions work with clipboard auto-save off.

## Local storage

The app saves the library in **%LOCALAPPDATA%\PromptTray\prompts.json** and keeps the previous revision in **prompts.json.bak**. Both are plain-text files. Linked files stay at their original locations. Stored copies go in the **Files** subfolder and can be opened even if the original moves. Deleting a library entry leaves its stored copy on disk so a restored library can reopen it. To back up the library and its file copies, save the whole **PromptTray** folder.

## MVP architecture

| Source | Responsibility |
| --- | --- |
| Program.cs | Starts the single-instance WinForms app and handles the tray icon, item list, search, clipboard actions, and file opening. |
| PromptEditor.cs | Edits text, file paths, and categories. |
| Library.cs | Stores the per-user JSON library and optional file copies. JSON writes go to a temporary file before replacing the library. |
| Foreground.cs | Remembers the last foreground app outside Prompt Tray and the notification area. The explicit paste action restores the captured window and checks focus and clipboard state before issuing Ctrl+V. |
| ShortcutWindow.cs | Registers the global shortcut and lets a second launch reopen the existing window. |

## Rebuild

From Windows PowerShell, run this command in the extracted folder. The build uses the Windows .NET Framework compiler and has no external packages.

```powershell
.\Build.ps1
```
