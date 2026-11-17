using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace PromptTray
{
    [DataContract]
    internal sealed class Prompt
    {
        [DataMember] public string Category;
        [DataMember] public string Text;
        [DataMember(EmitDefaultValue = false)] public string FilePath;
        [DataMember(EmitDefaultValue = false)] public string AttachmentId;

        public bool IsFile { get { return FilePath != null; } }
        public string Value { get { return IsFile ? FilePath : Text; } }
        public string Label
        {
            get
            {
                if (IsFile)
                    return Path.GetFileName(FilePath);
                string line = Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
                line = line.Trim();
                return line.Length <= 120 ? line : line.Substring(0, 119) + "…";
            }
        }
    }

    internal sealed class Library
    {
        private readonly string path;
        private readonly DataContractJsonSerializer serializer =
            new DataContractJsonSerializer(typeof(List<Prompt>));

        public Library(string path)
        {
            this.path = path;
        }

        public bool ShowOnTaskbar
        {
            get { return !File.Exists(FlagPath("hide-taskbar")); }
            set { SetFlag("hide-taskbar", !value); }
        }

        public bool AutoSaveClipboard
        {
            get { return File.Exists(FlagPath("auto-save-clipboard")); }
            set { SetFlag("auto-save-clipboard", value); }
        }

        private string FlagPath(string name)
        {
            return Path.Combine(Path.GetDirectoryName(path), name);
        }

        private void SetFlag(string name, bool enabled)
        {
            string flag = FlagPath(name);
            if (enabled)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(flag));
                using (File.Open(flag, FileMode.OpenOrCreate, FileAccess.Write)) { }
            }
            else
                File.Delete(flag);
        }

        public List<Prompt> Load()
        {
            if (!File.Exists(path))
                return new List<Prompt>();

            using (var stream = File.OpenRead(path))
            {
                var prompts = (List<Prompt>)serializer.ReadObject(stream);
                if (prompts == null || prompts.Any(prompt => prompt == null ||
                    string.IsNullOrWhiteSpace(prompt.Category) ||
                    string.IsNullOrWhiteSpace(prompt.Value) ||
                    (prompt.IsFile && !IsValidFile(prompt))))
                    throw new InvalidDataException("The prompt library contains an invalid entry.");
                return prompts;
            }
        }

        private static bool IsValidFile(Prompt item)
        {
            Guid id;
            return item.AttachmentId == null
                ? Path.IsPathRooted(item.FilePath) && !string.IsNullOrEmpty(Path.GetFileName(item.FilePath))
                : Guid.TryParseExact(item.AttachmentId, "N", out id) &&
                    Path.GetFileName(item.FilePath) == item.FilePath &&
                    item.FilePath.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
                    item.FilePath != "." && item.FilePath != "..";
        }

        public string ResolveFile(Prompt item)
        {
            return item.AttachmentId == null ? item.FilePath :
                Path.Combine(Path.GetDirectoryName(path), "Files", item.AttachmentId, item.FilePath);
        }

        public Prompt Attach(Prompt source)
        {
            return StoreFile(source.Category, Path.GetFileName(source.FilePath),
                destination => File.Copy(source.FilePath, destination));
        }

        public Prompt AttachImage(string category, Image image)
        {
            string name = "Image " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".png";
            return StoreFile(category, name, destination => image.Save(destination, ImageFormat.Png));
        }

        private Prompt StoreFile(string category, string name, Action<string> write)
        {
            var item = new Prompt
            {
                Category = category,
                FilePath = name,
                AttachmentId = Guid.NewGuid().ToString("N")
            };
            string destination = ResolveFile(item);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            try
            {
                write(destination);
                return item;
            }
            catch
            {
                DiscardAttachment(item);
                throw;
            }
        }

        public void DiscardAttachment(Prompt item)
        {
            string destination = ResolveFile(item);
            try
            {
                File.Delete(destination);
                Directory.Delete(Path.GetDirectoryName(destination));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public void Save(List<Prompt> prompts)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew))
                {
                    serializer.WriteObject(stream, prompts);
                    stream.Flush(true);
                }
                if (File.Exists(path))
                    File.Replace(temporary, path, path + ".bak");
                else
                    File.Move(temporary, path);
            }
            finally
            {
                File.Delete(temporary);
            }
        }

        public static bool Matches(Prompt prompt, string query)
        {
            return query.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
                .All(word => Contains(prompt.Category, word) || Contains(prompt.Value, word));
        }

        private static bool Contains(string value, string word)
        {
            return value.IndexOf(word, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }
    }
}
