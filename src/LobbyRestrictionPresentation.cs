using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal static class RestrictionPresentationRules {
 internal const float RowHeight=40,Gap=8;
 internal const int SideInset=12,TopInset=6,BottomInset=8;
 internal static float InnerWidth(float width){return Math.Max(0,width-SideInset*2);}
 internal static float ClearWidth(float width){return Math.Min(170,InnerWidth(width));}
 internal static bool HideNativeRow(string text){return string.Equals(text,"Back to the mods",StringComparison.OrdinalIgnoreCase)||string.Equals(text,"Clear the list",StringComparison.OrdinalIgnoreCase)||string.Equals(text,"Click a unit, a kind, a faction or a tech level; red is restricted",StringComparison.OrdinalIgnoreCase)||string.Equals(text,"The host's pick; red is restricted",StringComparison.OrdinalIgnoreCase);}
 internal static int Columns(float width,int count){return Math.Max(1,Math.Min(count,(int)Math.Floor((Math.Max(0,width)+Gap)/(170+Gap))));}
 internal static float GridHeight(float width,int count){int rows=(count+Columns(width,count)-1)/Columns(width,count);return rows*RowHeight+Math.Max(0,rows-1)*Gap;}
 internal static string CategoryText(string label,bool selected){return selected?"<s>"+label+"</s>":label;}
}

// This component is attached only to the integrated picker, never another mod's
// menu. Native layout retains its lifetime; all children disappear on rebuild.
public sealed class LobbyRestrictionContentLayout : MonoBehaviour {
 internal RectTransform ClearButton;
 void LateUpdate(){Refresh();}
 void OnRectTransformDimensionsChange(){Refresh();}
 internal void Refresh(){if(ClearButton==null)return;float width=RestrictionPresentationRules.ClearWidth(((RectTransform)transform).rect.width);if(Math.Abs(ClearButton.sizeDelta.x-width)>.1f)ClearButton.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,width);}
}

public sealed class LobbyRestrictionCategoryGrid : MonoBehaviour {
 GridLayoutGroup grid;LayoutElement height;float lastWidth=-1;int lastCount=-1;
 void LateUpdate(){Refresh();}
 void OnRectTransformDimensionsChange(){Refresh();}
 internal void Refresh(){
  if(grid==null)grid=GetComponent<GridLayoutGroup>();if(height==null)height=GetComponent<LayoutElement>();if(grid==null||height==null)return;
  float width=((RectTransform)transform).rect.width;int count=transform.childCount;if(width<=0||count==0||Math.Abs(width-lastWidth)<.1f&&count==lastCount)return;
  lastWidth=width;lastCount=count;int columns=RestrictionPresentationRules.Columns(width,count);
  // Override the grid's previous preferred width so a narrower viewport can
  // shrink it. The native vertical parent otherwise keeps the old minimum.
  height.minWidth=height.preferredWidth=0;height.flexibleWidth=1;
  grid.constraint=GridLayoutGroup.Constraint.FixedColumnCount;grid.constraintCount=columns;grid.spacing=new Vector2(8,8);
  grid.cellSize=new Vector2(Math.Max(1,(width-(columns-1)*8)/columns),RestrictionPresentationRules.RowHeight);
  float h=RestrictionPresentationRules.GridHeight(width,count);height.minHeight=height.preferredHeight=h;height.flexibleHeight=0;
  // Native list layout deliberately has childControlHeight=false.
  ((RectTransform)transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,h);
 }
}

internal static class LobbyRestrictionPresentation {
 internal static Transform InsetContent(Transform parent){
  var children=parent.Cast<Transform>().ToArray();
  var go=new GameObject("Restriction content",typeof(RectTransform),typeof(LayoutElement),typeof(VerticalLayoutGroup),typeof(ContentSizeFitter),typeof(LobbyRestrictionContentLayout));go.transform.SetParent(parent,false);
  var rect=(RectTransform)go.transform;rect.anchorMin=new Vector2(0,1);rect.anchorMax=Vector2.one;rect.pivot=new Vector2(.5f,1);rect.sizeDelta=Vector2.zero;
  var size=go.GetComponent<LayoutElement>();size.minWidth=size.preferredWidth=0;size.flexibleWidth=1;
  var native=parent.GetComponent<VerticalLayoutGroup>();var layout=go.GetComponent<VerticalLayoutGroup>();layout.spacing=native==null?15:native.spacing;layout.childControlWidth=true;layout.childControlHeight=false;layout.childForceExpandWidth=true;layout.childForceExpandHeight=false;
  layout.padding=new RectOffset(RestrictionPresentationRules.SideInset,RestrictionPresentationRules.SideInset,RestrictionPresentationRules.TopInset,RestrictionPresentationRules.BottomInset);
  go.GetComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
  foreach(var child in children)child.SetParent(go.transform,false);
  // The shared native list, viewport, scroll state and footer are untouched.
  // Native Clear destroys this owned wrapper with its children on rebuild.
  return go.transform;
 }
 internal static void PlaceCentered(RectTransform rect,float x,float width,float height){rect.anchorMin=rect.anchorMax=new Vector2(0,.5f);rect.pivot=new Vector2(0,.5f);rect.anchoredPosition=new Vector2(x,0);rect.sizeDelta=new Vector2(width,height);}
 internal static void Label(TMP_Text text,float left){
  if(text==null)return;var r=text.rectTransform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=new Vector2(left,3);r.offsetMax=new Vector2(-8,-3);
  text.alignment=TextAlignmentOptions.MidlineLeft;text.margin=Vector4.zero;if(!text.enableAutoSizing||text.fontSizeMin!=10||text.fontSizeMax!=16){text.enableAutoSizing=true;text.fontSizeMin=10;text.fontSizeMax=16;text.fontSize=16;}text.textWrappingMode=TextWrappingModes.Normal;text.overflowMode=TextOverflowModes.Ellipsis;
 }
 internal static void Card(Image back,Image glow,Image symbol,TMP_Text text,string unit,bool isCell){
  if(back==null)return;
  if(isCell){var size=back.GetComponent<LayoutElement>()??back.gameObject.AddComponent<LayoutElement>();size.minHeight=size.preferredHeight=RestrictionPresentationRules.RowHeight;size.flexibleHeight=0;}
  if(back.GetComponent<RectMask2D>()==null)back.gameObject.AddComponent<RectMask2D>();
  // Native RefreshLobbyValues runs before the final layout rebuild. Do not
  // capture that provisional height: centered anchors follow final geometry
  // immediately on open/reopen and resize without another option change.
  float glyph=24,left=42,center=22;bool hasPortrait=false;
  var old=back.transform.Find("Unit portrait");var raw=old==null?null:old.GetComponent<RawImage>();
  if(isCell&&!string.IsNullOrEmpty(unit)){
   try{
    string path=UnitRestrictionsUiRules.PortraitPath(unit);var entry=path==null?null:EM.Gamedata.Data.GetFileEntry(path);var asset=entry==null?null:EM.Gamedata.Load.LoadGamedataSprite(entry);var sprite=asset==null?null:asset.GetSprite();
    if(sprite!=null&&sprite.texture!=null){Rect uv,draw;float box=28;var texture=sprite.texture;
     if(LobbyPortraitGeometry.TryFit(sprite.textureRect,texture.width,texture.height,box,box,out uv,out draw)){
      if(raw==null){var go=new GameObject("Unit portrait",typeof(RectTransform),typeof(RawImage));go.transform.SetParent(back.transform,false);raw=go.GetComponent<RawImage>();}
      PlaceCentered(raw.rectTransform,6+draw.x,draw.width,draw.height);raw.texture=texture;raw.uvRect=uv;raw.color=Color.white;raw.raycastTarget=false;raw.enabled=true;hasPortrait=true;
     }
    }
   }catch{/* Keep the native white symbol and retry after a late pack mount. */}
  }
  if(raw!=null&&!hasPortrait)raw.enabled=false;
  if(hasPortrait){glyph=20;center=48;left=64;}
  bool restricted=isCell&&text!=null&&text.text.StartsWith("<s>",StringComparison.Ordinal);
  if(symbol!=null){PlaceCentered(symbol.rectTransform,center-glyph/2,glyph,glyph);symbol.color=restricted?new Color(1,.28f,.32f):Color.white;symbol.preserveAspect=true;symbol.raycastTarget=false;}
  // The native updater has just set allowed-blue or restricted-red. Keep its
  // hue as a restrained halo; never let the glow obscure the white red-channel
  // symbol mask. Back/text/strike-through continue to be native-owned.
  if(glow!=null){PlaceCentered(glow.rectTransform,center-glyph/2,glyph,glyph);var c=glow.color;glow.color=new Color(c.r,c.g,c.b,restricted?.4f:.12f);glow.raycastTarget=false;}
  Label(text,left);
 }
 internal static void Category(object box,bool selected,bool editable,string label){
  var t=box.GetType();const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
  var back=t.GetField("Back",flags).GetValue(box) as Image;var text=t.GetField("Text",flags).GetValue(box) as TMP_Text;if(back==null||text==null)return;
  back.color=selected?new Color(.40f,.08f,.10f,1):new Color(.055f,.10f,.15f,1);
  var button=back.GetComponent<Button>();if(button!=null){button.interactable=editable;var nav=button.navigation;nav.mode=Navigation.Mode.Automatic;button.navigation=nav;}
  var outline=back.GetComponent<Outline>()??back.gameObject.AddComponent<Outline>();outline.effectDistance=new Vector2(2,-2);outline.effectColor=selected?new Color(1,.18f,.22f,.55f):new Color(.30f,.50f,.65f,.22f);outline.useGraphicAlpha=true;
  text.text=RestrictionPresentationRules.CategoryText(label,selected);text.color=selected?new Color(1,.85f,.85f):Color.white;Label(text,8);text.alignment=TextAlignmentOptions.Center;
 }
}
