using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

public static partial class LobbyPresetRuntime {
 sealed class PresetSelection {
  public int Number;public string Name,Version;public bool Used;
 }
 static bool presetWriteBusy;
 static string FileVersion() {
  using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(PresetPath))).Replace("-","");
 }
 static bool CanEditPresets() { return !presetWriteBusy && pending==null && !LobbyBulkAi.IsRunning && InLobby() && IsHost() && ApiAllowsEditing(); }
 static void RequireEditable() {
  if(!CanEditPresets())throw new InvalidOperationException("Only the unlocked host lobby can edit presets.");
  if(File.Exists(PresetPath) && (File.GetAttributes(PresetPath)&FileAttributes.ReadOnly)!=0)throw new InvalidOperationException("The preset file is read-only.");
 }
 static PresetSelection SelectPreset(int number) {
  RequireEditable();
  var version=FileVersion();var preset=LoadPresets().SingleOrDefault(p=>p.Number==number);
  if(preset==null || number<=0)throw new InvalidOperationException("That preset no longer exists. Reopen the preset menu.");
  if(FileVersion()!=version)throw new IOException("The preset file changed. Reopen the preset menu.");
  return new PresetSelection{Number=preset.Number,Name=preset.Name,Version=version};
 }
 static bool CanUpdatePreset(PresetSelection selection) {
  try {
   if(selection==null || selection.Used || !CanEditPresets())return false;
   RequireEditable();
   if(FileVersion()!=selection.Version)return false;
   var p=LoadPresets().SingleOrDefault(x=>x.Number==selection.Number);
   if(p==null || p.Name!=selection.Name)return false;
   CaptureCurrentLayout();return true;
  }catch{return false;}
 }
 static Preset CaptureCurrentLayout() {
  if(!CanEditPresets())throw new InvalidOperationException("Only the unlocked host lobby can capture a preset.");
  var result=new Preset{Mode=SanctuaryLobbyState.ModeName};var players=Players();
  if(players==null)throw new InvalidOperationException("The lobby player list is not available yet.");
  for(int i=0;i<players.Count;i++) {
   var p=players[i];if(p==null)continue;
   int kind=IntField(p,"type");bool host=IsLocalPlayer(p);
   if((kind==1 || kind==2) && !host)throw new InvalidOperationException("Save presets in a lobby with only you and AI; other humans are not saved.");
   if(kind!=3 && !host)continue;
   int army=IntField(p,"armyID"),team=IntField(p,"team");
   if(i>=16 || army<0 || army>127 || team<1 || team>16)throw new InvalidDataException("Lobby seat/army/team is outside the supported preset range.");
   string faction,commander;
   if(!SanctuaryLobbyState.TryGetLobbyChoice(p,out faction,out commander)){faction=FactionName(p.GetType().GetField("faction",InstanceAll).GetValue(p));commander=StableCommander(p);}
   ValidateRandomChoice(faction,commander);
   var seat=new Seat{Slot=(byte)i,Army=(sbyte)army,Team=(byte)team,Host=host,Faction=faction,Commander=commander};
   if(kind==3)CaptureAiChoice(seat);
   result.Seats.Add(seat);
  }
  if(result.Seats.Count==0)throw new InvalidOperationException("The lobby has no supported seats to save.");
  return result;
 }
 static string SeatLine(Seat s) {
  string line=(s.Host?"Host":"Seat")+"=Slot:"+(s.Slot+1)+",Army:"+s.Army+",Team:"+s.Team+",Faction:"+s.Faction+(string.IsNullOrEmpty(s.Commander)?"":",Commander:"+s.Commander);
  if(s.AiSpecified)line+=s.AiMod==null?",AI:native":",AIMod:"+s.AiMod+",AIKey:"+s.AiKey;
  return line;
 }
 static string StableCommander(object player) {
  int faction=IntField(player,"faction");
   return StableCommanderForValue(faction,RegisteredFactions());
 }
 static string StableCommanderForValue(int faction,List<RegisteredFaction> entries) {
   var matches=entries.Where(x=>x.Value==faction).Select(x=>x.CommanderId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
   if(matches.Count!=1)throw new InvalidOperationException("The selected commander is not uniquely registered by the active mod set.");
   return matches[0];
 }
 static bool SameLayout(Preset a,Preset b) {
  return string.Equals(a.Mode,b.Mode,StringComparison.OrdinalIgnoreCase) &&
   a.Seats.OrderBy(s=>s.Slot).Select(SeatLine).SequenceEqual(b.Seats.OrderBy(s=>s.Slot).Select(SeatLine));
 }
 static bool UpdatePreset(PresetSelection selection) {
  RequireEditable();
  if(selection==null || selection.Used)throw new InvalidOperationException("Reopen the preset menu before updating again.");
  if(FileVersion()!=selection.Version)throw new IOException("The preset file changed. Reopen the preset menu.");
  var presets=LoadPresets();var index=presets.FindIndex(p=>p.Number==selection.Number);
  if(index<0 || presets[index].Name!=selection.Name)throw new InvalidOperationException("That preset was removed or renamed. Reopen the preset menu.");
  var current=presets[index];var snapshot=CaptureCurrentLayout();
  snapshot.Number=current.Number;snapshot.Name=current.Name;snapshot.ClearOtherAI=current.ClearOtherAI;snapshot.Mode=SanctuaryLobbyState.ModeName;
  if(SameLayout(current,snapshot)){selection.Used=true;return false;}
  string original=File.ReadAllText(PresetPath);string updated=ReplaceLayoutSection(original,index,snapshot);
  CommitPresetText(updated,selection.Version);selection.Used=true;return true;
 }
 static string ReplaceLayoutSection(string original,int sectionIndex,Preset snapshot) {
  var headers=Regex.Matches(original,@"(?m)^[ \t]*\[[^\r\n]*\][ \t]*(?:\r?\n|$)");
  if(sectionIndex<0 || sectionIndex>=headers.Count)throw new InvalidDataException("Preset section could not be located.");
  int start=headers[sectionIndex].Index+headers[sectionIndex].Length;
  int end=sectionIndex+1<headers.Count?headers[sectionIndex+1].Index:original.Length;
  string body=original.Substring(start,end-start);string eol=original.Contains("\r\n")?"\r\n":"\n";
  string lines=string.Join(eol,snapshot.Seats.Select(SeatLine));bool inserted=false;
  string changed=Regex.Replace(body,@"(?im)^[ \t]*(?:Seat|Host)[ \t]*=[^\r\n]*(?:\r?\n|$)",m=>{
   if(inserted)return "";inserted=true;
   return lines+(m.Value.EndsWith("\n") || m.Index+m.Length<body.Length?eol:"");
  });
  if(!inserted)throw new InvalidDataException("Selected preset has no editable layout.");
  bool modeUpdated=false;
  changed=Regex.Replace(changed,@"(?im)^[ \t]*Mode[ \t]*=[^\r\n]*(?:\r?\n|$)",m=>{modeUpdated=true;return "Mode="+snapshot.Mode+eol;});
  if(!modeUpdated)changed="Mode="+snapshot.Mode+eol+changed;
  return original.Substring(0,start)+changed+original.Substring(end);
 }
 static void WritePresets(List<Preset> presets) {
  RequireEditable();var expected=File.Exists(PresetPath)?FileVersion():null;
  var existing=File.Exists(PresetPath)?LoadPresets():new List<Preset>();
  string original=File.Exists(PresetPath)?File.ReadAllText(PresetPath):"";
  CommitPresetText(PreserveSections(original,existing,presets),expected);
 }
 // Save/rename/delete do not reserialize unrelated sections. Preserve comments,
 // unknown options, legacy formatting and hotkeys; only new sections are emitted.
 static string PreserveSections(string original,List<Preset> existing,List<Preset> desired) {
  var headers=Regex.Matches(original,@"(?m)^[ \t]*\[[^\r\n]*\][ \t]*(?:\r?\n|$)");
  if(headers.Count!=existing.Count)throw new InvalidDataException("Preset sections changed; no file was written.");
  string eol=original.Contains("\r\n")?"\r\n":"\n";
  var text=new StringBuilder(headers.Count==0?original:original.Substring(0,headers[0].Index));
  for(int i=0;i<existing.Count;i++) {
   var previous=existing[i];var next=desired.SingleOrDefault(p=>p.Number==previous.Number);if(next==null)continue;
   int start=headers[i].Index,end=i+1<headers.Count?headers[i+1].Index:original.Length;
   string section=original.Substring(start,end-start);
   if(previous.Name!=next.Name){int open=section.IndexOf('['),close=section.IndexOf(']');section=section.Substring(0,open+1)+next.Name+section.Substring(close);}
   text.Append(section);
  }
  foreach(var p in desired.Where(p=>!existing.Any(old=>old.Number==p.Number))) {
   if(text.Length>0&&!text.ToString().EndsWith("\n"))text.Append(eol);
   text.Append(eol).Append('[').Append(p.Name).Append(']').Append(eol);
   text.Append("Hotkey=Ctrl+").Append(p.Number).Append(eol).Append("Mode=").Append(p.Mode).Append(eol).Append("ClearOtherAI=").Append(p.ClearOtherAI.ToString().ToLowerInvariant()).Append(eol);
   foreach(var seat in p.Seats)text.Append(SeatLine(seat)).Append(eol);
  }
  return text.ToString();
 }
 static void CommitPresetText(string text,string expected) {
  RequireEditable();
  if((File.Exists(PresetPath)?FileVersion():null)!=expected)throw new IOException("The preset file changed. Reopen the preset menu.");
  presetWriteBusy=true;string temp=PresetPath+"."+Guid.NewGuid().ToString("N")+".tmp";
  try {
   var original=File.Exists(PresetPath)?File.ReadAllBytes(PresetPath):new byte[0];
   bool bom=original.Length>=3 && original[0]==239 && original[1]==187 && original[2]==191;
   File.WriteAllText(temp,text,new UTF8Encoding(bom));
   if((File.Exists(PresetPath)?FileVersion():null)!=expected)throw new IOException("The preset file changed. Reopen the preset menu.");
   if(File.Exists(PresetPath))File.Replace(temp,PresetPath,PresetPath+".bak");else File.Move(temp,PresetPath);
  }finally{presetWriteBusy=false;if(File.Exists(temp))File.Delete(temp);}
 }
}

