using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

// Read-through migration, not a shared-config rewrite. Native HostValues calls
// RememberedOptions only when the host first selects this identity. The native
// HostApply persists the imported values under the new identity on selection;
// SetOption/ResetOptions subsequently use the same native persistence path.
// A present new dictionary (even empty/all-off) ALWAYS wins. Keep the old key
// untouched for recovery; never re-import old restrictions after Clear Selected.
internal static class LobbyMergedOptions {
 internal const string LegacyId="com.sanctuary.overhauled-lobby.sync";
 internal const string CurrentId="com.sanctuary.overhauled-lobby";
 internal static string Failure;
 static MethodInfo remembered;
 static bool announced;
 internal static void ValidateOptions(string raw){
  if(string.IsNullOrWhiteSpace(raw))return;
  if(raw.Length>1048576)throw new InvalidOperationException("Saved options exceed the migration size limit.");
  using(var reader=new Newtonsoft.Json.JsonTextReader(new System.IO.StringReader(raw))){
   reader.DateParseHandling=Newtonsoft.Json.DateParseHandling.None;
   if(!reader.Read()||reader.TokenType!=Newtonsoft.Json.JsonToken.StartObject)throw new InvalidOperationException("Saved options must be an object.");
   var ids=new HashSet<string>(StringComparer.Ordinal);
   while(reader.Read()&&reader.TokenType!=Newtonsoft.Json.JsonToken.EndObject){
    if(reader.TokenType!=Newtonsoft.Json.JsonToken.PropertyName||!ids.Add((string)reader.Value))throw new InvalidOperationException("Duplicate or invalid saved mod identity.");
    if(!reader.Read()||reader.TokenType!=Newtonsoft.Json.JsonToken.StartObject)throw new InvalidOperationException("Saved mod options must be an object.");
    var keys=new HashSet<string>(StringComparer.Ordinal);
    while(reader.Read()&&reader.TokenType!=Newtonsoft.Json.JsonToken.EndObject){
     if(reader.TokenType!=Newtonsoft.Json.JsonToken.PropertyName||!keys.Add((string)reader.Value))throw new InvalidOperationException("Duplicate or invalid saved option.");
     if(!reader.Read()||reader.TokenType!=Newtonsoft.Json.JsonToken.String)throw new InvalidOperationException("Saved option values must be strings.");
    }
    if(reader.TokenType!=Newtonsoft.Json.JsonToken.EndObject)throw new InvalidOperationException("Incomplete saved options.");
   }
   if(reader.TokenType!=Newtonsoft.Json.JsonToken.EndObject||reader.Read())throw new InvalidOperationException("Incomplete or trailing saved options.");
  }
 }
 internal static IReadOnlyDictionary<string,string> Choose(IReadOnlyDictionary<string,string> current,IReadOnlyDictionary<string,string> legacy){
  if(current!=null||legacy==null)return current;
  if(legacy.Count>128)throw new InvalidOperationException("Too many legacy restriction options; original settings retained.");
  var copy=new Dictionary<string,string>(StringComparer.Ordinal);
  foreach(var pair in legacy){if(pair.Key==null||pair.Key.Length>256||pair.Value==null||pair.Value.Length>65536)throw new InvalidOperationException("Invalid legacy restriction options; original settings retained.");copy.Add(pair.Key,pair.Value);}
  return copy;
 }
 internal static void Install(Harmony harmony){
  try{
   if(!SanctuaryLobbyState.PrivateContracts)throw new InvalidOperationException("Unsupported installed contracts.");
   var api=SanctuaryLobbyState.FindType("Sanctuary.ModApi.ModApiPlugin");
   remembered=api.GetMethod("RememberedOptions",SanctuaryLobbyState.StaticAll,null,new[]{typeof(string)},null);
   if(api.Module.ModuleVersionId.ToString()!=LobbyUnitRestrictions.ApiMvid||remembered==null||!remembered.IsStatic||remembered.ReturnType!=typeof(IReadOnlyDictionary<string,string>))throw new InvalidOperationException("Saved option migration contract changed.");
   var config=api.GetField("_cfgOptions",SanctuaryLobbyState.StaticAll).GetValue(null) as BepInEx.Configuration.ConfigEntry<string>;
   if(config==null)throw new InvalidOperationException("Mod API settings are not initialized.");
   // Validate before the native reader can turn malformed JSON into defaults.
   ValidateOptions(config.Value);
   harmony.Patch(remembered,postfix:new HarmonyMethod(typeof(LobbyMergedOptions).GetMethod("AfterRead",SanctuaryLobbyState.StaticAll)));
   Failure=null;
  }catch(Exception ex){Failure="Saved lobby restriction migration is unavailable. "+SanctuaryLobbyState.Root(ex);SanctuaryLobbyState.PrivateContracts=false;SanctuaryLobbyState.Warn(Failure);}
 }
 static void AfterRead(string __0,ref IReadOnlyDictionary<string,string> __result){
  // The recursive call uses the old ID, so it cannot re-enter this branch.
  if(__0!=CurrentId||__result!=null)return;
  try{
   var legacy=(IReadOnlyDictionary<string,string>)remembered.Invoke(null,new object[]{LegacyId});
   __result=Choose(__result,legacy);
   if(legacy!=null&&!announced){announced=true;SanctuaryLobbyState.Warn("Imported legacy Sync restriction settings for the merged lobby. The original saved key is retained; future edits save under the merged identity.");}
  }catch(Exception ex){Failure="Legacy restriction settings could not be read safely. "+SanctuaryLobbyState.Root(ex);SanctuaryLobbyState.Warn(Failure);}
 }
 internal static string BlockReason(){
  if(Failure!=null)return Failure;
  try{
   var catalog=SanctuaryLobbyState.FindType("Sanctuary.ModApi.ModCatalog");
   if(catalog.GetMethod("Find",SanctuaryLobbyState.StaticAll,null,new[]{typeof(string)},null).Invoke(null,new object[]{LegacyId})!=null)
    return "The old Overhauled Lobby Sync is still installed. Close the game and move it outside SanctuaryMods using the merge installer; both copies must not run together.";
   return null;
  }catch(Exception ex){return "Merged lobby installation could not be verified. "+SanctuaryLobbyState.Root(ex);}
 }
}
