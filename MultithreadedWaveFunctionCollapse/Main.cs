/*
The MIT License(MIT)
Copyright(c) mxgmn 2016.
Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
The software is provided "as is", without warranty of any kind, express or implied, including but not limited to the warranties of merchantability, fitness for a particular purpose and noninfringement. In no event shall the authors or copyright holders be liable for any claim, damages or other liability, whether in an action of contract, tort or otherwise, arising from, out of or in connection with the software or the use or other dealings in the software.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

static class Program
{
    private const int num_req_args = 6;
    private static Random rand;
    private static XmlDocument xdoc;
    private static int seed, max_degree_parallelism, num_workers, num_trials, num_main_loop_iterations;
    private static bool write_images;
    private static string[] executions;

    static void Main(string[] args)
    {
        InitializeAndLoad(args);
        ulong[] runtimes = new ulong[num_main_loop_iterations];
        for (int i = 0; i < num_main_loop_iterations; i++)
        {
           runtimes[i] = DisplayTime(Execute(), i);
        }

        WriteRuntimes(runtimes);
        waitForESCKey();
    }

    private static void WriteRuntimes(ulong[] runtimes)
    {
        StringBuilder sb = new StringBuilder("Runtime Number, Execution Name, Seed, Max Degree of Parallelism, Number of Workers, Number of Trials, Number of Main Loop Iterations, Runtime (ms)\n");       
        for (int i = 0; i < runtimes.Length; i++)
        {            
            string line = i + ", " + executions[0] + ", " + seed + ", "+max_degree_parallelism+", "+num_workers+", "
                            +num_trials+", "+num_main_loop_iterations+", "+ runtimes[i] +"\n";
            sb.Append(line);
        }

        string path = Directory.GetCurrentDirectory() + "\\" + "MultithreadedWaveFunctionCollapseResults.csv";
        System.IO.File.WriteAllText(path, sb.ToString());
    }

    private static void InitializeAndLoad(string[] args)
    {
        if (args.Length < num_req_args)
        {
            Console.WriteLine("Usage: MultithreadedWaveFunctionCollapse int_seed int_max_degree_of_parallelism " +
                                "int_number_of_workers int_number_of_trials int_number_of_main_loop_iterations bool_write_images execution_mode ... execution_mode");
            waitForESCKey();
            Environment.Exit(exitCode: 24);
        }

        seed = Convert.ToInt32(args[0]);
        max_degree_parallelism = Convert.ToInt32(args[1]);
        num_workers = Convert.ToInt32(args[2]);
        num_trials = Convert.ToInt32(args[3]);
        num_main_loop_iterations = Convert.ToInt32(args[4]);
        write_images = Convert.ToBoolean(args[5]);

        rand = new Random(seed);
        xdoc = new XmlDocument();        
        executions = new string[args.Length - num_req_args];
        int i = 0;
        for (int j = num_req_args; j < args.Length; j++)
        {
            executions[i] = args[j];
            ValidExecutionName(executions[i], j);
            i++;
        }
        
        try
        {
            xdoc.Load("samples.xml");
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            waitForESCKey();
            Environment.Exit(exitCode: 2);
        }
    }

    private static bool ValidExecutionName(string v, int j)
    {
        switch (v)
        {
            case "parallel-main":
                return true;
            case "parallel-propagate":
                return true;
            case "parallel-observe":
                return true;
            case "sequential-main":
                return true;
            default:
                Console.WriteLine(j+"th argument " + v +" is invalid");
                Console.WriteLine("Use only one of the following: sequential-main, parallel-main, parallel-propagate, or parallel-observe");
                Environment.Exit(1);
                return false;
        }
    }

    private static ulong DisplayTime(TimeSpan[] execution_times, int trial_num)
    {
        Console.WriteLine();
        ulong total_runtime = 0;
        for (int i = 0; i < execution_times.Length; i++)
        {
            Console.WriteLine(executions[i] + " execution time: " + execution_times[i]);
            total_runtime += (ulong) execution_times[i].TotalMilliseconds;
        }
        Console.WriteLine("Trial " + trial_num + " Runtime: " + total_runtime + " ms\n");

        return total_runtime;
    }

    private static TimeSpan[] Execute()
    {
        TimeSpan[] runtimes = new TimeSpan[executions.Length];
        for (int i = 0; i < executions.Length; i++)
        {            
            runtimes[i] = Run(System.Diagnostics.Stopwatch.StartNew(), executions[i]);
        }

        return runtimes;
    }

    private static TimeSpan Run(Stopwatch watch, string wfc_version)
    {
        //Run WFC
        int counter = 1;
        foreach (XmlNode xnode in xdoc.FirstChild.ChildNodes)
        {
            if (xnode.Name == "#comment")
            {
                continue;
            }

            var image_name = xnode.Get<string>("name");
            Console.WriteLine($"< {image_name}");

            for (int screenshotNumber = 0; screenshotNumber < xnode.Get("screenshots", 2); screenshotNumber++)
            {
                WFC(wfc_version, xnode, counter, screenshotNumber);
            }
            counter++;
        }
        //End WFC
        watch.Stop();
        return watch.Elapsed;
    }

    private static void WFC(string wfc_version, XmlNode xnode, int counter, int screenshotNumber)
    {
        WaveFunctionCollapse wfc;
        switch (wfc_version)
        {
            case "parallel-main":
                wfc = new ParallelMain(num_trials, rand, write_images, num_workers);
                break;
            case "parallel-propagate":
                wfc = new ParallelPropagate(num_trials, rand, write_images);
                break;
            case "parallel-observe":
                wfc = new ParallelObserve(num_trials, rand, write_images);
                break;
            default:
                wfc = new SequentialMain(num_trials, rand, write_images);
                break;
        }
        wfc.Run(xnode, counter, screenshotNumber);
    }  

    private static void waitForESCKey()
    {
        Console.WriteLine("\nPress ESC to stop");
        do
        {
        }
        while (Console.ReadKey(true).Key != ConsoleKey.Escape);
    }
}

internal abstract class WaveFunctionCollapse
{
    public readonly int _numTriesMainLoop;
    public int _numWorkers;
    public readonly Random _random;
    public readonly bool _writeImages;
    public bool _parallel_propagate, _parallel_observe;

    protected WaveFunctionCollapse(int numTriesMainLoop, Random random, bool writeImages)
    {
        _numTriesMainLoop = numTriesMainLoop;
        _random = random;
        _writeImages = writeImages;
    }

    public abstract void Run(XmlNode xnode, int counter, int screenshotNumber);

    internal void RunSequential(XmlNode xnode, int counter, int screenshotNumber)
    {
        Compute(GetModel(xnode), xnode, counter, screenshotNumber, -1);
    }

    internal void RunParallel(XmlNode xnode, int counter, int screenshotNumber)
    {
        Task[] tasks = new Task[_numWorkers];
        CancellationTokenSource source = new CancellationTokenSource();
        CancellationToken token = source.Token;

        for (int i = 0; i < _numWorkers; i++)
        {
            int id = i;
            tasks[i] = Task.Run(
                () =>
                {
                    Model model = GetModel(xnode);
                    Compute(model, xnode, counter, screenshotNumber, id);
                    source.Cancel();
                });
        }

        Task.WaitAny(tasks);
    }

    private void Compute(Model model, XmlNode xnode, int counter, int screenshotNumber, int id)
    {
        var name = xnode.Get<string>("name");
        string ID_String = (id < 0) ? ("") : (" WITH " + id);
        for (int k = 0; k < _numTriesMainLoop; k++)
        {
            Console.Write(">");
            int seed = _random.Next();
            bool finished = model.Run(seed, xnode.Get("limit", 0));
            if (finished)
            {
                Console.WriteLine("DONE" + ID_String);

                if (_writeImages)
                {
                    model.Graphics().Save($"{counter} {name} {screenshotNumber}.png");
                    if (model is SimpleTiledModel && xnode.Get("textOutput", false))
                    {
                        File.WriteAllText($"{counter} {name} {screenshotNumber}.txt", (model as SimpleTiledModel).TextOutput());
                    }
                }
                break;
            }
            Console.WriteLine("CONTRADICTION" + ID_String);
        }
    }

    private Model GetModel(XmlNode xnode)
    {
        Model model;
        string name = xnode.Get<string>("name");
        switch (xnode.Name)
        {                
            case "overlapping":
                model = new OverlappingModel(name, xnode.Get("N", 2), xnode.Get("width", 48), xnode.Get("height", 48),
                    xnode.Get("periodicInput", true), xnode.Get("periodic", false), xnode.Get("symmetry", 8), xnode.Get("ground", 0));
                break;
            case "simpletiled":
                model = new SimpleTiledModel(name, xnode.Get<string>("subset"),
                    xnode.Get("width", 10), xnode.Get("height", 10), xnode.Get("periodic", false), xnode.Get("black", false));
                break;
            default:
                return null;
        }

        model.isParallelPropagate = _parallel_propagate;
        model.isParallelObserve = _parallel_observe;

        return model;
    }
}

internal class SequentialMain : WaveFunctionCollapse
{
    public SequentialMain(int numTriesMainLoop, Random random, bool writeImages) : base(numTriesMainLoop, random, writeImages)
    {
    }

    public override void Run(XmlNode xnode, int counter, int screenshotNumber)
    {
        RunSequential(xnode, counter, screenshotNumber);
    }
}

internal class ParallelMain : WaveFunctionCollapse
{
    public ParallelMain(int numTriesMainLoop, Random random, bool writeImages, int numWorkers) : base(numTriesMainLoop, random, writeImages)
    {
        _numWorkers = numWorkers;
    }

    public override void Run(XmlNode xnode, int counter, int screenshotNumber)
    {
        RunParallel(xnode, counter, screenshotNumber);
    }
}

internal class ParallelPropagate : WaveFunctionCollapse
{
    public ParallelPropagate(int numTriesMainLoop, Random random, bool writeImages) : base(numTriesMainLoop, random, writeImages)
    {
    }

    public override void Run(XmlNode xnode, int counter, int screenshotNumber)
    {
        RunSequential(xnode, counter, screenshotNumber);
    }
}

internal class ParallelObserve : WaveFunctionCollapse
{
    public ParallelObserve(int numTriesMainLoop, Random random, bool writeImages) : base(numTriesMainLoop, random, writeImages)
    {
    }

    public override void Run(XmlNode xnode, int counter, int screenshotNumber)
    {
        throw new NotImplementedException();
    }
}





