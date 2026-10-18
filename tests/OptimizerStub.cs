using System;
using System.IO;
using System.Threading;

internal static class OptimizerStub
{
    private static int Main(string[] args)
    {
        File.AppendAllText(args[0], "called\n");
        if (args.Length > 1) Thread.Sleep(int.Parse(args[1]));
        return args.Length > 2 ? int.Parse(args[2]) : 0;
    }
}
