﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;

namespace DDown.CLI
{
    public static class Extension
    {
        /*public static T GetResult<T>(ParserResult<T> result) {
            var promise = new TaskCompletionSource<T>();
            result.WithParsed(o => promise.TrySetResult(o));

        } */
    }
    class Program
    {
        private static bool _continued;
        private static Downloader downloader;

        public static void Exit(object sender, ConsoleCancelEventArgs ev)
        {
            if (downloader != null)
            {
                downloader.Pause();
            }

            ev.Cancel = true;
        }

        public static Task Error(IEnumerable<Error> errors)
        {
            /*foreach (var item in errors)
            {
                Console.WriteLine(item);
            } */
            return Task.FromResult(0);
        }

        private static int[] lefts;
        private static int top;
        public static object lockObject = new object();
        public static async Task Main(string[] args)
        {
            var parser = CommandLine.Parser.Default.ParseArguments<CommandOptions>(args);

            var task = parser.MapResult(opts =>
            {
                return Start(opts);
            },
                errors => Error(errors)
            );

            await task;
        }
        
        static async Task Start(CommandOptions options)
        {
            //string link = "https://github.com/OpenShot/openshot-qt/releases/download/v2.4.1/OpenShot-v2.4.1-x86_64.dmg";
            //string link = "http://www.itu.edu.tr/docs/default-source/KurumsalKimlik-2017/itu-sunum.rar?sfvrsn=2";
            //var link = "https://media.forgecdn.net/files/2573/89/DBM-Core-7.3.31.zip";
            // if (options.ClearConsole)
            Console.Clear();

            var downloadOptions = new Options();
            downloadOptions.BufferSize = options.BufferSize;
            downloadOptions.Override = options.Override;
            downloadOptions.Timeout = options.Timeout;

            if (options.PartitionCount != 0)
                downloadOptions.PartitionCount = options.PartitionCount;

            if (!string.IsNullOrEmpty(options.OutputFolder))
                downloadOptions.OutputFolder = options.OutputFolder;
            else if(options.DownloadFolder) 
                downloadOptions.OutputFolder = 
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

            Console.CancelKeyPress += Exit;
            downloader = new Downloader(options.Link, downloadOptions);
            downloader.Progress += ReportProgress;

            TimeLog.WriteLine("Preparing");
            var status = await downloader.PrepareAsync();

            TimeLog.WriteLine($"Source {downloader.Url}, {(status.IsRangeSupported ? "Range is supported" : "Range is not supported") }");
            TimeLog.WriteLine($"File Name {downloader.FileName}, Size {downloader.Length.AsReadable()}");
            _continued = status.Continued;

            if (status.Continued)
                TimeLog.WriteLine($"Download is continued");

            Stopwatch sw = new Stopwatch();
            sw.Start();

            lefts = new int[downloader.PartitionCount];
            top = Console.CursorTop;

            await downloader.StartAsync();

            Console.SetCursorPosition(0, top + downloader.PartitionCount);
            if (downloader.ConnectionLost)
                TimeLog.WriteLine("Connection is lost.");
            
            if (downloader.SourceException)
                TimeLog.WriteLine("There exist a problem with source.");
            
            if (!downloader.Canceled)
            {
                TimeLog.WriteLine("Merging partitions");
                await downloader.MergeAsync();
            }

            sw.Stop();

            TimeLog.WriteLine($"Total elapsed time {sw.ElapsedMilliseconds} ms");
            TimeLog.WriteLine("Download is finished");
        }

        static void ReportProgress(Report data)
        {
            lock (lockObject)
            {
                var left = lefts[data.PartitionId];
                var topPartition = top + data.PartitionId;
                string template = $"[{DateTime.Now.ToShortTimeString()}] P{(data.PartitionId + 1).ToString().PadRight(2)}: {(data.Percent + "%").PadRight(4)}";
                var howMuch = data.Percent - left;

                // set the header
                Console.SetCursorPosition(0, topPartition);
                Console.Write(template);

                // set the bars
                Console.SetCursorPosition(left + template.Length, topPartition);
                Console.Write(new string('|', howMuch));

                // update the last state
                lefts[data.PartitionId] = left + (howMuch);
            }
        }

        static void ArgumentError(string text)
        {
            TimeLog.WriteLine(text);
            Environment.Exit(-1);
        }
    }
}
