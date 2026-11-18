using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
public static partial class LobbyPresetRuntime {
 sealed class Seat {
  public byte Slot;
  public sbyte Army;
  public byte Team;
  public string Faction;
  public bool Host;
  public bool AiSpecified;
  public string AiMod,AiKey;
  public string Commander;
  public object ResolvedFaction;
  public object ResolvedCommanderFaction;
 }
 sealed class Preset {
  public string Name;
  public int Number;
  public bool ClearOtherAI=true;
  public string Mode="Vanilla";
  public readonly List<Seat> Seats=new List<Seat>();
 }

 static readonly BindingFlags StaticAll=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
 static readonly BindingFlags InstanceAll=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static DateTime nextTick=DateTime.MinValue;
 static Preset pending;
 public static bool IsApplying {get{return pending!=null;}}
 static int phase;
 static DateTime deadline;
 static readonly Queue<byte> removals=new Queue<byte>();
 static readonly Queue<byte> additions=new Queue<byte>();
 static readonly Queue<Seat> updates=new Queue<Seat>();
 static readonly Queue<Seat> commanderUpdates=new Queue<Seat>();
 static string lastError;
 static object selectorObject;
 static object selectorDropdown;
 static bool selectorSuppress;
 static readonly List<int> selectorPresetNumbers=new List<int>();

 public static void Tick() {
  try {
   if(DateTime.UtcNow<nextTick) return;
   nextTick=DateTime.UtcNow.AddMilliseconds(pending==null?40:175);
   bool inLobby=InLobby();
   bool isHost=inLobby && IsHost();
  // The integrated lobby owns preset presentation inside Match Settings.
  // Keep the legacy cloned bottom-bar selector retired to avoid two UIs.
  EnsureSelector(inLobby && isHost && !SanctuaryLobbyUi.IsActive);
   if(!isHost) CloseEditor();
   RefreshUpdateAvailability();
   if(pending!=null) { ContinueApply(); return; }
   if(!inLobby || !isHost || !ControlHeld()) return;
   for(int n=1;n<=9;n++) if(KeyDown("Alpha"+n)) { Begin(n); return; }
  } catch(Exception ex) {
   string message=RootMessage(ex);
   if(message!=lastError) { lastError=message; Console.WriteLine("[Lobby Presets] "+message); }
   if(pending!=null) Fail("Preset stopped: "+message);
  }
 }

 public sealed class PresetUiEntry { public int Number;public string Name;public string Mode; }
 public static PresetUiEntry[] PresetEntries(){try{return LoadPresets().OrderBy(x=>x.Number).Select(x=>new PresetUiEntry{Number=x.Number,Name=x.Name,Mode=x.Mode}).ToArray();}catch(Exception ex){lastError=RootMessage(ex);return new PresetUiEntry[0];}}
 public static string ApplyPresetFromUi(int number){try{Begin(number);return pending!=null?"Applying preset...":"Preset was not started; see the lobby message.";}catch(Exception ex){return RootMessage(ex);}}
 public static string CreatePresetFromUi(string name){try{RequireEditable();var snapshot=CaptureCurrentLayout();var presets=LoadPresets();snapshot.Number=1;while(presets.Any(p=>p.Number==snapshot.Number))snapshot.Number++;snapshot.Name=ValidName(name,presets,0);presets.Add(snapshot);WritePresets(presets);return "Saved '"+snapshot.Name+"'.";}catch(Exception ex){return RootMessage(ex);}}
 public static string RenamePresetFromUi(int number,string name){try{RequireEditable();var presets=LoadPresets();var p=presets.SingleOrDefault(x=>x.Number==number);if(p==null)throw new InvalidOperationException("That preset no longer exists.");p.Name=ValidName(name,presets,number);WritePresets(presets);return "Renamed preset to '"+p.Name+"'.";}catch(Exception ex){return RootMessage(ex);}}
 public static string UpdatePresetFromUi(int number){try{var selection=SelectPreset(number);bool changed=UpdatePreset(selection);return changed?"Updated '"+selection.Name+"'.":"'"+selection.Name+"' already matches the current lobby.";}catch(Exception ex){return RootMessage(ex);}}
 public static string DeletePresetFromUi(int number){try{RequireEditable();var presets=LoadPresets();var p=presets.SingleOrDefault(x=>x.Number==number);if(p==null)throw new InvalidOperationException("That preset no longer exists.");presets.Remove(p);WritePresets(presets);return "Deleted '"+p.Name+"'. Previous configuration is in presets.ini.bak.";}catch(Exception ex){return RootMessage(ex);}}


 static void Fail(string message) { pending=null; removals.Clear(); additions.Clear(); updates.Clear(); commanderUpdates.Clear(); SetSelectorInteractable(true); ResetSelector(); Chat(message); }

 public static void OnPresetSelected(int index) {
  if(selectorSuppress || index<=0 || index>selectorPresetNumbers.Count || pending!=null) return;
  int number=selectorPresetNumbers[index-1];
  if(number==0) { ResetSelector(); ShowCreate(); return; }
  Begin(number);
 }

 static void EnsureSelector(bool visible) {
  if(selectorObject!=null) {
   try { SetActive(selectorObject,visible); return; }
   catch { selectorObject=null; selectorDropdown=null; selectorPresetNumbers.Clear(); }
  }
  if(!visible) return;
  object lobby=FindType("EM.UI.LobbyInterface").GetField("Instance",StaticAll).GetValue(null);
  if(lobby==null) return;
  var lobbyType=lobby.GetType();
  object settings=lobbyType.GetField("settingsButton",InstanceAll).GetValue(lobby);
  var items=(IList)lobbyType.GetField("playerListItems",InstanceAll).GetValue(lobby);
  if(settings==null || items==null || items.Count==0) return;
  object sourceDropdown=null;
  foreach(object item in items) {
   if(item==null) continue;
   sourceDropdown=item.GetType().GetField("factionDropdown",InstanceAll).GetValue(item);
   if(sourceDropdown!=null) break;
  }
  if(sourceDropdown==null) return;
  object sourceGameObject=Property(sourceDropdown,"gameObject");
  object settingsGameObject=Property(settings,"gameObject");
  object settingsTransform=Property(settingsGameObject,"transform");
  object parent=Property(settingsTransform,"parent");
  // Match the working Dycom commander selector: clone the already-awake Beam
  // dropdown beneath an inactive staging object, sanitize its copied popup
  // state, and only then attach it to the live lobby canvas. Cloning directly
  // into the active lobby leaves the copied Canvas/EventTrigger intercepting
  // the pointer, which makes the selector visible but not clickable.
  object staging=Activator.CreateInstance(sourceGameObject.GetType(),new object[]{"Lobby preset selector staging"});
  SetActive(staging,false);
  object clone=InstantiateGameObject(sourceGameObject,Property(staging,"transform"));
  SetActive(clone,false);
  clone.GetType().GetProperty("name",InstanceAll).SetValue(clone,"Lobby Preset Selector",null);
  selectorObject=clone;
  selectorDropdown=clone.GetType().GetMethod("GetComponent",InstanceAll,null,new[]{typeof(Type)},null).Invoke(clone,new object[]{sourceDropdown.GetType()});
  if(selectorDropdown==null) throw new InvalidOperationException("The cloned lobby preset dropdown is missing its Dropdown component");
  var dropdownType=selectorDropdown.GetType();
  SetBoolField(selectorDropdown,"setHighPriority",false);
  SetBoolField(selectorDropdown,"initOnEnable",false);
  SetIntField(selectorDropdown,"selectedItemIndex",0);
  SetBoolField(selectorDropdown,"isOn",false);
  var unityObject=FindType("UnityEngine.Object");
  var triggerField=dropdownType.GetField("triggerObject",InstanceAll);
  object triggerObject=triggerField==null?null:triggerField.GetValue(selectorDropdown);
  if(triggerObject!=null) {
   var triggerType=FindType("UnityEngine.EventSystems.EventTrigger");
   var oldTrigger=triggerObject.GetType().GetMethod("GetComponent",InstanceAll,null,new[]{typeof(Type)},null).Invoke(triggerObject,new object[]{triggerType});
   if(oldTrigger!=null) unityObject.GetMethod("DestroyImmediate",StaticAll,null,new[]{unityObject},null).Invoke(null,new[]{oldTrigger});
   SetActive(triggerObject,false);
  }
  var list=(IList)dropdownType.GetField("items",InstanceAll).GetValue(selectorDropdown);
  list.Clear();
  var create=dropdownType.GetMethods(InstanceAll).Single(x=>x.Name=="CreateNewItem" && x.GetParameters().Length==1 && x.GetParameters()[0].ParameterType==typeof(string));
  create.Invoke(selectorDropdown,new object[]{"LOBBY PRESET..."});
  selectorPresetNumbers.Clear();
  foreach(var preset in LoadPresets().OrderBy(x=>x.Number)) {
   create.Invoke(selectorDropdown,new object[]{preset.Name});
   selectorPresetNumbers.Add(preset.Number);
  }
  create.Invoke(selectorDropdown,new object[]{"CREATE NEW PRESET..."});
  selectorPresetNumbers.Add(0);
  SetBoolField(selectorDropdown,"saveSelected",false);
  SetBoolField(selectorDropdown,"invokeOnEnable",false);
  SetBoolField(selectorDropdown,"updateOnEnable",false);
  var changed=dropdownType.GetField("onValueChanged",InstanceAll).GetValue(selectorDropdown);
  changed.GetType().GetMethod("RemoveAllListeners",InstanceAll).Invoke(changed,null);
  var add=changed.GetType().GetMethods(InstanceAll).Single(x=>x.Name=="AddListener" && x.GetParameters().Length==1);
  var callback=typeof(LobbyPresetRuntime).GetMethod("OnPresetSelected",BindingFlags.Public|BindingFlags.Static);
  var listener=Delegate.CreateDelegate(add.GetParameters()[0].ParameterType,callback);
  add.Invoke(changed,new object[]{listener});
  selectorSuppress=true;
  dropdownType.GetMethod("Initialize",InstanceAll).Invoke(selectorDropdown,null);
  dropdownType.GetMethod("SetDropdownIndex",InstanceAll).Invoke(selectorDropdown,new object[]{0});
  ConfigurePopupAbove(selectorDropdown);
  AttachPresetContextMenus();
  selectorSuppress=false;
  object cloneTransform=Property(clone,"transform");
  cloneTransform.GetType().GetMethod("SetParent",InstanceAll,null,new[]{FindType("UnityEngine.Transform"),typeof(bool)},null)
   .Invoke(cloneTransform,new[]{parent,(object)false});
  ConfigureSelectorLayout(cloneTransform,settingsTransform);
  unityObject.GetMethod("Destroy",StaticAll,null,new[]{unityObject},null).Invoke(null,new[]{staging});
  var lastSibling=cloneTransform.GetType().GetMethod("SetAsLastSibling",InstanceAll);
  if(lastSibling!=null) lastSibling.Invoke(cloneTransform,null);
  SetSelectorInteractable(true);
  SetActive(selectorObject,true);
  Console.WriteLine("[Lobby Presets] Added the preset selector inside compact Match Settings");
 }

 static void ConfigurePopupAbove(object dropdown) {
  var dropdownType=dropdown.GetType();
  var direction=dropdownType.GetField("panelDirection",InstanceAll);
  if(direction!=null) direction.SetValue(dropdown,Enum.Parse(direction.FieldType,"Bottom"));
  object listRect=dropdownType.GetField("listRect",InstanceAll).GetValue(dropdown);
  if(listRect==null) throw new InvalidOperationException("The lobby preset popup RectTransform is unavailable");
  Type vector=listRect.GetType().GetProperty("anchorMin",InstanceAll).PropertyType;
  listRect.GetType().GetProperty("anchorMin",InstanceAll).SetValue(listRect,Activator.CreateInstance(vector,new object[]{0f,0f}),null);
  listRect.GetType().GetProperty("anchorMax",InstanceAll).SetValue(listRect,Activator.CreateInstance(vector,new object[]{1f,0f}),null);
  listRect.GetType().GetProperty("pivot",InstanceAll).SetValue(listRect,Activator.CreateInstance(vector,new object[]{0.5f,1f}),null);
  listRect.GetType().GetProperty("anchoredPosition",InstanceAll).SetValue(listRect,Activator.CreateInstance(vector,new object[]{0f,0f}),null);
 }

 static void ConfigureSelectorLayout(object selectorTransform,object settingsTransform) {
  CopyProperty(settingsTransform,selectorTransform,"localScale");CopyProperty(settingsTransform,selectorTransform,"localRotation");
  var anchored=selectorTransform.GetType().GetProperty("anchoredPosition",InstanceAll);
  var size=selectorTransform.GetType().GetProperty("sizeDelta",InstanceAll);
  object settingsSize=settingsTransform.GetType().GetProperty("sizeDelta",InstanceAll).GetValue(settingsTransform,null);
  Type vector=anchored.PropertyType;
  float width=Math.Max(420f,Convert.ToSingle(vector.GetField("x").GetValue(settingsSize)));
  float height=Math.Max(48f,Convert.ToSingle(vector.GetField("y").GetValue(settingsSize)));
  foreach(string property in new[]{"anchorMin","anchorMax","pivot"})selectorTransform.GetType().GetProperty(property,InstanceAll).SetValue(selectorTransform,Activator.CreateInstance(vector,new object[]{1f,0f}),null);
  // The compact settings panel lives below the map on the right. Keep this
  // native themed control in its reserved preset area instead of the stock bar.
  anchored.SetValue(selectorTransform,Activator.CreateInstance(vector,new object[]{-22f,190f}),null);
  size.SetValue(selectorTransform,Activator.CreateInstance(vector,new object[]{width,height}),null);
  var sibling=selectorTransform.GetType().GetMethod("SetSiblingIndex",InstanceAll);
  var getSibling=settingsTransform.GetType().GetMethod("GetSiblingIndex",InstanceAll);
  if(sibling!=null && getSibling!=null) sibling.Invoke(selectorTransform,new[]{getSibling.Invoke(settingsTransform,null)});
 }

 static object InstantiateGameObject(object original,object parent) {
  var unityObject=FindType("UnityEngine.Object");
  var method=unityObject.GetMethods(StaticAll).First(x=>x.Name=="Instantiate" && !x.IsGenericMethod && x.GetParameters().Length==3
    && x.GetParameters()[1].ParameterType.FullName=="UnityEngine.Transform" && x.GetParameters()[2].ParameterType==typeof(bool));
  return method.Invoke(null,new[]{original,parent,(object)false});
 }
 static void CopyProperty(object source,object target,string name) {
  var sourceProperty=source.GetType().GetProperty(name,InstanceAll);
  var targetProperty=target.GetType().GetProperty(name,InstanceAll);
  if(sourceProperty!=null && targetProperty!=null && targetProperty.CanWrite) targetProperty.SetValue(target,sourceProperty.GetValue(source,null),null);
 }
 static object Property(object value,string name) { return value.GetType().GetProperty(name,InstanceAll).GetValue(value,null); }
 static void SetBoolField(object value,string name,bool setting) { var field=value.GetType().GetField(name,InstanceAll); if(field!=null) field.SetValue(value,setting); }
 static void SetIntField(object value,string name,int setting) { var field=value.GetType().GetField(name,InstanceAll); if(field!=null) field.SetValue(value,setting); }
 static void SetActive(object gameObject,bool active) { gameObject.GetType().GetMethod("SetActive",InstanceAll).Invoke(gameObject,new object[]{active}); }
 static void SetSelectorInteractable(bool enabled) {
  if(selectorDropdown==null) return;
  try { selectorDropdown.GetType().GetMethod("Interactable",InstanceAll).Invoke(selectorDropdown,new object[]{enabled}); }
  catch { selectorObject=null; selectorDropdown=null; selectorPresetNumbers.Clear(); }
 }
 static void ResetSelector() {
  if(selectorDropdown==null) return;
  try { selectorSuppress=true; selectorDropdown.GetType().GetMethod("SetDropdownIndex",InstanceAll).Invoke(selectorDropdown,new object[]{0}); }
  finally { selectorSuppress=false; }
 }

 static List<Preset> LoadPresets() {
  string path=PresetPath;
  if(!File.Exists(path)) throw new FileNotFoundException("presets.ini is missing",path);
  var result=new List<Preset>(); Preset current=null;
  foreach(string raw in File.ReadAllLines(path)) {
   string line=raw.Trim();
   if(line.Length==0 || line.StartsWith("#") || line.StartsWith(";")) continue;
   if(line.StartsWith("[") && line.EndsWith("]")) {
    current=new Preset { Name=line.Substring(1,line.Length-2).Trim() };
    result.Add(current); continue;
   }
   if(current==null) continue;
   int eq=line.IndexOf('='); if(eq<1) continue;
   string key=line.Substring(0,eq).Trim(); string value=line.Substring(eq+1).Trim();
   if(key.Equals("Hotkey",StringComparison.OrdinalIgnoreCase)) current.Number=ParseHotkey(value);
   else if(key.Equals("Mode",StringComparison.OrdinalIgnoreCase)) current.Mode=value.Equals("Overhauled",StringComparison.OrdinalIgnoreCase)?"Overhauled":"Vanilla";
   else if(key.Equals("ClearOtherAI",StringComparison.OrdinalIgnoreCase)) current.ClearOtherAI=ParseBool(value);
   else if(key.Equals("Seat",StringComparison.OrdinalIgnoreCase) || key.Equals("Host",StringComparison.OrdinalIgnoreCase)) {
    var seat=ParseSeat(value); seat.Host=key.Equals("Host",StringComparison.OrdinalIgnoreCase); current.Seats.Add(seat);
   }
  }
  foreach(var preset in result) {
   if(preset.Number<1) throw new InvalidDataException("Preset has no valid identifier");
   if(preset.Seats.Count==0) throw new InvalidDataException("Preset '"+preset.Name+"' has no Seat entries");
   if(preset.Seats.Select(x=>x.Slot).Distinct().Count()!=preset.Seats.Count) throw new InvalidDataException("Preset '"+preset.Name+"' repeats a slot");
   if(preset.Seats.Any(x=>x.Host && x.AiSpecified)) throw new InvalidDataException("A host seat cannot have an AI override");
  }
  if(result.Select(p=>p.Number).Distinct().Count()!=result.Count) throw new InvalidDataException("Preset identifiers must be unique");
  return result;
 }

 static int ParseHotkey(string value) {
  value=value.Trim(); if(value.StartsWith("Ctrl+",StringComparison.OrdinalIgnoreCase)) value=value.Substring(5);
  int n; return int.TryParse(value,out n)?n:0;
 }
 sealed class RegisteredFaction {
  public int Value; public string Mod,Key,Name,Unit;
  public string CommanderId { get { return Mod+":"+Key+":"+Unit; } }
  public string FactionId { get { return Mod+":"+Key; } }
 }
 static List<RegisteredFaction> RegisteredFactions() {
  var result=new List<RegisteredFaction> {
   new RegisteredFaction{Value=0,Mod="stock",Key="eda",Name="EDA",Unit="uel0000"},
   new RegisteredFaction{Value=1,Mod="stock",Key="chosen",Name="Chosen",Unit="ucl0000"},
   new RegisteredFaction{Value=2,Mod="stock",Key="guard",Name="Guard",Unit="ugl0000"}
  };
  var api=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Sanctuary.ModApi.Factions",false)).FirstOrDefault(t=>t!=null);
  if(api==null)return result;
  foreach(var entry in (IEnumerable)api.GetProperty("Current",StaticAll).GetValue(null,null)) {
   var type=entry.GetType();var faction=type.GetProperty("Faction").GetValue(entry,null);var commander=type.GetProperty("Commander").GetValue(entry,null);var mod=type.GetProperty("Mod").GetValue(entry,null);
   var nameProperty=faction.GetType().GetProperty("Name")??faction.GetType().GetProperty("Key");
   result.Add(new RegisteredFaction {
    Value=Convert.ToInt32(type.GetProperty("Value").GetValue(entry,null)),
    Mod=Convert.ToString(mod.GetType().GetProperty("Id").GetValue(mod,null)),
    Key=Convert.ToString(faction.GetType().GetProperty("Key").GetValue(faction,null)),
    Name=Convert.ToString(nameProperty.GetValue(faction,null)),
    Unit=Convert.ToString(commander.GetType().GetProperty("Unit").GetValue(commander,null))
   });
  }
  return result;
 }
 static bool TryCommanderIdentity(string value,out string mod,out string faction,out string unit) {
  mod=faction=unit=null;if(string.IsNullOrWhiteSpace(value))return false;var bits=value.Split(':');
  if(bits.Length!=3 || bits.Any(string.IsNullOrWhiteSpace))return false;
  mod=bits[0].Trim();faction=bits[1].Trim();unit=bits[2].Trim();return true;
 }
 static string LegacyCommanderIdentity(string faction,string commander) {
  if(!string.IsNullOrWhiteSpace(commander))return commander.Trim();
  if(string.Equals(faction,"Dycom",StringComparison.OrdinalIgnoreCase))return "com.dycom.faction:dycom:udl0000";
  if(string.Equals(faction,"DycomMech",StringComparison.OrdinalIgnoreCase))return "com.dycom.faction:dycom:udl0001";
  return null;
 }
 static RegisteredFaction ExactCommander(string id,List<RegisteredFaction> entries) {
  string mod,key,unit;if(!TryCommanderIdentity(id,out mod,out key,out unit))throw new InvalidDataException("Commander identity must be modId:factionKey:commanderUnit.");
  var matches=entries.Where(x=>string.Equals(x.Mod,mod,StringComparison.OrdinalIgnoreCase)&&string.Equals(x.Key,key,StringComparison.OrdinalIgnoreCase)&&string.Equals(x.Unit,unit,StringComparison.OrdinalIgnoreCase)).ToList();
  if(matches.Count!=1)throw new InvalidOperationException("Enable the mod that provides commander '"+id+"' before applying this preset.");
  return matches[0];
 }
 static bool IsRandomChoice(string value){return string.Equals(value,"Random",StringComparison.OrdinalIgnoreCase);}
 static void ValidateRandomChoice(string faction,string commander){if(IsRandomChoice(faction)&&!IsRandomChoice(commander))throw new InvalidDataException("Random faction requires Random commander.");}
 static int ResolveFactionValue(string factionValue,string commanderValue,bool commanderPhase,List<RegisteredFaction> entries) {
  ValidateRandomChoice(factionValue,commanderValue);
  if(IsRandomChoice(commanderValue)){
   IEnumerable<RegisteredFaction> candidates=entries??new List<RegisteredFaction>();
   if(!IsRandomChoice(factionValue)){
    int numeric;var randomBits=factionValue.Split(':');
    if(int.TryParse(factionValue,out numeric))candidates=candidates.Where(x=>numeric>=0&&numeric<=2&&x.Value==numeric);
    else if(randomBits.Length==2)candidates=candidates.Where(x=>string.Equals(x.Mod,randomBits[0],StringComparison.OrdinalIgnoreCase)&&string.Equals(x.Key,randomBits[1],StringComparison.OrdinalIgnoreCase));
    else candidates=candidates.Where(x=>string.Equals(x.Name,factionValue,StringComparison.OrdinalIgnoreCase)||string.Equals(x.Key,factionValue,StringComparison.OrdinalIgnoreCase));
    if(candidates.Select(x=>x.FactionId).Distinct(StringComparer.OrdinalIgnoreCase).Count()>1)throw new InvalidDataException("The preset faction name is ambiguous; use modId:factionKey for Random commander.");
   }
   // A stable registered placeholder, not a random draw. Intent resolves at start.
   var placeholder=candidates.Where(x=>!string.IsNullOrWhiteSpace(x.Mod)&&!string.IsNullOrWhiteSpace(x.Key)&&!string.IsNullOrWhiteSpace(x.Unit)).OrderBy(x=>x.CommanderId,StringComparer.OrdinalIgnoreCase).ThenBy(x=>x.Value).FirstOrDefault();
   if(placeholder==null)throw new InvalidOperationException("No registered commander is available for the preset's Random choice.");
   return placeholder.Value;
  }
  string commander=LegacyCommanderIdentity(factionValue,commanderValue);
  if(commanderPhase || !string.IsNullOrEmpty(commander))return ExactCommander(commander,entries).Value;
  int number;if(int.TryParse(factionValue,out number)) {
   if(number<0 || number>2)throw new InvalidDataException("Dynamic faction numbers are not stable. Re-save this preset using a stable faction identity.");
   return number;
  }
  var bits=factionValue.Split(':');IEnumerable<RegisteredFaction> matches;
  if(bits.Length==2)matches=entries.Where(x=>string.Equals(x.Mod,bits[0],StringComparison.OrdinalIgnoreCase)&&string.Equals(x.Key,bits[1],StringComparison.OrdinalIgnoreCase));
  else matches=entries.Where(x=>string.Equals(x.Name,factionValue,StringComparison.OrdinalIgnoreCase)||string.Equals(x.Key,factionValue,StringComparison.OrdinalIgnoreCase));
  var found=matches.ToList();
  if(found.Count==0)throw new InvalidOperationException("Enable the mod faction '"+factionValue+"' before applying this preset.");
  if(found.Select(x=>x.Value).Distinct().Count()!=1)throw new InvalidDataException("Faction '"+factionValue+"' has multiple commanders; this preset must include a stable Commander identity.");
  return found[0].Value;
 }
 static object ResolveSeatFaction(Type factionType,Seat seat,bool commanderPhase) {
  return Enum.ToObject(factionType,ResolveFactionValue(seat.Faction,seat.Commander,commanderPhase,RegisteredFactions()));
 }
 static object ParseFaction(Type factionType,string value) { return ResolveSeatFaction(factionType,new Seat{Faction=value},false); }
 static string FactionName(object value) {
  int number=Convert.ToInt32(value);var entry=RegisteredFactions().FirstOrDefault(x=>x.Value==number);
  if(entry==null)throw new InvalidOperationException("The selected faction is not registered by the active mod set.");
  return entry.Mod=="stock"?entry.Name:entry.FactionId;
 }
 static bool ParseBool(string value) { bool b; return bool.TryParse(value,out b)?b:true; }
 static Seat ParseSeat(string value) {
  var values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
  foreach(string bit in value.Split(',')) { int eq=bit.IndexOf(':'); if(eq<0) eq=bit.IndexOf('='); if(eq>0) values[bit.Substring(0,eq).Trim()]=bit.Substring(eq+1).Trim(); }
  int slot=RequiredInt(values,"Slot"), army=RequiredInt(values,"Army"), team=RequiredInt(values,"Team");
  string faction; if(!values.TryGetValue("Faction",out faction) || faction.Length==0) throw new InvalidDataException("Seat entry is missing Faction");
  if(slot<1 || slot>16 || army<0 || army>127 || team<1 || team>16) throw new InvalidDataException("Seat/Army/Team is outside the supported range");
  var seat=new Seat { Slot=(byte)(slot-1), Army=(sbyte)army, Team=(byte)team, Faction=faction };
  string mode,mod,key,commander;
  if(values.TryGetValue("Commander",out commander)) seat.Commander=commander;
  ValidateRandomChoice(seat.Faction,seat.Commander);
  if(IsRandomChoice(seat.Faction))seat.Faction="Random";
  if(IsRandomChoice(seat.Commander))seat.Commander="Random";
  if(values.TryGetValue("AI",out mode)) {
   if(!string.Equals(mode,"native",StringComparison.OrdinalIgnoreCase) || values.ContainsKey("AIMod") || values.ContainsKey("AIKey")) throw new InvalidDataException("Invalid native AI setting");
   seat.AiSpecified=true;
  } else if(values.ContainsKey("AIMod") || values.ContainsKey("AIKey")) {
   if(!values.TryGetValue("AIMod",out mod) || !values.TryGetValue("AIKey",out key) || !SafeAiToken(mod) || !SafeAiToken(key)) throw new InvalidDataException("AI override requires a valid mod and key");
   seat.AiSpecified=true;seat.AiMod=mod;seat.AiKey=key;
  }
  return seat;
 }
 static int RequiredInt(Dictionary<string,string> values,string name) { string s; int n; if(!values.TryGetValue(name,out s)||!int.TryParse(s,out n)) throw new InvalidDataException("Seat entry is missing numeric "+name); return n; }

 static bool InLobby() {
  var t=FindType("EM.UI.InterfaceManager"); var instanceField=t.GetField("Instance",StaticAll); if(instanceField==null) return false;
  object instance=instanceField.GetValue(null); if(instance==null) return false;
  var current=t.GetField("currentWindow",InstanceAll); if(current==null) return false;
  return Convert.ToInt32(current.GetValue(instance))==6;
 }
 static bool IsHost() { return (bool)FindType("EM.Network.LobbyManager").GetMethod("IsCurrentUserHost",StaticAll).Invoke(null,null); }
 static IList Players() {
  var t=FindType("EM.Network.LobbyManager"); var p=t.GetProperty("CurrentState",StaticAll)??t.GetProperty("currentState",StaticAll);
  object state=p==null?t.GetMethod("get_CurrentState",StaticAll).Invoke(null,null):p.GetValue(null,null);
  return state==null?null:(IList)state.GetType().GetField("players",InstanceAll).GetValue(state);
 }
 static int IntField(object value,string field) { return Convert.ToInt32(value.GetType().GetField(field,InstanceAll).GetValue(value)); }
 static void Send(string name,params object[] args) {
  var methods=FindType("EM.Network.LobbyManager").GetMethods(StaticAll).Where(x=>x.Name==name && x.GetParameters().Length==args.Length).ToArray();
  if(methods.Length!=1) throw new MissingMethodException("LobbyManager."+name);
  methods[0].Invoke(null,args);
 }
 static bool ControlHeld() { return KeyHeld("LeftControl") || KeyHeld("RightControl"); }
 static bool KeyHeld(string name) { return Key("GetKey",name); }
 static bool KeyDown(string name) { return Key("GetKeyDown",name); }
 static bool Key(string method,string name) {
  var input=FindType("UnityEngine.Input"); var keyCode=FindType("UnityEngine.KeyCode"); object key=Enum.Parse(keyCode,name);
  return (bool)input.GetMethod(method,StaticAll,null,new[]{keyCode},null).Invoke(null,new[]{key});
 }
 static void Chat(string message) {
  Console.WriteLine("[Lobby Presets] "+message);
  try {
   var t=FindType("EM.UI.LobbyInterface"); object instance=t.GetField("Instance",StaticAll).GetValue(null);
   if(instance!=null) instance.GetType().GetMethod("AddChatMessage",InstanceAll).Invoke(instance,new object[]{"[Lobby Presets] "+message});
  } catch { }
 }
 static Type FindType(string name) {
  foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies()) { var t=assembly.GetType(name,false); if(t!=null) return t; }
  throw new TypeLoadException(name);
 }
 static string RootMessage(Exception ex) { while(ex is TargetInvocationException && ex.InnerException!=null) ex=ex.InnerException; return ex.GetType().Name+": "+ex.Message; }
}

