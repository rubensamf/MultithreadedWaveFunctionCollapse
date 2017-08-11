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
    private static Random rand;
    private static XmlDocument xdoc;
    private static int seed, num_trials, num_main_loop_iterations;
    private static bool write_images;
    private static string[] execution;
    private static string cur_execution;
    private static int[] max_degree_parallelism, num_workers;
    private static int cur_max_degree_parallelism, cur_num_workers;
    private static int SCALE_FACTOR;

    static void Main(string[] args)
    {
        InitializeAndLoad(args);
        List<string[]> runtimes = new List<string[]>(num_main_loop_iterations);
        for (int i = 0; i < num_main_loop_iterations; i++)
        {
            runtimes.Add(DisplayTime(Execute(), i));
        }

        WriteRuntimes(runtimes);
        WaitForEscKey();
    }

    private static void InitializeAndLoad(string[] args)
    {
        try
        {
            write_images = Convert.ToBoolean(args[0]);
            seed = Convert.ToInt32(args[1]);
            num_main_loop_iterations = Convert.ToInt32(args[2]);
            num_trials = Convert.ToInt32(args[3]);
            SCALE_FACTOR = Convert.ToInt32(args[4]);

            int num_execution_modes = (args.Length - 5) / 3;

            execution = new string[num_execution_modes];
            max_degree_parallelism = new int[num_execution_modes];
            num_workers = new int[num_execution_modes];

            int i = 0;
            for (int j = 5; j < args.Length; j += 3)
            {
                execution[i] = args[j];
                max_degree_parallelism[i] = Convert.ToInt32(args[j + 1]);
                num_workers[i] = Convert.ToInt32(args[j + 2]);
                ValidExecutionName(execution[i], j);
                i++;
            }

            rand = new Random(seed);
            xdoc = new XmlDocument();

            xdoc.Load("samples.xml");
        }
        catch (Exception e)
        {
            Console.WriteLine("Usage: MultithreadedWaveFunctionCollapse bool_write_images int_seed int_number_of_main_loop_iterations int_number_of_trials scale_factor " +
                "execution_mode int_max_degree_of_parallelism int_number_of_workers ... " +
                "(execution_mode int_max_degree_of_parallelism int_number_of_workers)");
            WaitForEscKey();
            Environment.Exit(exitCode: 24);
        }
    }

    private static bool ValidExecutionName(string v, int j)
    {
        switch (v)
        {
            case "sequential-main":
                return true;
            case "parallel-main":
                return true;
            case "parallel-propagate":
                return true;
            case "parallel-observe":
                return true;
            default:
                Console.WriteLine(j + "th argument " + v + " is invalid");
                Console.WriteLine("Use only one of the following: sequential-main, parallel-main, parallel-propagate, or parallel-observe");
                Environment.Exit(1);
                return false;
        }
    }

    private static string[] DisplayTime(TimeSpan[] execution_times, int run_num)
    {
        Console.WriteLine();
        ulong total_runtime = 0;
        string[] lines = new string[execution.Length];
        for (int i = 0; i < execution.Length; i++)
        {
            Console.WriteLine(execution[i] + " execution time: " + execution_times[i]);
            lines[i] = run_num + ", " + SCALE_FACTOR + ", " + execution[i] + ", " + seed + ", " + max_degree_parallelism[i] + ", " +
                num_workers[i] + ", " + num_trials + ", " + num_main_loop_iterations + ", " + execution_times[i];
            total_runtime += (ulong) execution_times[i].TotalMilliseconds;
        }

        Console.WriteLine("Run #" + run_num + " Runtime: " + total_runtime + " ms\n");

        return lines;
    }

    private static void WriteRuntimes(List<string[]> runs)
    {
        StringBuilder sb = new StringBuilder("Run Number, Scale Factor, Execution Name, Seed, Max Degree of Parallelism, Number of Workers, Number of Trials, Number of Main Loop Iterations, Runtime (ms)\n");

        foreach (var runtimes in runs)
        {
            foreach (var runtime in runtimes)
            {
                sb.Append(runtime + "\n");
            }
        }

        string path = Directory.GetCurrentDirectory() + "\\" + "MultithreadedWaveFunctionCollapseResults.csv";
        System.IO.File.WriteAllText(path, sb.ToString());
    }

    private static TimeSpan[] Execute()
    {
        TimeSpan[] runtimes = new TimeSpan[execution.Length];
        for (int i = 0; i < execution.Length; i++)
        {
            rand = new Random(seed);
            cur_execution = execution[i];
            cur_max_degree_parallelism = max_degree_parallelism[i];
            cur_num_workers = num_workers[i];
            runtimes[i] = Run(System.Diagnostics.Stopwatch.StartNew());
        }

        return runtimes;
    }

    private static TimeSpan Run(Stopwatch watch)
    {
        watch.Stop();
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
                WFC(xnode, counter, screenshotNumber, watch);
            }

            counter++;
        }
        //End WFC
        //watch.Stop();
        return watch.Elapsed;
    }

    private static Stopwatch WFC(XmlNode xnode, int counter, int screenshotNumber, Stopwatch watch)
    {
        WaveFunctionCollapse wfc;
        switch (cur_execution)
        {
            case "parallel-main":
                wfc = new ParallelMain(num_trials, rand, write_images, cur_num_workers, SCALE_FACTOR, seed);
                break;
            case "parallel-propagate":
                wfc = new ParallelPropagate(num_trials, rand, write_images, cur_max_degree_parallelism, SCALE_FACTOR, seed);
                break;
            case "parallel-observe":
                wfc = new ParallelObserve(num_trials, rand, write_images, cur_max_degree_parallelism, SCALE_FACTOR, seed);
                break;
            default:
                wfc = new SequentialMain(num_trials, rand, write_images, SCALE_FACTOR, seed);
                break;
        }

        return wfc.Run(xnode, counter, screenshotNumber, watch);
    }

    private static void WaitForEscKey()
    {
        Console.WriteLine("\nPress ESC to stop");
        do { }
        while (Console.ReadKey(true).Key != ConsoleKey.Escape);
    }
}

internal abstract class WaveFunctionCollapse
{
    protected readonly int _numTriesMainLoop;
    protected int _numWorkers = 1, _maxParallelism = 1;
    protected readonly Random _random;
    protected readonly bool _writeImages;
    protected bool _parallel_propagate, _parallel_observe;
    protected int SCALE_FACTOR, SEED;

    protected WaveFunctionCollapse(int numTriesMainLoop, Random random, bool writeImages)
    {
        _numTriesMainLoop = numTriesMainLoop;
        _random = random;
        _writeImages = writeImages;
    }

    public abstract Stopwatch Run(XmlNode xnode, int counter, int screenshotNumber, Stopwatch watch);

    internal Stopwatch RunSequential(XmlNode xnode, int counter, int screenshotNumber, Stopwatch watch)
    {
        return Compute(GetModel(xnode), xnode, counter, screenshotNumber, -1, watch);
    }

    internal Stopwatch RunParallel(XmlNode xnode, int counter, int screenshotNumber, Stopwatch watch)
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
                    Compute(model, xnode, counter, screenshotNumber, id, watch);
                    source.Cancel();
                });
        }

        Task.WaitAny(tasks);
        return watch;
    }

    private Stopwatch Compute(Model model, XmlNode xnode, int counter, int screenshotNumber, int id, Stopwatch watch)
    {
        var name = xnode.Get<string>("name");
        string ID_String = (id < 0) ? ("") : (" WITH " + id);
        for (int k = 0; k < _numTriesMainLoop; k++)
        {
            Console.Write(">");
            //int seed = _random.Next();
            int seed = SEED;
            watch.Restart();
            bool finished = model.Run(seed, xnode.Get("limit", 0));
            watch.Stop();
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

        return watch;
    }

    private Model GetModel(XmlNode xnode)
    {
        Model model;
        string name = xnode.Get<string>("name");
        switch (xnode.Name)
        {
            case "overlapping":
                model = new OverlappingModel(name, xnode.Get("N", 2), xnode.Get("width", 48 * SCALE_FACTOR), xnode.Get("height", 48 * SCALE_FACTOR),
                    xnode.Get("periodicInput", true), xnode.Get("periodic", false), xnode.Get("symmetry", 8), xnode.Get("ground", 0));
                break;
            case "simpletiled":
                model = new SimpleTiledModel(name, xnode.Get<string>("subset"),
                    xnode.Get("width", 10 * SCALE_FACTOR), xnode.Get("height", 10 * SCALE_FACTOR), xnode.Get("periodic", false), xnode.Get("black", false));
                break;
            default:
                return null;
        }

        model.isParallelPropagate = _parallel_propagate;
        model.isParallelObserve = _parallel_observe;
        model.maxParallelism = _maxParallelism;

        return model;
    }
}

internal class SequentialMain : WaveFunctionCollapse
{
    public SequentialMain(int numTriesMainLoop, Random random, bool writeImages, int s, int seed) : base(numTriesMainLoop, random, writeImages)
    {
        _parallel_propagate = true;
        _parallel_observe = false;
        SCALE_FACTOR = s;
        SEED = seed;
    }

    public override Stopwatch Run(XmlNode xnode, int counter, int screenshotNumber, Stopwatch watch)
    {
        return RunSequential(xnode, counter, screenshotNumber, watch);
    }
}

internal class ParallelMain : WaveFunctionCollapse
{
    public ParallelMain(int numTriesMainLoop, Random random, bool writeImages, int numWorkers, int s, int seed) : base(numTriesMainLoop, random, writeImages)
    {
        _parallel_propagate = false;
        _parallel_observe = false;
        _numWorkers = numWorkers;
        SCALE_FACTOR = s;
    }

    public override Stopwatch Run(XmlNode xnode, int counter, int screenshotNumber, Stopwatch watch)
    {
        return RunParallel(xnode, counter, screenshotNumber, watch);
    }
}

internal class ParallelPropagate : WaveFunctionCollapse
{
    public ParallelPropagate(int numTriesMainLoop, Random random, bool writeImages, int maxParallel, int s, int seed) : base(numTriesMainLoop, random, writeImages)
    {
        _maxParallelism = maxParallel;
        _parallel_propagate = true;
        _parallel_observe = false;
        SCALE_FACTOR = s;
    }

    public override Stopwatch Run(XmlNode xnode, int counter, int screenshotNumber, Stopwatch watch)
    {
        return RunSequential(xnode, counter, screenshotNumber, watch);
    }
}

internal class ParallelObserve : WaveFunctionCollapse
{
    public ParallelObserve(int numTriesMainLoop, Random random, bool writeImages, int maxParallel, int s, int seed) : base(numTriesMainLoop, random, writeImages)
    {
        _maxParallelism = maxParallel;
        _parallel_propagate = false;
        _parallel_observe = true;
        SCALE_FACTOR = s;
    }

    public override Stopwatch Run(XmlNode xnode, int counter, int screenshotNumber, Stopwatch watch)
    {
        throw new NotImplementedException();
    }
}






