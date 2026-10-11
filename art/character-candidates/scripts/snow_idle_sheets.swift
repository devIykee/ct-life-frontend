import Foundation
import AppKit
let root=URL(fileURLWithPath:CommandLine.arguments[0]).deletingLastPathComponent().deletingLastPathComponent()
let proof=root.appendingPathComponent("unity-snow/animations")
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
let d="unity-snow/animations/renders/"
sheet("idle-pose-proof.jpg","SNOW / AUTHORED RELAXED IDLE","Actual Blender source preview; Unity interpretation and device acceptance are separate checks.",[
 Card(path:d+"front.png",title:"RELAXED BODY",subtitle:"Hip-width feet / grounded soles"),
 Card(path:d+"side.png",title:"NEUTRAL WRIST AND STANCE",subtitle:"No clench or wide legacy stance"),
 Card(path:d+"left_hand.png",title:"30 DIGIT JOINTS KEYED",subtitle:"Soft curl / separated fingers")])
sheet("idle-loop-samples.jpg","SNOW / ONE SIX-SECOND BREATHING LOOP","Same camera at 0 / 1.5 / 3 / 4.5 / 6 seconds. Endpoints exactly match; feet stay planted.",[
 Card(path:d+"loop_001.png",title:"0.0s",subtitle:"Start"),
 Card(path:d+"loop_046.png",title:"1.5s",subtitle:"Inhale"),
 Card(path:d+"loop_091.png",title:"3.0s",subtitle:"Mid cycle"),
 Card(path:d+"loop_136.png",title:"4.5s",subtitle:"Exhale"),
 Card(path:d+"loop_181.png",title:"6.0s",subtitle:"Exact closing pose")])
