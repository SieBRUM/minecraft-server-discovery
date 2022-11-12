using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using Flurl.Http;

namespace MasscanWrapper
{
    class Program
    {
        static Process p;
        static readonly Regex IPAd = new Regex(@"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b");
        static readonly Stopwatch stopwatch = new Stopwatch();
        static string apiUrl = "";
        static int hasRan = 0;
        static string enableDebug = "";

        static void Main(string[] args)
        {
            ConsoleWriteColor("Minecraft server scanner v1.0. Starting setup.", ConsoleColor.Green);
            var masscanLocation = ConsoleWriteQuestionColor($"Location of masscanner? ({Environment.GetFolderPath(Environment.SpecialFolder.Desktop) + "\\masscan.exe"}) ");
            masscanLocation = string.IsNullOrEmpty(masscanLocation) ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop) + "\\masscan.exe" : masscanLocation;

            var useDefault = "";

            do
            {
                useDefault = ConsoleWriteQuestionColor($"Use [d]efault list or own IP [r]ange? ").ToLower();
            } while (useDefault != "d" && useDefault != "r");

            do
            {
                enableDebug = ConsoleWriteQuestionColor($"Enable debug logs? [y/n] ").ToLower();
            } while (enableDebug != "y" && enableDebug != "n");

            List<string> selectedIpRanges;

            if(useDefault == "d")
            {
                var selectedList = -1;
                List<string> madeList;
                do
                {
                    Console.WriteLine("What list would you like to use?");
                    var i = 1;
                    madeList = new List<string>();
                    foreach (var item in IpRanges.DefaultIpRanges)
                    {
                        ConsoleWriteColor($"[{i}] {item.Key}");
                        madeList.Add(item.Key);
                        i++;
                    }
                    Console.Write("Please select number: ");
                    int.TryParse(Console.ReadLine(), out selectedList);

                } while (selectedList < 1 || selectedList > IpRanges.DefaultIpRanges.Count);

                selectedIpRanges = IpRanges.DefaultIpRanges[madeList[selectedList - 1]];
            }
            else
            {
                selectedIpRanges = new List<string>();
                selectedIpRanges.Add(ConsoleWriteQuestionColor($"Please insert IP range (example: 45.0.0.0/16) ").ToLower());
            }

            var keepLooping = "";
            do
            {
                keepLooping = ConsoleWriteQuestionColor($"Do you want to loop scanner after done? [y/n] ").ToLower();
            } while (keepLooping != "y" && keepLooping != "n");

            apiUrl = ConsoleWriteQuestionColor($"API GET URL to submit? (http://localhost:5000/main/submit/)");
            apiUrl = string.IsNullOrWhiteSpace(apiUrl) ? "http://localhost:5000/main/submit/" : apiUrl;

            var rateLimit = ConsoleWriteQuestionColor($"Rate limit? (10000) ");
            rateLimit = string.IsNullOrWhiteSpace(rateLimit) ? "10000" : rateLimit;


            ConsoleWriteColor("Setup all done!", ConsoleColor.Green);
            ConsoleWriteColor("Starting scanning process...", ConsoleColor.Green);
            stopwatch.Start();

            do
            {
                while (hasRan != selectedIpRanges.Count)
                {
                    if (p == default || string.IsNullOrWhiteSpace(p?.StartInfo?.Arguments))
                    {
                        CreateAndStartScanningProcess(masscanLocation, selectedIpRanges[hasRan], rateLimit);
                    }
                }

                if(keepLooping == "y")
                {
                    ConsoleWriteColor("Done with all Ip ranges! Restarting...", ConsoleColor.Green);
                    hasRan = 0;
                }
                else
                {
                    ConsoleWriteColor("Done with all Ip ranges!", ConsoleColor.Green);
                }

            } while (keepLooping == "y");


            ConsoleWriteColor("Press any key to exit");
            Console.ReadKey();
        }

        private static void ConsoleWriteColor(string message, ConsoleColor? color = null)
        {
            if(color != null)
                Console.ForegroundColor = (ConsoleColor)color;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        private static string ConsoleWriteQuestionColor(string question, ConsoleColor? color = null)
        {
            if (color != null)
                Console.ForegroundColor = (ConsoleColor)color;
            Console.Write(question);
            Console.ResetColor();
            return Console.ReadLine();
        }

        private static void MyProcOutputHandler(object sendingProcess, DataReceivedEventArgs outLine)
        {
            if (!string.IsNullOrEmpty(outLine.Data))
            {

                if (outLine.Data.Contains("Discovered"))
                {
                    ConsoleWriteColor($"Hit! IP {IPAd.Match(outLine.Data)} has possible Minecraft server running!", ConsoleColor.Green);
                    $"{apiUrl}{IPAd.Match(outLine.Data)}".AllowAnyHttpStatus().GetAsync();
                }
                else if (outLine.Data.Contains("100.00% done"))
                {
                    p.Kill();
                    p.CancelErrorRead();
                    p.CancelOutputRead();
                    p.Close();
                    ConsoleWriteColor("Done with scanning IP range!");
                    p = default;
                }
                else if (outLine.Data.StartsWith("rate:"))
                {
                    if (stopwatch.ElapsedMilliseconds > (5 * 1000))
                    {
                        Console.WriteLine(outLine.Data);
                        stopwatch.Restart();
                    }
                }
                else if(enableDebug == "y")
                {
                    Console.WriteLine(outLine.Data);
                }
            }
        }

        private static void CreateAndStartScanningProcess(string fileName, string ipRange, string rateLimit)
        {
            p = new Process();
            Thread.Sleep(1000); // So things can catch up
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardError = true;
            p.OutputDataReceived += new DataReceivedEventHandler(MyProcOutputHandler);
            p.ErrorDataReceived += new DataReceivedEventHandler(MyProcOutputHandler);
            p.StartInfo.FileName = fileName;
            ConsoleWriteColor($"Starting scanning process on range {ipRange}!");
            p.StartInfo.Arguments = $"-p25565 {ipRange} --rate {rateLimit} --exclude 255.255.255.255";
            hasRan++;
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
        }
    }
}
