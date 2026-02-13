using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using BepInEx;
using ImudTrustNameTag.Notifications;
using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Reflection;

namespace ImudTrustNameTag
{
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class NameTagPlugin : BaseUnityPlugin
    {
        private void Start()
        {
            GameObject modObject = new GameObject("ImudNametags");
            modObject.AddComponent<NameTags>();
            DontDestroyOnLoad(modObject);
        }
    }

    public class NameTags : MonoBehaviour
    {
        public Dictionary<VRRig, GameObject> InfoNameTags = new Dictionary<VRRig, GameObject>();
        private Dictionary<VRRig, TextMesh[]> CachedTexts = new Dictionary<VRRig, TextMesh[]>();
        private Dictionary<string, string> IDDatabase = new Dictionary<string, string>();
        private readonly HttpClient httpClient = new HttpClient();
        private bool databaseLoaded = false;
        private float nextDatabaseUpdate = 0f;
        private string lastRoom = "";
        private const string RAW_GITHUB_URL = "https://raw.githubusercontent.com/ImudTrust/TerminalData/refs/heads/main/playerids.txt";
        private const float MAX_DISPLAY_DISTANCE = 5f;
        private readonly Dictionary<string, string> specialCosmetics = new Dictionary<string, string>
        {
            { "LBAAD.", "Administrator" },
            { "LBAAK.", "Forest Guide" },
            { "LBADE.", "Finger Painter" },
            { "LBAGS.", "Illustrator" },
            { "LMAPY.", "Forest Guide" },
            { "LBANI.", "AA Creator" }
        };
        private HashSet<string> notifiedPlayers = new HashSet<string>();
        private FieldInfo fpsField;

        private void Awake()
        {
            fpsField = typeof(VRRig).GetField("fps", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        private void Update()
        {
            if (!databaseLoaded || Time.time > nextDatabaseUpdate)
            {
                FetchIDDatabase();
                nextDatabaseUpdate = Time.time + 10f;
            }

            if (Keyboard.current != null && Keyboard.current.f10Key.wasPressedThisFrame)
                FetchIDDatabase();

            string currentRoom = PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.Name : "";
            if (currentRoom != lastRoom)
            {
                lastRoom = currentRoom;
                CleanupAll();
                notifiedPlayers.Clear();
            }

            CleanupOrphanTags();

            if (GorillaParent.instance?.vrrigs == null) return;

            foreach (var rig in GorillaParent.instance.vrrigs)
            {
                if (rig == null || rig.isOfflineVRRig || rig.Creator == null) continue;

                if (!InfoNameTags.ContainsKey(rig))
                    InfoNameTags.Add(rig, CreateInfoTag(rig));

                float dist = Vector3.Distance(Camera.main.transform.position, rig.headMesh.transform.position);
                InfoNameTags[rig].SetActive(dist <= MAX_DISPLAY_DISTANCE);

                if (dist <= MAX_DISPLAY_DISTANCE)
                {
                    ProcessPlayer(rig);
                    PositionInfoTag(InfoNameTags[rig], rig);
                    CheckAndNotify(rig);
                }
            }
        }

        private int GetFPS(VRRig rig)
        {
            if (fpsField == null) return 0;
            try { return (int)fpsField.GetValue(rig); } catch { return 0; }
        }

        private string ColorizeFPS(int fps)
        {
            if (fps <= 0) return "";
            if (fps < 60) return $"<color=red>{fps}Hz</color>";
            if (fps < 90) return $"<color=yellow>{fps}Hz</color>";
            if (fps < 120) return $"<color=green>{fps}Hz</color>";
            return $"<color=cyan>{fps}Hz</color>";
        }

        private void CheckAndNotify(VRRig rig)
        {
            string pID = rig.Creator.UserId ?? "Unknown";
            if (notifiedPlayers.Contains(pID)) return;

            string raw = rig.rawCosmeticString ?? "";
            foreach (var kv in specialCosmetics)
            {
                if (raw.Contains(kv.Key))
                {
                    NotifiLib.SendNotification($"<color=red>[RARE]</color> {rig.Creator.NickName} has {kv.Value}");
                    notifiedPlayers.Add(pID);
                    break;
                }
            }
        }

        private GameObject CreateInfoTag(VRRig rig)
        {
            GameObject root = new GameObject("ImudTag");
            root.transform.localScale = Vector3.one * 0.16f;

            TextMesh[] lines = new TextMesh[5];
            for (int i = 0; i < lines.Length; i++)
            {
                GameObject l = new GameObject("Line");
                l.transform.SetParent(root.transform, false);
                l.transform.localPosition = new Vector3(0f, -i * 0.35f, 0f);
                TextMesh tm = l.AddComponent<TextMesh>();
                tm.fontSize = 50;
                tm.characterSize = 0.07f;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.fontStyle = FontStyle.Bold;
                lines[i] = tm;
            }

            CachedTexts[rig] = lines;
            return root;
        }

        private void ProcessPlayer(VRRig rig)
        {
            if (!CachedTexts.TryGetValue(rig, out var lines)) return;

            string pID = rig.Creator.UserId ?? "Unknown";
            string raw = rig.rawCosmeticString ?? "";
            string[] ownedItems = raw.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim().ToUpper()).ToArray();

            string dbTag = "";
            if (IDDatabase.TryGetValue(pID, out string dbEntry))
            {
                string[] parts = dbEntry.Split(';');
                string dbName = parts[0].Trim();
                string dbRole = parts.Length > 1 ? parts[parts.Length - 1].Trim() : "TRUSTED";
                lines[0].text = $"<color=yellow>{dbName}</color>";
                dbTag = $"<color=magenta>[ID MATCH - {dbRole}]</color>";
            }
            else
            {
                lines[0].text = rig.Creator.NickName;
            }

            int fps = GetFPS(rig);
            lines[1].text = (raw.Contains("FIRST LOGIN") ? "<color=blue>STEAM</color>" : "<color=gray>QUEST</color>") + " | " + ColorizeFPS(fps);

            string rareLine = string.Join(" ", ownedItems.Where(i => specialCosmetics.ContainsKey(i)).Select(i => $"<color=red>[{specialCosmetics[i]}]</color>"));
            lines[2].text = string.Join(" ", new string[] { dbTag, rareLine }.Where(x => !string.IsNullOrEmpty(x)));

            lines[3].text = rig.Creator.GetPlayerRef()?.CustomProperties.ToString().Contains("genesis") == true
                ? "<color=cyan>[GENESIS]</color>" : "";

            lines[4].text = "";

            foreach (var line in lines)
                line.gameObject.SetActive(!string.IsNullOrEmpty(line.text));
        }

        private void PositionInfoTag(GameObject root, VRRig rig)
        {
            root.transform.position = rig.headMesh.transform.position + Vector3.up * 0.75f;
            root.transform.LookAt(Camera.main.transform);
            root.transform.Rotate(0f, 180f, 0f);
        }

        private async void FetchIDDatabase()
        {
            databaseLoaded = true;
            try
            {
                string data = await httpClient.GetStringAsync(RAW_GITHUB_URL + "?t=" + DateTime.Now.Ticks);
                IDDatabase.Clear();
                foreach (string line in data.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(line) || !line.Contains(";")) continue;
                    string[] parts = line.Split(';');
                    string id = parts[0].Trim();
                    string value = string.Join(";", parts, 1, parts.Length - 1);
                    IDDatabase[id] = value;
                }
            }
            catch { }
        }

        private void CleanupOrphanTags()
        {
            List<VRRig> keys = new List<VRRig>();
            foreach (var r in InfoNameTags.Keys)
            {
                if (r == null || !GorillaParent.instance.vrrigs.Contains(r)) keys.Add(r);
            }

            foreach (var r in keys)
            {
                if (InfoNameTags.TryGetValue(r, out var o)) Destroy(o);
                InfoNameTags.Remove(r);
                CachedTexts.Remove(r);
            }
        }

        private void CleanupAll()
        {
            foreach (var o in InfoNameTags.Values) if (o != null) Destroy(o);
            InfoNameTags.Clear();
            CachedTexts.Clear();
        }

        private void OnDestroy()
        {
            CleanupAll();
        }
    }
}
