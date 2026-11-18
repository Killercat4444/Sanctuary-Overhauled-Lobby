using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using EM.Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using UnityEngine;

public enum SanctuaryLobbyMode { Vanilla=0, Overhauled=1 }
public enum SanctuaryTeamPolicy { Off=0, TopVsBottom=1, LeftVsRight=2, OddVsEven=3 }
public sealed class OverhaulHello { public string nonce;public string build; public string version; public int protocol; public string palette; }
public sealed class OverhaulRequest { public string nonce;public int kind;public string faction;public string player; public int color=-1; public string commander; public int revision; }
public sealed class OverhaulEntry { public string player; public string name; public int army; public int color=-1; public string commander; }
public sealed class OverhaulSeatColor { public int army; public int color=-1; }
public sealed class OverhaulSnapshot { public string hostNonce;public string[] capabilityNonces;public int choiceRevision;public OverhaulChoice[] choices;public string build; public int teams; public string version; public int protocol; public string palette; public int revision; public int mode; public OverhaulEntry[] players; public OverhaulSeatColor[] seats; public string[] acknowledged; }
internal sealed class OccupiedColorOwner { internal string key; internal int army; internal int preferred; }
internal sealed class ReferenceComparer : IEqualityComparer<object> { internal static readonly ReferenceComparer Instance=new ReferenceComparer();public new bool Equals(object a,object b){return ReferenceEquals(a,b);}public int GetHashCode(object value){return RuntimeHelpers.GetHashCode(value);} }

internal static class SanctuaryProtocolRules {
 internal enum RequestDecision { Reject=0,Unchanged=1,Changed=2 }
 internal const int MaxPlayers=64,MaxPlayerIdLength=20,MaxPlayerNameLength=128,MaxCommanderLength=256,MaxPacketBytes=65536;
 internal static bool PacketWithinBounds(int length){return length>0&&length<=MaxPacketBytes;}
 internal static string NormalizeBlockReason(string reason){return string.IsNullOrWhiteSpace(reason)?null:reason;}
 internal static bool StartAllowed(bool nativeCanStart,string reason){return nativeCanStart&&NormalizeBlockReason(reason)==null;}
 internal static bool ReserveHelloReply(ref DateTime next,DateTime now){if(now<next)return false;next=now.AddSeconds(2);return true;}
 internal static int JoinDecision(int kind,int currentArmy,int destination,bool available,bool expired){if(expired)return -1;if(kind==1&&currentArmy==destination)return 2;if(!available)return -1;return kind==1?1:0;}
 internal static bool WellFormedHello(OverhaulHello h){return h!=null&&!string.IsNullOrEmpty(h.build)&&h.build.Length<=64&&!string.IsNullOrEmpty(h.version)&&h.version.Length<=64&&!string.IsNullOrEmpty(h.palette)&&h.palette.Length<=64&&h.protocol>=0;}
 internal static int PreferredColor(int favorite,int second,int slotColor,ISet<int> used){
  foreach(int c in new[]{favorite,second,slotColor})if(c>=0&&c<SanctuaryLobbyState.Palette.Length&&!used.Contains(c))return c;
  for(int i=0;i<SanctuaryLobbyState.Palette.Length;i++)if(!used.Contains(i))return i;return -1;
 }
 internal static bool ValidNonce(string nonce){return nonce!=null&&nonce.Length==32&&nonce.All(c=>c>='0'&&c<='9'||c>='a'&&c<='f');}
 internal static bool ExactHello(OverhaulHello h,string version){return WellFormedHello(h)&&ValidNonce(h.nonce)&&h.protocol==SanctuaryLobbyState.ProtocolVersion&&h.version==version&&h.build==SanctuaryLobbyState.BuildIdentity&&h.palette==SanctuaryLobbyState.PaletteIdentity;}
 internal static bool SameHello(OverhaulHello a,OverhaulHello b){return a!=null&&b!=null&&a.nonce==b.nonce&&a.protocol==b.protocol&&a.version==b.version&&a.build==b.build&&a.palette==b.palette;}
 internal static bool ValidRequest(OverhaulRequest q,ulong sender,int revision,bool capabilityAcknowledged){
  if(!capabilityAcknowledged||q==null||q.revision!=revision||string.IsNullOrEmpty(q.player)||q.player.Length>MaxPlayerIdLength||string.IsNullOrEmpty(q.commander)||q.commander.Length>MaxCommanderLength||q.color<0||q.color>=SanctuaryLobbyState.Palette.Length)return false;
  ulong claimed;return ulong.TryParse(q.player,NumberStyles.None,CultureInfo.InvariantCulture,out claimed)&&claimed==sender;
 }
 internal static bool SameEntry(OverhaulEntry a,OverhaulEntry b){return a!=null&&b!=null&&a.player==b.player&&a.color==b.color&&string.Equals(a.commander,b.commander,StringComparison.Ordinal);}
 internal static bool TryParseCommanderIdentity(string identity,out string modId,out string factionKey,out string commanderUnit){modId=factionKey=commanderUnit=null;if(string.IsNullOrEmpty(identity))return false;var parts=identity.Split(':');if(parts.Length!=3||parts.Any(string.IsNullOrEmpty))return false;modId=parts[0];factionKey=parts[1];commanderUnit=parts[2];return true;}
 internal static bool TryResolveRegisteredCommander(IEnumerable current,string identity,out int value){
  value=0;string modId,key,unit;if(current==null||!TryParseCommanderIdentity(identity,out modId,out key,out unit))return false;
  foreach(var x in current){if(x==null)continue;try{var t=x.GetType();var faction=t.GetProperty("Faction").GetValue(x,null);var commander=t.GetProperty("Commander").GetValue(x,null);var mod=t.GetProperty("Mod").GetValue(x,null);string candidateMod=Convert.ToString(mod.GetType().GetProperty("Id").GetValue(mod,null)),candidateKey=Convert.ToString(faction.GetType().GetProperty("Key").GetValue(faction,null)),candidateUnit=Convert.ToString(commander.GetType().GetProperty("Unit").GetValue(commander,null));if(candidateMod==modId&&candidateKey==key&&candidateUnit==unit){value=Convert.ToInt32(t.GetProperty("Value").GetValue(x,null));return true;}}catch{}
  }
  return false;
 }
 internal static Dictionary<string,int> ReconcileOccupiedColors(IDictionary<string,int> previous,IEnumerable<OccupiedColorOwner> owners,ISet<int> reserved){
  var result=new Dictionary<string,int>(StringComparer.Ordinal);var claimed=new HashSet<int>(reserved??new HashSet<int>());var ordered=(owners??Enumerable.Empty<OccupiedColorOwner>()).Where(x=>x!=null&&!string.IsNullOrEmpty(x.key)&&x.army>0).OrderBy(x=>x.army).ThenBy(x=>x.key,StringComparer.Ordinal).ToArray();
  foreach(var owner in ordered){int color;if(previous!=null&&previous.TryGetValue(owner.key,out color)&&color>=0&&color<SanctuaryLobbyState.Palette.Length&&!claimed.Contains(color)){result[owner.key]=color;claimed.Add(color);}}
  foreach(var owner in ordered)if(!result.ContainsKey(owner.key)){int color=owner.preferred;if(color<0||color>=SanctuaryLobbyState.Palette.Length||claimed.Contains(color)){color=-1;for(int i=0;i<SanctuaryLobbyState.Palette.Length;i++)if(!claimed.Contains(i)){color=i;break;}}if(color>=0){result[owner.key]=color;claimed.Add(color);}}
  return result;
 }
 internal static RequestDecision EvaluateRequest(OverhaulRequest q,ulong sender,int revision,bool capabilityAcknowledged,bool commanderAllowed,ISet<int> usedColors,string authoritativeName,OverhaulEntry current,out OverhaulEntry accepted){
  accepted=null;if(!ValidRequest(q,sender,revision,capabilityAcknowledged)||!commanderAllowed||authoritativeName==null||authoritativeName.Length>MaxPlayerNameLength)return RequestDecision.Reject;
  int color=q.color;if(usedColors!=null&&usedColors.Contains(color)){color=-1;for(int i=0;i<SanctuaryLobbyState.Palette.Length;i++)if(!usedColors.Contains(i)){color=i;break;}if(color<0)return RequestDecision.Reject;}
  accepted=new OverhaulEntry{player=q.player,name=authoritativeName,color=color,commander=q.commander};
  return SameEntry(current,accepted)?RequestDecision.Unchanged:RequestDecision.Changed;
 }
 internal static bool ValidSnapshotEnvelope(OverhaulSnapshot s,string version,int currentRevision){
  return s!=null&&s.protocol==SanctuaryLobbyState.ProtocolVersion&&s.version==version&&s.build==SanctuaryLobbyState.BuildIdentity&&s.teams>=0&&s.teams<=3&&s.palette==SanctuaryLobbyState.PaletteIdentity&&s.revision>=currentRevision&&(s.mode==(int)SanctuaryLobbyMode.Vanilla||s.mode==(int)SanctuaryLobbyMode.Overhauled)&&(s.players==null||s.players.Length<=MaxPlayers)&&(s.seats==null||s.seats.Length<=MaxPlayers)&&(s.acknowledged==null||s.acknowledged.Length<=MaxPlayers);
 }
 internal static bool ValidSnapshotEntry(OverhaulEntry e){ulong id;return e!=null&&!string.IsNullOrEmpty(e.player)&&e.player.Length<=MaxPlayerIdLength&&ulong.TryParse(e.player,NumberStyles.None,CultureInfo.InvariantCulture,out id)&&e.name!=null&&e.name.Length<=MaxPlayerNameLength&&e.commander!=null&&e.commander.Length<=MaxCommanderLength&&e.army>=0&&e.army<=MaxPlayers&&e.color>=0&&e.color<SanctuaryLobbyState.Palette.Length;}
}

public static partial class SanctuaryLobbyState {
 internal const string SyncModId="com.sanctuary.overhauled-lobby";
 internal const int ProtocolVersion=4;
 internal const byte RequestType=242,SnapshotType=243,HelloType=244;
 internal const string PaletteIdentity="sss-stock24-plus8-v2";
 internal static readonly string BuildIdentity=BinaryIdentity();
 static string BinaryIdentity(){
  // Mod Manager loads UI assemblies from bytes, so Assembly.Location is empty.
  // Hash the canonical installed payload, not only its public version/MVID.
  string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"SanctuaryMods","SanctuaryOverhauledLobby","SanctuaryOverhauledLobby.dll");
  try{if(File.Exists(path))return Sha(path);}catch{}
  // Offline production tests have no installed candidate. This identity can
  // never equal the 64-character live DLL digest; live guard also requires it.
  return "offline-"+typeof(SanctuaryLobbyState).Assembly.ManifestModule.ModuleVersionId.ToString("N");
 }
 internal static readonly BindingFlags StaticAll=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
 internal static readonly BindingFlags InstanceAll=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static readonly Dictionary<ulong,OverhaulEntry> entries=new Dictionary<ulong,OverhaulEntry>();
 static readonly Dictionary<ulong,OverhaulHello> hellos=new Dictionary<ulong,OverhaulHello>();
 static readonly Dictionary<ulong,OverhaulEntry> matchEntries=new Dictionary<ulong,OverhaulEntry>();
 static readonly Dictionary<int,int> seatColors=new Dictionary<int,int>();
 static Dictionary<string,int> occupiedColorOwners=new Dictionary<string,int>(StringComparer.Ordinal);
 static readonly Dictionary<object,string> aiOwnerKeys=new Dictionary<object,string>(ReferenceComparer.Instance);
 static long nextAiOwnerKey;
 internal static readonly string[] TeamNames={"Off","Top vs Bottom","Left vs Right","Odd vs Even"};
 internal static readonly string[] ColorNames={"EDA green","Chosen red","Guard gold","Navy","Orange","Deep blue","Mauve grey","White","Burnt orange","Steel blue","Slate blue","Blue grey","Bright violet","Tangerine","Teal blue","Dark blue","Yellow","Muted blue","Emerald","Light blue","Golden amber","Magenta","Azure","Olive","Cyan","Rose","Lime","Purple","Coral","Mint","Lavender","Sky"};
 internal static readonly Color[] Palette={
  new Color(.078f,.329f,.196f),new Color(.412f,.008f,.008f),RGB(216,184,90),RGB(9,36,103),
  RGB(240,100,0),RGB(31,99,200),RGB(136,120,136),Color.white,RGB(161,36,0),RGB(79,148,205),
  RGB(72,61,139),RGB(97,109,126),RGB(143,103,254),RGB(253,141,23),RGB(37,114,150),RGB(34,62,85),
  RGB(255,254,12),RGB(79,79,119),RGB(46,139,87),RGB(90,181,255),RGB(168,118,3),RGB(134,34,110),
  RGB(0,115,255),RGB(47,105,0),RGB(0,220,220),RGB(245,76,142),RGB(140,235,24),RGB(164,50,245),
  RGB(255,118,100),RGB(96,230,168),RGB(205,161,255),RGB(80,214,255)
 };
 static Color RGB(int r,int g,int b){return new Color(r/255f,g/255f,b/255f,1);}
 internal static SanctuaryLobbyMode Mode=SanctuaryLobbyMode.Vanilla;
 internal static SanctuaryTeamPolicy TeamPolicy=SanctuaryTeamPolicy.Off;
 internal static bool PrivateContracts;
 internal static int Revision;
 static bool initialized,lastInLobby;
 static DateTime nextHello,nextSnapshot,nextRoster,joinDeadline;static int pendingJoinArmy;
 static string rosterFingerprint,lastWarning,lastSentPreference,teamRosterFingerprint;
 static int lastSentPreferenceRevision=-1;
 static bool hostCapabilityAcknowledged;
 static MethodInfo broadcastState,protocolSend,protocolBroadcast,protocolPeek,protocolRead;
 static FieldInfo hostStateField;
 public static string ModeName {get{return Mode==SanctuaryLobbyMode.Overhauled?"Overhauled":"Vanilla";}}

 public static void Initialize(){
  if(initialized)return;initialized=true;
  try{
   var lm=typeof(EM.Network.LobbyManager);hostStateField=lm.GetField("hostState",StaticAll);broadcastState=lm.GetMethod("BroadcastState",StaticAll,null,Type.EmptyTypes,null);
   var lp=FindType("Sanctuary.ModApi.LobbyProtocol");protocolSend=lp.GetMethod("SendToHost",StaticAll);protocolBroadcast=lp.GetMethod("Broadcast",StaticAll);protocolPeek=lp.GetMethod("TryPeekType",StaticAll);protocolRead=lp.GetMethods(StaticAll).Single(x=>x.Name=="TryRead"&&x.IsGenericMethodDefinition);
   string root=AppDomain.CurrentDomain.BaseDirectory,trebuchetPath=ContractFilePath(root,Path.Combine("Sanctuary_Data","Managed","Trebuchet.dll")),modApiPath=ContractFilePath(root,Path.Combine("BepInEx","plugins","Sanctuary.ModApi.dll"));
   PrivateContracts=BuildIdentity.Length==64&&Sha(trebuchetPath)=="a76118c593e6d88e37880f714067e81b595f0edfe5b6abb9c9cdf4aafcede0b2"&&Sha(modApiPath)=="b4066bce4e63fa394e3cebd0f5250c0957b2374ced26435f5882ee2c65172acf"&&Sha(ContractFilePath(root,Path.Combine("SanctuaryMods","ModManager","ModManager.dll")))=="ba2d59b9060faa78dc08eeef6e5eda214ec54629f4dbf106a0a632f100017804"&&Sha(ContractFilePath(root,Path.Combine("BepInEx","plugins","ModLoader.dll")))=="26b5b9939cdce400bb3f71c308565e0fa34cb856c6a04a04f00b5f0d423e16b7"&&hostStateField!=null&&broadcastState!=null&&InMatchColorBridge.ContractAvailable();
  }catch(Exception ex){PrivateContracts=false;Warn("Compatibility guard disabled private lobby features: "+Root(ex));}
  InitializeTransitionGuard();InitializeMatchColorHandoff();PersonalPreferences.Load();
 }
 internal static string ContractFilePath(string baseDirectory,string relativePath){if(string.IsNullOrEmpty(baseDirectory)||string.IsNullOrEmpty(relativePath))throw new ArgumentException("Contract path components are required.");return Path.GetFullPath(Path.Combine(baseDirectory,relativePath));}
 static string Sha(string p){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(p))).Replace("-","").ToLowerInvariant();}
 internal static Type FindType(string n){foreach(var a in AppDomain.CurrentDomain.GetAssemblies()){var t=a.GetType(n,false);if(t!=null)return t;}throw new TypeLoadException(n);}
 internal static string Root(Exception ex){while(ex is TargetInvocationException&&ex.InnerException!=null)ex=ex.InnerException;return ex.GetType().Name+": "+ex.Message;}
 internal static void Warn(string s){if(s==lastWarning)return;lastWarning=s;if(SanctuaryOverhauledLobbyPlugin.Log!=null)SanctuaryOverhauledLobbyPlugin.Log.LogWarning(s);}
 internal static bool InLobby {get{try{return EM.Network.LobbyManager.IsInLobby;}catch{return false;}}}
 internal static bool IsHost {get{try{return EM.Network.LobbyManager.IsCurrentUserHost();}catch{return false;}}}
 internal static ulong Id(EM.Network.PlayerID id){return Convert.ToUInt64(typeof(EM.Network.PlayerID).GetField("value",InstanceAll).GetValue(id));}
 internal static ulong Id(object p){return Id((EM.Network.PlayerID)p.GetType().GetField("id",InstanceAll).GetValue(p));}
 internal static IList Players(){var s=AuthoritativeState;return s==null?null:(IList)s.GetType().GetField("players",InstanceAll).GetValue(s);}
 internal static object PlayerAtSlot(int slot){try{return EM.Network.LobbyManager.CurrentState==null?null:EM.Network.LobbyManager.CurrentState.GetPlayerAtSlot(slot);}catch{return null;}}
 internal static int PlayerType(object player){try{return player==null?0:Convert.ToInt32(player.GetType().GetField("type",InstanceAll).GetValue(player));}catch{return 0;}}
 internal static string PlayerName(object player){try{return player==null?null:Convert.ToString(player.GetType().GetField("name",InstanceAll).GetValue(player));}catch{return null;}}
 internal static int PlayerFaction(object player){try{return player==null?0:Convert.ToInt32(player.GetType().GetField("faction",InstanceAll).GetValue(player));}catch{return 0;}}
 internal static int PlayerTeam(object player){try{return player==null?0:Convert.ToInt32(player.GetType().GetField("team",InstanceAll).GetValue(player));}catch{return 0;}}
 static void SendLobby(string method,params object[] args){typeof(EM.Network.LobbyManager).GetMethods(StaticAll).Single(x=>x.Name==method&&x.GetParameters().Length==args.Length).Invoke(null,args);}
 internal static bool JoinSlot(int slot){
  try{
   var state=AuthoritativeState;var local=LocalPlayer();if(!CanEdit||state==null||local==null||slot<1||slot>state.MaxPlayers||PlayerType(state.GetPlayerAtSlot(slot))!=0)return false;
   SendLobby("SendSetReady",false);
   if(PlayerType(local)==2){if(!ChangeType(EM.Core.PlayerType.Player))return false;pendingJoinArmy=slot;joinDeadline=DateTime.UtcNow.AddSeconds(5);}
   else if(PlayerType(local)==1){pendingJoinArmy=0;SendLobby("SendSetArmyID",(byte)slot);}else return false;
   return true;
  }catch(Exception ex){pendingJoinArmy=0;Warn("JOIN failed: "+Root(ex));return false;}
 }
 static void ContinueJoin(){
  if(pendingJoinArmy==0||!CanEdit)return;var state=AuthoritativeState;var local=LocalPlayer();if(state==null||local==null)return;
  int action=SanctuaryProtocolRules.JoinDecision(PlayerType(local),Convert.ToInt32(local.GetType().GetField("armyID",InstanceAll).GetValue(local)),pendingJoinArmy,PlayerType(state.GetPlayerAtSlot(pendingJoinArmy))==0,DateTime.UtcNow>joinDeadline);
  if(action==0)return;int slot=pendingJoinArmy;pendingJoinArmy=0;
  if(action<0){Chat("Join cancelled: the slot changed or the player transition timed out.");return;}if(action==1)SendLobby("SendSetArmyID",(byte)slot);
 }
 internal static bool AddAi(int slot){try{var state=EM.Network.LobbyManager.CurrentState;if(!IsHost||!CanEdit||LobbyBulkAi.IsRunning||LobbyPresetRuntime.IsApplying||state==null||slot<1||slot>state.MaxPlayers||PlayerType(state.GetPlayerAtSlot(slot))!=0)return false;SendLobby("SendAddAI",(byte)slot);return true;}catch(Exception ex){Warn("Add AI failed: "+Root(ex));return false;}}
 internal static bool RemoveAi(int slot){try{var state=EM.Network.LobbyManager.CurrentState;if(!IsHost||!CanEdit||LobbyBulkAi.IsRunning||LobbyPresetRuntime.IsApplying||state==null||slot<1||slot>state.MaxPlayers||PlayerType(state.GetPlayerAtSlot(slot))!=3)return false;SendLobby("SendRemoveAI",(byte)slot);return true;}catch(Exception ex){Warn("Remove AI failed: "+Root(ex));return false;}}
 internal static bool OpenAiSettings(object player){try{if(!IsHost||PlayerType(player)!=3)return false;var li=FindType("EM.UI.LobbyInterface").GetField("Instance",StaticAll).GetValue(null);if(li==null)return false;li.GetType().GetMethod("OnAISettingsPressedEvent",InstanceAll).Invoke(li,new[]{player});return true;}catch(Exception ex){Warn("AI settings could not open: "+Root(ex));return false;}}
 internal static object LocalPlayer(){var ps=Players();if(ps!=null)foreach(var p in ps)if(p!=null&&EM.Network.LobbyManager.IsLocalPlayer((EM.Network.Lobby.LobbyPlayer)p))return p;return null;}
 internal static object PlayerById(ulong id){var ps=Players();if(ps!=null)foreach(var p in ps)if(p!=null&&Id(p)==id)return p;return null;}

 public static void Tick(){
  bool inLobby=InLobby,host=inLobby&&IsHost;if(!inLobby){pendingJoinArmy=0;pendingType=-1;hostTypeGuardConfirmed=false;if(lastInLobby)ResetSession();lastInLobby=false;return;}if(!lastInLobby){preferenceApplied=false;requestedColor=null;requestedCommander=null;}lastInLobby=true;
  // IsInLobby remains true during loading/play. Do not mutate lobby preferences
  // or roster colors after the loading handoff; retry only an incomplete capture.
  if((int)LobbyManager.lobbyGameStatus>0){HandleMatchColorPhase(LobbyManager.lobbyGameStatus);return;}
  TrackTypeRequest();ContinueJoin();ApplyJoinPreference();TickRandomChoices();
  if(DateTime.UtcNow>=nextRoster){nextRoster=DateTime.UtcNow.AddMilliseconds(500);TrackRoster(host);TrackAutomaticTeams();}
  if(host&&PrivateContracts&&DateTime.UtcNow>=nextSnapshot){nextSnapshot=DateTime.UtcNow.AddSeconds(2);if(Mode==SanctuaryLobbyMode.Vanilla&&CanEdit&&SyncSelected()&&!LobbyUnitRestrictions.RequiresSync)SetSyncSelected(false);BroadcastSnapshot();}
  if(!host&&SanctuaryProtocolRules.ReserveHelloReply(ref nextHello,DateTime.UtcNow))SendClientUpdate();
 }
 static void TrackRoster(bool host){var ps=Players();if(ps==null)return;string fp=string.Join("|",ps.Cast<object>().Where(x=>x!=null).Select(x=>Convert.ToInt32(x.GetType().GetField("type",InstanceAll).GetValue(x))+":"+Id(x)+":"+Convert.ToInt32(x.GetType().GetField("armyID",InstanceAll).GetValue(x))+":"+Convert.ToString(x.GetType().GetField("name",InstanceAll).GetValue(x))));if(fp==rosterFingerprint)return;rosterFingerprint=fp;var live=new HashSet<ulong>(ps.Cast<object>().Where(x=>x!=null&&Convert.ToInt32(x.GetType().GetField("type",InstanceAll).GetValue(x))!=3).Select(Id));bool changed=false;foreach(var id in entries.Keys.Where(x=>!live.Contains(x)).ToArray()){entries.Remove(id);changed=true;}foreach(var id in hellos.Keys.Where(x=>!live.Contains(x)).ToArray()){hellos.Remove(id);changed=true;}if(host&&Mode==SanctuaryLobbyMode.Overhauled){foreach(var pair in entries){var p=PlayerById(pair.Key);if(p!=null&&pair.Value!=null&&pair.Value.army!=Convert.ToInt32(p.GetType().GetField("armyID",InstanceAll).GetValue(p))){changed=true;break;}}changed|=AssignHostDefaults();changed|=RefreshOccupiedSeatColors();if(changed)Revision++;BroadcastSnapshot();}}

 public static bool SetMode(SanctuaryLobbyMode requested){
  if(!CanEdit||!IsHost||!PrivateContracts)return false;if(requested!=SanctuaryLobbyMode.Vanilla&&requested!=SanctuaryLobbyMode.Overhauled)return false;bool requireSync=requested==SanctuaryLobbyMode.Overhauled||LobbyUnitRestrictions.RequiresSync;if(requested==Mode)return SyncSelected()==requireSync||SetSyncSelected(requireSync);if(requested==SanctuaryLobbyMode.Overhauled&&!PrivateContracts){Chat("Overhauled Lobby is unavailable because the installed contract is unsupported.");return false;}
  if(!SetSyncSelected(requireSync))return false;ClearMatchColors();Mode=requested;Revision++;var ps=Players();if(ps!=null)foreach(var p in ps)if(p!=null)Ready(p,false);
  if(requested==SanctuaryLobbyMode.Vanilla){entries.Clear();seatColors.Clear();occupiedColorOwners.Clear();aiOwnerKeys.Clear();ResetUnsupportedFactions();}else{AssignHostDefaults();RefreshOccupiedSeatColors();}BroadcastState();BroadcastSnapshot();return true;
 }
 public static bool SetModeFromPreset(string v){if(!SetMode(string.Equals(v,"Overhauled",StringComparison.OrdinalIgnoreCase)?SanctuaryLobbyMode.Overhauled:SanctuaryLobbyMode.Vanilla))return false;TeamPolicy=SanctuaryTeamPolicy.Off;teamRosterFingerprint=null;BroadcastSnapshot();return true;}
 public static bool PresetModeReady(string v){var requested=string.Equals(v,"Overhauled",StringComparison.OrdinalIgnoreCase)?SanctuaryLobbyMode.Overhauled:SanctuaryLobbyMode.Vanilla;return Mode==requested&&!ApiFlag("Settling",true)&&(requested!=SanctuaryLobbyMode.Overhauled||PrivateContracts);}
 static bool SetSyncSelected(bool selected){try{string blocked=LobbyMergedOptions.BlockReason();if(selected&&blocked!=null){Chat(blocked);return false;}return (bool)FindType("Sanctuary.ModApi.Lobby").GetMethod("SetSelected",StaticAll).Invoke(null,new object[]{SyncModId,selected});}catch(Exception ex){Chat("Mode change failed: "+Root(ex));return false;}}
 static void ResetUnsupportedFactions(){var valid=new HashSet<int>{0,1,2};try{var ft=FindType("Sanctuary.ModApi.Factions");foreach(var x in (IEnumerable)ft.GetProperty("Current",StaticAll).GetValue(null,null))valid.Add(Convert.ToInt32(x.GetType().GetProperty("Value").GetValue(x,null)));}catch{}var ps=Players();if(ps==null)return;foreach(var p in ps){if(p==null)continue;int f=Convert.ToInt32(p.GetType().GetField("faction",InstanceAll).GetValue(p));if(!valid.Contains(f))p.GetType().GetField("faction",InstanceAll).SetValue(p,EM.Core.Faction.EDA);}}
 static void Ready(object p,bool v){p.GetType().GetField("isReady",InstanceAll).SetValue(p,v);}
 static bool AssignHostDefaults(){var p=LocalPlayer();if(p==null)return false;ulong id=Id(p);if(entries.ContainsKey(id))return false;entries[id]=BuildPreference(id,Convert.ToString(p.GetType().GetField("name",InstanceAll).GetValue(p)));ApplyCommander(p,entries[id].commander);return true;}
 static string OwnerKey(object p,int kind){if(kind!=3)return "H:"+Id(p).ToString(CultureInfo.InvariantCulture);string key;if(!aiOwnerKeys.TryGetValue(p,out key)){key="A:"+(++nextAiOwnerKey).ToString(CultureInfo.InvariantCulture);aiOwnerKeys[p]=key;}return key;}
 static bool RefreshOccupiedSeatColors(){
  if(Mode!=SanctuaryLobbyMode.Overhauled){bool had=seatColors.Count>0||occupiedColorOwners.Count>0;seatColors.Clear();occupiedColorOwners.Clear();return had;}
  var ps=Players();if(ps==null)return false;var liveObjects=new HashSet<object>(ps.Cast<object>().Where(x=>x!=null),ReferenceComparer.Instance);foreach(var p in aiOwnerKeys.Keys.Where(x=>!liveObjects.Contains(x)).ToArray())aiOwnerKeys.Remove(p);
  var reserved=new HashSet<int>(entries.Values.Where(x=>x!=null&&x.color>=0&&x.color<Palette.Length).Select(x=>x.color));var owners=new List<OccupiedColorOwner>();
  foreach(var p in ps){if(p==null)continue;int kind=Convert.ToInt32(p.GetType().GetField("type",InstanceAll).GetValue(p)),army=Convert.ToInt32(p.GetType().GetField("armyID",InstanceAll).GetValue(p));if(kind==0||army<1)continue;ulong id=Id(p);if(kind!=3&&entries.ContainsKey(id))continue;owners.Add(new OccupiedColorOwner{key=OwnerKey(p,kind),army=army,preferred=army-1});}
  var reconciled=SanctuaryProtocolRules.ReconcileOccupiedColors(occupiedColorOwners,owners,reserved);var nextSeats=new Dictionary<int,int>();foreach(var owner in owners){int color;if(reconciled.TryGetValue(owner.key,out color))nextSeats[owner.army]=color;}
  bool changed=occupiedColorOwners.Count!=reconciled.Count||occupiedColorOwners.Any(x=>!reconciled.ContainsKey(x.Key)||reconciled[x.Key]!=x.Value)||seatColors.Count!=nextSeats.Count||seatColors.Any(x=>!nextSeats.ContainsKey(x.Key)||nextSeats[x.Key]!=x.Value);occupiedColorOwners=reconciled;seatColors.Clear();foreach(var pair in nextSeats)seatColors[pair.Key]=pair.Value;return changed;
 }
 static OverhaulEntry BuildPreference(ulong id,string name){
  var p=PlayerById(id);int army=p==null?0:Convert.ToInt32(p.GetType().GetField("armyID",InstanceAll).GetValue(p));
  var used=new HashSet<int>(entries.Where(x=>x.Key!=id).Select(x=>x.Value.color));foreach(var pair in seatColors)if(pair.Key!=army)used.Add(pair.Value);
  OverhaulEntry current;entries.TryGetValue(id,out current);
  int c=SanctuaryProtocolRules.PreferredColor(requestedColor??(current==null?PersonalPreferences.Favorite:current.color),current==null?PersonalPreferences.Second:-1,army-1,used);
  string commander=requestedCommander??(current==null?PersonalPreferences.Commander:DefaultCommander(p));
  if(string.IsNullOrEmpty(commander)||commander=="Random"||!CommanderAllowed(commander))commander=p==null?"Random":DefaultCommander(p);
  return new OverhaulEntry{player=id.ToString(CultureInfo.InvariantCulture),name=name,army=army,color=c,commander=commander};
 }
 static void SendClientUpdate(){try{if(!hostCapabilityAcknowledged){protocolSend.Invoke(null,new object[]{HelloType,new OverhaulHello{nonce=connectionNonce,build=BuildIdentity,version=SanctuaryOverhauledLobbyPlugin.PluginVersion,protocol=ProtocolVersion,palette=PaletteIdentity}});return;}if(Mode!=SanctuaryLobbyMode.Overhauled)return;var p=LocalPlayer();if(p==null||PlayerType(p)!=1)return;ulong id=Id(p);var e=BuildPreference(id,Convert.ToString(p.GetType().GetField("name",InstanceAll).GetValue(p)));OverhaulEntry current;if(entries.TryGetValue(id,out current)&&SanctuaryProtocolRules.SameEntry(current,e))return;string fingerprint=e.player+"\n"+e.color.ToString(CultureInfo.InvariantCulture)+"\n"+e.commander;if(fingerprint==lastSentPreference&&lastSentPreferenceRevision==Revision)return;protocolSend.Invoke(null,new object[]{RequestType,new OverhaulRequest{nonce=connectionNonce,player=e.player,color=e.color,commander=e.commander,revision=Revision}});lastSentPreference=fingerprint;lastSentPreferenceRevision=Revision;}catch(Exception ex){Warn("Overhaul client update failed: "+Root(ex));}}
 static bool Peek(NativeArray<byte> data,out byte type){type=0;try{object[] a={data,type};bool ok=(bool)protocolPeek.Invoke(null,a);type=(byte)a[1];return ok;}catch{return false;}}
 static bool Read<T>(NativeArray<byte> data,out T value)where T:class{value=null;try{var m=protocolRead.MakeGenericMethod(typeof(T));object[] a={data,null};bool ok=(bool)m.Invoke(null,a);value=a[1] as T;return ok&&value!=null;}catch(Exception ex){Warn("Protocol read failed: "+Root(ex));return false;}}
 internal static bool OnHostMessage(EM.Network.PlayerID sender,NativeArray<byte> data){
  if(data.Length>0&&data[0]==14)return HandleGuardedRandomLaunch(sender,data.Length);
  if(PrivateContracts&&data.Length>0&&data[0]==4)return HandleNativeTypeChange(sender,data);
  if(!SanctuaryProtocolRules.PacketWithinBounds(data.Length))return true;byte type;if(!Peek(data,out type)||type<RequestType||type>HelloType)return true;if(!IsHost||!PrivateContracts)return false;ulong sid=Id(sender);
  if(type==HelloType){
   OverhaulHello h;var p=PlayerById(sid);if(p==null)return false;bool readable=Read(data,out h)&&p!=null&&SanctuaryProtocolRules.WellFormedHello(h);
   OverhaulHello old;if(!readable||!hellos.TryGetValue(sid,out old)||!SanctuaryProtocolRules.SameHello(old,h))choiceAcknowledgements.Remove(sid);
   if(readable)hellos[sid]=h;else hellos.Remove(sid);
   // A repeated Hello is deliberately side-effect free, but always receives an
   // acknowledgement snapshot so reconnects cannot deadlock waiting for it.
   BroadcastSnapshot();return false;
  }
  if(type==RequestType){
   OverhaulRequest q;OverhaulHello hello;bool acknowledged=hellos.TryGetValue(sid,out hello)&&SanctuaryProtocolRules.ExactHello(hello,SanctuaryOverhauledLobbyPlugin.PluginVersion);
   if(Read(data,out q)&&q.kind!=0){HandleChoiceRequest(sid,q);return false;}
   if(!CanEdit||!Read(data,out q)||Mode!=SanctuaryLobbyMode.Overhauled||!acknowledged||q.nonce!=hello.nonce||!SanctuaryProtocolRules.ValidRequest(q,sid,Revision,acknowledged)){BroadcastSnapshot();return false;}
   var p=PlayerById(sid);if(p==null||Convert.ToInt32(p.GetType().GetField("type",InstanceAll).GetValue(p))!=1){BroadcastSnapshot();return false;}
   bool commanderAllowed=CommanderAllowed(q.commander);
   int senderArmy=Convert.ToInt32(p.GetType().GetField("armyID",InstanceAll).GetValue(p));var used=new HashSet<int>(entries.Where(x=>x.Key!=sid).Select(x=>x.Value.color));foreach(var pair in seatColors)if(pair.Key!=senderArmy)used.Add(pair.Value);
   OverhaulEntry current,accepted;entries.TryGetValue(sid,out current);var decision=SanctuaryProtocolRules.EvaluateRequest(q,sid,Revision,acknowledged,commanderAllowed,used,Convert.ToString(p.GetType().GetField("name",InstanceAll).GetValue(p)),current,out accepted);
   if(decision==SanctuaryProtocolRules.RequestDecision.Reject||decision==SanctuaryProtocolRules.RequestDecision.Unchanged){BroadcastSnapshot();return false;}
   accepted.army=senderArmy;entries[sid]=accepted;RefreshOccupiedSeatColors();ApplyCommander(p,q.commander);Ready(p,false);Revision++;BroadcastState();BroadcastSnapshot();return false;
  }
  return false;
 }
 internal static bool OnClientMessage(NativeArray<byte> data){
  if(!SanctuaryProtocolRules.PacketWithinBounds(data.Length))return true;byte type;if(!Peek(data,out type)||type!=SnapshotType)return true;if(IsHost||!PrivateContracts)return false;OverhaulSnapshot s;if(!Read(data,out s))return false;
  bool exactIdentity=s!=null&&s.protocol==ProtocolVersion&&s.version==SanctuaryOverhauledLobbyPlugin.PluginVersion&&s.build==BuildIdentity&&s.palette==PaletteIdentity;
  if(!exactIdentity){ClearTransitionCapability();ClearMatchColors();ClearRandomChoices();Mode=SanctuaryLobbyMode.Vanilla;Revision=0;entries.Clear();seatColors.Clear();occupiedColorOwners.Clear();aiOwnerKeys.Clear();hostCapabilityAcknowledged=false;lastSentPreference=null;lastSentPreferenceRevision=-1;TeamPolicy=SanctuaryTeamPolicy.Off;if(SanctuaryProtocolRules.ReserveHelloReply(ref nextHello,DateTime.UtcNow))SendClientUpdate();return false;}
  if((int)LobbyManager.lobbyGameStatus>0)return false; // Freeze visible intent throughout launch/loading.
  if(!SanctuaryProtocolRules.ValidNonce(s.hostNonce)||s.acknowledged==null||s.capabilityNonces==null||s.capabilityNonces.Length!=s.acknowledged.Length||s.capabilityNonces.Any(n=>!SanctuaryProtocolRules.ValidNonce(n)))return false;
  PrepareSnapshotContext(s.hostNonce);
  if(!SanctuaryProtocolRules.ValidSnapshotEnvelope(s,SanctuaryOverhauledLobbyPlugin.PluginVersion,Revision)){Warn("Ignored malformed or stale Overhauled Lobby snapshot.");return false;}
  var incoming=new Dictionary<ulong,OverhaulEntry>();var incomingSeats=new Dictionary<int,int>();var colors=new HashSet<int>();var armies=new HashSet<int>();
  if(s.players!=null)foreach(var e in s.players){ulong id;if(!SanctuaryProtocolRules.ValidSnapshotEntry(e)||!ulong.TryParse(e.player,NumberStyles.None,CultureInfo.InvariantCulture,out id)||incoming.ContainsKey(id)||!colors.Add(e.color)||(e.army>0&&!armies.Add(e.army))){Warn("Ignored malformed Overhauled Lobby player snapshot.");return false;}incoming[id]=e;}
  if(s.seats!=null)foreach(var seat in s.seats){if(seat==null||seat.army<1||seat.army>SanctuaryProtocolRules.MaxPlayers||seat.color<0||seat.color>=Palette.Length||incomingSeats.ContainsKey(seat.army)||!armies.Add(seat.army)||!colors.Add(seat.color)){Warn("Ignored malformed Overhauled Lobby occupied-seat colors.");return false;}incomingSeats[seat.army]=seat.color;}
  var acknowledgements=new HashSet<ulong>();if(s.acknowledged!=null)foreach(var raw in s.acknowledged){ulong id;if(string.IsNullOrEmpty(raw)||raw.Length>SanctuaryProtocolRules.MaxPlayerIdLength||!ulong.TryParse(raw,NumberStyles.None,CultureInfo.InvariantCulture,out id)||!acknowledgements.Add(id)){Warn("Ignored malformed Overhauled Lobby acknowledgement snapshot.");return false;}}
  if(!AcceptChoiceSnapshot(s)){Warn("Ignored invalid Random intent snapshot.");return false;}
  ConfirmTransitionHost();Mode=(SanctuaryLobbyMode)s.mode;TeamPolicy=(SanctuaryTeamPolicy)s.teams;Revision=s.revision;entries.Clear();foreach(var pair in incoming)entries[pair.Key]=pair.Value;seatColors.Clear();foreach(var pair in incomingSeats)seatColors[pair.Key]=pair.Value;
  var local=LocalPlayer();int capabilityIndex=local==null?-1:Array.IndexOf(s.acknowledged,Id(local).ToString(CultureInfo.InvariantCulture));hostCapabilityAcknowledged=capabilityIndex>=0&&s.capabilityNonces[capabilityIndex]==connectionNonce;OverhaulEntry echoed;if(local!=null&&entries.TryGetValue(Id(local),out echoed)){if(requestedColor==echoed.color)requestedColor=null;if(requestedCommander==echoed.commander)requestedCommander=null;}
  try{SendChoiceAcknowledgement();}catch(Exception ex){Warn("Random acknowledgement failed: "+Root(ex));}
  return false;
 }
 static void RefreshAuthoritativeEntryArmies(){foreach(var pair in entries){var p=PlayerById(pair.Key);if(p==null||pair.Value==null)continue;pair.Value.army=Convert.ToInt32(p.GetType().GetField("armyID",InstanceAll).GetValue(p));pair.Value.name=Convert.ToString(p.GetType().GetField("name",InstanceAll).GetValue(p));}}
 static void BroadcastSnapshot(){
  if(!IsHost||protocolBroadcast==null)return;
  try{
   RefreshAuthoritativeEntryArmies();if(RefreshOccupiedSeatColors())Revision++;var choices=ChoiceSnapshot();
   var capabilities=hellos.Where(x=>SanctuaryProtocolRules.ExactHello(x.Value,SanctuaryOverhauledLobbyPlugin.PluginVersion)).OrderBy(x=>x.Key).ToArray();
   var snapshot=new OverhaulSnapshot{hostNonce=connectionNonce,capabilityNonces=capabilities.Select(x=>x.Value.nonce).ToArray(),choices=choices,choiceRevision=ChoiceRevision,build=BuildIdentity,teams=(int)TeamPolicy,version=SanctuaryOverhauledLobbyPlugin.PluginVersion,protocol=ProtocolVersion,palette=PaletteIdentity,revision=Revision,mode=(int)Mode,players=entries.Values.ToArray(),seats=seatColors.Select(x=>new OverhaulSeatColor{army=x.Key,color=x.Value}).ToArray(),acknowledged=capabilities.Select(x=>x.Key.ToString(CultureInfo.InvariantCulture)).ToArray()};
   // Leave room for the native protocol envelope and never emit a snapshot
   // that our bounded receiver cannot read, including long UTF-8 identities.
   if(System.Text.Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(snapshot))+16>SanctuaryProtocolRules.MaxPacketBytes){snapshotBlockReason="Lobby synchronization exceeds the safe packet limit. Reduce the roster or use shorter names.";Warn(snapshotBlockReason);return;}
   snapshotBlockReason=null;protocolBroadcast.Invoke(null,new object[]{SnapshotType,snapshot});
  }catch(Exception ex){snapshotBlockReason="Lobby snapshot could not be synchronized; start is blocked.";Warn("Snapshot failed: "+Root(ex));}
 }
 static void BroadcastState(){if(!PrivateContracts||!IsHost)return;try{broadcastState.Invoke(null,null);}catch(Exception ex){Warn("Lobby broadcast failed: "+Root(ex));}}

 public static Color ColorFor(ulong player,int army){OverhaulEntry e;int color;if(Mode==SanctuaryLobbyMode.Overhauled&&entries.TryGetValue(player,out e)&&e.color>=0&&e.color<Palette.Length)return Palette[e.color];if(Mode==SanctuaryLobbyMode.Overhauled&&seatColors.TryGetValue(army,out color)&&color>=0&&color<Palette.Length)return Palette[color];int index=army-1;return index>=0&&index<Palette.Length?Palette[index]:new Color(.45f,.48f,.52f);}
 public static int ColorIndex(ulong player,int army){OverhaulEntry e;int color;if(Mode==SanctuaryLobbyMode.Overhauled&&entries.TryGetValue(player,out e))return e.color;if(Mode==SanctuaryLobbyMode.Overhauled&&seatColors.TryGetValue(army,out color))return color;return army-1;}
 internal static int[] AvailableColors(int keepA,int keepB){var claimed=new HashSet<int>(entries.Values.Select(x=>x.color));foreach(var color in seatColors.Values)claimed.Add(color);int owned=-1;var local=LocalPlayer();OverhaulEntry own;if(local!=null&&entries.TryGetValue(Id(local),out own))owned=own.color;var result=new List<int>{-1};for(int i=0;i<Palette.Length;i++)if(!claimed.Contains(i)||i==owned)result.Add(i);return result.ToArray();}
 public static bool Move(ulong player,int destination){
  if(!IsHost||!PrivateContracts||!CanEdit)return false;var state=AuthoritativeState;if(state==null)return false;
  var src=state.GetPlayers(true,true).FirstOrDefault(p=>Id(p)==player&&p.type==EM.Core.PlayerType.Player);
  if(src==null||EM.Network.LobbyManager.IsLocalPlayer(src))return false;
  string error;if(!LobbyAuthorityRules.Swap(state,src,destination,out error)){Chat(error);return false;}
  RefreshOccupiedSeatColors();Revision++;BroadcastState();BroadcastSnapshot();return true;
 }
 public static bool Kick(ulong player){if(!IsHost||!CanEdit)return false;var p=PlayerById(player) as EM.Network.Lobby.LobbyPlayer;if(p==null||EM.Network.LobbyManager.IsLocalPlayer(p)||!p.IsHumanPlayer)return false;var pid=(EM.Network.PlayerID)p.GetType().GetField("id",InstanceAll).GetValue(p);typeof(EM.Network.LobbyManager).GetMethod("SendKick",StaticAll).Invoke(null,new object[]{pid});return true;}
 public static bool ApplyTeamPolicy(SanctuaryTeamPolicy policy){
  if(!IsHost||!PrivateContracts||!CanEdit||!Enum.IsDefined(typeof(SanctuaryTeamPolicy),policy))return false;
  if(policy==SanctuaryTeamPolicy.Off){if(TeamPolicy==policy)return true;TeamPolicy=policy;teamRosterFingerprint=null;Revision++;BroadcastSnapshot();return true;}
  var state=AuthoritativeState;if(state==null)return false;var map=LobbyMapData.Read(AppDomain.CurrentDomain.BaseDirectory,state.mapPath);Dictionary<int,int> teams;bool changed;string reason;
  if(!map.Teams(policy,out teams)||!LobbyTeamRules.Apply(state,teams,out changed,out reason)){Chat("Spawn positions are unavailable or incomplete; automatic teams are Off.");if(TeamPolicy!=SanctuaryTeamPolicy.Off){TeamPolicy=SanctuaryTeamPolicy.Off;Revision++;BroadcastSnapshot();}return false;}
  bool policyChanged=TeamPolicy!=policy;TeamPolicy=policy;teamRosterFingerprint=TeamFingerprint(state);
  if(policyChanged){foreach(var p in state.GetPlayers(true,true))p.isReady=false;}
  if(changed||policyChanged){Revision++;BroadcastState();BroadcastSnapshot();}return true;
 }
 static string TeamFingerprint(EM.Network.Lobby.LobbyState state){return state.mapPath+"|"+TeamPolicy+"|"+string.Join(";",state.GetPlayers(true,true).Select(p=>p.type+":"+Id(p)+":"+p.armyID+":"+p.team).ToArray());}
 static void TrackAutomaticTeams(){
  if(TeamPolicy==SanctuaryTeamPolicy.Off){teamRosterFingerprint=null;return;}
  if(!IsHost||!PrivateContracts||!CanEdit)return;var state=AuthoritativeState;if(state==null)return;
  string fingerprint=TeamFingerprint(state);
  if(fingerprint==teamRosterFingerprint)return;
  ApplyTeamPolicy(TeamPolicy);
 }
 internal static string DefaultCommander(object p){var choice=Choices().FirstOrDefault(x=>x.Value==PlayerFaction(p));return choice==null?"Random":choice.Identity;}
 internal static string CommanderFor(object p){return p==null?"-":DefaultCommander(p);}
 internal static bool SelectCommander(string identity){if(string.IsNullOrEmpty(identity)||!CommanderAllowed(identity))return false;PersonalPreferences.Commander=identity;PersonalPreferences.Save();try{if(Mode==SanctuaryLobbyMode.Vanilla){int value;if(identity=="stock:eda:uel0000")value=0;else if(identity=="stock:chosen:ucl0000")value=1;else if(identity=="stock:guard:ugl0000")value=2;else{var f=FindType("Sanctuary.ModApi.Factions");if(!SanctuaryProtocolRules.TryResolveRegisteredCommander((IEnumerable)f.GetProperty("Current",StaticAll).GetValue(null,null),identity,out value))return false;}SendLobby("SendSetReady",false);SendLobby("SendSetFaction",(EM.Core.Faction)value);}else{lastSentPreference=null;nextHello=DateTime.MinValue;}return true;}catch(Exception ex){Warn("Commander selection failed: "+Root(ex));return false;}}
 internal static string[] CommanderCatalog(){return Choices().Select(c=>c.Identity).Distinct(StringComparer.Ordinal).ToArray();}
 internal static string[] FactionCatalog(){return new[]{"Random"}.Concat(Choices().Select(c=>c.FactionName)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();}
 static bool CommanderAllowed(string id){return string.IsNullOrEmpty(id)||id=="Random"||CommanderCatalog().Contains(id);}
 static void ApplyCommander(object p,string id){if(HasRandomIntent(p)||string.IsNullOrEmpty(id)||id=="Random")return;try{int f;if(id=="stock:eda:uel0000")f=0;else if(id=="stock:chosen:ucl0000")f=1;else if(id=="stock:guard:ugl0000")f=2;else{var factions=FindType("Sanctuary.ModApi.Factions");if(!SanctuaryProtocolRules.TryResolveRegisteredCommander((IEnumerable)factions.GetProperty("Current",StaticAll).GetValue(null,null),id,out f))return;}p.GetType().GetField("faction",InstanceAll).SetValue(p,Enum.ToObject(typeof(EM.Core.Faction),f));}catch(Exception ex){Warn("Commander selection could not be applied: "+Root(ex));}}
 public static string StartBlockReason(){if(IsHost&&LobbyBulkAi.IsRunning)return "Wait for the AI batch to finish.";if(IsHost&&LobbyPresetRuntime.IsApplying)return "Wait for the lobby preset to finish applying.";return (IsHost?LobbyMergedOptions.BlockReason():null)??(IsHost?snapshotBlockReason:null)??RandomStartBlockReason()??LobbyUnitRestrictions.StartBlockReason()??OverhaulStartBlockReason();}
 static string OverhaulStartBlockReason(){if(Mode!=SanctuaryLobbyMode.Overhauled||!IsHost)return null;if(!PrivateContracts)return "Overhauled Lobby compatibility guard failed. Switch to Vanilla Lobby.";string apiReason=ApiStartReason();if(!SyncSelected())return "Overhauled Lobby gameplay features are not selected. Switch to Vanilla Lobby or select Overhauled Lobby again.";var ps=Players();if(ps==null)return "Overhauled Lobby state is unavailable.";foreach(var p in ps){if(p==null||Convert.ToInt32(p.GetType().GetField("type",InstanceAll).GetValue(p))!=1||EM.Network.LobbyManager.IsLocalPlayer((EM.Network.Lobby.LobbyPlayer)p))continue;ulong id=Id(p);OverhaulHello h;string name=Convert.ToString(p.GetType().GetField("name",InstanceAll).GetValue(p));if(!hellos.TryGetValue(id,out h))return name+" does not have Overhauled Lobby installed. Switch to Vanilla Lobby or ask them to install it.";if(!SanctuaryProtocolRules.ExactHello(h,SanctuaryOverhauledLobbyPlugin.PluginVersion))return name+" has a different Overhauled Lobby version or copy.";if(!entries.ContainsKey(id))return name+" has not finished synchronizing Overhauled Lobby settings.";}if(ps.Cast<object>().Count(p=>p!=null&&(PlayerType(p)==1||PlayerType(p)==3))>Palette.Length)return "Overhauled Lobby supports at most "+Palette.Length+" occupied colors.";if(entries.Values.Any(e=>e.color<0||e.color>=Palette.Length))return "Overhauled Lobby has an invalid color assignment.";if(entries.Values.Select(e=>e.color).Concat(seatColors.Values).GroupBy(c=>c).Any(g=>g.Count()>1))return "Overhauled Lobby colors are not unique; wait for synchronization.";Dictionary<ulong,OverhaulEntry> finalColors;string colorError;if(!MatchColorRules.TryCapture(AuthoritativeState,entries,seatColors,out finalColors,out colorError))return colorError;return apiReason;}
 internal static void Chat(string s){try{var li=FindType("EM.UI.LobbyInterface").GetField("Instance",StaticAll).GetValue(null);if(li!=null)li.GetType().GetMethod("AddErrorChatMessage",InstanceAll).Invoke(li,new object[]{s});}catch{}Warn(s);}
 internal static string MatchColorScript(){if(!matchColorsCaptured)return null;string script,error;var assignments=matchEntries.Values.Select(e=>new MatchColorAssignment(e.army,Palette[e.color])).ToArray();if(!InMatchColorBridge.TryBuildScript(assignments,out script,out error)){Warn("Match colors unavailable: "+error);return null;}return script;}
 public static void ResetSession(){LobbyBulkAi.Reset();LobbyUnitRestrictions.Reset();LobbyRosterNativeData.ResetVisualCache();ClearRandomChoices();connectionNonce=Guid.NewGuid().ToString("N");ClearTransitionCapability();ClearMatchColors();entries.Clear();hellos.Clear();seatColors.Clear();occupiedColorOwners.Clear();aiOwnerKeys.Clear();nextAiOwnerKey=0;requestedColor=null;requestedCommander=null;Mode=SanctuaryLobbyMode.Vanilla;TeamPolicy=SanctuaryTeamPolicy.Off;Revision=0;rosterFingerprint=teamRosterFingerprint=null;hostCapabilityAcknowledged=false;lastSentPreference=null;lastSentPreferenceRevision=-1;nextHello=nextSnapshot=DateTime.MinValue;}
}

public static class PersonalPreferences {
 public static int Favorite=-1,Second=-1;public static string Faction="Random",Commander="Random";
 static string PathName {get{return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"SanctuaryMods","SanctuaryOverhauledLobby","settings.ini");}}
 public static void Load(){try{if(!File.Exists(PathName))return;foreach(var raw in File.ReadAllLines(PathName)){int eq=raw.IndexOf('=');if(eq<1)continue;string k=raw.Substring(0,eq).Trim(),v=raw.Substring(eq+1).Trim();int n;if(k=="Favorite"&&int.TryParse(v,out n))Favorite=n;else if(k=="Second"&&int.TryParse(v,out n))Second=n;else if(k=="Faction")Faction=v;else if(k=="Commander")Commander=v;}}catch{Favorite=Second=-1;Faction=Commander="Random";}}
 public static void Save(){string dir=Path.GetDirectoryName(PathName);Directory.CreateDirectory(dir);string text="Favorite="+Favorite+"\nSecond="+Second+"\nFaction="+Faction+"\nCommander="+Commander+"\n";string tmp=PathName+".tmp";File.WriteAllText(tmp,text);if(File.Exists(PathName))File.Replace(tmp,PathName,PathName+".bak");else File.Move(tmp,PathName);}
}
