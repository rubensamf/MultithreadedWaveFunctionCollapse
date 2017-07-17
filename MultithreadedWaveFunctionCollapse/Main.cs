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
    public enum DisplayResults { LineByLine, ByImage, ByRun, AllAtOnce }

    private const DisplayResults displayMode = DisplayResults.LineByLine;
    private const int SEED = 1000, MAX_DEGREE_PARALLELISM = 2, NUM_WORKERS = 2, NUM_TRIES_MAIN_LOOP = 10, NUMBER_OF_RUNS = 100;
    private const bool WRITE_IMAGES = true, DISPLAY_RUNTIME = true, REPORT_RESULTS = true;
    private const string RESULTS_FILE_NAME = "ScenarioResult", FILE_TYPE = ".csv";

    private static StringBuilder outputToDisplay;
    private static string MODE;
    private static Random random;
    private static XmlDocument xdoc;

    private static void Initialize(string[] args)
    {
        if (args.Length > 2)
        {
            Console.WriteLine("Usage: \n\nMultithreadedWaveFunctionCollapse\n\nOR\n\nMultithreadedWaveFunctionCollapse mode filename");
            Environment.Exit(exitCode: 24);
        }

        MODE = args.Length > 0 ? args[0] : "sequential-main";
        random = new Random(SEED);
        xdoc = new XmlDocument();
        xdoc.Load("samples.xml");
    }

    static void Main(string[] args)
    {
        Initialize(args);

        ulong runtimes = 0;
        for (int runNumber = 0; runNumber < NUMBER_OF_RUNS; runNumber++)
        {
            runtimes += (ulong) Execute(Stopwatch.StartNew());
        }
        if (DISPLAY_RUNTIME)
        {
            Console.WriteLine("===================================");
            Console.WriteLine(MODE + " average runtime after " + NUMBER_OF_RUNS + " run(s): " + (runtimes / NUMBER_OF_RUNS) + " ms");
            Console.WriteLine("===================================");
        }
    }

    private static long Execute(Stopwatch timer)
    {
        outputToDisplay = new StringBuilder();
        int counter = 1;
        foreach (XmlNode xnode in xdoc.FirstChild.ChildNodes)
        {
            if (xnode.Name == "#comment")
            {
                continue;
            }

            var name = xnode.Get<string>("name");
            Console.WriteLine($"< {name}");
            //outputToDisplay.AppendLine($"< {name}");

           
            Run(counter, xnode, name);
            counter++;
        }
        //Display();
        return timer.ElapsedMilliseconds;
    }

    private static void Run(int counter, XmlNode xnode, string name)
    {        
        for (int screenshotNumber = 0; screenshotNumber < xnode.Get("screenshots", 2); screenshotNumber++)
        {
            switch (MODE)
            {
                case "parallel-main":
                    ParallelMain(xnode, name, counter, screenshotNumber);
                    break;
                case "parallel-propagate":
                    ParallelPropagate(xnode, name, counter, screenshotNumber);
                    break;
                case "parallel-observe":
                    ParallelObserve(xnode, name, counter, screenshotNumber);
                    break;
                default:
                    SequentialMain(xnode, name, counter, screenshotNumber);
                    break;
            }
        }
    }

    private static void Display()
    {
        if (displayMode == DisplayResults.AllAtOnce)
        {
            Display(new StringBuilder());
        }        
    }

    private static void Display(StringBuilder output)
    {      
        switch (displayMode)
        {            
            case DisplayResults.ByImage:                
                break;
            
            case DisplayResults.ByRun:                
                break;

            case DisplayResults.AllAtOnce:
                Console.WriteLine(outputToDisplay);
                break;

            default:
                Console.WriteLine(output);
                break;
        }
    }

    private static void ParallelPropagate(XmlNode xnode,  string name, int counter, int screenshotNumber)
    {
        Model model;
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
                return;
        }

        model.isParallelPropagate = true;
        model.isParallelObserve = false;
        SequentialMain(xnode, name, counter, screenshotNumber);
    }

    private static void ParallelObserve(XmlNode xnode,  string name, int counter, int screenshotNumber)
    {
        Model model;
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
                return;
        }

        model.maxParallelism = MAX_DEGREE_PARALLELISM;
        model.isParallelPropagate = false;
        model.isParallelObserve = true;
        throw new NotImplementedException();
        SequentialMain(xnode, name, counter, screenshotNumber);
    }

    private static void SequentialMain(XmlNode xnode, string name, int counter, int screenshotNumber)
    {

        Model model;
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
                return;
        }

        model.isParallelPropagate = false;
        model.isParallelObserve = false;
        for (int k = 0; k < NUM_TRIES_MAIN_LOOP; k++)
        {
            Console.Write(">");
            //outputToDisplay.Append(">");
            int seed = random.Next();
            bool finished = model.Run(seed, xnode.Get("limit", 0));
            if (finished)
            {
                Console.WriteLine("DONE");
                //outputToDisplay.AppendLine("DONE");
                if (WRITE_IMAGES)
                {
                    model.Graphics().Save($"{counter} {name} {screenshotNumber}.png");
                    if (model is SimpleTiledModel && xnode.Get("textOutput", false))
                    {
                        File.WriteAllText($"{counter} {name} {screenshotNumber}.txt",
                            (model as SimpleTiledModel).TextOutput());
                    }
                }

                break;
            }
            Console.WriteLine("CONTRADICTION");
            //outputToDisplay.AppendLine("CONTRADICTION");
        }
    }

    private static void ParallelMain(XmlNode xnode, string name, int counter, int screenshotNumber)
    {
        Model model;
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
                return;
        }

        model.isParallelPropagate = false;
        model.isParallelObserve = false;
        Task[] tasks = new Task[NUM_WORKERS];
        CancellationTokenSource source = new CancellationTokenSource();
        CancellationToken token = source.Token;

        for (int i = 0; i < NUM_WORKERS; i++)
        {
            int id = i;
            tasks[i] = Task.Run(
                () =>
                {
                    Compute(xnode, name, counter, screenshotNumber, id);
                    source.Cancel();
                });
        }
        Task.WaitAny(tasks);
    }
  
    private static void Compute(XmlNode xnode, string name, int counter, int screenshotNumber, int id)
    {
        Model model;
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
                return;
        }
        for (int k = 0; k < NUM_TRIES_MAIN_LOOP; k++)
        {
            Console.Write(">");
            //outputToDisplay.Append(">");
            int seed = random.Next();
            bool finished = model.Run(seed, xnode.Get("limit", 0));
            if (finished)
            {
                Console.WriteLine("DONE WITH " + id);
                //outputToDisplay.AppendLine("DONE WITH " + id);

                if (WRITE_IMAGES)
                {
                    model.Graphics().Save($"{counter} {name} {screenshotNumber}.png");
                    if (model is SimpleTiledModel && xnode.Get("textOutput", false))
                    {
                        File.WriteAllText($"{counter} {name} {screenshotNumber}.txt", (model as SimpleTiledModel).TextOutput());
                    }
                }

                break;
            }
            Console.WriteLine("CONTRADICTION with " + id);
            //outputToDisplay.AppendLine("CONTRADICTION with " + id);
        }
    }
}
