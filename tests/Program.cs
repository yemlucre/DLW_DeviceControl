using System;

// 独立测试入口：运行 DeviceControlPackageChecks 的全部检查（与上游 Agamemnon 仓库同一检查集）。
internal static class Program
{
    private static int _passed;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        _passed++; Console.WriteLine("PASS: " + name);
    }
    private static int Main(string[] args)
    {
        try { DeviceControlPackageChecks.Run(Check, args.Length > 0 ? args[0] : "."); }
        catch (Exception ex)
        {
            for (Exception current = ex; current != null; current = current.InnerException)
                Console.WriteLine(current.GetType().Name + ": " + current.Message + Environment.NewLine + current.StackTrace);
            return 1;
        }
        Console.WriteLine("Passed " + _passed + " checks; no hardware accessed.");
        return _passed == 0 ? 1 : 0;
    }
}
