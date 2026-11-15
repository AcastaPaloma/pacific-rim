using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>DEF-M1save-1: a participant whose Read throws must leave earlier participants unchanged and give a Corrupt result.</summary>
    public class SaveRestoreRollbackTests
    {
        private sealed class Counter : ISaveParticipant
        {
            public long Value; public bool Throw;
            private readonly string _name;
            public Counter(string name) { _name = name; }
            public string SectionName => _name;
            public int SectionVersion => 1;
            public void Write(SaveSection s) { s.Add("v").Set("n", SaveValue.Of(Value)); }
            public void Read(SaveSection s)
            {
                if (Throw) throw new FormatException("bad section");
                foreach (var r in s.Records) Value = r.Get("n").AsInt64;
            }
        }

        [Test]
        public void Restore_FailingSecondParticipant_RollsBackFirst_AndReturnsCorrupt()
        {
            var a = new Counter("a.first") { Value = 5 };
            var b = new Counter("b.second") { Value = 7 };
            var system = new SaveSystem("test");
            system.Register(a); system.Register(b);
            var doc = new SaveDocument("test");
            doc.AddSection("a.first", 1).Add("v").Set("n", SaveValue.Of(99L));
            doc.AddSection("b.second", 1).Add("v").Set("n", SaveValue.Of(100L));
            b.Throw = true;
            SaveLoadResult result = default(SaveLoadResult);
            Assert.DoesNotThrow(() => result = system.Restore(doc));
            Assert.That(result.Status, Is.EqualTo(SaveLoadStatus.Corrupt));
            Assert.That(a.Value, Is.EqualTo(5), "first participant must be rolled back");
            Assert.That(b.Value, Is.EqualTo(7));
        }
    }
}
