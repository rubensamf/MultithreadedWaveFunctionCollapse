/*
The MIT License(MIT)
Copyright(c) mxgmn 2016.
Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
The software is provided "as is", without warranty of any kind, express or implied, including but not limited to the warranties of merchantability, fitness for a particular purpose and noninfringement. In no event shall the authors or copyright holders be liable for any claim, damages or other liability, whether in an action of contract, tort or otherwise, arising from, out of or in connection with the software or the use or other dealings in the software.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

static class Program
{
    private const int ERROR_BAD_LENGTH = 24, seed = 1000;
    private static bool isDefault;      
    private static string MODE, FILE_NAME, newLine;
    private static List<string> _lines;

    static void Main(string[] args)
    {
        if (args.Length > 2 || (args.Length == 1 && args[0] != "default"))
        {
            Console.WriteLine("Usage: \n\nMultithreadedWaveFunctionCollapse\n\nOR\n\nMultithreadedWaveFunctionCollapse mode filename");
            Environment.Exit(exitCode: ERROR_BAD_LENGTH);
        }
        else if (args.Length < 1 || (args.Length == 1 && args[0] == "default"))
        {
            isDefault = true;
        }
        else if (args.Length == 2)
        {
            MODE = args[0];
            FILE_NAME = args[1];
            _lines = new List<string>();
            isDefault = false;
        }
        newLine = Environment.NewLine;

        Random random = new Random(seed);
        var xdoc = new XmlDocument();
        xdoc.Load("samples.xml");

        int counter = 1;
        foreach (XmlNode xnode in xdoc.FirstChild.ChildNodes)
        {
            if (xnode.Name == "#comment")
            {
                continue;
            }

            Model model;
            string name = xnode.Get<string>("name");            
            Output($"< {name}" + newLine);

            if (xnode.Name == "overlapping")
            {
                model = new OverlappingModel(name, xnode.Get("N", 2), xnode.Get("width", 48), xnode.Get("height", 48),
                xnode.Get("periodicInput", true), xnode.Get("periodic", false), xnode.Get("symmetry", 8), xnode.Get("ground", 0));
            }
            else if (xnode.Name == "simpletiled")
            {
                model = new SimpleTiledModel(name, xnode.Get<string>("subset"),
                xnode.Get("width", 10), xnode.Get("height", 10), xnode.Get("periodic", false), xnode.Get("black", false));
            }
            else
            {
                continue;
            }

            for (int screenshotNumber = 0; screenshotNumber < xnode.Get("screenshots", 2); screenshotNumber++)
            {
                switch (MODE)
                {
                    case "sequential-main":
                        SequentialMain(random, model, xnode, counter, name, screenshotNumber);
                        break;
                    case "parallel-main":
                        ParallelMain(random, xnode, model, counter, name, screenshotNumber);
                        break;
                    case "parallel-propagate":
                        ParallelPropagate(random, model, xnode, counter, name, screenshotNumber);
                        break;
                    case "parallel-observe":
                        ParallelObserve(random, model, xnode, counter, name, screenshotNumber);
                        break;
                    default:
                        SequentialMain(random, model, xnode, counter, name, screenshotNumber);
                        break;
                }
            }
            counter++;
        }
        Write();
    }

    private static void Write()
    {
        if (!isDefault)
        {
            File.WriteAllLines(Path.GetFullPath(FILE_NAME), _lines);
        }        
    }

    private static void Output(string s)
    {
        if (isDefault)
        {
            Console.Write(s);
        }
        else
        {
            _lines.Add(s);
        }        
    }

    private static void ParallelPropagate(Random random, Model model, XmlNode xnode, int counter, string name, int screenshotNumber)
    {
        model.isParallelPropagate = true;
        model.isParallelObserve = false;
        SequentialMain(random, model, xnode, counter, name, screenshotNumber);
    }

    private static void ParallelObserve(Random random, Model model, XmlNode xnode, int counter, string name, int screenshotNumber)
    {
        model.isParallelPropagate = false;
        model.isParallelObserve = true;
        throw new NotImplementedException();
    }

    private static void SequentialMain(Random random, Model model, XmlNode xnode, int counter, string name, int screenshotNumber)
    {
        model.isParallelPropagate = false;
        model.isParallelObserve = false;
        for (int k = 0; k < 10; k++)
        {
            string outputString = ">";
            int seed = random.Next();
            bool finished = model.Run(seed, xnode.Get("limit", 0));
            if (finished)
            {
                outputString += "DONE" + newLine;
                Output(outputString);

                model.Graphics().Save($"{counter} {name} {screenshotNumber}.png");
                if (model is SimpleTiledModel && xnode.Get("textOutput", false))
                {
                    File.WriteAllText($"{counter} {name} {screenshotNumber}.txt", (model as SimpleTiledModel).TextOutput());
                }

                break;
            }
            outputString += "CONTRADICTION" + newLine;
            Output(outputString);
        }
    }

    private static void ParallelMain(Random rand, XmlNode xnode, Model model, int counter, string name, int screenshotNumber)
    {
        model.isParallelPropagate = false;
        model.isParallelObserve = false;
        int workers = 1;
        Task[] tasks = new Task[workers];
        CancellationTokenSource source = new CancellationTokenSource();
        CancellationToken token = source.Token;

        for (int i = 0; i < workers; i++)
        {
            int id = i;
            tasks[i] = Task.Run(
                () =>
                {
                    Compute(id, token, rand, model, xnode, counter, name, screenshotNumber);
                    source.Cancel();
                });
        }
        Task.WaitAny(tasks);
    }

    private static void Compute(int id, CancellationToken token, Random random, Model model, XmlNode xnode, int counter, string name, int screenshotNumber)
    {
        for (int k = 0; k < 10; k++)
        {
            string outputString = ">";
            int seed = random.Next();
            bool finished = model.Run(seed, xnode.Get("limit", 0));
            if (finished)
            {
                outputString += "DONE WITH " + id;
                Output(outputString);

                model.Graphics().Save($"{counter} {name} {screenshotNumber}.png");
                if (model is SimpleTiledModel && xnode.Get("textOutput", false))
                {
                    File.WriteAllText($"{counter} {name} {screenshotNumber}.txt", (model as SimpleTiledModel).TextOutput());
                }

                break;
            }
            outputString += "CONTRADICTION with " + id;
            Output(outputString);
        }
    }
}
