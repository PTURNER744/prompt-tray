using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PromptTray
{
    internal static class ItemTransfer
    {
        public const string OwnFormat = "PromptTray.Copy";
        public static List<Prompt> ReadClipboard(IDataObject data, List<Prompt> existing, Library library)
        {
            if (data == null || data.GetDataPresent(OwnFormat, false) ||
                data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing", false) ||
                IsZeroFlag(data.GetData("CanIncludeInClipboardHistory", false)))
                return new List<Prompt>();

            return ReadItems(data, existing, library, "Clipboard");
        }

        public static DragDropEffects GetDropEffect(IDataObject data, DragDropEffects allowed)
        {
            if (data == null)
                return DragDropEffects.None;
            bool files = data.GetDataPresent(DataFormats.FileDrop, false);
            if ((allowed & DragDropEffects.Copy) != 0 && (files ||
                data.GetDataPresent(DataFormats.UnicodeText, true) ||
                data.GetDataPresent(DataFormats.Bitmap, true) || data.GetDataPresent("PNG", false)))
                return DragDropEffects.Copy;
            return files && (allowed & DragDropEffects.Link) != 0 ? DragDropEffects.Link : DragDropEffects.None;
        }

        public static DataObject FileData(string path)
        {
            var data = new DataObject();
            data.SetFileDropList(new StringCollection { path });
            return data;
        }

        public static List<Prompt> ReadItems(IDataObject data, List<Prompt> existing, Library library, string category)
        {
            var added = new List<Prompt>();
            var paths = data.GetData(DataFormats.FileDrop, false) as string[];
            if (paths != null)
            {
                var seen = new HashSet<string>(existing.Where(item => item.IsFile)
                    .Select(library.ResolveFile), StringComparer.OrdinalIgnoreCase);
                foreach (string path in paths)
                    if (File.Exists(path) && seen.Add(path))
                        added.Add(new Prompt { Category = category, FilePath = Path.GetFullPath(path) });
                return added;
            }

            using (Image image = ReadImage(data))
            {
                if (image != null)
                {
                    added.Add(library.AttachImage(category, image));
                    return added;
                }
            }

            string text = data.GetData(DataFormats.UnicodeText, true) as string
                ?? data.GetData(DataFormats.Text, true) as string;
            if (!string.IsNullOrWhiteSpace(text) && !existing.Any(item => !item.IsFile && item.Text == text))
                added.Add(new Prompt { Category = category, Text = text });
            return added;
        }

        private static bool IsZeroFlag(object value)
        {
            var stream = value as MemoryStream;
            var bytes = stream == null ? value as byte[] : stream.ToArray();
            return bytes != null && bytes.Length >= sizeof(int) && BitConverter.ToInt32(bytes, 0) == 0;
        }

        private static Image ReadImage(IDataObject data)
        {
            var png = data.GetData("PNG", false) as Stream;
            if (png != null)
            {
                try
                {
                    using (Image image = Image.FromStream(png))
                        return new Bitmap(image);
                }
                catch (ArgumentException) { }
            }
            return data.GetData(DataFormats.Bitmap, true) as Image;
        }
    }
}
