using System;
using System.Management;
using System.Reflection;
using MemoryCompression;

internal static class AdversarialTests
{
    private static int _checks;
    private static int _failures;

    private static void Check(string command, bool expected, bool server = true, string processName = "node.exe")
    {
        using (ManagementClass type = new ManagementClass("Win32_Process"))
        using (ManagementObject process = type.CreateInstance())
        {
            // These are in-memory process facts. Do not create or terminate OS processes.
            process["Name"] = processName;
            process["CommandLine"] = command;
            MethodInfo method = typeof(PreviewProcesses).GetMethod("IsPreview", BindingFlags.NonPublic | BindingFlags.Static);
            bool actual = (bool)method.Invoke(null, new object[] { process, server });
            _checks++;
            if (actual != expected)
            {
                _failures++;
                Console.WriteLine("FAIL classification: expected=" + expected + " actual=" + actual + " command=" + command);
            }
        }
    }

    private static int Main()
    {
        Check(@"node.exe D:\app\plain-api.cjs", false);
        Check(@"node.exe D:\app\plain-api.cjs --label=node_modules/next/dist/bin/next", false);
        Check("node.exe -e \"/* node_modules/next/dist/bin/next */ require('http').createServer().listen(12345)\"", false);
        Check("node.exe --eval=\"/* node_modules/next/dist/bin/next */\"", false);
        Check(@"node.exe -p node_modules/next/dist/bin/next", false);
        Check(@"node.exe --require node_modules/next/dist/bin/next D:\app\plain-api.cjs", false);
        Check(@"node.exe D:\app\plain-api.cjs --label=node_modules/vite/bin/vite.js", false);
        Check(@"node.exe D:\app\plain-api.cjs --label=node_modules/astro/astro.js", false);
        Check(@"node.exe D:\app\plain-api.cjs --label=npm-cli.js run dev", false, false);
        Check(@"node.exe D:\app\node_modules\next\dist\bin\next-api.cjs", false);
        Check(@"node.exe D:\app\node_modules\next\dist\server\lib\start-server.js.backup", false);
        Check(@"node.exe D:\app\notnode_modules\next\dist\bin\next", false);
        Check(@"node.exe D:\app\node_modules\next\dist\bin\next build", false);
        Check(@"node.exe D:\app\node_modules\vite\bin\vite.js build", false);
        Check(@"node.exe D:\app\node_modules\astro\astro.js build", false);
        Check(@"node.exe D:\app\node_modules\next\dist\bin\next dev", true);
        Check(@"node.exe D:\app\node_modules\next\dist\bin\next start -p 3001", true);
        Check(@"node.exe D:\app\node_modules\next\dist\bin\next", true);
        Check(@"node.exe D:\app\node_modules\next\dist\server\lib\start-server.js", true);
        Check(@"node.exe D:\app\node_modules\.bin\..\next\dist\bin\next dev", true);
        Check("\"D:\\Program Files\\nodejs\\node.exe\" \"D:\\project with spaces\\node_modules\\next\\dist\\bin\\next\" dev", true);
        Check(@"node.exe --max-old-space-size=2048 D:\app\node_modules\next\dist\bin\next dev", true);
        Check(@"node.exe --require D:\app\preload.cjs D:\app\node_modules\next\dist\bin\next dev", true);
        Check(@"node.exe --inspect=127.0.0.1:9230 D:\app\node_modules\next\dist\bin\next dev", true);
        Check(@"node.exe -- D:\app\node_modules\next\dist\bin\next dev", true);
        Check(@"node.exe --unknown-option value D:\app\node_modules\next\dist\bin\next dev", false);
        Check(@"node.exe D:\app\node_modules\vite\bin\vite.js", true);
        Check(@"node.exe D:\app\node_modules\vite\bin\vite.js preview --port 3001", true);
        Check(@"node.exe D:\app\node_modules\astro\astro.js dev", true);
        Check(@"node.exe D:\app\node_modules\astro\astro.js preview", true);
        Check(@"node.exe D:\app\node_modules\npm\bin\npm-cli.js run dev", false);
        Check(@"node.exe D:\app\node_modules\npm\bin\npm-cli.js run dev", true, false);
        Check(@"node.exe D:\app\node_modules\npm\bin\npm-cli.js run build", false, false);
        Check("cmd.exe /c \"echo next dev\"", false, false, "cmd.exe");
        Check("cmd.exe /c \"unrelated-task.exe --label vite\"", false, false, "cmd.exe");
        Check(@"node.exe D:\app\node_modules\vite\bin\vite.js --mode staging build", false);
        Check(@"node.exe D:\app\node_modules\next\dist\bin\next --help", false);
        Check(@"node.exe --test D:\app\node_modules\next\dist\bin\next", false);
        Check(@"node.exe --require", false);
        Check(@"node.exe --require -- node_modules/next/dist/bin/next dev", false);
        Check(@"node.exe --require=D:\app\preload.cjs node_modules/next/dist/bin/next dev", true);
        Check(@"node.exe --no-warnings node_modules/next/dist/bin/next dev", true);
        Check(@"node.exe .\node_modules\.bin\..\next\dist\bin\next dev", true);
        Check(@"node.exe D:\app\node_modules\next\dist\bin\..\..\..\..\plain-api.cjs", false);
        Check(@"node.exe -- node_modules/next/dist/bin/next build", false);
        Check("", false);
        Check("node.exe", false);
        Check(@"node.exe node_modules/vite/bin/vite.js serve", true);
        Check(@"node.exe node_modules/vite/bin/vite.js dev", true);
        Check(@"node.exe node_modules/astro/astro.js check", false);
        Console.WriteLine((_failures == 0 ? "PASS" : "FAIL") + " adversarial: " + _checks + " cases, " + _failures + " failures.");
        return _failures == 0 ? 0 : 1;
    }
}
