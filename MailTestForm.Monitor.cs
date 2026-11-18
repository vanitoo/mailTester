using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using mailTester.Monitoring;

namespace mailTester;

public partial class MailTestForm
{
    private MonitorStore monitorStore;
    private System.Windows.Forms.Timer monitorTimer;
    private Button bMonitorRun, bMonitorSchedule, bMonitorAdd, bMonitorEdit, bMonitorDelete, bMonitorRefresh, bMonitorExport;
    private Label monitorState, monitorSummary, historySummary;
    private RichTextBox monitorLog, monitorDetail;
    private DataGridView monitorGrid, historyGrid;
    private Panel monitorChart;
    private IReadOnlyList<MonitorRun> chartRuns = Array.Empty<MonitorRun>();
    private List<MonitorEntry> monitorEntries = new();
    private bool monitorRunning, monitorSchedule, refreshingMonitor, monitorEditing;
    private sealed record MonitorRow(string Id,string Name,string Kind,string Target,string Status,string Next,string Last);

    private static DataGridView MonitorGrid()
    {
        var grid = new DataGridView {
            Dock=DockStyle.Fill, BackgroundColor=Surface, BorderStyle=BorderStyle.None, GridColor=BorderColor, ReadOnly=true,
            AllowUserToAddRows=false, AllowUserToDeleteRows=false, AllowUserToResizeRows=false, AutoGenerateColumns=false,
            MultiSelect=false, RowHeadersVisible=false, SelectionMode=DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill, EnableHeadersVisualStyles=false, ColumnHeadersHeight=30,
            RowTemplate={Height=28}
        };
        grid.ColumnHeadersDefaultCellStyle.BackColor=Canvas; grid.ColumnHeadersDefaultCellStyle.ForeColor=Muted;
        grid.DefaultCellStyle.SelectionBackColor=Color.FromArgb(219,234,254); grid.DefaultCellStyle.SelectionForeColor=Ink;
        return grid;
    }
    private static void MonitorCol(DataGridView grid,string prop,string caption,int weight)
        => grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName=prop, HeaderText=caption, FillWeight=weight, SortMode=DataGridViewColumnSortMode.NotSortable });

    private void BuildMonitor()
    {
        var layout=Table(1,3,Canvas); Col(layout,100,true); Row(layout,260);FillRow(layout);Row(layout,102);
        AttachScrollablePage(pageMonitor,layout,640);
        var jobs=Card(1,3,new Padding(0,0,0,10));Col(jobs,100,true);Row(jobs,40);Row(jobs,27);FillRow(jobs);
        var bar=Table(6,1,Surface);Col(bar,100,true);Col(bar,93);Col(bar,82);Col(bar,98);Col(bar,98);Col(bar,139);FillRow(bar);
        bMonitorAdd=MakeButton("Добавить");bMonitorEdit=MakeButton("Правка",false);bMonitorDelete=MakeButton("Удалить",false);
        bMonitorRefresh=MakeButton("Обновить",false);bMonitorRun=MakeButton("Запустить");bMonitorSchedule=MakeButton("Вкл. расписание",false);
        Span(bar,bMonitorAdd,0,0);Span(bar,bMonitorEdit,1,0);Span(bar,bMonitorDelete,2,0);Span(bar,bMonitorRefresh,3,0);
        Span(bar,bMonitorRun,4,0);Span(bar,bMonitorSchedule,5,0);
        Span(jobs,bar,0,0);
        monitorSummary=TextLabel("Расписание выключено. Профили добавляются вручную.",9,Muted);
        Span(jobs,monitorSummary,0,1);
        monitorGrid=MonitorGrid();
        MonitorCol(monitorGrid,"Name","Профиль",23);MonitorCol(monitorGrid,"Kind","Тип",13);MonitorCol(monitorGrid,"Target","Цель",23);
        MonitorCol(monitorGrid,"Status","Статус",15);MonitorCol(monitorGrid,"Last","Последняя",13);MonitorCol(monitorGrid,"Next","Следующая",13);
        monitorGrid.SelectionChanged+=(_,_)=>LoadMonitorHistory();
        monitorGrid.CellFormatting+=(_,args)=>{
            if(args.RowIndex<0 || args.ColumnIndex!=3)return;
            args.CellStyle.ForeColor=args.Value?.ToString() switch {
                "Ok"=>Green,"Error"=>Red,"Warning"=>Color.FromArgb(161,98,7),_=>Muted
            };
        };
        Span(jobs,monitorGrid,0,2);Span(layout,jobs,0,0);

        var lower=Card(1,3,new Padding(0,0,0,10));Col(lower,100,true);Row(lower,36);Row(lower,95);FillRow(lower);
        var historyBar=Table(2,1,Surface);Col(historyBar,100,true);Col(historyBar,125);FillRow(historyBar);
        historySummary=TextLabel("История и график",10,Muted,true);
        bMonitorExport=MakeButton("Экспорт CSV",false);
        Span(historyBar,historySummary,0,0);Span(historyBar,bMonitorExport,1,0);Span(lower,historyBar,0,0);
        monitorChart=new Panel {Dock=DockStyle.Fill,BackColor=Canvas,Margin=new Padding(0,0,0,5)};
        monitorChart.Paint+=PaintMonitorChart;monitorChart.Resize+=(_,_)=>monitorChart.Invalidate();
        Span(lower,monitorChart,0,1);
        var panes=Table(2,1,Surface);Col(panes,54,true);Col(panes,46,true);FillRow(panes);
        historyGrid=MonitorGrid();MonitorCol(historyGrid,"Started","Дата/время UTC",35);MonitorCol(historyGrid,"Status","Статус",20);
        MonitorCol(historyGrid,"Seconds","Время (с)",20);MonitorCol(historyGrid,"DeliverySeconds","Доставка (с)",25);
        historyGrid.Columns[0].DefaultCellStyle.Format="dd.MM HH:mm:ss"; historyGrid.Columns[2].DefaultCellStyle.Format="F2";historyGrid.Columns[3].DefaultCellStyle.Format="F2";
        monitorDetail=TextArea(true);
        historyGrid.SelectionChanged+=(_,_)=>{
            if(monitorDetail!=null) monitorDetail.Text=historyGrid.SelectedRows.Count>0 && historyGrid.SelectedRows[0].DataBoundItem is MonitorRun r? r.Summary+"\n\n"+r.Detail:"";
        };
        Span(panes,historyGrid,0,0);Span(panes,monitorDetail,1,0);Span(lower,panes,0,2);Span(layout,lower,0,1);
        var log=Card(1,2,Padding.Empty);Col(log,100,true);Row(log,28);FillRow(log);
        monitorState=TextLabel("Журнал мониторинга",13,Ink,true);monitorLog=TextArea(true,true);Span(log,monitorState,0,0);Span(log,monitorLog,0,1);
        Span(layout,log,0,2);
        bMonitorAdd.Click+=(_,_)=>EditMonitor(null);
        bMonitorEdit.Click+=(_,_)=>{if(SelectedMonitor()!=null)EditMonitor(SelectedMonitor().Job);};
        bMonitorDelete.Click+=(_,_)=>DeleteMonitor();
        bMonitorRefresh.Click+=(_,_)=>RefreshMonitor();
        bMonitorRun.Click+=async (_,_)=>{
            if(operation!=null){if(activeButton==bMonitorRun)operation.Cancel();return;}
            if(SelectedMonitor() is MonitorEntry entry) await RunMonitor(entry.Job.Id);
        };
        bMonitorSchedule.Click+=(_,_)=>{
            if(monitorStore==null)return;
            monitorSchedule=!monitorSchedule;
            bMonitorSchedule.Text=monitorSchedule?"Стоп расписания":"Вкл. расписание";
            monitorTimer.Enabled=monitorSchedule;
            RefreshMonitor();
        };
        bMonitorExport.Click+=(_,_)=>ExportMonitor();
        monitorTimer=new System.Windows.Forms.Timer {Interval=15000};
        monitorTimer.Tick+=async (_,_)=>{
            if(!monitorSchedule || monitorRunning || monitorEditing || operation!=null || monitorStore==null)return;
            try {
                RefreshMonitor();
                if(monitorEntries.Any(x=>x.Job.Enabled && !x.Busy && x.NextDue<=DateTimeOffset.UtcNow))await RunMonitor(null);
            } catch(Exception ex){Log(monitorLog,"Расписание: "+ex.GetType().Name);}
        };
        Shown+=(_,_)=>{
            try {monitorStore=new MonitorStore();RefreshMonitor();}
            catch(Exception ex){Log(monitorLog,"SQLite: "+ex.GetType().Name);monitorSummary.Text="База мониторинга недоступна.";UpdateMonitorButtons();}
        };
        UpdateMonitorButtons();
    }

    private MonitorEntry SelectedMonitor()
    {
        if(monitorGrid?.SelectedRows.Count>0 && monitorGrid.SelectedRows[0].DataBoundItem is MonitorRow row)
            return monitorEntries.FirstOrDefault(x=>x.Job.Id==row.Id);
        return null;
    }
    private void UpdateMonitorButtons()
    {
        if(bMonitorRun==null)return;
        bool ready=monitorStore!=null;
        var item=SelectedMonitor();
        bMonitorAdd.Enabled=ready&&!monitorRunning;
        bMonitorEdit.Enabled=ready&&!monitorRunning&&item!=null&&!item.Busy;
        bMonitorDelete.Enabled=bMonitorEdit.Enabled;
        bMonitorRefresh.Enabled=ready&&!monitorRunning;
        bMonitorSchedule.Enabled=ready;
        bMonitorRun.Enabled=activeButton==bMonitorRun || (ready&&operation==null&&!monitorRunning&&item!=null&&!item.Busy);
        bMonitorExport.Enabled=ready&&chartRuns.Count>0;
    }

    private void RefreshMonitor()
    {
        if(monitorStore==null){UpdateMonitorButtons();return;}
        try {
            string selected=SelectedMonitor()?.Job.Id;
            refreshingMonitor=true;
            monitorEntries=monitorStore.Entries().ToList();
            monitorGrid.DataSource=monitorEntries.Select(x=>new MonitorRow(x.Job.Id,x.Job.Name,x.Job.Kind.ToString(),x.Job.Target,
                x.Busy?"Running":x.Job.Enabled && x.Last!=null && x.Last.Started.AddMinutes(2*x.Job.IntervalMinutes)<DateTimeOffset.UtcNow?"Stale":x.Last?.Status.ToString()??"Never",x.Job.Enabled?x.NextDue.ToLocalTime().ToString("dd.MM HH:mm"):"Откл.",x.Last?.Started.ToLocalTime().ToString("dd.MM HH:mm")??"—")).ToList();
            if(selected!=null)foreach(DataGridViewRow row in monitorGrid.Rows)
                if(row.DataBoundItem is MonitorRow item&&item.Id==selected){row.Selected=true;monitorGrid.CurrentCell=row.Cells[0];break;}
            int enabled=monitorEntries.Count(x=>x.Job.Enabled);
            int problems=monitorEntries.Count(x=>x.Last?.Status is MonitorStatus.Warning or MonitorStatus.Error or MonitorStatus.Unknown);
            monitorSummary.Text=$"Расписание: {(monitorSchedule?"ВКЛ":"ВЫКЛ")} | профилей: {monitorEntries.Count} | активны: {enabled} | требуют внимания: {problems}";
        } catch(Exception ex){Log(monitorLog,"SQLite: "+ex.GetType().Name);}
        finally{refreshingMonitor=false;}
        LoadMonitorHistory();
    }

    private void LoadMonitorHistory()
    {
        if(refreshingMonitor||historyGrid==null||monitorStore==null)return;
        try{
            var entry=SelectedMonitor();
            chartRuns=entry==null?Array.Empty<MonitorRun>():monitorStore.History(entry.Job.Id,100);
            historyGrid.DataSource=chartRuns.ToList();
            if(chartRuns.Count==0)monitorDetail.Clear();
            int good=chartRuns.Count(x=>x.Status==MonitorStatus.Ok), completed=chartRuns.Count(x=>x.Status!=MonitorStatus.Unknown);
            string share=completed==0?"—":(100.0*good/completed).ToString("F1")+"%";
            historySummary.Text=chartRuns.Count==0?"История: нет проверок":$"OK {good}/{completed} ({share}) · WARN {chartRuns.Count(x=>x.Status==MonitorStatus.Warning)} · ERR {chartRuns.Count(x=>x.Status==MonitorStatus.Error)} · UNKNOWN {chartRuns.Count-completed}";
            monitorChart.Invalidate();
        } catch(Exception ex){Log(monitorLog,"История: "+ex.GetType().Name);}
        UpdateMonitorButtons();
    }

    private void PaintMonitorChart(object sender,PaintEventArgs args)
    {
        var g=args.Graphics;g.Clear(Canvas);g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        bool roundTrip=SelectedMonitor()?.Job.Kind==MonitorKind.RoundTrip;
        var samples=chartRuns.Reverse().TakeLast(50).ToArray();
        double? Value(MonitorRun r)=>roundTrip?r.DeliverySeconds:r.Seconds;
        var usable=samples.Where(r=>Value(r).HasValue).ToArray();
        using var pen=new Pen(Accent,2);using var faint=new Pen(BorderColor,1);
        using var brush=new SolidBrush(Muted);using var red=new SolidBrush(Red);using var green=new SolidBrush(Green);
        using var warning=new SolidBrush(Color.FromArgb(217,119,6));using var unknown=new SolidBrush(Color.FromArgb(148,163,184));
        Brush StateBrush(MonitorStatus state)=>state switch{MonitorStatus.Ok=>green,MonitorStatus.Warning=>warning,MonitorStatus.Error=>red,_=>unknown};
        var rect=monitorChart.ClientRectangle;
        if(rect.Width<100||rect.Height<65)return;
        g.DrawString(roundTrip?"Время до обнаружения письма, с":"Длительность проверок, с",Font,brush,8,3);
        float left=42,right=rect.Width-10,top=28,bottom=rect.Height-22;
        if(samples.Length>0){
            float cell=(right-left)/samples.Length;
            for(int i=0;i<samples.Length;i++)
                g.FillRectangle(StateBrush(samples[i].Status),left+i*cell,rect.Height-14,Math.Max(1,cell-1),9);
        }
        if(usable.Length==0){g.DrawString("Нет данных о длительности",Font,brush,8,36);return;}
        double max=Math.Max(0.1,usable.Max(r=>Value(r).Value));
        g.DrawLine(faint,left,top,left,bottom);g.DrawLine(faint,left,bottom,right,bottom);
        g.DrawString(max.ToString("F1"),Font,brush,3,top);
        PointF? previous=null;
        for(int i=0;i<samples.Length;i++){
            float x=left+(right-left)*(samples.Length==1?0.5f:(float)i/(samples.Length-1));
            var val=Value(samples[i]);
            if(val==null){previous=null;g.FillEllipse(StateBrush(samples[i].Status),x-3,bottom-6,6,6);continue;}
            var point=new PointF(x,bottom-(bottom-top)*(float)(val.Value/max));
            if(previous.HasValue)g.DrawLine(pen,previous.Value,point);
            g.FillEllipse(StateBrush(samples[i].Status),point.X-3,point.Y-3,6,6);
            previous=point;
        }
    }

    private void EditMonitor(MonitorJob job)
    {
        if(monitorStore==null||monitorRunning)return;
        monitorEditing=true;
        try{
            using var dialog=new MonitorProfileForm(job);
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            monitorStore.Save(dialog.Result);RefreshMonitor();
        }catch(Exception ex){MessageBox.Show(this,"Не удалось сохранить профиль: "+ex.GetType().Name);}
        finally{monitorEditing=false;}
    }

    private void DeleteMonitor()
    {
        var entry=SelectedMonitor();if(entry==null||monitorStore==null||monitorRunning)return;
        monitorEditing=true;
        try{
            if(MessageBox.Show(this,"Удалить профиль и его историю?","Мониторинг",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            if(!monitorStore.Delete(entry.Job.Id))MessageBox.Show(this,"Профиль занят или удалён.");RefreshMonitor();
        }catch(Exception ex){Log(monitorLog,"Удаление: "+ex.GetType().Name);}
        finally{monitorEditing=false;}
    }

    private async Task RunMonitor(string id)
    {
        if(monitorStore==null||monitorRunning||operation!=null)return;
        MonitorClaim claim;
        try{claim=monitorStore.Claim(id);}catch(Exception ex){Log(monitorLog,"SQLite: "+ex.GetType().Name);return;}
        if(claim==null){RefreshMonitor();return;}
        monitorRunning=true;
        try {
            // Refresh before RunOperation: ensure the active button can subsequently cancel.
            RefreshMonitor();
            var runner=new MonitorRunner(monitorStore);
            bMonitorRun.Enabled=true;
            await RunOperation(bMonitorRun,monitorLog,async (ct,progress)=>{
                var result=await Task.Run(()=>runner.RunClaimAsync(claim,progress,ct));
                Log(monitorLog,$"{result.Name}: {result.Status} | {result.Summary}");
            });
        } finally {monitorRunning=false;if(!IsDisposed&&!Disposing)RefreshMonitor();}
    }

    private void ExportMonitor()
    {
        if(chartRuns.Count==0)return;
        using var dialog=new SaveFileDialog{Filter="CSV (*.csv)|*.csv",FileName="monitor-history.csv",AddExtension=true};
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        string Q(string s){
            s??="";
            // Formula injection protection for spreadsheet import.
            if(s.Length>0 && "=+-@\t\r\n".Contains(s[0]))s="'"+s;
            return "\"" + s.Replace("\"","\"\"") + "\"";
        }
        try{
            var sb=new StringBuilder("started_utc,status,duration_sec,delivery_sec,summary\r\n");
            foreach(var r in chartRuns)sb.AppendLine(string.Join(",",Q(r.Started.ToString("O")),Q(r.Status.ToString()),r.Seconds.ToString("F3",CultureInfo.InvariantCulture),r.DeliverySeconds?.ToString("F3",CultureInfo.InvariantCulture)??"",Q(r.Summary)));
            File.WriteAllText(dialog.FileName,sb.ToString(),new UTF8Encoding(true));
        }catch(Exception ex){MessageBox.Show(this,"Экспорт: "+ex.GetType().Name);}
    }
}
