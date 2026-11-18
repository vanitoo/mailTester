using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using mailTester.Mail;

namespace mailTester.Monitoring;

public sealed class MonitorRunner
{
    private readonly MonitorStore store;
    public MonitorRunner(MonitorStore store) { this.store = store; }
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(20) };

    public static MonitorStatus StatusOf(IEnumerable<CheckFinding> input)
    {
        var items = input.ToArray();
        if (items.Any(x => x.Level == CheckLevel.Error)) return MonitorStatus.Error;
        if (items.Any(x => x.Level == CheckLevel.Warning)) return MonitorStatus.Warning;
        if (items.Any(x => x.Level == CheckLevel.Unknown)) return MonitorStatus.Unknown;
        if (items.Any(x => x.Level == CheckLevel.Ok)) return MonitorStatus.Ok;
        return MonitorStatus.Unknown;
    }

    public async Task<MonitorRun> RunClaimAsync(MonitorClaim claim, IProgress<string> progress, CancellationToken ct)
    {
        var job = claim.Job;
        var started = DateTimeOffset.UtcNow;
        var watch = Stopwatch.StartNew();
        MonitorStatus status = MonitorStatus.Unknown;
        string summary = "", detail = "";
        double? delivery = null;
        // Job budget is shorter than the 10-minute lease.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(4));
        var token = timeout.Token;
        try
        {
            job.Validate();
            progress?.Report("Monitor: " + job.Name);
            if (job.Kind == MonitorKind.RoundTrip)
            {
                var trip = await MailEngine.RoundTripAsync(job.Sender(), job.Receiver(), job.ReceiveProtocol, job.From, job.To,
                    job.WaitSeconds, job.PollSeconds, progress, token).ConfigureAwait(false);
                delivery = trip.DeliverySeconds;
                status = trip.State == TripState.Delivered ? MonitorStatus.Ok :
                    trip.State == TripState.SendUnknown || trip.State == TripState.ReceiveFailed ? MonitorStatus.Unknown : MonitorStatus.Error;
                summary = trip.State + ": " + trip.Summary;
                detail = $"ID: {trip.CorrelationId}\nОбщее время: {trip.ElapsedSeconds:F2} с\nВремя до обнаружения: {trip.DeliverySeconds?.ToString("F2") ?? "—"} с";
            }
            else
            {
                var findings = await CheckAsync(job, progress, token).ConfigureAwait(false);
                status = StatusOf(findings);
                if(job.Kind==MonitorKind.Dnsbl && findings.All(x=>x.Level==CheckLevel.Skipped || x.Summary.StartsWith("UNKNOWN",StringComparison.OrdinalIgnoreCase)))
                    status=MonitorStatus.Unknown;
                summary = string.Join(" | ", findings.Where(x => x.Level is CheckLevel.Error or CheckLevel.Warning or CheckLevel.Unknown).Take(3).Select(x => x.Summary));
                if (string.IsNullOrWhiteSpace(summary)) summary = findings.FirstOrDefault(x => x.Level == CheckLevel.Ok)?.Summary ?? "Нет подтверждённого результата";
                detail = string.Join("\n\n", findings.Select(f => $"[{f.Level}] {f.Section} {f.Name}\n{f.Summary}\n{f.Detail}"));
            }
        }
        catch (OperationCanceledException)
        {
            status = MonitorStatus.Unknown; summary = ct.IsCancellationRequested ? "Отменено" : "Таймаут проверки";
            detail = job.Kind == MonitorKind.RoundTrip ? "Письмо могло быть принято. Повторной отправки этой попытки нет." : "";
        }
        catch (Exception ex)
        {
            status = MonitorStatus.Error;
            summary = "Ошибка проверки: " + ex.GetType().Name;
        }
        // Secrets / raw exceptions never enter history. Cap exported diagnostics.
        var run = new MonitorRun(job.Id,job.Name,started,status,watch.Elapsed.TotalSeconds,delivery,summary,
            detail.Length > 200000 ? detail[..200000] + "\n[truncated]" : detail);
        store.Complete(claim,run);
        return run;
    }

    public async Task<IReadOnlyList<MonitorRun>> RunDueAsync(int maximum, IProgress<string> progress, CancellationToken ct)
    {
        var result = new List<MonitorRun>();
        for (int i=0;i<Math.Clamp(maximum,1,100);i++)
        {
            if (ct.IsCancellationRequested) break;
            var claim = store.Claim();
            if (claim == null) break;
            result.Add(await RunClaimAsync(claim,progress,ct).ConfigureAwait(false));
        }
        return result;
    }

    private static async Task<IReadOnlyList<CheckFinding>> CheckAsync(MonitorJob job, IProgress<string> progress, CancellationToken ct)
    {
        switch (job.Kind)
        {
            case MonitorKind.Http:
                using (var request = new HttpRequestMessage(HttpMethod.Get, job.Target))
                using (var response = await Http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false))
                {
                    int code = (int)response.StatusCode;
                    return new[] { new CheckFinding("HTTP",job.Target, code >= 200 && code < 300 ? CheckLevel.Ok : code >= 300 && code < 400 ? CheckLevel.Warning : CheckLevel.Error,
                        $"HTTP {code}", "GET, только заголовки ответа; редиректы не выполняются.") };
                }
            case MonitorKind.Tcp:
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                using (var tcp = new TcpClient())
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(15));
                    await tcp.ConnectAsync(job.Target,job.Port,timeout.Token).ConfigureAwait(false);
                    return new[] { new CheckFinding("TCP",$"{job.Target}:{job.Port}",CheckLevel.Ok,"TCP подключение установлено","Протокол и авторизация не проверялись.") };
                }
            case MonitorKind.Mail:
                await MailEngine.ProbeServiceAsync(job.Sender(),job.MailProtocol.ToString(),ct).ConfigureAwait(false);
                return new[] { new CheckFinding("MAIL",$"{job.Target}:{job.Port}",CheckLevel.Ok,
                    job.MailProtocol + (string.IsNullOrEmpty(job.Login) ? ": TLS и протокол" : ": TLS, протокол и AUTH"),
                    "Письма не отправлялись; доставка проверяется отдельным Round Trip.") };
            case MonitorKind.Tls:
                return await TlsDiagnostics.InspectAsync(job.Target,job.Port,job.TlsMode,ct).ConfigureAwait(false);
            case MonitorKind.Registration:
                return await RegistrationDiagnostics.InspectAsync(job.Target,progress,ct).ConfigureAwait(false);
            default:
                IPAddress resolver = string.IsNullOrWhiteSpace(job.Resolver) ? null : IPAddress.Parse(job.Resolver);
                var engine = new DnsDiagnosticEngine(resolver);
                if (job.Kind == MonitorKind.Dnsbl) return (await engine.CheckIpAsync(IPAddress.Parse(job.Target),true,progress,ct).ConfigureAwait(false)).Where(f=>f.Section=="DNSBL").ToArray();
                if (job.Kind == MonitorKind.Dns) return await engine.CheckMailDnsAsync(job.Target,ct).ConfigureAwait(false);
                var report = await engine.InspectAsync(job.Target,DnsPolicyChecks.ParseSelectors(job.Selectors),progress,ct).ConfigureAwait(false);
                return report.Findings.Where(x => x.Section is "SPF" or "DKIM" or "DMARC").Select(x=>x.Level==CheckLevel.Skipped?x with { Level=CheckLevel.Warning }:x).ToArray();
        }
    }
}
