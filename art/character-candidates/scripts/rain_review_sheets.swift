import Foundation
import AppKit
let root=URL(fileURLWithPath:CommandLine.arguments[0]).deletingLastPathComponent().deletingLastPathComponent()
let proof=root.appendingPathComponent("rain-adult-production")
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
 label("Rain Rig (CC) Blender Foundation | studio.blender.org / CC-BY 4.0. Adaptation + renders: CT Life exploration.",26,H-44,17,.lightGray)
 NSGraphicsContext.restoreGraphicsState();try! rep.representation(using:.jpeg,properties:[.compressionFactor:0.94])!.write(to:proof.appendingPathComponent(file));print(file)
}
let d="rain-adult-production/renders_compiled/"
sheet("rain-character-review.jpg","RAIN / DISTINCT ADULT CHARACTER","Actual compiled one-atlas mesh. Original face, clothing and detailed hands; Unity/device review follows.",[
 Card(path:d+"front.png",title:"COMPLETE BREEZE LOOK",subtitle:"76,974 triangles / 55 bones"),
 Card(path:d+"back.png",title:"BACK AND PONYTAIL",subtitle:"Original source detail retained"),
 Card(path:d+"face.png",title:"SOURCE FACE + BLUE EYES",subtitle:"Corrected collar and texture routing"),
 Card(path:d+"hand.png",title:"ARTICULATED HANDS",subtitle:"30 independently weighted digit joints")])
let l="rain-adult-production/renders_lods/"
sheet("rain-world-lods.jpg","RAIN / REVIEWED WORLD TIERS","The full creator is unchanged. Alternate tiers protect real anatomy and preserve the source hand surface.",[
 Card(path:l+"lod0_front.png",title:"CREATOR / 76,974 TRI",subtitle:"40,013 Blender vertices"),
 Card(path:l+"lod1_front.png",title:"WORLD LOD1 / 41,416 TRI",subtitle:"One mesh / one shared atlas"),
 Card(path:l+"lod2_front.png",title:"WORLD LOD2 / 34,817 TRI",subtitle:"Higher quality floor; device cost unmeasured")])
let p="rain-adult-production/renders_deformation/"
sheet("rain-deformation-review.jpg","RAIN / STATIC DEFORMATION CHECKS","Actual rig poses, not gameplay animation captures. Shared Unity idle/movement still need retarget and contact review.",[
 Card(path:p+"lod0_wave.png",title:"CREATOR OPEN WAVE",subtitle:"Separate fingers and shoulder surface"),
 Card(path:p+"lod0_bend.png",title:"CREATOR KNEE BEND",subtitle:"Cloth and joint diagnostic pose"),
 Card(path:p+"lod2_bend.png",title:"WORLD LOD2 KNEE BEND",subtitle:"Same 55-bone binding / atlas")])
