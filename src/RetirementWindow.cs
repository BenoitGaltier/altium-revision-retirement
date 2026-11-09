using System;
using System.Windows.Forms;

namespace AltiumRevisionRetirement
{
    internal static class RetirementWindow
    {
        static RetirementForm current;
        public static void Show(Func<RetirementForm> create=null)
        {
            if(current==null || current.IsDisposed)
            {
                var form=create==null ? new RetirementForm() : create();current=form;
                form.FormClosed+=(s,e)=>{if(object.ReferenceEquals(current,form)) current=null;};
                form.Show();
            }
            else
            {
                if(current.WindowState==FormWindowState.Minimized) current.WindowState=FormWindowState.Normal;
                current.Show();current.Activate();
            }
        }
    }
}
