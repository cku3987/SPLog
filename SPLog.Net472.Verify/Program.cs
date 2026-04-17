using System;
using System.IO;
using SPLog;

internal static class Program
{
    private static int Main()
    {
        var outputRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "artifacts", "net472-verify");
        Directory.CreateDirectory(outputRoot);

        var logDirectory = Path.Combine(outputRoot, "logs");

        using (var rootLogger = SPLogFactory.Create(options =>
               {
                   options.Name = "VerifyApp";
                   options.EnableConsole = false;
                   options.EnableFile = true;
                   options.FilePath = logDirectory;
                   options.FileRollingMode = FileRollingMode.None;
                   options.FileConflictMode = FileConflictMode.Append;
                   options.IncludeSequenceNumber = true;
               }))
        {
            var networkLogger = rootLogger.CreateCategory("Network");

            rootLogger.Information("VERIFY|ROOT|START");
            networkLogger.Warning("VERIFY|NETWORK|READY");

            try
            {
                throw new InvalidOperationException("net472 verification failure sample");
            }
            catch (Exception ex)
            {
                rootLogger.Error(ex, "VERIFY|ROOT|EXCEPTION");
            }
        }

        var expectedFile = Path.Combine(logDirectory, "VerifyApp.log");
        if (!File.Exists(expectedFile))
        {
            Console.Error.WriteLine("Verification failed: expected log file was not created.");
            return 1;
        }

        var content = File.ReadAllText(expectedFile);
        if (!content.Contains("VERIFY|ROOT|START")
            || !content.Contains("VERIFY|NETWORK|READY")
            || !content.Contains("VERIFY|ROOT|EXCEPTION")
            || !content.Contains("[VerifyApp.Network]")
            || !content.Contains("[Q:"))
        {
            Console.Error.WriteLine("Verification failed: expected log content was not found.");
            return 1;
        }

        Console.WriteLine("SPLog net472 verification passed.");
        Console.WriteLine(expectedFile);
        return 0;
    }
}
