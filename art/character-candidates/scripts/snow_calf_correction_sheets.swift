import Foundation
import AppKit
let root=URL(fileURLWithPath:CommandLine.arguments[0]).deletingLastPathComponent().deletingLastPathComponent()
let proof=root.appendingPathComponent("unity-snow/calf-quality-candidate")
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
let d="unity-snow/calf-quality-candidate/"
sheet("calf-correction.jpg","SNOW / ATLAS CALF CORRECTION","Same detailed geometry, camera and lights. Corrected source-texture bake; no painted cover-up.",[
 Card(path:d+"inspection/compiled_calves.png",title:"BEFORE / REJECTED ATLAS",subtitle:"Hidden shoe faces skipped UV packing"),
 Card(path:d+"inspection/retained_source_calves.png",title:"RETAINED SOURCE MATERIAL",subtitle:"Authored denim is clean"),
 Card(path:d+"renders/lod0_calves.png",title:"CORRECTED COMPILED ATLAS",subtitle:"All copied faces packed and rebaked")],true)
sheet("whole-look-review.jpg","SNOW / CORRECTED WHOLE LOOK","Actual creator mesh and final atlas. Front, back and both sides reviewed.",[
 Card(path:d+"renders/lod0_front.png",title:"FRONT",subtitle:"43,478 triangles / 55 bones"),
 Card(path:d+"renders/lod0_back.png",title:"BACK",subtitle:"Clean denim and nape"),
 Card(path:d+"renders/lod0_left.png",title:"LEFT",subtitle:"Authored sleeve / shoe silhouettes"),
 Card(path:d+"renders/lod0_right.png",title:"RIGHT",subtitle:"Detailed hands preserved")])
sheet("lod-back-review.jpg","SNOW / BACKS OF ALL THREE TIERS","Same corrected atlas. Source scalp and underlying nape protected in the reduced tiers.",[
 Card(path:d+"renders/lod0_back.png",title:"CREATOR / 43,478 TRI",subtitle:"Full source-quality surface"),
 Card(path:d+"renders/lod1_back.png",title:"LOD1 / 21,005 TRI",subtitle:"Scalp and nape boundary retained"),
 Card(path:d+"renders/lod2_back.png",title:"LOD2 / 15,436 TRI",subtitle:"Extra head/hair budget preserves silhouette")])
sheet("lod-bent-knee-review.jpg","SNOW / BENT KNEE ATLAS REVIEW","Diagnostic articulation on the shared 55-bone rig. This is not an in-game animation capture.",[
 Card(path:d+"renders/lod0_bent_knee.png",title:"CREATOR / 43,478 TRI",subtitle:"Clean denim through the bend"),
 Card(path:d+"renders/lod1_bent_knee.png",title:"LOD1 / 21,005 TRI",subtitle:"Same rig and atlas"),
 Card(path:d+"renders/lod2_bent_knee.png",title:"LOD2 / 15,436 TRI",subtitle:"Simpler cloth folds and soles; far only")])
