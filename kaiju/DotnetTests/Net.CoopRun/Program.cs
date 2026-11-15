// T-030c two-process co-op run. Host and client are separate OS processes talking over real UDP (Game.Net.UdpPeer), through the
// same Net / ControlBridge / Units.Placeholder code the game uses. Usage:
//   Net.CoopRun host   --port 47311 --seconds 40 --out DIR
//   Net.CoopRun client --port 47311 --seconds 12 --gap 2 --seconds2 12 --out DIR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using Game.Control;
using Game.Control.Contracts;
using Game.Core;
using Game.Net;
using Game.Net.ControlBridge;
using Game.Units.Placeholder;

internal static class Program
{
    private const double StepS = 0.02;
    private const int Hz = 50;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static FeatureFlagSnapshot Flags() => new FeatureFlagSnapshot(new List<KeyValuePair<string, bool>>
    {
        new KeyValuePair<string, bool>("net.coop", true),
        new KeyValuePair<string, bool>("net.seatBridge", true),
        new KeyValuePair<string, bool>("units.placeholderBody", true),
    });

    private static string Arg(string[] a, string name, string def)
    {
        int i = Array.IndexOf(a, "--" + name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    private static int Main(string[] args)
    {
        if (args.Length == 0) { Console.Error.WriteLine("host|client"); return 2; }
        string outDir = Arg(args, "out", ".");
        Directory.CreateDirectory(outDir);
        int port = int.Parse(Arg(args, "port", "47311"), Inv);
        return args[0] == "host" ? Host(port, double.Parse(Arg(args, "seconds", "40"), Inv), outDir)
             : Client(port, double.Parse(Arg(args, "seconds", "12"), Inv), double.Parse(Arg(args, "gap", "2"), Inv), double.Parse(Arg(args, "seconds2", "12"), Inv), outDir);
    }

    private static EntityId BodyId(int slot) => EntityId.FromValue((ulong)(100 + slot));

    // ---- host ------------------------------------------------------------------------------------------------------------------

    private static int Host(int port, double seconds, string outDir)
    {
        var flags = Flags();
        var log = new NetLog();
        var bodies = new Dictionary<int, PlaceholderBody>();
        var seats = new Dictionary<int, MechSeat>();
        var snapshotSource = new Func<long, StateSnapshot>(step =>
        {
            var l = bodies.OrderBy(kv => kv.Key).Select(kv =>
            {
                var b = kv.Value;
                return new EntityState(b.Id, b.X, b.Y, b.Z, b.VelX, 0, b.VelZ, b.Heading, b.YawRate);
            }).ToArray();
            return new StateSnapshot { HostStep = step, Entities = l };
        });
        using (var listener = new UdpHostListener(port))
        {
            var session = new NetHostSession(new HandshakeValidator(flags, StepS, "coop-run", 4), snapshotSource, log, 2, Hz) { SlotTimeoutSteps = 100 };
            var pump = new HostSeatPump(session);
            pump.AssignPolicy = slot =>
                {
                    if (!seats.TryGetValue(slot.Index, out var seat))
                    {
                        seats[slot.Index] = seat = new MechSeat(flags, slot.Index, new SinglePilotArbiter());
                        var body = new PlaceholderBody(BodyId(slot.Index), PlaceholderLimits.Default());
                        body.Place(slot.Index * 100f, 0, 0, 0);
                        bodies[slot.Index] = body;
                    }
                    return seat;
                };
            var truth = new StringBuilder("step,slot,x,z,heading,speed\n");
            var events = new StringBuilder();
            session.SlotJoined += s => events.Append("join,").Append(session.Log.Records.Count).Append(',').Append(s.Index).Append('\n');
            session.SlotReleased += s => events.Append("release,").Append(session.Log.Records.Count).Append(',').Append(s.Index).Append('\n');

            var sw = Stopwatch.StartNew();
            long totalSteps = (long)(seconds * Hz), step = 0, late = 0; double maxLateMs = 0;
            int peak = 1;
            while (step < totalSteps)
            {
                double due = step * StepS * 1000.0;
                double now = sw.Elapsed.TotalMilliseconds;
                if (now < due) { Thread.Sleep(now + 2 < due ? 1 : 0); continue; }
                double lateMs = now - due; if (lateMs > 5) late++; if (lateMs > maxLateMs) maxLateMs = lateMs;
                while (listener.TryAccept(out var p)) session.AddConnection(p);
                pump.Advance(step * StepS + StepS);   // polls, delivers intents to the seats, reads them
                foreach (var kv in bodies)
                {
                    seats.TryGetValue(kv.Key, out var seat);
                    var intent = seat != null ? seat.LastIntent : default(IntentFrame);
                    kv.Value.Step(intent, (float)StepS);
                    var b = kv.Value;
                    truth.Append(step).Append(',').Append(kv.Key).Append(',').Append(b.X.ToString("R", Inv)).Append(',').Append(b.Z.ToString("R", Inv)).Append(',').Append(b.Heading.ToString("R", Inv)).Append(',').Append(b.Speed.ToString("R", Inv)).Append('\n');
                }
                peak = Math.Max(peak, session.OccupiedSlots);
                step++;
            }
            File.WriteAllText(Path.Combine(outDir, "host_truth.csv"), truth.ToString());
            File.WriteAllText(Path.Combine(outDir, "host_events.csv"), "event,logRecords,slot\n" + events);
            File.WriteAllText(Path.Combine(outDir, "host_net_log.txt"), log.ToText());
            var j = new StringBuilder("{\n");
            j.Append("  \"role\": \"host\", \"steps\": ").Append(step).Append(", \"seconds\": ").Append(seconds.ToString(Inv)).Append(",\n");
            j.Append("  \"stepsLateOver5ms\": ").Append(late).Append(", \"maxLateMs\": ").Append(maxLateMs.ToString("F2", Inv)).Append(",\n");
            j.Append("  \"peakOccupiedSlots\": ").Append(peak).Append(", \"finalOccupiedSlots\": ").Append(session.OccupiedSlots).Append(",\n");
            j.Append("  \"accepts\": ").Append(log.Count(NetLogKind.HandshakeAccept)).Append(", \"refuses\": ").Append(log.Count(NetLogKind.HandshakeRefuse)).Append(", \"slotReleases\": ").Append(log.Count(NetLogKind.SlotRelease)).Append(",\n");
            j.Append("  \"intentHoldSteps\": ").Append(log.Count(NetLogKind.IntentHold)).Append(", \"intentNeutralSteps\": ").Append(log.Count(NetLogKind.IntentNeutral)).Append(", \"staleDrops\": ").Append(session.Intents.DroppedStale).Append("\n}\n");
            File.WriteAllText(Path.Combine(outDir, "host_summary.json"), j.ToString());
            Console.WriteLine(j);
        }
        return 0;
    }

    // ---- client ----------------------------------------------------------------------------------------------------------------

    private sealed class SquarePilot : IPilotSource
    {
        public long Step;
        // Two far targets 600 m apart, swapped every 8 s: the body keeps walking and swings its heading by about 0.2 rad (inside the turn-first limit).
        private static readonly Vector3[] Wp = { new Vector3(100, 0, 3000), new Vector3(700, 0, 3000) };
        public PilotInput Read()
        {
            int leg = (int)(Step / (Hz * 8)) % Wp.Length;
            return new PilotInput { move = MoveId.Walk, target = Wp[leg], effort = 0.8f, valid = true };
        }
    }

    private static int Client(int port, double seconds, double gap, double seconds2, string outDir)
    {
        var flags = Flags();
        var view = new StringBuilder("phase,clientStep,renderHostStep,x,z,stale\n");
        var events = new StringBuilder();
        var pilot = new SquarePilot();
        var sw = Stopwatch.StartNew();
        long clientStep = 0; double maxLateMs = 0; long late = 0;
        var sbLog = new StringBuilder();
        int[] slots = new int[2];
        double staleTotal = 0;

        bool Phase(int phase, double secs)
        {
            var log = new NetLog();
            var session = new NetClientSession(UdpPeer.Connect("127.0.0.1", port), flags, StepS, "coop-run", log, Hz);
            var link = new ClientSeatLink(flags, session, pilot);
            session.Join();
            long end = clientStep + (long)(secs * Hz);
            long joinedAt = -1;
            while (clientStep < end)
            {
                double due = clientStep * StepS * 1000.0, now = sw.Elapsed.TotalMilliseconds;
                if (now < due) { Thread.Sleep(now + 2 < due ? 1 : 0); continue; }
                double lateMs = now - due; if (lateMs > 5) late++; if (lateMs > maxLateMs) maxLateMs = lateMs;
                pilot.Step = clientStep;
                link.Advance(clientStep * StepS + StepS);
                if (session.State == ClientState.Refused) { events.Append("refused,").Append(phase).Append('\n'); return false; }
                if (session.State == ClientState.Joined)
                {
                    if (joinedAt < 0) { joinedAt = clientStep; slots[phase - 1] = session.Slot.Index; events.Append("joined,").Append(phase).Append(',').Append(session.Slot.Index).Append(',').Append(clientStep).Append('\n'); }
                    session.Update(link.Step); // the link owns the step clock the session was polled with
                    foreach (var b in session.Interpolator.Bodies)
                        if (b.Id == BodyId(session.Slot.Index))
                            view.Append(phase).Append(',').Append(clientStep).Append(',').Append(session.Interpolator.RenderStep.ToString("F3", Inv)).Append(',').Append(b.PosX.ToString("F4", Inv)).Append(',').Append(b.PosZ.ToString("F4", Inv)).Append(',').Append(b.Stale ? 1 : 0).Append('\n');
                }
                clientStep++;
            }
            sbLog.Append("# phase ").Append(phase).Append('\n').Append(log.ToText());
            events.Append("end,").Append(phase).Append(',').Append(link.Sent).Append(" intents sent,bytes up ").Append(session.Link.SentBytes).Append(",bytes down ").Append(session.Link.ReceivedBytes).Append('\n');
            if (phase == 1)
            {
                session.Leave(clientStep);
                events.Append("left,").Append(phase).Append(',').Append(clientStep).Append('\n');
            }
            else session.Link.Close();
            return true;
        }

        bool ok = Phase(1, seconds);
        if (ok)
        {
            // idle gap with the old link gone, then a fresh socket (a new client process in spirit: new endpoint, new sequence numbers)
            double gapEndMs = sw.Elapsed.TotalMilliseconds + gap * 1000.0;
            while (sw.Elapsed.TotalMilliseconds < gapEndMs) Thread.Sleep(5);
            clientStep = (long)Math.Ceiling(sw.Elapsed.TotalMilliseconds / (StepS * 1000.0)); // keep the step clock on the wall clock
            ok = Phase(2, seconds2);
        }
        File.WriteAllText(Path.Combine(outDir, "client_view.csv"), view.ToString());
        File.WriteAllText(Path.Combine(outDir, "client_events.csv"), "event,phase,a,b\n" + events);
        File.WriteAllText(Path.Combine(outDir, "client_net_log.txt"), sbLog.ToString());
        var j = $"{{\n  \"role\": \"client\", \"slotPhase1\": {slots[0]}, \"slotPhase2\": {slots[1]}, \"sameSlotOnRejoin\": {(slots[0] == slots[1] && slots[0] > 0).ToString().ToLowerInvariant()}, \"stepsLateOver5ms\": {late}, \"maxLateMs\": {maxLateMs.ToString("F2", Inv)}\n}}\n";
        File.WriteAllText(Path.Combine(outDir, "client_summary.json"), j);
        Console.WriteLine(j);
        return ok ? 0 : 1;
    }
}
