using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MimeKit;

namespace mailTester.Mail
{
    public static class AttachmentStore
    {
        public static string ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name == "." || name == ".." ||
                name.IndexOfAny(new[] { '/', '\\', ':', '<', '>', '"', '|', '?', '*' }) >= 0 ||
                name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                name.EndsWith(".") || name.EndsWith(" "))
                throw new IOException("Недопустимое имя вложения.");
            string device = name.Split('.')[0].TrimEnd().ToUpperInvariant();
            if (device == "CON" || device == "PRN" || device == "AUX" || device == "NUL" ||
                device == "CONIN$" || device == "CONOUT$" ||
                Regex.IsMatch(device, @"^(COM|LPT)[0-9¹²³]$"))
                throw new IOException("Зарезервированное имя вложения.");
            foreach (char c in name)
                if (char.IsControl(c)) throw new IOException("Недопустимый символ в имени.");
            return name;
        }

        public static async Task<IReadOnlyList<string>> SaveAsync(MimeMessage message, string directory, CancellationToken ct)
        {
            var saved = new List<string>();
            foreach (var entity in message.Attachments)
            {
                ct.ThrowIfCancellationRequested();
                if (entity is MessagePart nested)
                {
                    string name = entity.ContentDisposition?.FileName ?? entity.ContentType.Name ?? "message.eml";
                    saved.Add(await SaveOneAsync(directory, name, output => nested.Message.WriteToAsync(output, ct), ct).ConfigureAwait(false));
                }
                else if (entity is MimePart part)
                {
                    string name = string.IsNullOrEmpty(part.FileName) ? "attachment.bin" : part.FileName;
                    saved.Add(await SaveOneAsync(directory, name, output => part.Content.DecodeToAsync(output, ct), ct).ConfigureAwait(false));
                }
                else
                {
                    string name = entity.ContentDisposition?.FileName ?? entity.ContentType.Name ?? "attachment.mime";
                    saved.Add(await SaveOneAsync(directory, name, output => entity.WriteToAsync(output, ct), ct).ConfigureAwait(false));
                }
            }
            return saved;
        }

        private static async Task<string> SaveOneAsync(string directory, string name, Func<Stream, Task> write, CancellationToken ct)
        {
            name = ValidateName(name);
            string root = Path.GetFullPath(directory);
            Directory.CreateDirectory(root);
            string rootPrefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
            string stem = Path.GetFileNameWithoutExtension(name);
            string extension = Path.GetExtension(name);
            for (int i = 0; i < 10000; i++)
            {
                ct.ThrowIfCancellationRequested();
                string candidate = i == 0 ? name : $"{stem} ({i}){extension}";
                string target = Path.GetFullPath(Path.Combine(root, candidate));
                if (!target.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Вложение выходит за границы выбранной папки.");
                FileStream output;
                try { output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true); }
                catch (IOException) when (File.Exists(target) || Directory.Exists(target)) { continue; }
                try
                {
                    await using (output) { await write(output).ConfigureAwait(false); }
                    return candidate;
                }
                catch
                {
                    try { File.Delete(target); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    throw;
                }
            }
            throw new IOException("Не удалось подобрать свободное имя вложения.");
        }
    }
}
