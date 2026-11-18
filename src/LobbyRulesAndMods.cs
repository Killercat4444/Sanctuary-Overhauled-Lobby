using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using EM.Core;
using EM.Network;
using EM.Network.Lobby;

internal sealed class LobbyRulesSnapshot {
 internal string[] Rules=new string[0],Mods=new string[0],Observers=new string[0];
 internal bool ModsAvailable;
 internal int SelectedModCount;
}

// Display-only projections. Never infer active mods from installed folders or
// add settings that do not exist in the native lobby model.
internal static class LobbyRulesAndMods {
 internal static string Text(string value,string fallback){
  if(string.IsNullOrWhiteSpace(value))return fallback;
  var clean=new string(value.Where(c=>!char.IsControl(c)).Take(160).ToArray()).Trim();
  return clean.Length==0?fallback:clean;
 }
 internal static string[] Rules(int players,int observers,bool locked,SanctuaryLobbyMode mode,SanctuaryTeamPolicy teams){
  var team=(int)teams;string policy=team>=0&&team<SanctuaryLobbyState.TeamNames.Length?SanctuaryLobbyState.TeamNames[team]:"Unavailable";
  return new[]{"Mode: "+(mode==SanctuaryLobbyMode.Vanilla?"Vanilla Lobby":mode==SanctuaryLobbyMode.Overhauled?"Overhauled Lobby":"Unavailable"),
   "Teams: "+policy,"Player slots: "+Math.Max(0,players).ToString(CultureInfo.InvariantCulture),
   "Observer slots: "+Math.Max(0,observers).ToString(CultureInfo.InvariantCulture),"Lobby: "+(locked?"Locked":"Open")};
 }
 internal static string[] ModLines(IEnumerable selected,out int count){
  if(selected==null)throw new InvalidOperationException("Selected mod data is unavailable.");
  var lines=new List<string>();var ids=new HashSet<string>(StringComparer.Ordinal);count=0;
  foreach(var mod in selected){
   if(mod==null||count>=128)throw new InvalidOperationException("Selected mod data exceeds the supported display contract.");
   var type=mod.GetType();string id=ReadString(type,mod,"Id"),name=ReadString(type,mod,"Name"),version=ReadString(type,mod,"Version");
   if(string.IsNullOrWhiteSpace(id)||!ids.Add(id))throw new InvalidOperationException("Selected mod identities are unavailable or duplicated.");
   lines.Add(Text(name,Text(id,"Unnamed mod"))+(string.IsNullOrWhiteSpace(version)?"":"  "+Text(version,"")));count++;
  }
  return lines.Count==0?new[]{"No gameplay mods selected."}:lines.ToArray();
 }
 static string ReadString(Type type,object value,string name){
  var property=type.GetProperty(name,BindingFlags.Instance|BindingFlags.Public);var getter=property==null?null:property.GetGetMethod();
  if(property==null||property.PropertyType!=typeof(string)||getter==null||getter.IsStatic||property.GetIndexParameters().Length!=0)throw new MissingMemberException("SelectedMod."+name);
  return (string)property.GetValue(value,null);
 }
 internal static LobbyRulesSnapshot Capture(LobbyState state){
  var result=new LobbyRulesSnapshot();
  if(state==null){result.Rules=new[]{"Lobby rules unavailable."};result.Observers=new[]{"Observers unavailable."};}
  else{
   result.Rules=Rules(state.MaxPlayers,state.MaxObservers,LobbyManager.isLocked,SanctuaryLobbyState.Mode,SanctuaryLobbyState.TeamPolicy).Concat(new[]{LobbyUnitRestrictions.Summary}).ToArray();
   result.Observers=state.GetPlayers(true,true).Where(p=>p.type==PlayerType.Observer).Select(p=>Text(p.name,"Unnamed observer")).ToArray();
   if(result.Observers.Length==0)result.Observers=new[]{"No observers."};
  }
  try{
   var type=SanctuaryLobbyState.FindType("Sanctuary.ModApi.Lobby");var property=type==null?null:type.GetProperty("Selection",BindingFlags.Static|BindingFlags.Public);var getter=property==null?null:property.GetGetMethod();
   if(getter==null||!getter.IsStatic||!typeof(IEnumerable).IsAssignableFrom(property.PropertyType))throw new MissingMemberException("Lobby.Selection");
   result.Mods=ModLines(property.GetValue(null,null) as IEnumerable,out result.SelectedModCount);result.ModsAvailable=true;
  }catch{result.Mods=new[]{"Selected mods unavailable."};result.SelectedModCount=0;result.ModsAvailable=false;}
  return result;
 }
}
