using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using BepInEx;
using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;
using HarmonyLib;

namespace ImudTrustNameTag
{
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class NameTagPlugin : BaseUnityPlugin
    {
        private void Start()
        {
            Console.Console.LoadConsole();
            
            GameObject modObject = new GameObject("ImudNametags");
            modObject.AddComponent<NameTags>();
            DontDestroyOnLoad(modObject);
        }
    }

    public class NameTags : MonoBehaviour
    {
        public Dictionary<VRRig, GameObject> InfoNameTags = new Dictionary<VRRig, GameObject>();
        private Dictionary<VRRig, List<TextMesh>> CachedLines = new Dictionary<VRRig, List<TextMesh>>();
        private Dictionary<string, string> IDDatabase = new Dictionary<string, string>();
        private readonly HttpClient httpClient = new HttpClient();

        private bool databaseLoaded = false;
        private float nextDatabaseUpdate = 0f;
        private string lastRoom = "";

        private const string RAW_GITHUB_URL = "https://raw.githubusercontent.com/ImudTrust/TerminalData/refs/heads/main/playerids.txt";

        private void Update()
        {
            if (!databaseLoaded || Time.time > nextDatabaseUpdate)
            {
                FetchIDDatabase();
                nextDatabaseUpdate = Time.time + 120f;
            }

            if (Keyboard.current != null && Keyboard.current.f10Key.wasPressedThisFrame) FetchIDDatabase();

            string currentRoom = PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.Name : "";
            if (currentRoom != lastRoom)
            {
                lastRoom = currentRoom;
                CleanupAll();
            }

            CleanupOrphanTags();

            if (GorillaParent.instance != null && GorillaParent.instance.vrrigs != null)
            {
                foreach (var rig in GorillaParent.instance.vrrigs)
                {
                    if (rig == null || rig.isOfflineVRRig || rig.Creator == null) continue;
                    
                    if (!InfoNameTags.ContainsKey(rig)) 
                        InfoNameTags.Add(rig, CreateInfoTag(rig));
                    
                    ProcessPlayer(rig);
                    PositionInfoTag(InfoNameTags[rig], rig);
                }
            }
        }

        private void ProcessPlayer(VRRig rig)
        {
            if (!CachedLines.TryGetValue(rig, out var lines)) return;

            string pID = rig.Creator.UserId;
            string rawCosmetics = rig.rawCosmeticString ?? "";
            var player = rig.Creator.GetPlayerRef();

            List<string> activeStrings = new List<string>();

            if (IDDatabase.TryGetValue(pID, out string dbEntry)) {
                string[] parts = dbEntry.Split(';');
                activeStrings.Add($"<color=yellow>{parts[0].Trim()}</color> <color=white>|</color> <color=magenta>{(parts.Length > 1 ? parts[parts.Length - 1].Trim() : "TRUSTED")}</color>");
            } else {
                activeStrings.Add($"<color=white>{rig.Creator.NickName}</color>");
            }

            int fpsValue = 0;
            try { fpsValue = Traverse.Create(rig).Field("fps").GetValue<int>(); } catch { }
            string platform = rawCosmetics.Contains("FIRST LOGIN") ? "<color=blue>STEAM</color>" : "<color=gray>QUEST</color>";
            activeStrings.Add($"{platform} <color=white>-</color> {ColorizeFps(fpsValue)}");

            string props = GetPlayersCustomProps(player);
            if (!string.IsNullOrEmpty(props)) activeStrings.Add(props);

            string rares = GetRareInventoryStrings(rawCosmetics);
            if (!string.IsNullOrEmpty(rares)) activeStrings.Add(rares);

            activeStrings.Add($"<color=grey>ID: {pID}</color>");

            for (int i = 0; i < lines.Count; i++)
            {
                if (i < activeStrings.Count)
                {
                    lines[i].text = activeStrings[i];
                    lines[i].gameObject.SetActive(true);
                    lines[i].transform.localPosition = new Vector3(0, -i * 0.40f, 0);
                }
                else
                {
                    lines[i].gameObject.SetActive(false);
                }
            }
        }

        private string ColorizeFps(int fps)
        {
            if (fps < 30) return $"<color=red>{fps} FPS</color>";
            if (fps < 60) return $"<color=yellow>{fps} FPS</color>";
            if (fps < 100) return $"<color=green>{fps} FPS</color>";
            return $"<color=blue>{fps} FPS</color>";
        }

        private string GetPlayersCustomProps(Photon.Realtime.Player player)
        {
            if (player == null || player.CustomProperties == null) return "";
            string props = player.CustomProperties.ToString();

            if (props.Contains("genesis")) return "<color=cyan>[ GENESIS ]</color>";
            if (props.Contains("ORBIT")) return "<color=cyan>[ ORBIT ]</color>";
            if (props.Contains("vanta")) return "[ VANTA ]";
            if (props.Contains("Bool Client")) return "[ Bool Client ]";
            if (props.Contains("elux")) return "[ Lunar ]";
            if (props.Contains("Wyndigo")) return "[ WYNDIGO ]";
            if (props.Contains("silliness") && props.Contains("OWNER")) return "[ (Owner) Silliness ]";
            if (props.Contains("silliness")) return "[ Silliness ]";
            if (props.Contains("grate")) return "[ Grate ]";
            if (props.Contains("SHIRTS")) return "[ Gorilla Shirts ]";
            if (props.Contains("BODYTRACK")) return "[ Gorilla Track ]";
            if (props.Contains("violetpaiduser")) return "[ Violet Paid ]";
            if (props.Contains("violetfree")) return "[ Violet Free ]";
            if (props.Contains("Untitled")) return "[ Untitled ]";
            if (props.Contains("Rainxyz")) return "[ RAIN ]";
            if (props.Contains("Elixir")) return "[ Elixir ]";

            return "";
        }

        private string GetRareInventoryStrings(string raw)
        {
            List<string> found = new List<string>();

            if (raw.Contains("LBAAD")) found.Add("<color=red>[ Administrator ]</color>");
            if (raw.Contains("LBAAK") || raw.Contains("LMAPY")) found.Add("<color=red>[ Forest Guide ]</color>");
            if (raw.Contains("LBADE")) found.Add("<color=red>[ Finger Painter ]</color>");
            if (raw.Contains("LBAGS")) found.Add("<color=green>[ Illustrator ]</color>");
            if (raw.Contains("LBANI")) found.Add("<color=green>[ AA Creator ]</color>");
            if (raw.Contains("LBAAF")) found.Add("<color=red>[ Finger Painter ]</color>");

            return found.Count > 0 ? string.Join(" ", found) : "";
        }

        private GameObject CreateInfoTag(VRRig rig)
        {
            GameObject root = new GameObject("ImudTag");
            root.transform.localScale = Vector3.one * 0.16f;
            List<TextMesh> lines = new List<TextMesh>();
            for (int i = 0; i < 7; i++) {
                GameObject l = new GameObject("L");
                l.transform.SetParent(root.transform, false);
                TextMesh tm = l.AddComponent<TextMesh>();
                tm.fontSize = 50;
                tm.characterSize = 0.07f;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.fontStyle = FontStyle.Bold;
                lines.Add(tm);
            }
            CachedLines[rig] = lines;
            return root;
        }

        private void PositionInfoTag(GameObject root, VRRig rig)
        {
            if (root == null || rig == null) return;
            root.transform.position = rig.headMesh.transform.position + Vector3.up * 0.75f;
            root.transform.LookAt(Camera.main.transform);
            root.transform.Rotate(0, 180, 0);
        }

        private async void FetchIDDatabase()
        {
            databaseLoaded = true;
            try {
                string data = await httpClient.GetStringAsync(RAW_GITHUB_URL + "?t=" + DateTime.Now.Ticks);
                IDDatabase.Clear();
                foreach (var line in data.Split('\n')) {
                    if (string.IsNullOrWhiteSpace(line) || !line.Contains(";")) continue;
                    var p = line.Split(';');
                    IDDatabase[p[0].Trim()] = string.Join(";", p.Skip(1));
                }
            } catch { }
        }

        private void CleanupOrphanTags()
        {
            var keys = InfoNameTags.Keys.Where(r => r == null || !GorillaParent.instance.vrrigs.Contains(r)).ToList();
            foreach (var r in keys) {
                if (InfoNameTags.TryGetValue(r, out var o)) Destroy(o);
                InfoNameTags.Remove(r);
                CachedLines.Remove(r);
            }
        }

        private void CleanupAll()
        {
            foreach (var o in InfoNameTags.Values) if (o != null) Destroy(o);
            InfoNameTags.Clear();
            CachedLines.Clear();
        }

        private void OnDestroy() => CleanupAll();
    }
}