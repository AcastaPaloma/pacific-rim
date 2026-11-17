// Web demo (task/web-demo): a WebGL player cannot File-read StreamingAssets (the path is an http URL), and the geo
// loaders use System.IO. Before the slice loads, this downloads every file listed in StreamingAssets/web_files.txt
// (written by Tools/Build/build_web.sh) into the Emscripten in-memory filesystem and points GeoModule.StreamingRoot there.
// Desktop players never call it.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Game.World.Geo.Unity;
using UnityEngine;
using UnityEngine.Networking;

namespace Game.Slice
{
    public static class SliceWebPreload
    {
        public const string Root = "/kaiju_sa";
        public static string Status = "";
        public static bool Active;

        public static IEnumerator Run()
        {
            Active = true; var t0 = Time.realtimeSinceStartup;
            string baseUrl = Application.streamingAssetsPath;
            var list = UnityWebRequest.Get(baseUrl + "/web_files.txt");
            yield return list.SendWebRequest();
            if (list.result != UnityWebRequest.Result.Success) { Debug.LogError("[WebPreload] web_files.txt: " + list.error); Active = false; yield break; }
            var files = new List<string>();
            foreach (var line in list.downloadHandler.text.Split('\n')) { var f = line.Trim(); if (f.Length > 0) files.Add(f); }
            int next = 0, done = 0, failed = 0; long bytes = 0;
            var inFlight = new List<(UnityWebRequest req, string rel)>();
            while (done + failed < files.Count)
            {
                while (inFlight.Count < 6 && next < files.Count)
                {
                    var rel = files[next++]; var r = UnityWebRequest.Get(baseUrl + "/" + rel); r.SendWebRequest(); inFlight.Add((r, rel));
                }
                for (int i = inFlight.Count - 1; i >= 0; i--)
                {
                    var (req, rel) = inFlight[i];
                    if (!req.isDone) continue;
                    inFlight.RemoveAt(i);
                    if (req.result != UnityWebRequest.Result.Success) { failed++; Debug.LogError("[WebPreload] " + rel + ": " + req.error); req.Dispose(); continue; }
                    var data = req.downloadHandler.data; bytes += data.Length;
                    string dest = Path.Combine(Root, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    File.WriteAllBytes(dest, data);
                    req.Dispose(); done++;
                }
                Status = "Loading San Francisco tiles: " + done + " / " + files.Count + " files (" + (bytes >> 20) + " MB)";
                yield return null;
            }
            GeoModule.StreamingRootOverride = Root;
            Debug.Log("[WebPreload] " + done + " files, " + (bytes >> 20) + " MB, " + failed + " failed, in " + (Time.realtimeSinceStartup - t0).ToString("F1") + " s");
            Status = ""; Active = false;
        }
    }
}
