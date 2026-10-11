using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

// Minimal NUnit-attribute runner: [TestFixture]/public classes, [SetUp], [TearDown], [Test], [TestCase(...)].
// Evidence harness only (the Unity Test Runner is the reference). Lives in a `~` folder: Unity and compile_check ignore it.
// Copied from Features/Money/Tests~/Runner.cs so the audio lane does not depend on another lane's folder.
public static class Runner
{
    public static int Main(string[] args)
    {
        var asm = Assembly.LoadFrom(args[0]);
        int pass = 0, fail = 0;
        foreach (var type in asm.GetTypes().Where(t => t.IsClass && !t.IsAbstract && t.IsPublic).OrderBy(t => t.Name))
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            MethodInfo Find(string attr) => methods.FirstOrDefault(m => m.GetCustomAttributes().Any(a => a.GetType().Name == attr));
            var setUp = Find("SetUpAttribute");
            var tearDown = Find("TearDownAttribute");
            foreach (var m in methods.OrderBy(m => m.Name))
            {
                var attrs = m.GetCustomAttributes().ToList();
                var sets = attrs.Where(a => a.GetType().Name == "TestCaseAttribute")
                    .Select(a => (object[])a.GetType().GetProperty("Arguments").GetValue(a)).ToList();
                if (attrs.Any(a => a.GetType().Name == "TestAttribute")) sets.Add(new object[0]);
                foreach (var raw in sets)
                {
                    var ps = m.GetParameters();
                    var call = new object[ps.Length];
                    for (int i = 0; i < ps.Length; i++)
                    {
                        var v = i < raw.Length ? raw[i] : null;
                        call[i] = v != null && v.GetType() != ps[i].ParameterType && v is IConvertible ? Convert.ChangeType(v, ps[i].ParameterType) : v;
                    }
                    var name = type.Name + "." + m.Name + (raw.Length > 0 ? "(" + string.Join(", ", raw.Select(x => x == null ? "null" : "\"" + x + "\"")) + ")" : "");
                    var inst = Activator.CreateInstance(type);
                    try
                    {
                        setUp?.Invoke(inst, null);
                        m.Invoke(inst, call);
                        pass++;
                    }
                    catch (TargetInvocationException e)
                    {
                        fail++;
                        Console.WriteLine("FAIL " + name + ": " + e.InnerException?.GetType().Name + ": " + e.InnerException?.Message);
                    }
                    finally
                    {
                        try { tearDown?.Invoke(inst, null); } catch (Exception) { }
                    }
                }
            }
        }
        Console.WriteLine($"PASSED {pass}  FAILED {fail}");
        return fail == 0 ? 0 : 1;
    }
}
