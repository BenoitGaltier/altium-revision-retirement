using System;
using System.IO;
using System.Text;

namespace AltiumRevisionRetirement
{
    public sealed class AuditJournal : IAudit
    {
        readonly FileStream file;
        readonly StreamWriter writer;
        public string Path { get; private set; }
        public AuditJournal():this(null) { }
        internal AuditJournal(string dataDirectory)
        {
            var directory=System.IO.Path.Combine(dataDirectory??System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AltiumRevisionRetirement"),"Audit");
            Directory.CreateDirectory(directory);
            Path=System.IO.Path.Combine(directory,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+".csv");
            file=new FileStream(Path,FileMode.CreateNew,FileAccess.Write,FileShare.Read,4096,FileOptions.WriteThrough);
            writer=new StreamWriter(file,new UTF8Encoding(true));
            writer.WriteLine("utc,event,workspace_guid,item_guid,revision_guid,revision,from_state_guid,to_state_guid,transition_guid,detail");
            Flush();
        }
        static string Q(string text) { return "\""+(text??"").Replace("\"","\"\"")+"\""; }
        public void Write(string eventName, Candidate c, string detail)
        {
            writer.WriteLine(string.Join(",",new[]{Q(DateTime.UtcNow.ToString("o")),Q(eventName),Q(c.Workspace.Guid),Q(c.ItemGuid),Q(c.RevisionGuid),Q(c.RevisionHrid),Q(c.BeforeGuid),Q(c.TargetGuid),Q(c.TransitionGuid),Q(detail)}));
            Flush();
        }
        void Flush() { writer.Flush(); file.Flush(true); }
        public void Dispose() { writer.Dispose(); }
    }
}
