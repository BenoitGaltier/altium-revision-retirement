using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DXP;

namespace CSharpPlugin
{
    public interface IPluginFactory { object InvokePluginFactory(IClient client); }
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class PluginFactory : IPluginFactory
    {
        public object InvokePluginFactory(IClient client) { return new RevisionRetirementServer(client,"AltiumRevisionRetirement"); }
    }
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public sealed class RevisionRetirementServer : ServerModule
    {
        public RevisionRetirementServer(IClient client,string name):base(client,name) { }
        protected override void InitializeCommands()
        { ((CommandLauncher)CommandLauncher).RegisterCommand("Manage",Manage); }
        static void Manage(IServerDocumentView view,ref string parameters)
        {
            try { AltiumRevisionRetirement.RetirementWindow.Show(); }
            catch(Exception ex) { MessageBox.Show(ex.Message,"Altium Revision Retirement",MessageBoxButtons.OK,MessageBoxIcon.Error); }
        }
        protected override IServerDocument NewDocumentInstance(string kind,string fileName) { return null; }
    }
}
