using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>
    /// C16: runs every other test in this assembly twice in one process, each pass in a different shuffled
    /// order, on fresh fixture instances. Any hidden shared state shows up as a failure in the second pass
    /// or under reordering. Tests tagged Slow are skipped here (they run on their own).
    /// </summary>
    public class SuiteHygieneTests
    {
        private struct Case { public Type Fixture; public MethodInfo Method; public object[] Args; public string Name; }

        private static List<Case> Discover()
        {
            var cases = new List<Case>();
            foreach (var t in typeof(SuiteHygieneTests).Assembly.GetTypes())
            {
                if (t == typeof(SuiteHygieneTests) || !t.IsClass || t.IsAbstract || t.GetConstructor(Type.EmptyTypes) == null) continue;
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.ReturnType != typeof(void)) continue; // [UnityTest] coroutines are not part of the shared suite
                    bool slow = false;
                    foreach (CategoryAttribute c in m.GetCustomAttributes(typeof(CategoryAttribute), true)) slow |= c.Name == "Slow";
                    if (slow) continue;
                    if (m.GetCustomAttributes(typeof(TestAttribute), true).Length > 0)
                        cases.Add(new Case { Fixture = t, Method = m, Args = new object[0], Name = t.Name + "." + m.Name });
                    foreach (TestCaseAttribute tc in m.GetCustomAttributes(typeof(TestCaseAttribute), true))
                        cases.Add(new Case { Fixture = t, Method = m, Args = tc.Arguments, Name = t.Name + "." + m.Name + "(" + string.Join(",", tc.Arguments) + ")" });
                }
            }
            cases.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return cases;
        }

        private static void InvokeAll(Type fixture, object instance, Type attribute)
        {
            foreach (var m in fixture.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                if (m.GetCustomAttributes(attribute, true).Length > 0) m.Invoke(instance, null);
        }

        [Test]
        public void Suite_PassesTwiceInShuffledOrder_InOneProcess()
        {
            var cases = Discover();
            Assert.That(cases.Count, Is.GreaterThan(40));
            var failures = new List<string>();
            Evidence.Suppressed = true;
            try
            {
                foreach (ulong seed in new ulong[] { 1, 0xC0FFEE })
                {
                    var order = new List<Case>(cases);
                    var rng = new SplitMix64(seed);
                    for (int i = order.Count - 1; i > 0; i--) { int j = rng.NextInt(i + 1); var tmp = order[i]; order[i] = order[j]; order[j] = tmp; }
                    foreach (var c in order)
                    {
                        object instance = Activator.CreateInstance(c.Fixture);
                        try
                        {
                            InvokeAll(c.Fixture, instance, typeof(SetUpAttribute));
                            try { c.Method.Invoke(instance, c.Args); }
                            finally { InvokeAll(c.Fixture, instance, typeof(TearDownAttribute)); }
                        }
                        catch (TargetInvocationException ex)
                        {
                            failures.Add("seed " + seed + " " + c.Name + ": " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message);
                        }
                    }
                }
            }
            finally { Evidence.Suppressed = false; }
            Evidence.Line("test-hygiene.txt", "shuffled double pass: cases=" + cases.Count + " seeds=1,0xC0FFEE failures=" + failures.Count);
            Assert.That(failures, Is.Empty);
        }
    }
}
