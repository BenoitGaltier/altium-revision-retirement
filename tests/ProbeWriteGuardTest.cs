using System;
using AltiumRevisionRetirement;
static class ProbeWriteGuardTest
{
    static int Main()
    {
        try {new AltiumGateway().Transition(null,null);}
        catch(InvalidOperationException error)
        {
            if(error.Message=="Lifecycle transitions are disabled in this read-only diagnostic build.")
            {Console.WriteLine("PASS diagnostic writer throws before any native workspace access; native lifecycle-write reference excluded from DLL.");return 0;}
            Console.Error.WriteLine(error);return 1;
        }
        catch(Exception error) {Console.Error.WriteLine(error);return 1;}
        Console.Error.WriteLine("Diagnostic build did not block Transition.");return 1;
    }
}
