/*
The MIT License(MIT)
Copyright(c) mxgmn 2016.
Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
The software is provided "as is", without warranty of any kind, express or implied, including but not limited to the warranties of merchantability, fitness for a particular purpose and noninfringement. In no event shall the authors or copyright holders be liable for any claim, damages or other liability, whether in an action of contract, tort or otherwise, arising from, out of or in connection with the software or the use or other dealings in the software.
*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

static class Program
{
    private static XmlDocument xdoc;
    private static int base_seed, num_tries, num_trials, seed, SCALE_FACTOR, num_execution_modes, cur_max_degree_parallelism, cur_num_workers;
    private static bool write_images;
    private static string[] execution_names;
    private static string cur_execution_name;
    private static int[] max_degree_parallelism, replicate_seeds, num_workers;
    private static Random rand;
    private static Stopwatch overall_runtime;
    private static List<string> main_results_table;

    static void Main(string[] args)
    {
        overall_runtime = Stopwatch.StartNew();
        // Command line arguments: bool_write_images int_seed int_number_of_trials int_number_of_tries scale_factor [execution_mode int_max_degree_of_parallelism int_number_of_workers] ...
        InitializeAndLoad(args);
        List<string[]> runtimes = new List<string[]>(num_trials);
        for (int i = 0; i < num_trials; i++)
        {
            seed = replicate_seeds[i];
            runtimes.Add(DisplayTime(Execute(), i));
        }
        overall_runtime.Stop();
        WriteRuntimes(runtimes);
        //WriteMainResultTable();
        WaitForEscKey();
    }

    

    private static Dictionary<string, WaveFunctions> Execute()
    {   
        var executions = new Dictionary<string, WaveFunctions>();
        for (int i = 0; i < execution_names.Length; i++)
        {
            cur_execution_name = execution_names[i];
            cur_max_degree_parallelism = max_degree_parallelism[i];
            cur_num_workers = num_workers[i];
            AddToRuns(executions, cur_execution_name, Run());
        }
        return executions;
    }

    private static void AddToRuns(Dictionary<string, WaveFunctions> executions, string execution_name, WaveFunctionCollapse run)
    {
        WaveFunctions runs;
        if (executions.TryGetValue(execution_name, out runs))
        {
            runs.Add(run);
        }
        else
        {
            runs = new WaveFunctions(run);
            executions.Add(execution_name, runs);
        }
    }

    private static WaveFunctionCollapse Run()
    {
        Stopwatch wfc_timer = Stopwatch.StartNew();
        var searches = new List<Search[]>();
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

            int num_Screenshots = xnode.Get("screenshots", 2);
            Search[] searches_by_screenshot = new Search[num_Screenshots];
            for (int screenshotNumber = 0; screenshotNumber < num_Screenshots; screenshotNumber++)
            {
                searches_by_screenshot[screenshotNumber] = WFC(xnode, counter, screenshotNumber, image_name);                
            }
            searches.Add(searches_by_screenshot);
            counter++;
        }

        wfc_timer.Stop();
        var wfc = new WaveFunctionCollapse(searches, cur_execution_name, wfc_timer);
        return wfc;
    }

    private static Search WFC(XmlNode xnode, int counter, int screenshotNumber, string imageName)
    {
        Search searcher;
        switch (cur_execution_name)
        {
            case "parallel-main":
                searcher = ParallelSearch.Construct(write_images, num_tries, cur_num_workers, SCALE_FACTOR, seed, cur_max_degree_parallelism, xnode, counter, screenshotNumber, imageName);
                break;
            case "parallel-propagate":
                searcher = ParallelPropagate.Construct(write_images, num_tries, cur_num_workers, SCALE_FACTOR, seed, cur_max_degree_parallelism, xnode, counter, screenshotNumber, imageName);
                break;
            case "parallel-observe":
                searcher = ParallelObserve.Construct(write_images, num_tries, cur_num_workers, SCALE_FACTOR, seed, cur_max_degree_parallelism, xnode, counter, screenshotNumber, imageName);
                break;
            default:
                searcher = SequentialSearch.Construct(write_images, num_tries, cur_num_workers, SCALE_FACTOR, seed, cur_max_degree_parallelism, xnode, counter, screenshotNumber, imageName);
                break;
        }
        searcher.total_runtime_timer = Stopwatch.StartNew();
        searcher.Run();
        searcher.total_runtime_timer.Stop();
        return searcher;
    }

    private static void InitializeAndLoad(string[] args)
    {
        main_results_table = new List<string>();
        main_results_table.Add(" , , Wall-clock time to result, , Time in propagation phase\n" +
                               "Strategy, Degree of Parallelism, Mean, Stdev, Mean, Stdev\n");
        try
        {
            write_images = Convert.ToBoolean(args[0]);
            base_seed = Convert.ToInt32(args[1]);
            num_trials = Convert.ToInt32(args[2]);
            num_tries = Convert.ToInt32(args[3]);
            SCALE_FACTOR = Convert.ToInt32(args[4]);

            rand = new Random(base_seed);
            replicate_seeds = new int[num_trials];
            
            for (int seed_num = 0; seed_num < num_trials; seed_num++)
            {
                replicate_seeds[seed_num] = rand.Next();
            }
            

            num_execution_modes = (args.Length - 5) / 3;

            execution_names = new string[num_execution_modes];
            max_degree_parallelism = new int[num_execution_modes];
            num_workers = new int[num_execution_modes];

            int i = 0;
            for (int j = 5; j < args.Length; j += 3)
            {
                execution_names[i] = args[j];
                max_degree_parallelism[i] = Convert.ToInt32(args[j + 1]);
                num_workers[i] = Convert.ToInt32(args[j + 2]);
                ValidExecutionName(execution_names[i], j);
                i++;
            }
            xdoc = new XmlDocument();
            xdoc.Load("samples.xml");
        }
        catch (Exception e)
        {
            Console.WriteLine("\nUsage: MultithreadedWaveFunctionCollapse bool_write_images int_seed int_number_of_main_loop_iterations int_number_of_tries scale_factor " +
                "execution_mode int_max_degree_of_parallelism int_number_of_workers ... " +
                "(execution_mode int_max_degree_of_parallelism int_number_of_workers)\n");
            Console.WriteLine(e);
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

    private static string[] DisplayTime(Dictionary<string, WaveFunctions> executions, int run_num)
    {
        Console.WriteLine();
        ulong total_runtime = 0, total_searchtime = 0, total_proptime = 0;        
        var lines = new List<string>(execution_names.Length);
        int replicateSeed = replicate_seeds[run_num];

        foreach (var execution in executions)
        {
            Console.WriteLine();
            Console.WriteLine("Execution: " + execution.Key);
            Console.WriteLine();
            foreach (var run in execution.Value.waves)
            {
                bool flag = true;
                Console.Write("Run: " + run.name);
                foreach (var search in run.searches)
                {                    
                    int screenshot_num = 0;
                    string img_name = "";                    
                    foreach (var screenshot in search)
                    {
                        if (flag)
                        {
                            Console.WriteLine(" " + screenshot._maxParallelism + " " + screenshot.num_workers + "\tRuntime: " + run.timer.Elapsed);
                            flag = false;
                        }
                        if(img_name != screenshot.Name)
                            Console.WriteLine("\n" + screenshot.Name);
                        Console.WriteLine("\tScreenshot " + screenshot_num + " Search time: " + screenshot.search_time + " Propagation time: " + screenshot.propagation_time + " Total Runtime: " + screenshot.total_runtime_timer.Elapsed);
                        lines.Add(run_num + ", " + run.name + ", " + screenshot.name + ", " + SCALE_FACTOR + ", " + base_seed + ", " + replicateSeed + ", " + screenshot._maxParallelism + ", " +
                            screenshot.num_workers + ", " + num_tries + ", " + num_trials + ", " + screenshot_num + ", " + screenshot.propagation_time + ", " + screenshot.search_time);
                        total_searchtime += (ulong) screenshot.search_time.TotalMilliseconds;
                        total_proptime += (ulong) screenshot.propagation_time.TotalMilliseconds;
                        screenshot_num++;
                        img_name = screenshot.Name;
                    }
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine("Run #" + run_num + " Proptime: " + total_proptime + " ms Searchtime: " + total_searchtime + " ms");

        return lines.ToArray();
    }

    private static void WriteRuntimes(List<string[]> runs)
    {
        Console.WriteLine("\nOverall Runtime: " + overall_runtime.Elapsed);
        StringBuilder sb = new StringBuilder("Run Number, Run Name, Image Name, Scale Factor, Base Seed, Replicate Seed, Max Degree of Parallelism, Number of Workers, Number of Tries, Number of Trials, Screenshot Number, Propagate Time, Search Time\n");

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

    private static void WriteMainResultTable()
    {
        throw new NotImplementedException();
    }

    private static void WaitForEscKey()
    {
        Console.WriteLine("\nPress ESC to stop");
        do { }
        while (Console.ReadKey(true).Key != ConsoleKey.Escape);
    }
}

internal class WaveFunctions
{
    public List<WaveFunctionCollapse> waves { get; set; }
    public Stopwatch timer;

    public WaveFunctions(WaveFunctionCollapse run)
    {
        waves = new List<WaveFunctionCollapse>();
        waves.Add(run);
    }
    
    public void Add(WaveFunctionCollapse run)
    {
        waves.Add(run);
    }
}

internal class WaveFunctionCollapse
{
    public string name;
    public Stopwatch timer;
    public List<Search[]> searches;

    public WaveFunctionCollapse(List<Search[]> searches, string name, Stopwatch timer)
    {
        this.searches = searches;
        this.name = name;
        this.timer = timer;
    }    
}

internal abstract class Search
{
    public string name;
    public int SCALE_FACTOR, _maxParallelism, counter, SEED, screenshotNumber, num_workers, _numSearches;
    public bool _parallel_propagate, _parallel_observe, _writeImages;
    public XmlNode xnode;
    public TimeSpan search_time, propagation_time;
    public Stopwatch total_runtime_timer;

    public string Name
    {
        get { return name; }
    }

    public abstract void Run();

    private Model GetModel(XmlNode xnode)
    {
        Model model;
        switch (xnode.Name)
        {
            case "overlapping":
                model = new OverlappingModel(Name, xnode.Get("N", 2), xnode.Get("width", 48 * SCALE_FACTOR), xnode.Get("height", 48 * SCALE_FACTOR),
                    xnode.Get("periodicInput", true), xnode.Get("periodic", false), xnode.Get("symmetry", 8), xnode.Get("ground", 0));
                break;
            case "simpletiled":
                model = new SimpleTiledModel(Name, xnode.Get<string>("subset"),
                    xnode.Get("width", 10 * SCALE_FACTOR), xnode.Get("height", 10 * SCALE_FACTOR), xnode.Get("periodic", false), xnode.Get("black", false));
                break;
            default:
                return null;
        }

        model.isParallelPropagate = _parallel_propagate;
        model.isParallelObserve = _parallel_observe;
        model.maxParallelism = _maxParallelism;
        model.prop_watch = new Stopwatch();

        return model;
    }
    
    internal void RunSequential(bool parallel_propagate, bool parallel_observe)
    {
        Stopwatch search_watch, prop_watch;
        Compute(GetModel(xnode), -1, out search_watch, out prop_watch);
        search_time = search_watch.Elapsed;
        propagation_time = prop_watch.Elapsed;
    }  

    internal void RunParallel(bool parallel_propagate, bool parallel_observe)
    {        
        Task[] tasks = new Task[num_workers];
        CancellationTokenSource source = new CancellationTokenSource();
        CancellationToken token = source.Token;
        // create varz to accumulate propagate and search timez       
        TimeSpan search = TimeSpan.Zero, propagate = TimeSpan.Zero;
        for (int i = 0; i < num_workers; i++)
        {
            int id = i;
            tasks[i] = Task.Run(
                () =>
                {
                    Model model = GetModel(xnode);
                    // Create Ztopwatchez for zearch and for propagate here                   
                    Stopwatch search_watch, prop_watch;
                    // pazz thoze to compute
                    Compute(model, id, out search_watch, out prop_watch); // create a new watch for each task
                    source.Cancel();

                    // add the time for each 'watch to the accumulatorz
                    search += search_watch.Elapsed;
                    propagate += prop_watch.Elapsed;
                });
        }
        Task.WaitAny(tasks);
        search_time = search;
        propagation_time = propagate;
    }

    /**
     * Accumulates all search and propagation times
     */
    private void Compute(Model model, int id, out Stopwatch search_time, out Stopwatch propagate_time)
    {
        Stopwatch search_watch = new Stopwatch();
        string ID_String = (id < 0) ? ("") : (" WITH " + id);
        for (int k = 0; k < _numSearches; k++)
        {
            Console.Write(">");            
            search_watch.Start();
            bool finished = model.Run(SEED, xnode.Get("limit", 0));
            search_watch.Stop();
            if (finished)
            {
                Console.WriteLine("DONE" + ID_String);

                if (_writeImages)
                {
                    model.Graphics().Save($"{counter} {Name} {screenshotNumber}.png");
                    if (model is SimpleTiledModel && xnode.Get("textOutput", false))
                    {
                        File.WriteAllText($"{counter} {Name} {screenshotNumber}.txt", (model as SimpleTiledModel).TextOutput());
                    }
                }
                break;
            }
            Console.WriteLine("CONTRADICTION" + ID_String);
            //model.prop_watch.Reset();
        }

        search_time = search_watch;
        propagate_time = model.prop_watch;
    }    
}

internal class SequentialSearch : Search
{
    private SequentialSearch(bool writeImages, int numTries, int numWorkers, int scaleFactor, int seed, int maxDegreeParallelism, XmlNode xmlNode, int ct, int screenshotNum)
    {
        _writeImages = writeImages;
        _numSearches = numTries;
        num_workers = numWorkers;
        SCALE_FACTOR = scaleFactor;
        SEED = seed;
        _maxParallelism = maxDegreeParallelism;
        xnode = xmlNode;
        counter = ct;
        screenshotNumber = screenshotNum;
    }

    public override void Run()
    {
        RunSequential(false, false);
    }

    public static Search Construct(bool writeImages, int numTries, int numWorkers, int scaleFactor, int seed, int maxDegreeParallelism, XmlNode xmlNode, int counter, int screenshotNumber, string imageName)
    {
        var s = new SequentialSearch(writeImages, numTries, numWorkers, scaleFactor, seed, maxDegreeParallelism, xmlNode, counter, screenshotNumber);
        s.name = imageName;
        return s;
    }
}

internal class ParallelSearch : Search
{
    public ParallelSearch(bool writeImages, int numTries, int numWorkers, int scaleFactor, int seed, int maxDegreeParallelism, XmlNode xmlNode, int iD, int screenshotNum)
    {
        SCALE_FACTOR = scaleFactor;
        _maxParallelism = maxDegreeParallelism;
        SEED = seed;
        screenshotNumber = screenshotNum;
        num_workers = numWorkers;
        _numSearches = numTries;
        _writeImages = writeImages;
        xnode = xmlNode;
    }

    public override void Run()
    {
        RunParallel(false, false);
    }

    public static Search Construct(bool writeImages, int numTries, int numWorkers, int scaleFactor, int seed, int maxDegreeParallelism, XmlNode xmlNode, int ID, int screenshotNumber, string imageName)
    {
        var ps = new ParallelSearch(writeImages, numTries,  numWorkers,  scaleFactor,  seed,  maxDegreeParallelism, xmlNode, ID,  screenshotNumber);
        ps.name = imageName;
        return ps;
    }
}

internal class ParallelPropagate : Search
{
    private ParallelPropagate(bool writeImages, int numTries, int numWorkers, int scaleFactor, int seed, int maxDegreeParallelism, XmlNode xmlNode, int iD, int screenshotNum)
    {
        SCALE_FACTOR = scaleFactor;
        _maxParallelism = maxDegreeParallelism;
        SEED = seed;
        screenshotNumber = screenshotNum;
        num_workers = numWorkers;
        _numSearches = numTries;
        _writeImages = writeImages;
        xnode = xmlNode;
    }

    public override void Run()
    {
        RunSequential(true, false);
    }

    public static Search Construct(bool writeImages, int numTries, int numWorkers, int scaleFactor, int seed, int maxDegreeParallelism, XmlNode xmlNode, int ID, int screenshotNumber, string imageName)
    {

        var pp = new ParallelPropagate(writeImages, numTries, numWorkers, scaleFactor, seed, maxDegreeParallelism, xmlNode, ID, screenshotNumber);
        pp.name = imageName;
        return pp;
    }
}

internal class ParallelObserve : Search
{
    private ParallelObserve(bool writeImages, int numTries, int numWorkers, int scaleFactor, int seed, int maxDegreeParallelism, XmlNode xmlNode, int iD, int screenshotNum)
    {
        throw new NotImplementedException();
    }

    public override void Run()
    {
        throw new NotImplementedException();
/*
        RunSequential(false, true);
*/
    }

    public static Search Construct(bool writeImages, int numTries, int numWorkers, int scaleFactor, int seed, int maxDegreeParallelism, XmlNode xmlNode, int ID, int screenshotNumber, string imageName)
    {
        var po = new ParallelObserve(writeImages, numTries, numWorkers, scaleFactor, seed, maxDegreeParallelism, xmlNode, ID, screenshotNumber);
        po.name = imageName;
        return po;
    }
}






