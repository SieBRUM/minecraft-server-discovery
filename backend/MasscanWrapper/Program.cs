using System;
using Flurl.Http;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;

namespace MasscanWrapper
{
    class Program
    {
        static readonly Process p = new Process();
        static readonly Regex IPAd = new Regex(@"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b");
        static readonly Stopwatch stopwatch = new Stopwatch();

        static void Main(string[] args)
        {
            Console.WriteLine("Starting scanning process. Sleeping for 5 seconds to allow the API to start...");
            Thread.Sleep(5000);
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardError = true;
            p.OutputDataReceived += new DataReceivedEventHandler(MyProcOutputHandler);
            p.ErrorDataReceived += new DataReceivedEventHandler(MyProcOutputHandler);
            p.StartInfo.FileName = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) + "/masscan.exe";
            p.StartInfo.Arguments = "-p25565 0.0.0.0/0 --rate 69696 --exclude 255.255.255.255";
            stopwatch.Start();
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            Console.ReadLine();
        }

        private static void MyProcOutputHandler(object sendingProcess,
                    DataReceivedEventArgs outLine)
        {
            // Collect the sort command output. 
            if (!string.IsNullOrEmpty(outLine.Data))
            {
                if (outLine.Data.Contains("Starting scanning process"))
                {
                    Console.WriteLine("Scanner has started!");
                }
                else if (outLine.Data.Contains("Discovered"))
                {
                    Console.WriteLine($"Hit! IP {IPAd.Match(outLine.Data)} has possible Minecraft server running!");
                    $"https://localhost:44315/main/submit/{IPAd.Match(outLine.Data)}".AllowAnyHttpStatus().GetAsync();
                }
                else
                {
                    if(stopwatch.ElapsedMilliseconds > (5 * 1000))
                    {
                        Console.WriteLine(outLine.Data);
                        stopwatch.Restart();
                    }
                }
            }
        }
    }
}
