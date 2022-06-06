using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using Flurl.Http;

namespace MasscanWrapper
{
    class Program
    {
        static readonly Process p = new Process();
        static readonly Regex IPAd = new Regex(@"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b");
        static readonly Stopwatch stopwatch = new Stopwatch();
        static int hasRan = 0;

        static void Main(string[] args)
        {
            try
            {
                Console.WriteLine("Starting scanning process. Sleeping for 5 seconds to allow the API to start...");
                Thread.Sleep(5000);
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.OutputDataReceived += new DataReceivedEventHandler(MyProcOutputHandler);
                p.ErrorDataReceived += new DataReceivedEventHandler(MyProcOutputHandler);
                p.StartInfo.FileName = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) + "/masscan.exe";
                stopwatch.Start();

                while (hasRan != IpRanges.Netherlands.Count)
                {
                    if (p.StartInfo.Arguments == "" || p.HasExited)
                    {
                        Console.WriteLine($"Restarting scanning process on range {IpRanges.Netherlands[hasRan]}!");
                        p.StartInfo.Arguments = $"-p25565 {IpRanges.Netherlands[hasRan]} --rate 10000000000 --exclude 255.255.255.255";
                        hasRan++;
                        p.Start();
                        p.BeginOutputReadLine();
                        p.BeginErrorReadLine();
                    }
                }

                Console.ReadLine();
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex);
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.Source);
                Console.WriteLine(ex.Data);
                Console.WriteLine(ex.StackTrace);
                Console.WriteLine(ex.InnerException);
                Console.ReadLine();
                Console.ReadKey();
            }
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
                    $"http://localhost:5000/main/submit/{IPAd.Match(outLine.Data)}".AllowAnyHttpStatus().GetAsync();
                }
                else if (outLine.Data.Contains("100.00% done"))
                {
                    p.Kill();
                    p.CancelErrorRead();
                    p.CancelOutputRead();
                    p.Close();
                    Console.WriteLine("Done!");
                }
                else if(outLine.Data.StartsWith("rate:"))
                {
                    if (stopwatch.ElapsedMilliseconds > (5 * 1000))
                    {
                        Console.WriteLine(outLine.Data);
                        stopwatch.Restart();
                    }
                }
                else
                {
                    Console.WriteLine(outLine.Data);
                }
            }
        }
    }
}
