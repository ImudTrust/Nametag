using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using BepInEx;
using ImudTrustNameTag.Notifications;
using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;

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

                float distance = Vector3.Distance(Camera.main.transform.position, rig.headMesh.transform.position);
                InfoNameTags[rig].SetActive(distance <= MAX_DISPLAY_DISTANCE);

                if (distance <= MAX_DISPLAY_DISTANCE)
                {
                    ProcessPlayer(rig);
                    PositionInfoTag(InfoNameTags[rig], rig);
                    
                    CheckAndNotify(rig);
                }
            }
        }

        private void CheckAndNotify(VRRig rig)
        {
            string pID = rig.Creator.UserId ?? "Unknown";
            string rawCosmetics = rig.rawCosmeticString ?? "";
            string[] ownedItems = rawCosmetics.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim().ToUpper()).ToArray();
            
            var found = ownedItems.FirstOrDefault(item => specialCosmetics.ContainsKey(item));
            if (found != null && !notifiedPlayers.Contains(pID))
            {
                string cosmeticName = specialCosmetics[found];
                NotifiLib.SendNotification($"<color=red>[RARE]</color> {rig.Creator.NickName} has {cosmeticName}!");
                notifiedPlayers.Add(pID);
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
            if (!CachedTexts.TryGetValue(rig, out TextMesh[] lines)) return;
            if (rig.Creator == null) return;

            string pID = rig.Creator.UserId ?? "Unknown";
            string rawCosmetics = rig.rawCosmeticString ?? "";
            string[] ownedItems = rawCosmetics.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
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
            
            lines[1].text = rawCosmetics.Contains("FIRST LOGIN") ? "<color=blue>STEAM</color>" : "<color=gray>QUEST</color>";
            
            string rareLine = GetRareInventoryStrings(ownedItems);
            lines[2].text = string.Join(" ", new string[] { dbTag, rareLine }.Where(x => !string.IsNullOrEmpty(x)));
            
            lines[3].text = rig.Creator.GetPlayerRef()?.CustomProperties.ToString().Contains("genesis") == true
                ? "<color=cyan>[GENESIS]</color>" : "";
            
            lines[4].text = "";

            foreach (var line in lines)
                line.gameObject.SetActive(!string.IsNullOrEmpty(line.text));
        }

        private string GetRareInventoryStrings(string[] items)
        {
            List<string> found = new List<string>();
            foreach (string item in items)
            {
                if (specialCosmetics.ContainsKey(item))
                    found.Add($"<color=red>[{specialCosmetics[item]}]</color>");
            }
            return string.Join(" ", found);
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
                GameObject o;
                if (InfoNameTags.TryGetValue(r, out o)) Destroy(o);
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
