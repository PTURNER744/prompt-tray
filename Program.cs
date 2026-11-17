using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows.Forms;

namespace PromptTray
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool firstInstance;
            using (var instance = new Mutex(true, @"Local\PromptTray.Library", out firstInstance))
            {
                if (!firstInstance)
                {
                    if (!ShortcutWindow.OpenExisting())
                        MessageBox.Show("Prompt Tray is already running. Open it from the notification area.",
                            "Prompt Tray", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PromptTray", "prompts.json");
                var library = new Library(path);
                try
                {
                    using (var foreground = new Foreground())
                    using (var window = new TrayWindow(library, library.Load(), foreground))
                        Application.Run(window);
                }
                catch (Exception error)
                {
                    if (!IsLibraryError(error))
                        throw;
                    MessageBox.Show("Could not open the prompt library.\n\n" + path + "\n\n" + error.Message,
                        "Prompt Tray", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        internal static bool IsLibraryError(Exception error)
        {
            return error is IOException || error is UnauthorizedAccessException || error is SerializationException;
        }
    }

    internal sealed class TrayWindow : Form
    {
        private readonly Library library;
        private readonly Foreground foreground;
        private List<Prompt> prompts;
        private Foreground.Target target;
        private uint clipboardVersion;
        private uint lastCapturedSequence;
        private uint pendingClipboardSequence;
        private int clipboardAttempts;
        private bool capturingClipboard;
        private bool draggingItem;
        private DateTime pasteDeadline;
        private bool exiting;
        private bool refreshing;
        private string categoryFilter;

        private static readonly Color Canvas = Color.FromArgb(246, 248, 252);
        private static readonly Color Ink = Color.FromArgb(30, 41, 59);
        private static readonly Color Muted = Color.FromArgb(71, 85, 105);
        private static readonly Color Border = Color.FromArgb(207, 216, 230);
        private static readonly Color Accent = Color.FromArgb(52, 79, 192);
        private static readonly Color Selection = Color.FromArgb(233, 239, 255);
        private readonly NotifyIcon tray;
        private readonly Icon windowIcon;
        private readonly Icon notificationIcon;
        private readonly ShortcutWindow shortcuts;
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly ToolStripMenuItem taskbarOption = new ToolStripMenuItem("Show on taskbar");
        private readonly ToolStripMenuItem autoSaveOption = new ToolStripMenuItem("Auto-save clipboard");
        private readonly ToolStripMenuItem categoryOption = new ToolStripMenuItem("Filter by category");
        private readonly ToolStripMenuItem previewOption = new ToolStripMenuItem("Show preview");
        private readonly ToolStripMenuItem edit = new ToolStripMenuItem("Edit selected item");
        private readonly ToolStripMenuItem delete = new ToolStripMenuItem("Delete selected item");
        private readonly RowStyle previewRow = new RowStyle(SizeType.Absolute, 0);
        private readonly Panel details;
        private readonly ImageList rowSpacing;
        private readonly Font metadataFont;
        private readonly System.Windows.Forms.Timer pasteTimer = new System.Windows.Forms.Timer { Interval = 45 };
        private readonly System.Windows.Forms.Timer clipboardTimer = new System.Windows.Forms.Timer { Interval = 60 };
        private readonly TextBox search = new TextBox
        {
            Dock = DockStyle.Top, BorderStyle = BorderStyle.None, AccessibleName = "Search your items"
        };
        private readonly ListView results = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false,
            HideSelection = false, AccessibleName = "Matching items", ShowGroups = false,
            HeaderStyle = ColumnHeaderStyle.None, BorderStyle = BorderStyle.None
        };
        private readonly TextBox preview = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            BackColor = SystemColors.Window, AccessibleName = "Item preview"
        };
        private readonly Label status = new Label { AutoSize = true, Margin = new Padding(3, 8, 3, 2) };
        private readonly Button copy = MakeButton("Copy");
        private readonly Button copyPath = MakeButton("Copy path");
        private readonly Button open = MakeButton("Open file");
        private readonly Button paste = MakeButton("Paste into previous app");

        public TrayWindow(Library library, List<Prompt> prompts, Foreground foreground)
        {
            this.library = library;
            this.prompts = prompts;
            this.foreground = foreground;
            target = foreground.Capture();

            Text = "Prompt Tray";
            using (Stream icon = typeof(TrayWindow).Assembly.GetManifestResourceStream("PromptTray.Icon"))
                windowIcon = new Icon(icon, SystemInformation.IconSize);
            notificationIcon = new Icon(windowIcon, SystemInformation.SmallIconSize);
            Icon = windowIcon;
            Font = new Font("Segoe UI", 10);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(600, 355);
            MinimumSize = new Size(505, 290);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = library.ShowOnTaskbar;
            BackColor = SystemInformation.HighContrast ? SystemColors.Control : Canvas;
            ForeColor = SystemInformation.HighContrast ? SystemColors.ControlText : Ink;
            KeyPreview = true;

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(14, 14, 14, 9) };
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(previewRow);
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(grid);

            var searchSurface = new Panel
            {
                Dock = DockStyle.Fill, AutoSize = true, BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(11, 12, 11, 12), Margin = new Padding(0, 0, 0, 10),
                BackColor = SystemColors.Window
            };
            search.HandleCreated += delegate { SendMessage(search.Handle, 0x1501, new IntPtr(1), "Search your items…"); };
            searchSurface.Controls.Add(search);
            grid.Controls.Add(searchSurface, 0, 0);
            results.Margin = new Padding(0, 0, 0, 8);
            grid.Controls.Add(results, 0, 1);
            details = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8), Visible = false };
            details.Controls.Add(preview);
            grid.Controls.Add(details, 0, 2);

            var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
            StylePrimary(copy);
            var add = MakeButton("New item");
            var options = MakeButton("Options");
            options.Click += delegate { menu.Show(options, Point.Empty, ToolStripDropDownDirection.AboveLeft); };
            actions.Controls.AddRange(new Control[] { open, copy, copyPath, paste, add, options });
            grid.Controls.Add(actions, 0, 3);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1 };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var shortcutHint = new Label { AutoSize = true, Anchor = AnchorStyles.Right, Margin = status.Margin };
            if (!SystemInformation.HighContrast)
                status.ForeColor = shortcutHint.ForeColor = Muted;
            footer.Controls.Add(status, 0, 0);
            footer.Controls.Add(shortcutHint, 1, 0);
            grid.Controls.Add(footer, 0, 4);

            results.Columns.Add("Item");
            if (!SystemInformation.HighContrast)
            {
                rowSpacing = new ImageList { ImageSize = new Size(1, (int)Math.Ceiling(Font.GetHeight() * 2.4)) };
                results.SmallImageList = rowSpacing;
                metadataFont = new Font(Font.FontFamily, 8.5f);
                results.OwnerDraw = true;
                results.DrawSubItem += DrawResult;
            }
            results.SizeChanged += delegate { ResizeColumns(); };
            search.TextChanged += delegate { RefreshResults(Selected); };
            results.SelectedIndexChanged += delegate { if (!refreshing) UpdateSelection(); };
            results.DoubleClick += delegate { UseSelected(); };
            results.ItemDrag += DragItemOut;
            foreach (Control surface in new Control[] { this, results, search, preview })
            {
                surface.AllowDrop = true;
                surface.DragEnter += OfferDrop;
                surface.DragOver += OfferDrop;
                surface.DragDrop += ImportDrop;
            }
            copy.Click += delegate
            {
                if (Selected != null && Selected.IsFile)
                    CopySelectedFile();
                else
                    CopySelected(false);
            };
            copyPath.Click += delegate { CopySelected(false); };
            open.Click += delegate { OpenSelected(); };
            paste.Click += delegate { CopySelected(true); };
            add.Click += delegate { EditPrompt(null); };
            edit.Click += delegate { if (Selected != null) EditPrompt(Selected); };
            delete.Click += delegate { DeletePrompt(); };
            Shown += delegate { ResizeColumns(); RefreshResults(Selected); search.Focus(); };
            Activated += delegate { target = foreground.Capture(); UpdateSelection(); };

            var show = new ToolStripMenuItem("Open Prompt Tray", null, delegate { OpenTray(); });
            previewOption.Click += delegate
            {
                previewOption.Checked = !previewOption.Checked;
                previewRow.Height = previewOption.Checked ? Math.Max(100, Font.Height * 6) : 0;
                details.Visible = previewOption.Checked;
            };
            taskbarOption.Checked = ShowInTaskbar;
            taskbarOption.Click += delegate { ToggleTaskbar(); };
            menu.Items.Add(show);
            menu.Items.Add(categoryOption);
            menu.Items.Add(previewOption);
            menu.Items.Add(edit);
            menu.Items.Add(delete);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(autoSaveOption);
            menu.Items.Add(taskbarOption);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { exiting = true; Close(); });
            tray = new NotifyIcon { Icon = notificationIcon, Text = "Prompt Tray", ContextMenuStrip = menu, Visible = true };
            tray.MouseClick += delegate(object sender, MouseEventArgs args)
            {
                if (args.Button == MouseButtons.Left) OpenTray();
            };
            shortcuts = new ShortcutWindow(OpenTray, ClipboardChanged);
            autoSaveOption.Click += delegate { ToggleClipboardCapture(); };
            clipboardTimer.Tick += delegate { CaptureClipboard(); };
            if (shortcuts.Registered)
                show.ShortcutKeyDisplayString = ShortcutWindow.ShortcutText;
            shortcutHint.Text = shortcuts.Registered ? ShortcutWindow.ShortcutText + " to open" :
                ShortcutWindow.ShortcutText + " unavailable";
            pasteTimer.Tick += delegate { FinishPaste(); };
            RefreshCategories(null);
            if (library.AutoSaveClipboard)
                SetClipboardCapture(true);
        }

        internal static Button MakeButton(string text)
        {
            var button = new Button { Text = text, AutoSize = true, Padding = new Padding(10, 4, 10, 4) };
            if (!SystemInformation.HighContrast)
            {
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Border;
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(233, 239, 249);
                button.BackColor = Color.White;
                button.ForeColor = Ink;
            }
            return button;
        }

        private void ResizeColumns()
        {
            results.Columns[0].Width = Math.Max(0, results.ClientSize.Width);
        }

        private void DrawResult(object sender, DrawListViewSubItemEventArgs args)
        {
            var item = (Prompt)args.Item.Tag;
            bool selected = args.Item.Selected;
            var bounds = args.Bounds;
            using (var background = new SolidBrush(selected ? Selection : SystemColors.Window))
                args.Graphics.FillRectangle(background, bounds);
            int left = bounds.Left + 12;
            int width = Math.Max(0, bounds.Width - 24);
            int lineHeight = Font.Height;
            int top = bounds.Top + Math.Max(0, (bounds.Height - lineHeight - metadataFont.Height) / 2);
            const TextFormatFlags flags = TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine |
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(args.Graphics, item.Label, Font,
                new Rectangle(left, top, width, lineHeight), Ink, flags);
            string type = item.IsFile ? item.AttachmentId == null ? "File link" : "Stored file" : "Text";
            TextRenderer.DrawText(args.Graphics, type + "  ·  " + item.Category, metadataFont,
                new Rectangle(left, top + lineHeight, width, metadataFont.Height), Muted, flags);
            if (selected && results.Focused)
                ControlPaint.DrawFocusRectangle(args.Graphics, bounds);
        }

        private static void StylePrimary(Button button)
        {
            if (SystemInformation.HighContrast)
                return;
            button.BackColor = Accent;
            button.ForeColor = Color.White;
            button.FlatAppearance.BorderColor = Accent;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(26, 59, 146);
        }

        private void ToggleTaskbar()
        {
            bool enabled = !ShowInTaskbar;
            try
            {
                library.ShowOnTaskbar = enabled;
            }
            catch (Exception error)
            {
                if (!Program.IsLibraryError(error))
                    throw;
                MessageBox.Show("Could not save the taskbar preference.\n\n" + error.Message, Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            taskbarOption.Checked = ShowInTaskbar = enabled;
            if (enabled && !Visible)
                OpenTray();
            else if (!enabled && WindowState == FormWindowState.Minimized)
                Hide();
        }

        private void ToggleClipboardCapture()
        {
            bool previous = autoSaveOption.Checked;
            if (!SetClipboardCapture(!previous))
                return;
            try
            {
                library.AutoSaveClipboard = !previous;
            }
            catch (Exception error)
            {
                if (!Program.IsLibraryError(error))
                    throw;
                SetClipboardCapture(previous);
                status.Text = "Could not save the clipboard preference.";
                return;
            }
            status.Text = previous ? "Clipboard auto-save is off." : "New copies will be saved under Clipboard.";
        }

        private bool SetClipboardCapture(bool enabled)
        {
            lastCapturedSequence = Foreground.GetClipboardSequenceNumber();
            clipboardTimer.Stop();
            if (!shortcuts.SetClipboardListening(enabled))
            {
                status.Text = "Windows did not allow clipboard monitoring.";
                return false;
            }
            autoSaveOption.Checked = enabled;
            tray.Text = enabled ? "Prompt Tray — clipboard auto-save on" : "Prompt Tray";
            return true;
        }

        private void ClipboardChanged()
        {
            uint sequence = Foreground.GetClipboardSequenceNumber();
            if (!autoSaveOption.Checked || sequence == lastCapturedSequence)
                return;
            if (sequence != pendingClipboardSequence)
            {
                pendingClipboardSequence = sequence;
                clipboardAttempts = 0;
            }
            if (capturingClipboard)
                clipboardTimer.Start();
            else
                CaptureClipboard();
        }

        private void CaptureClipboard()
        {
            if (capturingClipboard)
                return;
            clipboardTimer.Stop();
            uint sequence = Foreground.GetClipboardSequenceNumber();
            if (!autoSaveOption.Checked || sequence == lastCapturedSequence || sequence == clipboardVersion)
                return;
            capturingClipboard = true;
            try
            {
                IDataObject data = Clipboard.GetDataObject();
                List<Prompt> added = ItemTransfer.ReadClipboard(data, prompts, library);
                lastCapturedSequence = sequence;
                if (!autoSaveOption.Checked || added.Count == 0)
                {
                    DiscardCapturedFiles(added);
                    return;
                }
                if (!SaveImportedItems(added))
                    return;
                RefreshCategories(Selected);
                status.Text = added.Count == 1 ? "Saved to Clipboard." : added.Count + " items saved to Clipboard.";
            }
            catch (ExternalException)
            {
                if (++clipboardAttempts < 4 && autoSaveOption.Checked)
                    clipboardTimer.Start();
                else
                {
                    lastCapturedSequence = sequence;
                    status.Text = "Could not read the copied item.";
                }
            }
            catch (Exception error)
            {
                if (!Program.IsLibraryError(error) && !(error is ArgumentException))
                    throw;
                lastCapturedSequence = sequence;
                status.Text = "Could not auto-save the copied item.";
            }
            finally
            {
                capturingClipboard = false;
            }
        }

        private bool SaveImportedItems(List<Prompt> items)
        {
            if (Save(prompts.Concat(items).ToList(), false))
                return true;
            DiscardCapturedFiles(items);
            return false;
        }

        private void DiscardCapturedFiles(List<Prompt> items)
        {
            foreach (Prompt item in items.Where(value => value.AttachmentId != null))
                library.DiscardAttachment(item);
        }

        private void OfferDrop(object sender, DragEventArgs args)
        {
            args.Effect = DragDropEffects.None;
            if (draggingItem)
                return;
            try
            {
                args.Effect = ItemTransfer.GetDropEffect(args.Data, args.AllowedEffect);
            }
            catch (ExternalException) { }
        }

        private void ImportDrop(object sender, DragEventArgs args)
        {
            OfferDrop(sender, args);
            if (args.Effect == DragDropEffects.None)
                return;
            List<Prompt> added;
            try
            {
                added = ItemTransfer.ReadItems(args.Data, prompts, library, categoryFilter ?? "Uncategorized");
            }
            catch (Exception error)
            {
                if (!Program.IsLibraryError(error) && !(error is ArgumentException) && !(error is ExternalException))
                    throw;
                args.Effect = DragDropEffects.None;
                status.Text = "Could not read the dropped items.";
                return;
            }
            if (added.Count == 0)
            {
                status.Text = "No new items to add.";
                args.Effect = DragDropEffects.None;
                return;
            }
            if (!SaveImportedItems(added))
            {
                args.Effect = DragDropEffects.None;
                return;
            }

            Prompt selected = added.FirstOrDefault(item => Library.Matches(item, search.Text));
            if (selected == null)
            {
                search.Clear();
                selected = added[0];
            }
            RefreshCategories(selected);
            status.Text = added.Count == 1 ? "Added 1 item." : "Added " + added.Count + " items.";
        }

        private void DragItemOut(object sender, ItemDragEventArgs args)
        {
            var row = args.Item as ListViewItem;
            if (args.Button != MouseButtons.Left || row == null)
                return;
            var item = (Prompt)row.Tag;
            DataObject data;
            if (item.IsFile)
            {
                string path = library.ResolveFile(item);
                if (!File.Exists(path))
                {
                    status.Text = "File unavailable. Use Edit to choose its new location.";
                    return;
                }
                data = ItemTransfer.FileData(path);
            }
            else
                data = new DataObject(DataFormats.UnicodeText, item.Text);

            draggingItem = true;
            try
            {
                results.DoDragDrop(data, DragDropEffects.Copy);
            }
            finally
            {
                draggingItem = false;
            }
        }

        private Prompt Selected
        {
            get { return results.SelectedItems.Count == 0 ? null : (Prompt)results.SelectedItems[0].Tag; }
        }

        private void RefreshCategories(Prompt selected)
        {
            var comparer = StringComparer.CurrentCultureIgnoreCase;
            string[] categories = prompts.Select(prompt => prompt.Category).Distinct(comparer)
                .OrderBy(value => value, comparer).ToArray();
            if (categoryFilter != null)
            {
                string wanted = selected == null ? categoryFilter : selected.Category;
                categoryFilter = categories.FirstOrDefault(value => comparer.Equals(value, wanted));
            }
            while (categoryOption.DropDownItems.Count > 0)
            {
                ToolStripItem previous = categoryOption.DropDownItems[0];
                categoryOption.DropDownItems.RemoveAt(0);
                previous.Dispose();
            }
            foreach (string category in new string[] { null }.Concat(categories))
            {
                var option = new ToolStripMenuItem(category ?? "All categories")
                {
                    Checked = comparer.Equals(category, categoryFilter)
                };
                option.Click += delegate
                {
                    categoryFilter = category;
                    RefreshCategories(null);
                };
                categoryOption.DropDownItems.Add(option);
            }
            RefreshResults(selected);
        }

        private void RefreshResults(Prompt selected)
        {
            refreshing = true;
            results.BeginUpdate();
            try
            {
                results.Items.Clear();
                foreach (Prompt prompt in prompts.OrderBy(value => value.Label, StringComparer.CurrentCultureIgnoreCase))
                {
                    if ((categoryFilter != null && !string.Equals(categoryFilter, prompt.Category,
                        StringComparison.CurrentCultureIgnoreCase)) || !Library.Matches(prompt, search.Text))
                        continue;
                    string label = prompt.IsFile
                        ? (prompt.AttachmentId == null ? "File link  ·  " : "File copy  ·  ") + prompt.Label
                        : prompt.Label;
                    var row = new ListViewItem(label + "  ·  " + prompt.Category) { Tag = prompt };
                    results.Items.Add(row);
                    row.Selected = prompt == selected;
                }
                if (results.Items.Count > 0 && results.SelectedItems.Count == 0)
                    results.Items[0].Selected = true;
                if (results.SelectedItems.Count > 0)
                    results.SelectedItems[0].EnsureVisible();
            }
            finally
            {
                results.EndUpdate();
                refreshing = false;
            }
            UpdateSelection();
            status.Text = prompts.Count == 0 ? "Drop an item here or choose New item." :
                results.Items.Count == 0 ? "No items match these filters." :
                results.Items.Count + (results.Items.Count == 1 ? " item" : " items") +
                (categoryFilter == null ? "" : "  ·  " + categoryFilter);
        }

        private void UpdateSelection()
        {
            Prompt prompt = Selected;
            bool isFile = prompt != null && prompt.IsFile;
            preview.Text = prompt == null ? "" : isFile ? library.ResolveFile(prompt) : prompt.Text;
            copy.Enabled = edit.Enabled = delete.Enabled = prompt != null;
            copy.Text = isFile ? "Copy file" : "Copy text";
            copyPath.Visible = isFile;
            open.Visible = isFile;
            paste.Visible = !isFile;
            paste.Enabled = prompt != null && !isFile && target.Window != IntPtr.Zero;
        }

        private void OpenTray()
        {
            pasteTimer.Stop();
            target = foreground.Capture();
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            UpdateSelection();
            search.Focus();
            search.SelectAll();
        }

        private void EditPrompt(Prompt original)
        {
            string filePath = original != null && original.IsFile ? library.ResolveFile(original) : null;
            using (var editor = new PromptEditor(original, filePath, prompts.Select(prompt => prompt.Category),
                categoryFilter ?? ""))
            {
                editor.Icon = windowIcon;
                if (editor.ShowDialog(this) != DialogResult.OK)
                    return;
                var updated = new List<Prompt>(prompts);
                Prompt replacement = editor.Value;
                if (editor.ImportFile)
                {
                    try
                    {
                        replacement = library.Attach(replacement);
                    }
                    catch (Exception error)
                    {
                        if (!Program.IsLibraryError(error))
                            throw;
                        MessageBox.Show(this, "Could not store a copy of the file.\n\n" + error.Message,
                            Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }
                if (original == null)
                    updated.Add(replacement);
                else
                    updated[updated.IndexOf(original)] = replacement;
                if (!Save(updated))
                {
                    if (editor.ImportFile)
                        library.DiscardAttachment(replacement);
                    return;
                }
                if (!Library.Matches(replacement, search.Text))
                    search.Clear();
                RefreshCategories(replacement);
                status.Text = "Item saved.";
            }
        }

        private void DeletePrompt()
        {
            Prompt prompt = Selected;
            if (prompt == null || MessageBox.Show(this, "Remove this item from the library?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            var updated = new List<Prompt>(prompts);
            updated.Remove(prompt);
            if (Save(updated))
            {
                RefreshCategories(null);
                status.Text = "Item removed.";
            }
        }

        private void UseSelected()
        {
            if (Selected != null && Selected.IsFile)
                OpenSelected();
            else
                CopySelected(false);
        }

        private void OpenSelected()
        {
            Prompt item = Selected;
            if (item == null || !item.IsFile)
                return;
            string path = library.ResolveFile(item);
            if (!File.Exists(path))
            {
                status.Text = "File unavailable. Use Edit to choose its new location.";
                return;
            }
            try
            {
                using (Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })) { }
            }
            catch (Win32Exception error)
            {
                MessageBox.Show(this, "Could not open the file.\n\n" + error.Message,
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CopySelectedFile()
        {
            Prompt item = Selected;
            if (item == null || !item.IsFile)
                return;
            string path = library.ResolveFile(item);
            if (!File.Exists(path))
            {
                status.Text = "File unavailable. Use Edit to choose its new location.";
                return;
            }

            var data = ItemTransfer.FileData(path);
            const int dropEffectCopy = 1;
            using (var effect = new MemoryStream(BitConverter.GetBytes(dropEffectCopy)))
            {
                data.SetData("Preferred DropEffect", false, effect);
                if (WriteClipboard(data))
                    status.Text = "File copied. Paste it into a folder.";
            }
        }

        private bool Save(List<Prompt> updated, bool showDialog = true)
        {
            try
            {
                library.Save(updated);
                prompts = updated;
                return true;
            }
            catch (Exception error)
            {
                if (!Program.IsLibraryError(error))
                    throw;
                if (showDialog)
                    MessageBox.Show(this, "Could not save the library.\n\n" + error.Message,
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                else
                    status.Text = "Could not save the new items.";
                return false;
            }
        }

        private void CopySelected(bool pasteIntoPrevious)
        {
            Prompt prompt = Selected;
            if (prompt == null || (pasteIntoPrevious && prompt.IsFile))
                return;
            string value = prompt.IsFile ? library.ResolveFile(prompt) : prompt.Text;
            if (!WriteClipboard(new DataObject(DataFormats.UnicodeText, value)))
                return;
            status.Text = prompt.IsFile ? "File path copied." : "Copied to clipboard.";
            if (!pasteIntoPrevious)
                return;
            if (Foreground.Activate(target))
            {
                DismissWindow();
                pasteDeadline = DateTime.UtcNow.AddMilliseconds(700);
                pasteTimer.Start();
            }
            else
            {
                status.Text = "Copied. Switch to your app and press Ctrl+V.";
            }
        }

        private bool WriteClipboard(DataObject data)
        {
            try
            {
                using (var marker = new MemoryStream(new byte[] { 1 }))
                {
                    data.SetData(ItemTransfer.OwnFormat, false, marker);
                    Clipboard.SetDataObject(data, true, 5, 40);
                }
                clipboardVersion = Foreground.GetClipboardSequenceNumber();
                return true;
            }
            catch (ExternalException)
            {
                status.Text = "The clipboard is busy. Try again.";
                return false;
            }
        }

        private void FinishPaste()
        {
            if (DateTime.UtcNow < pasteDeadline && Foreground.IsActive(target) && Foreground.ModifiersDown())
                return;
            pasteTimer.Stop();
            if (!Foreground.ModifiersDown() && Foreground.Paste(target, clipboardVersion))
                return;
            string fallback = Foreground.GetClipboardSequenceNumber() == clipboardVersion
                ? "Switch to your app and press Ctrl+V."
                : "The clipboard changed. Copy the prompt again to paste it.";
            status.Text = fallback;
            tray.ShowBalloonTip(3000, Text, fallback, ToolTipIcon.Info);
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == Keys.Escape)
                DismissWindow();
            else if (keyData == (Keys.Control | Keys.F))
                search.Focus();
            else if (keyData == (Keys.Control | Keys.N))
                EditPrompt(null);
            else if (search.Focused && (keyData == Keys.Up || keyData == Keys.Down))
                MoveSelection(keyData == Keys.Down ? 1 : -1);
            else if (keyData == Keys.Enter && (search.Focused || results.Focused))
                UseSelected();
            else
                return base.ProcessCmdKey(ref message, keyData);
            return true;
        }

        private void MoveSelection(int direction)
        {
            if (results.Items.Count == 0)
                return;
            int current = results.SelectedIndices.Count == 0 ? -1 : results.SelectedIndices[0];
            int index = Math.Max(0, Math.Min(results.Items.Count - 1, current + direction));
            var item = results.Items[index];
            item.Selected = true;
            item.Focused = true;
            item.EnsureVisible();
        }

        private void DismissWindow()
        {
            if (ShowInTaskbar)
                WindowState = FormWindowState.Minimized;
            else
                Hide();
        }

        protected override void OnFormClosing(FormClosingEventArgs args)
        {
            base.OnFormClosing(args);
            if (!exiting && args.CloseReason == CloseReason.UserClosing)
            {
                args.Cancel = true;
                DismissWindow();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                pasteTimer.Dispose();
                clipboardTimer.Dispose();
                if (shortcuts != null)
                    shortcuts.Dispose();
                if (tray != null)
                    tray.Dispose();
                menu.Dispose();
            }
            base.Dispose(disposing);
            if (disposing)
            {
                if (notificationIcon != null)
                    notificationIcon.Dispose();
                if (windowIcon != null)
                    windowIcon.Dispose();
                if (rowSpacing != null)
                    rowSpacing.Dispose();
                if (metadataFont != null)
                    metadataFont.Dispose();
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr flag, string text);
    }
}
