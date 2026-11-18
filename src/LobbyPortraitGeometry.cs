using System;
using UnityEngine;

// Gamedata DDS sprites deliberately carry signed UV rectangles. UGUI Image's
// preserveAspect interprets that sign as geometry and creates a negative-width
// quad. Preserve the signed UVs, but fit the visible rectangle using magnitudes.
internal static class LobbyPortraitGeometry {
 internal static bool TryFit(Rect source,int textureWidth,int textureHeight,float boxWidth,float boxHeight,out Rect uv,out Rect draw){
  uv=draw=new Rect();
  if(textureWidth<=0||textureHeight<=0||!Finite(source.x)||!Finite(source.y)||!Finite(source.width)||!Finite(source.height)||!Finite(boxWidth)||!Finite(boxHeight)||boxWidth<=0||boxHeight<=0)return false;
  float width=Math.Abs(source.width),height=Math.Abs(source.height);
  if(width<=0||height<=0||Math.Min(source.x,source.x+source.width)<0||Math.Min(source.y,source.y+source.height)<0||Math.Max(source.x,source.x+source.width)>textureWidth||Math.Max(source.y,source.y+source.height)>textureHeight)return false;
  float scale=Math.Min(boxWidth/width,boxHeight/height),w=width*scale,h=height*scale;
  draw=new Rect((boxWidth-w)/2,(boxHeight-h)/2,w,h);
  uv=new Rect(source.x/textureWidth,source.y/textureHeight,source.width/textureWidth,source.height/textureHeight);
  return Finite(w)&&Finite(h)&&w>0&&h>0;
 }
 static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
}
