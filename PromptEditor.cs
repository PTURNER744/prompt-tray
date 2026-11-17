using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PromptTray
{
    internal sealed class PromptEditor : Form
    {
        private readonly ComboBox type = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ComboBox category = new ComboBox
        {
            Dock = DockStyle.Fill, MaxLength = 80, AutoCompleteMode = AutoCompleteMode.Suggest,
            AutoCompleteSource = AutoCompleteSource.ListItems
        };
        private readonly TextBox body = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical, MaxLength = 0
        };
        private readonly TextBox file = new TextBox { Dock = DockStyle.Fill, AccessibleName = "File path" };
        private readonly Label valueLabel = new Label { AutoSize = true };
        private readonly TableLayoutPanel fileRow;
        private readonly Prompt original;
        private readonly string originalPath;

        public Prompt Value { get; private set; }
        public bool ImportFile { get; private set; }

        public PromptEditor(Prompt original, string originalPath, IEnumerable<string> categories, string defaultCategory)
        {
            this.original = original;
            this.originalPath = originalPath;
            Text = original == null ? "New item" : "Edit item";
            Font = new Font("Segoe UI", 10);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(580, 420);
            MinimumSize = new Size(420, 350);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = MaximizeBox = false;
            category.Items.AddRange(categories.Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase).ToArray());
            type.Items.AddRange(new[] { "Text", "Link to a file", "Store a copy of a file" });
            category.Text = original == null ? defaultCategory : original.Category;
            body.Text = original == null ? "" : original.Text ?? "";
            file.Text = originalPath ?? "";

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(14) };
            for (int row = 0; row < 5; row++)
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(grid);
            AddField(grid, 0, "&Type", type);
            AddField(grid, 2, "&Category (optional)", category);
            grid.Controls.Add(valueLabel, 0, 4);
            var valuePanel = new Panel { Dock = DockStyle.Fill };
            fileRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1 };
            fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var browse = TrayWindow.MakeButton("Browse…");
            browse.Click += delegate
            {
                using (var picker = new OpenFileDialog { CheckFileExists = true, RestoreDirectory = true })
                    if (picker.ShowDialog(this) == DialogResult.OK)
                        file.Text = picker.FileName;
            };
            fileRow.Controls.Add(file, 0, 0);
            fileRow.Controls.Add(browse, 1, 0);
            valuePanel.Controls.Add(body);
            valuePanel.Controls.Add(fileRow);
            grid.Controls.Add(valuePanel, 0, 5);

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft
            };
            var save = TrayWindow.MakeButton("Save");
            var cancel = TrayWindow.MakeButton("Cancel");
            cancel.DialogResult = DialogResult.Cancel;
            actions.Controls.AddRange(new Control[] { save, cancel });
            grid.Controls.Add(actions, 0, 6);
            AcceptButton = save;
            CancelButton = cancel;
            save.Click += delegate { Commit(); };
            type.SelectedIndexChanged += delegate { UpdateValueField(); };
            type.SelectedIndex = originalPath == null ? 0 : original.AttachmentId == null ? 1 : 2;
            ActiveControl = originalPath == null ? (Control)body : file;
        }

        private void UpdateValueField()
        {
            bool isText = type.SelectedIndex == 0;
            valueLabel.Text = isText ? "&Text" : "&File path";
            body.Visible = isText;
            fileRow.Visible = !isText;
            if (!isText)
                fileRow.BringToFront();
        }

        private static void AddField(TableLayoutPanel grid, int row, string label, Control control)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true }, 0, row);
            grid.Controls.Add(control, 0, row + 1);
        }

        private void Commit()
        {
            string itemCategory = string.IsNullOrWhiteSpace(category.Text) ? "Uncategorized" : category.Text.Trim();
            if (type.SelectedIndex == 0 && !string.IsNullOrWhiteSpace(body.Text))
            {
                Value = new Prompt { Category = itemCategory, Text = body.Text };
                DialogResult = DialogResult.OK;
                return;
            }
            if (type.SelectedIndex != 0)
            {
                string path = file.Text.Trim().Trim('"');
                bool unchanged = string.Equals(path, originalPath, StringComparison.OrdinalIgnoreCase);
                bool keepAttachment = type.SelectedIndex == 2 && unchanged && original.AttachmentId != null;
                if (keepAttachment || (type.SelectedIndex == 1 && unchanged) || File.Exists(path))
                {
                    ImportFile = type.SelectedIndex == 2 && !keepAttachment;
                    Value = new Prompt
                    {
                        Category = itemCategory,
                        FilePath = keepAttachment ? original.FilePath : Path.GetFullPath(path),
                        AttachmentId = keepAttachment ? original.AttachmentId : null
                    };
                    DialogResult = DialogResult.OK;
                    return;
                }
            }
            MessageBox.Show(this, type.SelectedIndex == 0 ? "Enter some text." : "Choose an existing file.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            (type.SelectedIndex == 0 ? body : file).Focus();
        }
    }
}
