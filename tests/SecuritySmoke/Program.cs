using System.Net.Mail;
using System.Text;
using Tbc.Common.Mail.Pop3.Mime;

static void Assert(bool ok, string description)
{
    if (!ok) throw new Exception("FAIL: " + description);
    Console.WriteLine("PASS: " + description);
}

static RxMailMessage Message(string name, string body)
{
    var message = new RxMailMessage();
    message.Attachments.Add(new Attachment(new MemoryStream(Encoding.UTF8.GetBytes(body)), name));
    return message;
}

string root = Path.Combine(Path.GetTempPath(), "mailtester-test-" + Guid.NewGuid().ToString("N"));
string output = Path.Combine(root, "attachments");
Directory.CreateDirectory(output);
try
{
    File.WriteAllText(Path.Combine(output, "test.txt"), "original");
    using (var message = Message("test.txt", "new content"))
    {
        Assert(message.SaveAttachments(output, out _), "save duplicate attachment");
        Assert(File.ReadAllText(Path.Combine(output, "test.txt")) == "original", "existing file unchanged");
        Assert(File.ReadAllText(Path.Combine(output, "test (1).txt")) == "new content", "duplicate renamed");
    }

    foreach (string name in new[] { "../escape.txt", @"..\escape.txt", "C:escape.txt", "CON.txt", "file.txt:stream" })
    {
        using var message = Message(name, "unsafe");
        Assert(!message.SaveAttachments(output, out _), "reject unsafe filename: " + name);
    }
    Assert(!File.Exists(Path.Combine(root, "escape.txt")), "no file outside selected directory");

    try
    {
        using var client = new Pop3MailClient("localhost", 110, false, "user\r\nQUIT", "test-only");
        throw new Exception("FAIL: POP3 command injection not rejected");
    }
    catch (ArgumentException)
    {
        Console.WriteLine("PASS: POP3 credential line breaks rejected");
    }

    Console.WriteLine("Security smoke tests completed.");
}
finally
{
    Directory.Delete(root, true);
}
