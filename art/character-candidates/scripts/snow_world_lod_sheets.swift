import Foundation
import AppKit
let root=URL(fileURLWithPath:CommandLine.arguments[0]).deletingLastPathComponent().deletingLastPathComponent()
let relative=CommandLine.arguments.count>1 ? CommandLine.arguments[1] : "unity-snow"
let proof=root.appendingPathComponent(relative)
struct Card{let path:String;let title:String;let subtitle:String}
func sheet(_ file:String,_ title:String,_ subtitle:String,_ cards:[Card],_ square:Bool=false){
 let W=cards.count*600,H=square ? 920 : 1120
 let rep=NSBitmapImageRep(bitmapDataPlanes:nil,pixelsWide:W,pixelsHigh:H,bitsPerSample:8,samplesPerPixel:4,hasAlpha:true,isPlanar:false,colorSpaceName:.deviceRGB,bytesPerRow:0,bitsPerPixel:0)!
 NSGraphicsContext.saveGraphicsState();NSGraphicsContext.current=NSGraphicsContext(bitmapImageRep:rep)
 NSColor(calibratedRed:0.08,green:0.10,blue:0.13,alpha:1).setFill();NSRect(x:0,y:0,width:W,height:H).fill()
 func label(_ s:String,_ x:Int,_ y:Int,_ size:CGFloat,_ color:NSColor){let a:[NSAttributedString.Key:Any]=[.font:NSFont.systemFont(ofSize:size,weight:size>30 ? .semibold : .regular),.foregroundColor:color];NSString(string:s).draw(at:NSPoint(x:x,y:H-y-Int(size*1.2)),withAttributes:a)}
 label(title,28,25,42,.white);label(subtitle,29,85,21,.lightGray)
 let imageH=square ? 600.0 : 800.0
 for (i,c) in cards.enumerated(){
  let im=NSImage(contentsOf:root.appendingPathComponent(c.path))!;let scale=min(580/im.size.width,imageH/im.size.height);let w=im.size.width*scale,h=im.size.height*scale
  im.draw(in:NSRect(x:CGFloat(i*600)+(600-w)/2,y:CGFloat(H-140)-h,width:w,height:h))
  label(c.title,i*600+23,Int(155+imageH),29,.white);label(c.subtitle,i*600+23,Int(200+imageH),21,.lightGray)
 }
 label("Snow Rig (CC) Blender Foundation | studio.blender.org / CC-BY 4.0. Adaptation + renders: CT Life exploration.",26,H-44,17,.lightGray)
 NSGraphicsContext.restoreGraphicsState();try! rep.representation(using:.jpeg,properties:[.compressionFactor:0.94])!.write(to:proof.appendingPathComponent(file));print(file)
}
let d=relative+"/renders/lods/"
sheet("lod-comparison.jpg","SNOW / SHARED-ATLAS WORLD LODS","Actual compiled meshes, same camera and lighting. Corrected atlas; protected nape/collar. World thresholds still require device review.",[
 Card(path:d+"lod0_full.png",title:"CREATOR / 43,478 TRI",subtitle:"Detailed source-quality whole look"),
 Card(path:d+"lod1_full.png",title:"WORLD LOD1 / 21,005 TRI",subtitle:"Detailed hands / protected face"),
 Card(path:d+"lod2_full.png",title:"FAR LOD2 / 15,436 TRI",subtitle:"Detailed hands / simpler hair + clothes")])
sheet("lod-face-review.jpg","SNOW / DELIBERATELY ENLARGED FACE REVIEW","Same atlas and source surface normals. Far LOD2 shows ear, hair and neck simplification; keep creator preview on the full mesh.",[
 Card(path:d+"lod0_face.png",title:"CREATOR / FULL DETAIL",subtitle:"Unchanged source-quality face"),
 Card(path:d+"lod1_face.png",title:"WORLD LOD1",subtitle:"1,882 front-face vertices locked"),
 Card(path:d+"lod2_face.png",title:"FAR LOD2 / NOT A PREVIEW",subtitle:"1,116 key face vertices locked")],true)
sheet("lod-wave-review.jpg","SNOW / SAME 55-BONE WAVE ACROSS LODS","One renderer and one material per tier. Original hand positions and atlas UV samples survive both reductions unchanged.",[
 Card(path:d+"lod0_wave.png",title:"CREATOR / WAVE",subtitle:"30 independent digit joints"),
 Card(path:d+"lod1_wave.png",title:"LOD1 / WAVE",subtitle:"Entire detailed arm-hand surface retained"),
 Card(path:d+"lod2_wave.png",title:"LOD2 / WAVE",subtitle:"Detailed hands / reduced arms")])
