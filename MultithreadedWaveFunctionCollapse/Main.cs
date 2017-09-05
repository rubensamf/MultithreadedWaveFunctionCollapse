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
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using ComTypes = System.Runtime.InteropServices.ComTypes;

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
    private static Results _results;
    private static int NUM_SCREENSHOTS=1;

    static void Main(string[] args)
    {
        overall_runtime = Stopwatch.StartNew();
        
        // Command line arguments: bool_write_images int_seed int_number_of_trials int_number_of_tries scale_factor [execution_mode int_max_degree_of_parallelism int_number_of_workers] ...
        InitializeAndLoad(args);
        List<string[]> runtimes = new List<string[]>(num_trials);
        for (int i = 0; i < num_trials; i++)
        {
            seed = replicate_seeds[i];
            var trial = Execute();
            runtimes.Add(DisplayTime(trial, i));
        }
        overall_runtime.Stop();
        WriteResultTable(runtimes);
        WriteSummaryTable();
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
            string name = cur_execution_name + " " + cur_max_degree_parallelism + " " + cur_num_workers;                       
            var run = Run();            
            _results.Add(seed, name, run);
            AddToExecutions(executions, name, run);
        }

        return executions;
    }

    private static void AddToExecutions(Dictionary<string, WaveFunctions> executions, string execution_name, WaveFunctionCollapse run)
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
        var searches = new List<Searches>();
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

            int num_Screenshots = xnode.Get("screenshots", NUM_SCREENSHOTS);
            Search[] searches_by_screenshot = new Search[num_Screenshots];
            Stopwatch searches_timer = Stopwatch.StartNew();
            for (int screenshotNumber = 0; screenshotNumber < num_Screenshots; screenshotNumber++)
            {
                searches_by_screenshot[screenshotNumber] = WFC(xnode, counter, screenshotNumber, image_name);                
            }
            searches_timer.Stop();
            searches.Add(new Searches(searches_by_screenshot, image_name, searches_timer));
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
        searcher._cu = new ProcessorUsage();
        searcher.total_runtime_timer = Stopwatch.StartNew();        
        searcher.Run();
        searcher.total_runtime_timer.Stop();
        searcher.cpuUsage = searcher._cu.GetCurrentValue();
        return searcher;
    }

    private static void InitializeAndLoad(string[] args)
    {
        _results = new Results();
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
        ulong total_runtime = 0, total_searchtime = 0, total_proptime = 0, total_observetime = 0;        
        var lines = new List<string>(execution_names.Length);
        int replicateSeed = replicate_seeds[run_num];

        foreach (var execution in executions)
        {
            Console.WriteLine();
            Console.WriteLine("Strategy: " + execution.Key);
            Console.WriteLine();
            foreach (var run in execution.Value.waves)
            {
                foreach (var search in run.searches)
                {                    
                    int screenshot_num = 0;
                    string img_name = "";                    
                    foreach (var screenshot in search.screenshots)
                    {
                        if(img_name != screenshot.Name)
                            Console.WriteLine("\n" + screenshot.Name);
                        // Trying to make work with Excel better
                        int xyz = 0;
                        var search_time = string.Format("{0}:{1}", Math.Floor(screenshot.search_time.TotalMinutes), screenshot.search_time.ToString("ss\\.ff"));
                        var obs_time = string.Format("{0}:{1}", Math.Floor(screenshot.observation_time.TotalMinutes), screenshot.observation_time.ToString("ss\\.ff"));
                        var prop_time = string.Format("{0}:{1}", Math.Floor(screenshot.propagation_time.TotalMinutes), screenshot.propagation_time.ToString("ss\\.ff"));
                        Console.WriteLine("\tScreenshot " + screenshot_num + " Search time: " + search_time + " Observation time: " + obs_time + " Propagation time: " + prop_time);
                        //string cpuUsage = screenshot.cpuUsage + "";
                        string cpuUsage = "";
                        lines.Add(run_num + ", " + run.name + ", " + screenshot.name + ", " + SCALE_FACTOR + ", " + base_seed + ", " + replicateSeed + ", " + screenshot._maxParallelism + ", " +
                            screenshot.num_workers + ", " + num_tries + ", " + num_trials + ", " + screenshot_num + ", " + obs_time + ", " + prop_time + ", " + search_time + "," + cpuUsage);
                        total_searchtime += (ulong) screenshot.search_time.TotalMilliseconds;
                        total_proptime += (ulong) screenshot.propagation_time.TotalMilliseconds;                       
                        total_observetime += (ulong)screenshot.observation_time.TotalMilliseconds;
                        screenshot_num++;
                        img_name = screenshot.Name;
                    }
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine("Run #" + run_num + " Observationtime: "+ "ms Proptime: " + total_proptime + " ms Searchtime: " + total_searchtime + " ms");

        return lines.ToArray();
    }

    private static void WriteResultTable(List<string[]> runs)
    {
        Console.WriteLine("\nOverall Runtime: " + overall_runtime.Elapsed);
        StringBuilder sb = new StringBuilder("Run Number, Run Name, Image Name, Scale Factor, Base Seed, Replicate Seed, Max Degree of Parallelism, Number of Workers, Number of Tries, Number of Trials, Screenshot Number, Observation Time, Propagate Time, Search Time, CPU Usage\n");

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

    private static void WriteSummaryTable()
    {
        StringBuilder sb = new StringBuilder(" , , ,Wall-clock time to result, , Time in search, , , Time in propagation phase, , , Time in observation phase\n" +
                                             "Strategy, Degree of Parallelism, Number of Trials, Mean (ms), Stdev (ms), Mean (ms), Time per Thread (ms), Stdev (ms),  Mean (ms), Time per Thread (ms), Stdev (ms), Mean (ms), Time per Thread (ms), Stdev (ms)\n");
        foreach (var result in _results.Get())
        {
            var r = result.Value;
            var seperator = new[] {' '};
            var split = result.Key.Split(seperator);
            string name = split[0];
            int max_parallel = Convert.ToInt32(split[1]);
            int n_workers = Convert.ToInt32(split[2]);
            var degree_parallelism = max_parallel > n_workers ? max_parallel : n_workers;
            var search_time_per_thread = r.GetMeanSearchTime() / degree_parallelism;
            var prop_time_per_thread = r.GetMeanPropTime() / degree_parallelism;
            var obs_time_per_thread = r.GetMeanObsTime() / degree_parallelism;          
            var search_time_thread_result = name == "parallel-main" ? (search_time_per_thread +"") : "";
            var prop_time_thread_result = name == "parallel-main" ? (prop_time_per_thread + "") : "";
            var obs_time_thread_result = name == "parallel-main" ? (obs_time_per_thread + "") : "";
            sb.Append(name + ", " + degree_parallelism + ", " + num_trials + ", " + r.GetMeanWallClockTime() + ", " + r.GetStandardDeviationWallClockTime() + ", " + r.GetMeanSearchTime() + ", " + search_time_thread_result +  ", " + r.GetStandardDeviationSearchTime() + ", " + r.GetMeanPropTime() + ", " + prop_time_thread_result + ", " + + r.GetStandardDeviationPropTime() + ", " + r.GetMeanObsTime() + ", " + obs_time_thread_result + ", " + + r.GetStandardDeviationObsTime() + "\n");           
        }

        string path = Directory.GetCurrentDirectory() + "\\" + "MultithreadedWaveFunctionCollapseSummary.csv";
        System.IO.File.WriteAllText(path, sb.ToString());
    }

    private static void WaitForEscKey()
    {
        Console.WriteLine("\nPress ESC to stop");
        do { }
        while (Console.ReadKey(true).Key != ConsoleKey.Escape);
    }
}

internal class Results
{
    private Dictionary<string, Result> results;

    public Results()
    {
        results = new Dictionary<string, Result>();
    }

    public void Add(int seed, string name, WaveFunctionCollapse run)
    {
        Result r;
        if (!results.TryGetValue(name, out r))
        {
            r = new Result();
            r.Add(seed, run);
            results.Add(name, r);
        }
        r.Add(seed, run);
    }

    public Dictionary<string, Result> Get()
    {
        return results;
    }
}

internal class Result
{
    private Dictionary<int, List<WaveFunctionCollapse>> waves;
    private List<double> wall_clock_times, prop_times, search_times, obs_times;

    private double mean_wall_clock_time, stdev_wall_clock_time, mean_prop_time, stdev_prop_time, mean_search_time, stdev_search_time, mean_obs_time, stdev_obs_time, total_cpu_usage, mean_cpu_usage;
    private TimeSpan total_wall_clock_time, total_prop_time, total_search_time, total_obs_time;
    private int count;
    private bool CPU_FLAG;

    public Result()
    {
        waves = new Dictionary<int, List<WaveFunctionCollapse>>();
        wall_clock_times = null;
        prop_times = null;
        search_times = null;
        obs_times = null;
        mean_wall_clock_time = -1;
        stdev_wall_clock_time = -1;
        mean_prop_time = -1;
        stdev_prop_time = -1;
        mean_search_time = -1;
        stdev_search_time = -1;
        mean_obs_time = -1;
        stdev_obs_time = -1;
        total_wall_clock_time = TimeSpan.Zero;
        total_prop_time = TimeSpan.Zero;
        total_search_time = TimeSpan.Zero;
        total_obs_time = TimeSpan.Zero;
        total_cpu_usage = 0;
        mean_cpu_usage = -1;
        CPU_FLAG = false;
        count = -1;
    }

    public void Add(int key, WaveFunctionCollapse val)
    {
        List<WaveFunctionCollapse> l;
        if (waves.TryGetValue(key, out l))
        {
            l.Add(val);
        }
        else
        {
            l = new List<WaveFunctionCollapse>();
            l.Add(val);
            waves.Add(key, l);
        }            
    }

    public Dictionary<int, List<WaveFunctionCollapse>> GetWaves()
    {
        return waves;
    }

    public int GetCount()
    {
        if (count < 0)
        {
            ComputeCount();
        }
        return count;
    }

    private double GetTotalCPUUsage()
    {
        if (!CPU_FLAG)
        {
            ComputeTotalCPUUsage();
        }
        return total_cpu_usage;
    }

    public TimeSpan GetTotalWallClockTime()
    {
        if (total_wall_clock_time == TimeSpan.Zero)
        {
            ComputeTotalWallClockTime();
        }

        return total_wall_clock_time;
    }

    public TimeSpan GetTotalPropTime()
    {
        if (total_prop_time == TimeSpan.Zero)
        {
            ComputePropTime();
        }

        return total_prop_time;
    }

    public TimeSpan GetTotalSearchTime()
    {
        if (total_search_time == TimeSpan.Zero)
        {
            ComputeSearchTime();
        }
        return total_search_time;
    }

    public TimeSpan GetTotalObservationTime()
    {
        if(total_obs_time == TimeSpan.Zero)
        {
            ComputeObservationTime();
        }

        return total_obs_time;
    }

    public double GetMeanCPUUsage()
    {
        if (!CPU_FLAG)
        {
            ComputeMeanCPUUsage();
        }
        return mean_cpu_usage;
    }

    public double GetMeanWallClockTime()
    {
        if (mean_wall_clock_time < 0)
        {
            ComputeMeanWallClockTime();
        }

        return mean_wall_clock_time;        
    }

    public double GetMeanPropTime()
    {
        if (mean_prop_time < 0)
        {
            ComputeMeanPropTime();
        }

        return mean_prop_time;
    }

    public double GetMeanSearchTime()
    {
        if (mean_search_time < 0)
        {
            ComputeMeanSearchTime();
        }
        return mean_search_time;
    }

    public double GetMeanObsTime()
    {
        if (mean_obs_time < 0)
        {
            ComputeMeanObsTime();
        }
        return mean_obs_time;
    }

    public double GetStandardDeviationWallClockTime()
    {
        if (stdev_wall_clock_time < 0)
        {
            ComputeStdDevWallClockTime();
        }
        return stdev_wall_clock_time;
    }

    public double GetStandardDeviationPropTime()
    {
        if (stdev_prop_time < 0)
        {
            ComputeStdDevPropTime();
        }
        return stdev_prop_time;
    }

    public double GetStandardDeviationSearchTime()
    {
        if (stdev_search_time < 0)
        {
            ComputeStdDevSearchTime();
        }
        return stdev_search_time;
    }

    public double GetStandardDeviationObsTime()
    {
        if (stdev_obs_time < 0)
        {
            ComputeStdDevObsTime();
        }
        return stdev_obs_time;
    }

    private List<double> GetWallClockTimes()
    {
        if (wall_clock_times == null)
        {
            FindWallClockTimes();
        }
        return wall_clock_times;
    }

    private void FindWallClockTimes()
    {
        if (wall_clock_times == null)
        {
            wall_clock_times = new List<double>();
        }
        foreach (var waveList in waves.Values)
        {
            foreach (var wave in waveList)
            {
                foreach (var search in wave.searches)
                {
                    foreach (var screenshot in search.screenshots)
                    {
                        wall_clock_times.Add(screenshot.total_runtime_timer.Elapsed.TotalMilliseconds);
                    }
                }
            }
        }
    } 

    private List<double> GetPropTimes()
    {
        if (prop_times == null)
        {
            FindPropTimes();
        }
        return prop_times;
    }

    private void FindPropTimes()
    {
        if (prop_times == null)
        {
            prop_times = new List<double>();
        }
        foreach (var waveList in waves.Values)
        {
            foreach (var wave in waveList)
            {
                foreach (var search in wave.searches)
                {
                    foreach (var screenshot in search.screenshots)
                    {
                        prop_times.Add(screenshot.propagation_time.TotalMilliseconds);
                    }
                }
            }
        }
    }

    public List<double> GetSearchTimes()
    {
        if (search_times == null)
        {
            FindSearchTimes();
        }
        return search_times;
    }

    private void FindSearchTimes()
    {
        if (search_times == null)
        {
            search_times = new List<double>();
        }
        foreach (var waveList in waves.Values)
        {
            foreach (var wave in waveList)
            {
                foreach (var search in wave.searches)
                {
                    foreach (var screenshot in search.screenshots)
                    {
                        search_times.Add(screenshot.search_time.TotalMilliseconds);
                    }
                }
            }
        }
    }

    private List<double> GetObsTimes()
    {
        if (obs_times == null)
        {
            FindObsTimes();
        }
        return obs_times;
    }

    private void FindObsTimes()
    {
        if (obs_times == null)
        {
            obs_times = new List<double>();
        }
        foreach (var waveList in waves.Values)
        {
            foreach (var wave in waveList)
            {
                foreach (var search in wave.searches)
                {
                    foreach (var screenshot in search.screenshots)
                    {
                        obs_times.Add(screenshot.observation_time.TotalMilliseconds);
                    }
                }
            }
        }
    }

    private void ComputeCount()
    {
        count = 0;
        foreach (var waveList in waves.Values)
        {
            foreach (var wave in waveList)
            {
                foreach (var waveSearch in wave.searches)
                {
                    foreach (var waveSearchScreenshot in waveSearch.screenshots)
                    {
                        count++;
                    }
                }
            }
        }
    }

    private void ComputeTotalCPUUsage()
    {
        if (CPU_FLAG)
            return;

        total_cpu_usage = 0;
        foreach (var waveList in waves.Values)
        {
            foreach (var wave in waveList)
            {
                foreach (var search in wave.searches)
                {
                    foreach (var screenshot in search.screenshots)
                    {
                        total_cpu_usage += screenshot.cpuUsage;
                    }
                }
            }
        }
        CPU_FLAG = true;
    }

    private void ComputeMeanCPUUsage()
    {
        mean_cpu_usage = GetTotalCPUUsage() / GetCount();
    }

    private void ComputeTotalWallClockTime()
    {
        foreach (var waveList in waves.Values)
        {
            foreach (var wave in waveList)
            {
                foreach (var waveSearch in wave.searches)
                {
                    foreach (var waveSearchScreenshot in waveSearch.screenshots)
                    {
                        total_wall_clock_time += waveSearchScreenshot.total_runtime_timer.Elapsed;
                    }
                }
            }
        }
    }

    private void ComputeSearchTime()
    {
        foreach (var waveList in waves.Values)
        {
            foreach (var wave in waveList)
            {
                foreach (var waveSearch in wave.searches)
                {
                    foreach (var waveSearchScreenshot in waveSearch.screenshots)
                    {
                        total_search_time += waveSearchScreenshot.search_time;
                    }
                }
            }
        }
    }

    private void ComputePropTime()
    {
        foreach (var waveList in waves.Values)
        {
            foreach (var wave in waveList)
            {
                foreach (var waveSearch in wave.searches)
                {
                    foreach (var waveSearchScreenshot in waveSearch.screenshots)
                    {
                        total_prop_time += waveSearchScreenshot.propagation_time;
                    }
                }
            }
        }
    }

    private void ComputeObservationTime()
    {
        foreach (var waveList in waves.Values)
        {
            foreach (var wave in waveList)
            {
                foreach (var waveSearch in wave.searches)
                {
                    foreach (var waveSearchScreenshot in waveSearch.screenshots)
                    {
                        total_obs_time += waveSearchScreenshot.observation_time;
                    }
                }
            }
        }
    }

    private void ComputeMeanWallClockTime()
    {
        mean_wall_clock_time = GetTotalWallClockTime().TotalMilliseconds / GetCount();
    }

    private void ComputeMeanSearchTime()
    {
        mean_search_time = GetTotalSearchTime().TotalMilliseconds / GetCount();
    }

    private void ComputeMeanPropTime()
    {
        mean_prop_time = GetTotalPropTime().TotalMilliseconds / GetCount();
    }

    private void ComputeMeanObsTime()
    {
        mean_obs_time = GetTotalObservationTime().TotalMilliseconds / GetCount();
    }

    private void ComputeStdDevWallClockTime()
    {        
        stdev_wall_clock_time = ComputeStdDev(GetWallClockTimes());
    }

    private void ComputeStdDevSearchTime()
    {
        stdev_search_time = ComputeStdDev(GetSearchTimes());
    }

    private void ComputeStdDevPropTime()
    {
        stdev_prop_time = ComputeStdDev(GetPropTimes());
    }

    private void ComputeStdDevObsTime()
    {
        stdev_obs_time = ComputeStdDev(GetObsTimes());
    }

    //Adapted from Calculate Standard Deviation of Double Variables in C# by Victor Chen
    private double ComputeStdDev(List<double> collection)
    {
        //var collection = watches.ToDictionary(x => x.Key, x => x.Value.TotalMilliseconds).Values.ToList();
        double ave = collection.Average();
        double sumOfDerivation = 0;
        foreach (var time in collection)
        {
            sumOfDerivation += (time) * (time);
        }

        double sumOfDerivationAverage = sumOfDerivation / (collection.Count - 1);
        return Math.Sqrt(sumOfDerivationAverage - (ave * ave));
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
    public List<Searches> searches;

    public WaveFunctionCollapse(List<Searches> searches, string name, Stopwatch timer)
    {
        this.searches = searches;
        this.name = name;
        this.timer = timer;
    }    
}

internal class Searches
{
    public string name;
    public Stopwatch timer;
    public Search[] screenshots;

    public Searches(Search[] screenshots, string name, Stopwatch timer)
    {
        this.screenshots = screenshots;
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
    public TimeSpan search_time, propagation_time, observation_time;
    public Stopwatch total_runtime_timer;
    public ProcessorUsage _cu;
    public float cpuUsage;

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
        model.ob_watch = new Stopwatch();

        return model;
    }
    
    internal void RunSequential(bool parallel_propagate, bool parallel_observe)
    {
        Stopwatch search_watch, prop_watch, ob_watch;
        Model model = GetModel(xnode);
        model.isParallelPropagate = parallel_propagate;
        model.isParallelObserve = parallel_observe;
        Compute(model, 0, out search_watch, out prop_watch, out ob_watch);
        search_time = search_watch.Elapsed;
        propagation_time = prop_watch.Elapsed;
        observation_time = ob_watch.Elapsed;
    }  

    internal void RunParallel(bool parallel_propagate, bool parallel_observe)
    {        
        Task[] tasks = new Task[num_workers];
        CancellationTokenSource source = new CancellationTokenSource();
        CancellationToken token = source.Token;
        // create vars to accumulate propagate and search timez       
        TimeSpan search = TimeSpan.Zero, propagate = TimeSpan.Zero, observation = TimeSpan.Zero;
        for (int i = 0; i < num_workers; i++)
        {
            int id = i;
            tasks[i] = Task.Run(
                () =>
                {
                    Model model = GetModel(xnode);
                    model.isParallelPropagate = parallel_propagate;
                    model.isParallelObserve = parallel_observe;
                    model.token = token;
                    // Create Stopwatchez for search and for propagate here                   
                    Stopwatch search_watch, prop_watch, ob_watch;
                    // pazz those to compute
                    Compute(model, id, out search_watch, out prop_watch, out ob_watch); // create a new watch for each task
                    source.Cancel();
                    
                    // add the time for each 'watch to the accumulators
                    search += search_watch.Elapsed;
                    propagate += prop_watch.Elapsed;
                    observation += ob_watch.Elapsed;
                });
        }
        Task.WaitAll(tasks);
        search_time = search;
        propagation_time = propagate;
        observation_time = observation;
    }

    /**
     * Accumulates all search and propagation times
     */
    private void Compute(Model model, int id, out Stopwatch search_time, out Stopwatch propagate_time, out Stopwatch observation_time)
    {
        Stopwatch search_watch = new Stopwatch();
        string ID_String = (id < 0) ? ("") : (" WITH " + id);
        for (int k = 0; k < _numSearches; k++)
        {
            Console.Write(">");            
            search_watch.Start();
            bool finished = model.Run(SEED+id+101*k, xnode.Get("limit", 0));
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
        }

        search_time = search_watch;
        propagate_time = model.prop_watch;
        observation_time = model.ob_watch;
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

internal class ProcessorUsage
{
    const float sampleFrequencyMillis = 1000;

    protected object syncLock = new object();
    protected PerformanceCounter counter;
    protected float lastSample;
    protected DateTime lastSampleTime;

    /// <summary>
    /// 
    /// </summary>
    public ProcessorUsage()
    {
        this.counter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
        //PerformanceCounter theCPUCounter = new PerformanceCounter("Process", "% Processor Time", Process.GetCurrentProcess().ProcessName);
}

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    public float GetCurrentValue()
    {
        if ((DateTime.UtcNow - lastSampleTime).TotalMilliseconds > sampleFrequencyMillis)
        {
            lock (syncLock)
            {
                if ((DateTime.UtcNow - lastSampleTime).TotalMilliseconds > sampleFrequencyMillis)
                {
                    lastSample = counter.NextValue();
                    lastSampleTime = DateTime.UtcNow;
                }
            }
        }

        return lastSample;
    }
}