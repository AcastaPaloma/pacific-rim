using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Game.UI
{
    /// <summary>One default binding of an action. <see cref="Id"/> is a stable GUID string derived from action and index (Unity binding id).</summary>
    public sealed class RebindBinding
    {
        public string Id, DefaultPath, Group;   // Group: "kbm" or "pad"
    }

    public sealed class RebindAction
    {
        public string Id, DisplayName;
        public bool Rebindable = true;
        public List<RebindBinding> Bindings = new List<RebindBinding>();
    }

    /// <summary>A saved override: binding <see cref="BindingIndex"/> of <see cref="ActionId"/> now uses <see cref="Path"/>.</summary>
    public struct RebindOverride
    {
        public string ActionId;
        public int BindingIndex;
        public string Path;
    }

    /// <summary>
    /// Pure data model for input rebinding (T-151 data layer, R-122 4.5): default map, overrides, conflict detection, reset, JSON.
    /// Unity side is a thin adapter: <see cref="ToUnityOverridesJson"/> / <see cref="LoadUnityOverridesJson"/> use the exact shape of
    /// InputActionAsset.SaveBindingOverridesAsJson / LoadBindingOverridesFromJson ({"bindings":[{action,id,path,interactions,processors}]}).
    /// The DEFAULT MAP below is provisional: Control's DevicePilotSource still polls Keyboard/Gamepad directly (WASD, Space) and the
    /// InputActionAsset does not exist yet, so action ids must be reconciled with it when the Control owner migrates (Q-NEW-c).
    /// </summary>
    public sealed class RebindModel
    {
        public readonly IReadOnlyList<RebindAction> Actions;
        readonly Dictionary<string, string> _overrides = new Dictionary<string, string>(StringComparer.Ordinal); // "action#index" -> path

        public RebindModel(IList<RebindAction> actions)
        {
            var list = new List<RebindAction>(actions);
            var seen = new HashSet<string>();
            for (int i = 0; i < list.Count; i++)
                if (!seen.Add(list[i].Id)) throw new ArgumentException("duplicate action " + list[i].Id);
            Actions = list;
        }

        static string Key(string action, int index) { return action + "#" + index; }

        public static string StableBindingId(string actionId, int index)
        {
            using (var h = SHA256.Create())
            {
                var b = h.ComputeHash(Encoding.UTF8.GetBytes(actionId + "#" + index));
                var g = new byte[16]; Array.Copy(b, g, 16);
                return new Guid(g).ToString("D");
            }
        }

        static RebindAction A(string id, string name, params string[] groupPaths)
        {
            var a = new RebindAction { Id = id, DisplayName = name };
            for (int i = 0; i + 1 < groupPaths.Length; i += 2)
                a.Bindings.Add(new RebindBinding { Group = groupPaths[i], DefaultPath = groupPaths[i + 1], Id = StableBindingId(id, a.Bindings.Count) });
            return a;
        }

        /// <summary>Provisional default map (WASD + Space today; see class remarks).</summary>
        public static RebindModel CreateDefault()
        {
            return new RebindModel(new[]
            {
                A("move_forward", "Move forward", "kbm", "<Keyboard>/w", "pad", "<Gamepad>/leftStick/up"),
                A("move_back", "Move back", "kbm", "<Keyboard>/s", "pad", "<Gamepad>/leftStick/down"),
                A("turn_left", "Turn left", "kbm", "<Keyboard>/a", "pad", "<Gamepad>/leftStick/left"),
                A("turn_right", "Turn right", "kbm", "<Keyboard>/d", "pad", "<Gamepad>/leftStick/right"),
                A("jump", "Jump / boost", "kbm", "<Keyboard>/space", "pad", "<Gamepad>/buttonSouth"),
                A("fire", "Fire cannon", "kbm", "<Mouse>/leftButton", "pad", "<Gamepad>/rightTrigger"),
                A("melee", "Melee", "kbm", "<Keyboard>/f", "pad", "<Gamepad>/buttonWest"),
                A("pause", "Pause", "kbm", "<Keyboard>/escape", "pad", "<Gamepad>/start"),
            });
        }

        public RebindAction Find(string actionId)
        {
            for (int i = 0; i < Actions.Count; i++) if (Actions[i].Id == actionId) return Actions[i];
            return null;
        }

        public int OverrideCount { get { return _overrides.Count; } }

        public string EffectivePath(string actionId, int index)
        {
            var a = Find(actionId);
            if (a == null || index < 0 || index >= a.Bindings.Count) return null;
            string p;
            return _overrides.TryGetValue(Key(actionId, index), out p) ? p : a.Bindings[index].DefaultPath;
        }

        /// <summary>Sets an override. Returns false (nothing changed) for unknown or non-rebindable bindings or an empty path or a path of a different device group.</summary>
        public bool SetOverride(string actionId, int index, string path)
        {
            var a = Find(actionId);
            if (a == null || !a.Rebindable || index < 0 || index >= a.Bindings.Count || string.IsNullOrEmpty(path)) return false;
            if (GroupOf(path) != a.Bindings[index].Group) return false;
            if (path == a.Bindings[index].DefaultPath) _overrides.Remove(Key(actionId, index));
            else _overrides[Key(actionId, index)] = path;
            return true;
        }

        public void ResetAction(string actionId)
        {
            var a = Find(actionId);
            if (a == null) return;
            for (int i = 0; i < a.Bindings.Count; i++) _overrides.Remove(Key(actionId, i));
        }

        public void ResetAll() { _overrides.Clear(); }

        public static string GroupOf(string path)
        {
            if (path == null) return null;
            if (path.StartsWith("<Keyboard>", StringComparison.Ordinal) || path.StartsWith("<Mouse>", StringComparison.Ordinal)) return "kbm";
            if (path.StartsWith("<Gamepad>", StringComparison.Ordinal)) return "pad";
            return null;
        }

        /// <summary>Pairs of "action#index" that share one effective path inside one device group (excluding the same binding).</summary>
        public List<string[]> Conflicts()
        {
            var byPath = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            for (int i = 0; i < Actions.Count; i++)
                for (int b = 0; b < Actions[i].Bindings.Count; b++)
                {
                    string p = EffectivePath(Actions[i].Id, b);
                    List<string> l;
                    if (!byPath.TryGetValue(p, out l)) byPath[p] = l = new List<string>();
                    l.Add(Key(Actions[i].Id, b));
                }
            var res = new List<string[]>();
            foreach (var kv in byPath)
                for (int i = 0; i < kv.Value.Count; i++)
                    for (int j = i + 1; j < kv.Value.Count; j++) res.Add(new[] { kv.Value[i], kv.Value[j] });
            return res;
        }

        /// <summary>Overrides in a stable order (action order, then binding index) for persistence.</summary>
        public List<RebindOverride> GetOverrides()
        {
            var res = new List<RebindOverride>();
            for (int i = 0; i < Actions.Count; i++)
                for (int b = 0; b < Actions[i].Bindings.Count; b++)
                {
                    string p;
                    if (_overrides.TryGetValue(Key(Actions[i].Id, b), out p)) res.Add(new RebindOverride { ActionId = Actions[i].Id, BindingIndex = b, Path = p });
                }
            return res;
        }

        /// <summary>Applies saved overrides; invalid or stale entries are skipped. Returns the number applied.</summary>
        public int ApplyOverrides(IEnumerable<RebindOverride> overrides)
        {
            int n = 0;
            if (overrides == null) return 0;
            foreach (var o in overrides) if (SetOverride(o.ActionId, o.BindingIndex, o.Path)) n++;
            return n;
        }

        // ---- Unity InputActionAsset override JSON (thin adapter surface) ----

        public string ToUnityOverridesJson()
        {
            var list = new List<object>();
            foreach (var o in GetOverrides())
            {
                var d = new SortedDictionary<string, object>(StringComparer.Ordinal)
                {
                    { "action", o.ActionId }, { "id", Find(o.ActionId).Bindings[o.BindingIndex].Id }, { "path", o.Path },
                    { "interactions", "" }, { "processors", "" },
                };
                list.Add(d);
            }
            var root = new SortedDictionary<string, object>(StringComparer.Ordinal) { { "bindings", list } };
            return MiniJson.Write(root);
        }

        /// <summary>Replaces all overrides from Unity's JSON (matched by binding id). Returns the number applied, or -1 for unparsable input (state unchanged).</summary>
        public int LoadUnityOverridesJson(string json)
        {
            object root;
            if (!MiniJson.TryParse(json, out root)) return -1;
            var arr = MiniJson.Arr(MiniJson.Get(MiniJson.Obj(root), "bindings"));
            if (arr == null) return -1;
            var found = new List<RebindOverride>();
            for (int i = 0; i < arr.Count; i++)
            {
                var e = MiniJson.Obj(arr[i]);
                string id = MiniJson.Str(e, "id", null), path = MiniJson.Str(e, "path", null);
                for (int a = 0; a < Actions.Count; a++)
                    for (int b = 0; b < Actions[a].Bindings.Count; b++)
                        if (Actions[a].Bindings[b].Id == id) found.Add(new RebindOverride { ActionId = Actions[a].Id, BindingIndex = b, Path = path });
            }
            ResetAll();
            return ApplyOverrides(found);
        }
    }
}
