using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;
using System;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace AkuTrack.ApiTypes
{
    public class AkuGameObject
    {
        public unsafe AkuGameObject(IGameObject obj, IClientState clientState) {
            this.created_at = DateTimeOffset.Now;
            this.objectKind = obj.ObjectKind;
            this.pos = obj.Position;
            // Fixme actually load the unique id
            this.unique_ingame_id = obj.GameObjectId;
            this.bid = obj.BaseId;
            this.hr = obj.HitboxRadius;
            this.name = obj.Name.ToString();
            this.mid = clientState.MapId;
            this.zid = clientState.TerritoryType;
            this.r = obj.Rotation;
            this.ownerId = obj.OwnerId;

            if (obj is ICharacter c)
            {
                this.nid = c.NameId;
                if (c.Address != nint.Zero)
                {
                    FFXIVClientStructs.FFXIV.Client.Game.Character.Character* chr = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)c.Address;
                    this.moid = chr->ModelContainer.ModelCharaId;
                    this.npiid = chr->NamePlateIconId;
                }
            }

            if(obj is IBattleNpc bnpc) {
                this.battleNpcSubKind = bnpc.BattleNpcKind;
            }
        }

        public AkuGameObject(DownloadGameObject dgo) {
            this.created_at = dgo.created_at;
            this.lastseen_at = dgo.last_seen_at;
            if (Enum.TryParse<ObjectKind>(dgo.objecttype, out var parsedObjectKind))
            {
                this.objectKind = parsedObjectKind;
            }
            else
            {
                this.objectKind = ObjectKind.None;
                this.syntheticType = dgo.objecttype;
            }
            this.name = "<downloaded>";
            this.mid = dgo.map_id;
            this.zid = dgo.zone_id;
            this.pos = new Vector3(dgo.x, dgo.y, dgo.z);
            this.r = dgo.rotation;
            this.bid = dgo.base_id;
            this.unique_ingame_id = dgo.unique_ingame_id;
            this.npiid = dgo.npiid;
            this.moid = dgo.moid;
            this.hr = dgo.hit_radius;
            this.nid = dgo.nid;
            this.isDownloaded = true;
        }
        [JsonIgnore]
        public DateTimeOffset? created_at { get; set; }
        [JsonIgnore]
        public DateTimeOffset? lastseen_at { get; set; }
        public string t { get { return syntheticType ?? objectKind.ToString(); } }
        [JsonIgnore]
        public string name { get; set; }
        [JsonIgnore]
        public bool isDownloaded { get; set; } = false;
        [JsonIgnore]
        public ObjectKind objectKind { get; set;  }
        [JsonIgnore]
        public string? syntheticType { get; set; }
        [JsonIgnore]
        public BattleNpcSubKind? battleNpcSubKind { get; set; }
        [JsonIgnore]
        public uint ownerId { get; set; }
        public uint mid { get; set; }
        public uint zid { get; set; }
        public Vector3 pos { get; set; }
        public float r { get; set; }
        public ulong? unique_ingame_id { get; set; }
        public uint bid { get; set; }
        public uint? npiid {  get; set; }
        public int moid { get; set; }
        //public bool v { get; set; }
        public float hr { get; set; }
        // Only BattleNpc
        public uint? nid { get; set; }
        public string? uuid
        {
            get
            {
                return GetUniqueId();
            }
        }
        public string? GetUniqueId()
        {
            string input = string.Empty;
            if (t == "EventNpc" || t == "BattleNpc")
            {
                input = $"{t},{bid},{Math.Round(pos.X / 10, 0) * 10},{Math.Round(pos.Y / 10, 0) * 10},{Math.Round(pos.Z / 10, 0) * 10},{nid}";
            }
            else
            {
                input = $"{t},{bid},{pos.X},{pos.Y},{pos.Z}";
            }
            if (input == string.Empty)
                return null;
            return CalculateUniqueId(input);
        }

        private static string CalculateUniqueId(string input) {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            string hashString = string.Empty;
            foreach (byte x in hash)
            {
                hashString += String.Format("{0:x2}", x);
            }
            //log.Debug($"Hashing {input} to {hashString}");
            return hashString;
        }

        public static string? GetUniqueId(IGameObject o)
        {
            string input = string.Empty;
            if (o.ObjectKind == ObjectKind.EventNpc || o.ObjectKind == ObjectKind.BattleNpc)
            {
                if (o is ICharacter c)
                {
                    input = $"{o.ObjectKind},{o.BaseId},{Math.Round(o.Position.X / 10, 0) * 10},{Math.Round(o.Position.Y / 10, 0) * 10},{Math.Round(o.Position.Z / 10, 0) * 10},{c.NameId}";
                } else {
                    return null;
                }
            }
            else
            {
                input = $"{o.ObjectKind},{o.BaseId},{o.Position.X},{o.Position.Y},{o.Position.Z}";
            }
            if (input == string.Empty)
                return null;
            return CalculateUniqueId(input);
        }
    }
}
