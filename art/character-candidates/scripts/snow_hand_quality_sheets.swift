import Foundation
import AppKit
let root=URL(fileURLWithPath:CommandLine.arguments[0]).deletingLastPathComponent().deletingLastPathComponent()
let proof=root.appendingPathComponent("snow-hand-quality")
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
let d="snow-hand-quality/renders/"
sheet("hand-before-after.jpg","SNOW / HAND QUALITY CORRECTION","Source hand geometry retained in city LOD0 and creator tiers. Actual rendered meshes; earlier defective proof is preserved for comparison.",[
 Card(path:"snow-adult-proof/renders/adult_lod0_hand.png",title:"BEFORE / REDUCED HAND",subtitle:"Dark patches introduced by simplification"),
 Card(path:"snow-adult-proof/renders/adult_ref_hand.png",title:"DETAILED SOURCE REFERENCE",subtitle:"Authored hand surfaces and UVs"),
 Card(path:d+"city_hand_relaxed.png",title:"AFTER / CITY LOD0",subtitle:"Source detail retained / patches absent")],true)
sheet("gesture-proof.jpg","SNOW / INDEPENDENT HAND ARTICULATION","55-bone candidate with 30 weighted digit joints. Authored static poses; no final animation, collider or device-performance claim.",[
 Card(path:d+"city_phone_grip.png",title:"CITY / PHONE GRIP",subtitle:"11,451 triangles / diagnostic phone proxy"),
 Card(path:d+"city_open_wave.png",title:"CITY / OPEN-HAND WAVE",subtitle:"Independent thumb + finger spread"),
 Card(path:d+"creator_relaxed.png",title:"CREATOR / RELAXED",subtitle:"18,365 triangles / same detailed hands")])
sheet("gesture-details.jpg","SNOW / HAND GESTURE DETAILS","The avatar FBXs retain the finger deformation through a fresh Blender import. Phone proxy is excluded from avatar export.",[
 Card(path:d+"city_phone_grip_close.png",title:"CITY / PHONE GRIP DETAIL",subtitle:"Separate proximal, middle and distal joints"),
 Card(path:d+"creator_phone_grip_close.png",title:"CREATOR / SAME HAND DETAIL",subtitle:"Same source hand geometry and weights"),
 Card(path:d+"city_open_wave_close.png",title:"OPEN PALM / FINGER SPREAD",subtitle:"Articulation preserved at four influences")],true)
