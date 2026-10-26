using System.IO;
using System.Windows;

namespace Mosaic.PetWorldDemo;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var app=new Application {ShutdownMode=ShutdownMode.OnMainWindowClose};
        if(args.Length==2&&args[0]=="--verify")
        {
            try{WorldChecks.Run(Path.GetFullPath(args[1]));app.Shutdown();return 0;}
            catch(Exception ex){Directory.CreateDirectory(args[1]);File.WriteAllText(Path.Combine(args[1],"failure.txt"),ex.ToString());app.Shutdown();return 1;}
        }
        return app.Run(new WorldWindow());
    }
}
