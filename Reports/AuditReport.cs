using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using mailTester.Mail;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace mailTester.Reports
{
    public sealed record AuditReport(string Title, string Target, string Resolver, DateTimeOffset Timestamp,
        string AdviceDomain, IReadOnlyList<CheckFinding> Findings)
    {
        public int Count(CheckLevel level) => Findings.Count(f=>f.Level==level);
        public string Counts => $"Всего {Findings.Count} | OK {Count(CheckLevel.Ok)} | Внимание {Count(CheckLevel.Warning)} | Проблемы {Count(CheckLevel.Error)} | Не определено {Count(CheckLevel.Unknown)} | Инфо {Count(CheckLevel.Info)} | Пропущено {Count(CheckLevel.Skipped)}";
        public string Advice(CheckFinding f) => f.Section=="EML" ? "" : DiagnosticAdvice.For(f,AdviceDomain);
        public string AsText()
        {
            var sb=new StringBuilder();
            sb.AppendLine($"MailTester | {Title} | {Timestamp:O}");
            sb.AppendLine($"Цель: {Target}\nDNS: {Resolver}\n{Counts}");
            sb.AppendLine("Результаты относятся к наблюдаемым данным; не гарантия доставки.\n");
            foreach(var finding in Findings)
            {
                sb.AppendLine($"[{finding.Level}] {finding.Section} | {finding.Name}\n{finding.Summary}\n{finding.Detail}");
                string advice=Advice(finding);
                if(advice.Length>0)sb.AppendLine("Рекомендации: "+advice);
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }

    public static class AuditReportStyle
    {
        public static string Label(CheckLevel level)=>level switch{
            CheckLevel.Ok=>"OK",CheckLevel.Warning=>"ВНИМАНИЕ",CheckLevel.Error=>"ПРОБЛЕМА",
            CheckLevel.Unknown=>"НЕ ОПРЕДЕЛЕНО",CheckLevel.Skipped=>"ПРОПУЩЕНО",_=>"ИНФО"
        };
        public static Color ColorFor(CheckLevel level)=>level switch{
            CheckLevel.Ok=>Color.FromArgb(21,128,61),
            CheckLevel.Warning=>Color.FromArgb(161,98,7),
            CheckLevel.Error=>Color.FromArgb(185,28,28),
            CheckLevel.Unknown=>Color.FromArgb(109,40,217),
            CheckLevel.Skipped=>Color.FromArgb(100,116,139),
            _=>Color.FromArgb(29,78,216)
        };
        public static bool Matches(CheckFinding f,int filter)=>filter switch{
            1=>f.Level is CheckLevel.Error or CheckLevel.Warning or CheckLevel.Unknown,
            2=>f.Level==CheckLevel.Ok,3=>f.Level==CheckLevel.Info,
            4=>f.Level==CheckLevel.Skipped,_=>true
        };
        private static readonly Color Body=Color.FromArgb(15,23,42);
        private static readonly Color Secondary=Color.FromArgb(71,85,105);

        public static void Show(RichTextBox box,AuditReport report,int filter=0)
        {
            box.SuspendLayout();
            try
            {
                box.Clear();
                box.WordWrap=true;
                box.DetectUrls=false;
                box.ScrollBars=RichTextBoxScrollBars.Vertical;
                if(report==null)return;
                using var heading=new Font(box.Font.FontFamily,12,FontStyle.Bold);
                using var bold=new Font(box.Font,FontStyle.Bold);
                void Write(string s,Color color,Font font)
                {
                    box.SelectionStart=box.TextLength;box.SelectionLength=0;
                    box.SelectionColor=color;box.SelectionFont=font;box.AppendText(s);
                }
                Write(report.Title+"\n",Body,heading);
                Write($"Цель: {report.Target} | {report.Timestamp:dd.MM.yyyy HH:mm}\nDNS: {report.Resolver}\n",Secondary,box.Font);
                foreach(var level in new[]{CheckLevel.Ok,CheckLevel.Warning,CheckLevel.Error,CheckLevel.Unknown,CheckLevel.Info,CheckLevel.Skipped})
                    Write($"{Label(level)}: {report.Count(level)}   ",ColorFor(level),bold);
                Write("\nРезультаты относятся к моменту проверки; не гарантия доставки.\n\n",Secondary,box.Font);
                var filtered=report.Findings.Where(f=>Matches(f,filter)).ToArray();
                if(filtered.Length==0)Write("Нет результатов для данного фильтра.\n",Secondary,box.Font);
                foreach(var finding in filtered)
                {
                    Write($"[{Label(finding.Level)}]  {finding.Section} | {finding.Name}\n",ColorFor(finding.Level),bold);
                    Write((finding.Summary??"")+"\n",ColorFor(finding.Level),bold);
                    if(!string.IsNullOrWhiteSpace(finding.Detail))Write(finding.Detail+"\n",Body,box.Font);
                    string advice=report.Advice(finding);
                    if(advice.Length>0) {Write("Рекомендации: ",Secondary,bold);Write(advice+"\n",Secondary,box.Font);}
                    Write("\n",Body,box.Font);
                }
                box.SelectionStart=0;box.SelectionLength=0;box.ScrollToCaret();
            }
            finally{box.ResumeLayout();}
        }

        public static void SavePdfWithDialog(IWin32Window owner,AuditReport report)
        {
            if(report==null)return;
            using var dialog=new SaveFileDialog{Filter="PDF (*.pdf)|*.pdf",DefaultExt="pdf",AddExtension=true,OverwritePrompt=true,
                FileName="mailtester-audit-"+report.Timestamp.ToString("yyyyMMdd-HHmmss")+".pdf"};
            if(dialog.ShowDialog(owner)!=DialogResult.OK)return;
            try{AuditPdf.Save(report,dialog.FileName);}
            catch(Exception ex){MessageBox.Show(owner,"Ошибка PDF: "+ex.GetType().Name,"Экспорт",MessageBoxButtons.OK,MessageBoxIcon.Error);}
        }
    }

    public static class AuditPdf
    {
        private static XColor PdfColor(Color color)=>XColor.FromArgb(color.R,color.G,color.B);
        private static XSolidBrush Brush(Color color)=>new XSolidBrush(PdfColor(color));
        private static readonly Color Ink=Color.FromArgb(15,23,42);
        private static readonly Color Muted=Color.FromArgb(71,85,105);
        private static readonly Color Rule=Color.FromArgb(226,232,240);

        public static void Save(AuditReport report,string path)
        {
            if(report==null)throw new ArgumentNullException(nameof(report));
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                using var doc=new PdfDocument();
                doc.Info.Title="MailTester | "+report.Title;
                doc.Info.Author="MailTester";
                using(var layout=new Layout(doc,report))layout.Render();
                doc.Save(temp);
                File.Move(temp,path,true);
            }
            finally{if(File.Exists(temp))File.Delete(temp);}
        }

        private sealed class Layout:IDisposable
        {
            private readonly PdfDocument doc;
            private readonly AuditReport report;
            private readonly XFont title=new XFont("Arial",19,XFontStyleEx.Bold);
            private readonly XFont heading=new XFont("Arial",12,XFontStyleEx.Bold);
            private readonly XFont bold=new XFont("Arial",10,XFontStyleEx.Bold);
            private readonly XFont regular=new XFont("Arial",9.5,XFontStyleEx.Regular);
            private readonly XFont small=new XFont("Arial",8,XFontStyleEx.Regular);
            private XGraphics gfx;
            private double width,height,y;
            private int pages;
            private const double M=40;
            private double Available=>width-2*M;
            public Layout(PdfDocument doc,AuditReport report){this.doc=doc;this.report=report;}
            public void Dispose(){gfx?.Dispose();gfx=null;}
            private void NewPage()
            {
                gfx?.Dispose();
                var page=doc.AddPage();page.Size=PageSize.A4;
                width=page.Width.Point;height=page.Height.Point;
                gfx=XGraphics.FromPdfPage(page);pages++;
                gfx.DrawRectangle(Brush(Ink),0,0,width,7);
                gfx.DrawString("MAILTESTER  /  DIAGNOSTICS",small,Brush(Muted),M,28);
                gfx.DrawLine(new XPen(PdfColor(Rule)),M,height-32,width-M,height-32);
                gfx.DrawString("MailTester  •  "+report.Timestamp.ToString("dd.MM.yyyy HH:mm")+"   |   "+pages,small,Brush(Muted),M,height-18);
                y=48;
            }
            private void Ensure(double amount){if(y+amount>height-44)NewPage();}
            // Split even uninterrupted DNS keys/hostnames, without truncating content.
            private IEnumerable<string> Wrap(string text,XFont font,double available)
            {
                foreach(string raw in (text??"").Replace("\r\n","\n").Replace("\r","\n").Replace("\t","    ").Split('\n'))
                {
                    // PDF text rendering should not include invisible control characters.
                    string line=new string(raw.Where(c=>!char.IsControl(c)).ToArray());
                    if(line.Length==0){yield return "";continue;}
                    while(line.Length>0)
                    {
                        if(gfx.MeasureString(line,font).Width<=available){yield return line;break;}
                        int lo=1,hi=line.Length;
                        while(lo<hi)
                        {
                            int mid=(lo+hi+1)/2;
                            if(gfx.MeasureString(line[..mid],font).Width<=available)lo=mid;else hi=mid-1;
                        }
                        int split=lo;
                        int space=line.LastIndexOf(' ',Math.Max(0,lo-1),lo);
                        if(space>0&&space>=lo/2)split=space;
                        if(split<line.Length&&split>0&&char.IsHighSurrogate(line[split-1])&&char.IsLowSurrogate(line[split]))
                            split=split==1?2:split-1;
                        yield return line[..split].TrimEnd();
                        line=line[split..].TrimStart();
                    }
                }
            }
            private void Text(string text,XFont font,Color color,double lineHeight)
            {
                // Materialize before a possible page break: measuring uses the current graphics.
                foreach(string line in Wrap(text,font,Available).ToArray())
                {
                    Ensure(lineHeight);
                    if(line.Length>0)gfx.DrawString(line,font,Brush(color),M,y+lineHeight-3);
                    y+=lineHeight;
                }
            }
            private void Gap(double size){y+=size;}
            public void Render()
            {
                NewPage();
                Text(report.Title,title,Ink,25);
                Text("Цель: "+report.Target,heading,Ink,18);
                Text("Проверка: "+report.Timestamp.ToString("dd.MM.yyyy HH:mm:ss zzz")+" | DNS: "+report.Resolver,regular,Muted,14);
                Text("Результаты относятся к наблюдаемым данным, не подтверждают доставку.",small,Muted,12);
                Gap(10);Ensure(86);
                var levels=new[]{CheckLevel.Ok,CheckLevel.Warning,CheckLevel.Error,CheckLevel.Unknown,CheckLevel.Info,CheckLevel.Skipped};
                double cell=(Available-12)/3;
                for(int i=0;i<levels.Length;i++)
                {
                    var level=levels[i];double x=M+(i%3)*(cell+6), top=y+(i/3)*40;
                    var color=AuditReportStyle.ColorFor(level);
                    gfx.DrawRectangle(Brush(Color.FromArgb(248,250,252)),x,top,cell,35);
                    gfx.DrawRectangle(Brush(color),x,top,3,35);
                    gfx.DrawString(AuditReportStyle.Label(level),small,Brush(color),x+9,top+12);
                    gfx.DrawString(report.Count(level).ToString(),heading,Brush(Ink),x+9,top+28);
                }
                Gap(91);
                Text("Приоритетные замечания",heading,Ink,19);
                var problems=report.Findings.Where(f=>AuditReportStyle.Matches(f,1)).ToArray();
                if(problems.Length==0)Text("Проблемы и неопределённые результаты не выявлены в выполненных проверках.",regular,Muted,14);
                else foreach(var f in problems)Text($"{AuditReportStyle.Label(f.Level)} | {f.Section} | {f.Name}: {f.Summary}",bold,AuditReportStyle.ColorFor(f.Level),15);
                Gap(10);
                Text("Подробные результаты",heading,Ink,19);
                foreach(var group in report.Findings.GroupBy(f=>f.Section))
                {
                    Ensure(60);Gap(7);Text(group.Key,heading,Ink,18);
                    gfx.DrawLine(new XPen(PdfColor(Rule)),M,y,width-M,y);Gap(5);
                    foreach(var f in group)
                    {
                        Ensure(55);
                        var color=AuditReportStyle.ColorFor(f.Level);
                        Text($"[{AuditReportStyle.Label(f.Level)}] {f.Name}",bold,color,15);
                        Text(f.Summary,bold,color,15);
                        if(!string.IsNullOrWhiteSpace(f.Detail))Text(f.Detail,regular,Ink,13);
                        string advice=report.Advice(f);
                        if(advice.Length>0)Text("Рекомендации: "+advice,regular,Muted,13);
                        Gap(7);
                    }
                }
            }
        }
    }
}
