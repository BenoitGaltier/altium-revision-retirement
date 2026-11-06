using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

static class RuntimeBundleProbe
{
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            string payload=Path.GetFullPath(args[0]).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            foreach(string name in new[]{"Altium.SDK.Interfaces.dll","Altium.SDK.dll","AltiumRevisionRetirement.dll"})
            {
                var assembly=Assembly.LoadFrom(Path.Combine(payload,name));
                assembly.GetTypes();
                if(!assembly.Location.StartsWith(payload,StringComparison.OrdinalIgnoreCase)) throw new Exception("Assembly loaded outside the release: "+name);
                Console.WriteLine("Loaded from release payload: "+name);
            }
            Application.EnableVisualStyles();
            var extension=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="AltiumRevisionRetirement");
            using(var form=(Form)Activator.CreateInstance(extension.GetType("AltiumRevisionRetirement.RetirementForm",true)))
                if(form.Text!="Fluidra | Altium Revision Retirement") throw new Exception("Extension UI did not initialize.");
            Console.WriteLine("PASS isolated extension and UI load using only bundled Altium DLLs; no native workspace calls.");
            return 0;
        }
        catch(Exception error) {Console.Error.WriteLine(error);return 1;}
    }
}
