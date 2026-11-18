using System.Text;
using MailKit.Security;
using MimeKit;
using mailTester.Mail;
using mailTester.Monitoring;

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
    Assert(DnsPolicyChecks.Spf("example.invalid", new[] { "v=spf1 ~all" }).Level == CheckLevel.Warning, "SPF softfail");
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
    Assert(DnsDiagnosticEngine.TreeWalkNames("a.b.c.d.e.f.g.h.i.example.com").Length == 8, "bounded DMARC DNS Tree Walk");
    Assert(DnsDiagnosticEngine.TreeWalkNames("mail.example.com").SequenceEqual(new[] { "mail.example.com", "example.com", "com" }), "DMARC Tree Walk parent order");
    Assert(DnsDiagnosticEngine.ReverseName(System.Net.IPAddress.Parse("192.0.2.10")) == "10.2.0.192.in-addr.arpa", "IPv4 PTR owner");
    Assert(DnsDiagnosticEngine.ReverseName(System.Net.IPAddress.Parse("::1")).StartsWith("1.0.0.0."), "IPv6 PTR owner");
    Assert(DnsDiagnosticEngine.ExpectedTestPolicy("reject") == "quarantine" && DnsDiagnosticEngine.ExpectedTestPolicy("quarantine") == "none", "DMARC testing policy downgrade");
    Assert(TlsDiagnostics.DefaultPort(TlsMode.Https) == 443 && TlsDiagnostics.DefaultPort(TlsMode.SmtpImplicit) == 465, "TLS port defaults");
    var route = MailHeaderDiagnostics.ParseRoute(new[] {
        "from mx2.example.invalid by mx3.example.invalid; Wed, 18 Nov 2026 13:00:05 +0000",
        "from mx1.example.invalid by mx2.example.invalid; Wed, 18 Nov 2026 13:00:00 +0000"
    });
    Assert(route.Count == 2 && route[0].From == "mx1.example.invalid" && route[1].DelaySeconds == 5, "Received hop order and delay");
    message.Headers.Add("X-MailTester-ID","abc123");
    Assert(MailEngine.IsTripHeader(message.Headers,"abc123") && !MailEngine.IsTripHeader(message.Headers,"abc124"),"roundtrip exact correlation");
    Assert(MonitorJob.DefaultInterval(MonitorKind.RoundTrip)==15 && MonitorJob.DefaultInterval(MonitorKind.Tls)==1440,"monitor default intervals");
    string cipher=SecretStore.Protect("temporary-test-only");
    Assert(!cipher.Contains("temporary-test-only") && SecretStore.Unprotect(cipher)=="temporary-test-only","DPAPI current-user roundtrip");
    var store=new MonitorStore(Path.Combine(root,"monitor.db"));
    var job=new MonitorJob{Name="local test",Kind=MonitorKind.Mail,Target="smtp.example.invalid",Port=587,Login="test",PasswordCipher=cipher,Enabled=true};
    store.Save(job);
    Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(store.PathName)).Contains("temporary-test-only",StringComparison.Ordinal),"no plain password in SQLite main file");
    var claim=store.Claim(); Assert(claim!=null&&claim.Job.Id==job.Id,"due profile claimed");
    var store2=new MonitorStore(store.PathName);
    Assert(store2.Claim()==null&&store2.Claim(job.Id)==null,"concurrent claim blocked");
    store.Complete(claim,new MonitorRun(job.Id,job.Name,DateTimeOffset.UtcNow,MonitorStatus.Ok,1.2,null,"test OK",""));
    Assert(store2.History(job.Id).Count==1&&store2.Claim()==null,"history persisted and rescheduled");
    job.Enabled=false; store.Save(job);
    Assert(store.Claim()==null,"disabled job not scheduled");
    Assert(store.Delete(job.Id)&&store.History(job.Id).Count==0,"profile and history cascade delete");
    Console.WriteLine("Security, DNS and monitor smoke tests completed.");
}
finally { Directory.Delete(root, true); }
