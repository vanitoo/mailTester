using System.Text;
using MailKit.Security;
using MimeKit;
using mailTester.Mail;

static void Assert(bool ok, string description)
{
    if (!ok) throw new Exception("FAIL: " + description);
    Console.WriteLine("PASS: " + description);
}

string root = Path.Combine(Path.GetTempPath(), "mailtester-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    using var message = new MimeMessage();
    message.From.Add(MailboxAddress.Parse("sender@example.invalid"));
    message.To.Add(MailboxAddress.Parse("receiver@example.invalid"));
    message.Subject = "Тема";
    var builder = new BodyBuilder { TextBody = "Привет" };
    builder.Attachments.Add("test.txt", Encoding.UTF8.GetBytes("new content"));
    message.Body = builder.ToMessageBody();

    using var stream = new MemoryStream();
    message.WriteTo(stream);
    stream.Position = 0;
    using var parsed = new ReceivedMessage(MimeMessage.Load(stream));
    Assert(parsed.Subject == "Тема" && parsed.Body.Trim() == "Привет", "MIME UTF-8 roundtrip");
    Assert(parsed.HasAttachment, "MIME attachment recognized");
    File.WriteAllText(Path.Combine(root, "test.txt"), "original");
    var saved = await AttachmentStore.SaveAsync(parsed.Message, root, CancellationToken.None);
    Assert(saved.Count == 1 && File.ReadAllText(Path.Combine(root, "test.txt")) == "original", "existing file unchanged");
    Assert(File.ReadAllText(Path.Combine(root, "test (1).txt")) == "new content", "duplicate renamed and decoded");

    foreach (string name in new[] { "../escape.txt", @"..\escape.txt", "C:escape.txt", "CON.txt", "file.txt:stream" })
    {
        try { AttachmentStore.ValidateName(name); throw new Exception("FAIL: unsafe filename accepted: " + name); }
        catch (IOException) { Console.WriteLine("PASS: unsafe filename rejected"); }
    }

    var connection = new MailConnection("localhost", 110, SecureSocketOptions.None, "user\r\nQUIT", "test-only");
    try { connection.Validate(true); throw new Exception("FAIL: command injection accepted"); }
    catch (ArgumentException) { Console.WriteLine("PASS: credential line breaks rejected"); }
    Assert(!connection.ToString().Contains("test-only"), "connection diagnostics omit password");
    var error = new MailDiagnosticException("AUTH", new Exception("test-only"));
    Assert(!error.ToString().Contains("test-only"), "server error text omitted");

    var downgrade = new MailConnection("localhost", 110, SecureSocketOptions.StartTlsWhenAvailable, "user", "test-only");
    try { downgrade.Validate(true); throw new Exception("FAIL: opportunistic TLS accepted"); }
    catch (ArgumentException) { Console.WriteLine("PASS: opportunistic TLS rejected"); }
    Console.WriteLine("Security smoke tests completed.");
}
finally { Directory.Delete(root, true); }
