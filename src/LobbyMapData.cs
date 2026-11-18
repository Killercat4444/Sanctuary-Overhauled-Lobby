using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Read-only view of the actual .sanmap. preview.png is cropped to PlayableArea.
internal sealed class LobbyMapData {
 static string cachedPath;static long cachedLength,cachedTicks;static LobbyMapData cached;internal static int ParseCount;
 internal sealed class Spawn { internal int Army;internal float X,Z,U,V; }
 internal readonly List<Spawn> Spawns=new List<Spawn>();
 internal string Error,Source;
 internal static LobbyMapData Read(string root,string map){
  var result=new LobbyMapData();
  try{
   string mapsRoot=Path.GetFullPath(Path.Combine(root,"Sanctuary_Data","Maps"));
   string path=Path.IsPathRooted(map)?Path.GetFullPath(map):Path.GetFullPath(Path.Combine(root,"Sanctuary_Data",(map??"").TrimStart('/','\\')));
   if(!path.StartsWith(mapsRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!File.Exists(path)){
    var matches=Directory.GetFiles(mapsRoot,Path.GetFileNameWithoutExtension(map??"")+".sanmap",SearchOption.AllDirectories);
    if(matches.Length!=1)throw new InvalidDataException("Map path cannot be resolved uniquely.");path=matches[0];
   }
   var info=new FileInfo(path);if(info.Length>64*1024*1024)throw new InvalidDataException("Map file exceeds the supported size.");
   if(cached!=null&&path==cachedPath&&info.Length==cachedLength&&info.LastWriteTimeUtc.Ticks==cachedTicks)return cached;
   result=Parse(JObject.Parse(File.ReadAllText(path)));result.Source=path;ParseCount++;
   cachedPath=path;cachedLength=info.Length;cachedTicks=info.LastWriteTimeUtc.Ticks;cached=result;
  }catch(Exception ex){result.Error=ex.Message;}
  return result;
 }
 internal static LobbyMapData Parse(JObject data){
  var r=new LobbyMapData();var area=data["areas"]?["PlayableArea"];
  float x=Number(area?["x"],0),z=Number(area?["y"],0),w=Number(area?["width"],Number(data["width"],0)),h=Number(area?["height"],Number(data["length"],0));
  if(w<=0||h<=0)throw new InvalidDataException("Map dimensions are unavailable.");
  var tr=data["markers"]?["Spawn"]?["transforms"] as JObject;
  if(tr==null)throw new InvalidDataException("Map spawn transforms are unavailable.");
  foreach(var p in tr.Properties()){
   int army;if(!p.Name.StartsWith("Army_",StringComparison.Ordinal)||!int.TryParse(p.Name.Substring(5),out army)||army<1||army>64)continue;
   var pos=p.Value["position"];float px=Number(pos?["x"],float.NaN),pz=Number(pos?["z"],float.NaN);
   if(float.IsNaN(px)||float.IsNaN(pz)||px<x||px>x+w||pz<z||pz>z+h)throw new InvalidDataException("Spawn position is outside the playable map.");
   r.Spawns.Add(new Spawn{Army=army,X=px,Z=pz,U=(px-x)/w,V=1-(pz-z)/h});
  }
  if(r.Spawns.Count==0||r.Spawns.GroupBy(s=>s.Army).Any(g=>g.Count()!=1))throw new InvalidDataException("Map has missing or duplicate spawn IDs.");
  return r;
 }
 static float Number(JToken token,float fallback){float value;return token!=null&&float.TryParse(token.ToString(),NumberStyles.Float,CultureInfo.InvariantCulture,out value)&&!float.IsInfinity(value)&&!float.IsNaN(value)?value:fallback;}
 internal bool Teams(SanctuaryTeamPolicy policy,out Dictionary<int,int> result){
  result=new Dictionary<int,int>();if(Error!=null||Spawns.Count<2)return false;
  float mid=policy==SanctuaryTeamPolicy.LeftVsRight?(Spawns.Min(s=>s.X)+Spawns.Max(s=>s.X))/2:(Spawns.Min(s=>s.Z)+Spawns.Max(s=>s.Z))/2;
  foreach(var s in Spawns)result[s.Army]=policy==SanctuaryTeamPolicy.OddVsEven?(s.Army-1)%2+1:policy==SanctuaryTeamPolicy.LeftVsRight?(s.X<mid?1:2):(s.Z>=mid?1:2);
  return result.Values.Distinct().Count()==2;
 }
}
