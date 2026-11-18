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
    Assert(DnsPolicyChecks.NormalizeDomain("ПРИМЕР.РФ.") == "xn--e1afmkfd.xn--p1ai", "IDN normalization");
    Assert(DnsPolicyChecks.ParseSelectors("selector1, selector2; selector1").Length == 2, "selector parsing");
    Assert(DnsPolicyChecks.Spf("example.invalid", new[] { "v=spf1 ip4:192.0.2.0/24 -all" }).Level == CheckLevel.Ok, "SPF basic");
    Assert(DnsPolicyChecks.Spf("example.invalid", new[] { "v=spf1 -all", "v=spf1 ~all" }).Level == CheckLevel.Error, "SPF duplicate");
    Assert(DnsPolicyChecks.Spf("example.invalid", new[] { "v=spf1 +all" }).Level == CheckLevel.Warning, "SPF permissive");
    Assert(DnsPolicyChecks.Spf("example.invalid", new[] { "v=spf1 ip4:192.0.2.1/99 -all" }).Level == CheckLevel.Error, "SPF invalid CIDR");
    Assert(DnsPolicyChecks.Dmarc("_dmarc.example.invalid", new[] { "v=DMARC1; p=reject; rua=mailto:reports@example.invalid" }).Level == CheckLevel.Ok, "DMARC reject");
    Assert(DnsPolicyChecks.Dmarc("_dmarc.example.invalid", new[] { "v=DMARC1; p=reject; t=y" }).Level == CheckLevel.Warning, "DMARC test mode");
    Assert(DnsPolicyChecks.Dmarc("_dmarc.example.invalid", new[] { "v=DMARC1; p=bad" }).Level == CheckLevel.Error, "DMARC invalid p");
    Assert(DnsPolicyChecks.Dkim("s._domainkey.example.invalid", new[] { "v=DKIM1; p=" }).Level == CheckLevel.Warning, "DKIM revoked");
    using (var rsa = System.Security.Cryptography.RSA.Create(2048))
    {
        string key = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
        Assert(DnsPolicyChecks.Dkim("s._domainkey.example.invalid", new[] { "v=DKIM1; k=rsa; p=" + key }).Level == CheckLevel.Ok, "DKIM RSA 2048");
    }
    Console.WriteLine("Security and DNS policy smoke tests completed.");
}
finally { Directory.Delete(root, true); }
