using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

internal static class LobbyUiGeometry {
 // Reference-approved cards; extra aspect-ratio space stays outside the layout.
 internal static float[][] Layout(float width,float height){
  if(width<1671||height<940)throw new ArgumentException("Lobby reference canvas is too small.");
  float x=(width-1672)/2,y=(height-941)/2;
  return new[]{new[]{x+41,y+26,1592f,46f},new[]{x+41,y+86,1076f,444f},new[]{x+1133,y+86,500f,444f},new[]{x+41,y+690,1076f,165f},new[]{x+1133,y+543,500f,312f},new[]{x+41,y+869,1592f,58f},new[]{x+41,y+543,1076f,134f}};
 }
 internal static float[] Fit(float width,float height,float aspect){
  if(width<=0||height<=0||aspect<=0||float.IsNaN(aspect)||float.IsInfinity(aspect))throw new ArgumentException("Invalid map image bounds.");
  float w=Math.Min(width,height*aspect),h=w/aspect;return new[]{(width-w)/2,(height-h)/2,w,h};
 }
 internal static bool DarkLabel(float r,float g,float b){return .2126f*r+.7152f*g+.0722f*b>.55f;}
 static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
 internal static bool RetainOutgoing(bool committed,int window,bool nativeActive,bool hasFade,float alpha){return committed&&window==10&&nativeActive&&hasFade&&Finite(alpha)&&alpha>0;}
 internal static bool ClosingModalAllowsPresentation(bool active,bool knownOpenState,bool isOn,bool rendersAboveOverlay){return active&&knownOpenState&&!isOn&&rendersAboveOverlay;}
 internal static bool OverlayInputAllowed(bool requested,bool nativeAllows,bool closingModal,float alpha){return requested&&nativeAllows&&!closingModal&&Finite(alpha)&&alpha>0;}
 // Bounds are normalized sprite coordinates, with y measured from the bottom.
 // Position the visible artwork, not its possibly asymmetric transparent frame.
 internal static float[] CenterArtwork(float x,float y,float width,float height,float spriteWidth,float spriteHeight,float bx,float by,float bw,float bh){
  if(!Finite(x)||!Finite(y)||!Finite(width)||!Finite(height)||!Finite(spriteWidth)||!Finite(spriteHeight)||!Finite(bx)||!Finite(by)||!Finite(bw)||!Finite(bh)||width<=0||height<=0||spriteWidth<=0||spriteHeight<=0||bw<=0||bh<=0||bx<-.001f||by<-.001f||bx+bw>1.001f||by+bh>1.001f)throw new ArgumentException("Invalid artwork bounds.");
  float scale=Math.Min(width/(spriteWidth*bw),height/(spriteHeight*bh));float w=spriteWidth*scale,h=spriteHeight*scale;
  float left=x+width/2-(bx+bw/2)*w,top=y+height/2-(1-by-bh/2)*h;if(!Finite(left)||!Finite(top)||!Finite(w)||!Finite(h)||w<=0||h<=0)throw new ArgumentException("Artwork bounds overflow.");return new[]{left,top,w,h};
 }
}

internal static class LobbyFactionIcons {
 internal static string CatalogKey(IEnumerable<SanctuaryLobbyState.FactionChoice> choices){return string.Join("|",choices.OrderBy(c=>c.Value).ThenBy(c=>c.Identity,StringComparer.Ordinal).Select(c=>c.Value+":"+string.Join("",new[]{c.Identity,c.FactionId,c.FactionName,c.CommanderName}.Select(s=>(s??"").Length+":"+(s??"")).ToArray())).ToArray());}
 static string PropertyText(object value,string property){if(value==null)return null;var p=value.GetType().GetProperty(property);return p==null?null:Convert.ToString(p.GetValue(value,null));}
 internal static bool MatchesIdentity(SanctuaryLobbyState.FactionChoice choice,object entry){
  if(choice==null||entry==null)return false;var t=entry.GetType();var mod=t.GetProperty("Mod").GetValue(entry,null);var faction=t.GetProperty("Faction").GetValue(entry,null);var commander=t.GetProperty("Commander").GetValue(entry,null);
  string identity=PropertyText(mod,"Id")+":"+PropertyText(faction,"Key")+":"+PropertyText(commander,"Unit");return !string.IsNullOrEmpty(choice.Identity)&&string.Equals(choice.Identity,identity,StringComparison.Ordinal);
 }
 static readonly Dictionary<string,Sprite> provided=new Dictionary<string,Sprite>();
 static readonly HashSet<string> attempted=new HashSet<string>();
 static readonly HashSet<string> nativeAttempted=new HashSet<string>();
 internal static string NativeResourcePath(string factionId){
  // Verified ResourceManager Sprite entries in game 0.0.1.21.1. Never map
  // registered factions by display name or their dynamically allocated byte.
  switch(factionId){case "stock:eda":return "ui/icons/eda";case "stock:chosen":return "ui/icons/chosen";case "stock:guard":return "ui/icons/guard";default:return null;}
 }
 internal static Sprite Get(SanctuaryLobbyState.FactionChoice choice,Component lobby){
  if(choice==null)return null;
  if(choice.FactionId.StartsWith("stock:",StringComparison.Ordinal)){
   string resourcePath=NativeResourcePath(choice.FactionId);if(resourcePath==null)return null;
   string key=choice.FactionId.Substring(6);Sprite icon;
   if(provided.TryGetValue(key,out icon)&&icon!=null)return icon;
   if(attempted.Add(key)){
    string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"SanctuaryMods","SanctuaryOverhauledLobby","assets","factions",key+".png");
    try{if(File.Exists(path)&&new FileInfo(path).Length<=4*1024*1024){var texture=new Texture2D(2,2);var loader=typeof(ImageConversion).GetMethod("LoadImage",new[]{typeof(Texture2D),typeof(byte[]),typeof(bool)});if(loader!=null&&(bool)loader.Invoke(null,new object[]{texture,File.ReadAllBytes(path),false})){texture.filterMode=FilterMode.Bilinear;icon=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f));provided[key]=icon;return icon;}UnityEngine.Object.Destroy(texture);}}catch(Exception ex){SanctuaryLobbyState.Warn("Faction icon could not load: "+ex.Message);}
   }
   // The stock dropdown has text-only items. Load the real Sprite resource
   // directly; no artwork is generated, copied into the package or modified.
   if(nativeAttempted.Add(key))try{icon=Resources.Load<Sprite>(resourcePath);if(icon!=null){provided[key]=icon;return icon;}SanctuaryLobbyState.Warn("Native faction Sprite unavailable: "+resourcePath);}catch(Exception ex){SanctuaryLobbyState.Warn("Native faction Sprite could not load: "+resourcePath+" ("+SanctuaryLobbyState.Root(ex)+")");}
   // Retain native UI fallback for a supported caller providing an actual icon.
   if(lobby!=null)foreach(var row in lobby.GetComponentsInChildren<EM.UI.PlayerListItem>(true)){
    var dropdown=row.GetType().GetField("factionDropdown",SanctuaryLobbyState.InstanceAll).GetValue(row);if(dropdown==null)continue;
    var items=dropdown.GetType().GetField("items",SanctuaryLobbyState.InstanceAll).GetValue(dropdown) as IEnumerable;if(items==null)continue;
    foreach(var item in items){var t=item.GetType();string name=Convert.ToString(t.GetField("itemName").GetValue(item));if(!string.Equals(name,choice.FactionName,StringComparison.OrdinalIgnoreCase))continue;icon=t.GetField("itemIcon").GetValue(item) as Sprite;if(icon!=null)return icon;}
   }
   return null;
  }
  // Exact installed Mod API contract supplies each registered mod's real icon,
  // including DYCOM variants. Never infer a faction from its commander byte.
  try{var entries=SanctuaryLobbyState.FindType("Sanctuary.ModApi.Factions").GetProperty("Current",SanctuaryLobbyState.StaticAll).GetValue(null,null) as IEnumerable;
   foreach(var entry in entries){if(!MatchesIdentity(choice,entry))continue;return SanctuaryLobbyState.FindType("Sanctuary.ModApi.FactionLobby").GetMethod("IconFor",SanctuaryLobbyState.StaticAll).Invoke(null,new[]{entry}) as Sprite;}
  }catch(Exception ex){SanctuaryLobbyState.Warn("Registered faction icon unavailable: "+SanctuaryLobbyState.Root(ex));}
  return null;
 }
}
