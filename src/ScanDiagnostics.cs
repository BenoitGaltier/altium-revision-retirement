using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AltiumRevisionRetirement
{
    internal sealed class ScanDiagnostics
    {
        readonly DateTime started=DateTime.UtcNow;
        DateTime written=DateTime.MinValue;
        string lastProgress="Scan started";
        string workspace="";
        readonly string path=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AltiumRevisionRetirement","Diagnostics","last-scan.txt");
        internal ScanDiagnostics() { Write("running","Scan started",null); }
        internal void Workspace(Workspace value) { workspace=value.Name;Write("running","Connected to "+workspace,null); }
        internal void Progress(string message) { lastProgress=message;if((DateTime.UtcNow-written).TotalSeconds>=1) Write("running",message,null); }
        internal void Complete(IList<Candidate> result)
        { Write("succeeded",result.Count+" old non-Obsolete revisions; "+result.Count(c=>c.Selectable)+" eligible for selection. "+string.Join("; ",result.GroupBy(c=>c.Kind+" / "+c.BeforeName).Select(g=>g.Key+": "+g.Count())),null); }
        internal void Failed(Exception error) { Write(error is OperationCanceledException ? "cancelled" : "failed",error.Message,error); }
        void Write(string state,string message,Exception error)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                var text="status="+state+Environment.NewLine+"started_utc="+started.ToString("o")+Environment.NewLine+
                    "updated_utc="+DateTime.UtcNow.ToString("o")+Environment.NewLine+
                    "elapsed_seconds="+(DateTime.UtcNow-started).TotalSeconds.ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+Environment.NewLine+
                    "workspace="+workspace+Environment.NewLine+"message="+message+Environment.NewLine+"last_progress="+lastProgress+Environment.NewLine+(error==null ? "" : "error="+error+Environment.NewLine);
                string temporary=path+".tmp";
                File.WriteAllText(temporary,text,new UTF8Encoding(false));
                if(File.Exists(path)) File.Replace(temporary,path,null); else File.Move(temporary,path);
                written=DateTime.UtcNow;
            }
            catch(IOException) { }
            catch(UnauthorizedAccessException) { }
        }
    }
}
