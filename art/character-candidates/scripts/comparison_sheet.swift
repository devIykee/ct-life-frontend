import Foundation
import AppKit
let root=URL(fileURLWithPath:CommandLine.arguments[0]).deletingLastPathComponent().deletingLastPathComponent()
let cards=[("snow","SNOW","Blender Studio / Free CC-BY","Production source; conversion required"),("rain","RAIN","Blender Studio / Free CC-BY","Production source; conversion required"),("quaternius_male","QUATERNIUS / MALE","Universal Base Standard / Free CC0","Superhero build; no face morphs"),("quaternius_female","QUATERNIUS / FEMALE","Universal Base Standard / Free CC0","Superhero build; no face morphs"),("metaperson_male","METAPERSON / MALE","Official sample / Evaluation only","Photo route; paid service + license review"),("metaperson_female","METAPERSON / FEMALE","Official sample / Evaluation only","Photo route; paid service + license review")]
let W=1800,H=1810
for kind in ["face","full"] {
 let rep=NSBitmapImageRep(bitmapDataPlanes:nil,pixelsWide:W,pixelsHigh:H,bitsPerSample:8,samplesPerPixel:4,hasAlpha:true,isPlanar:false,colorSpaceName:.deviceRGB,bytesPerRow:0,bitsPerPixel:0)!
 NSGraphicsContext.saveGraphicsState();NSGraphicsContext.current=NSGraphicsContext(bitmapImageRep:rep)
 NSColor(calibratedRed:0.08,green:0.10,blue:0.13,alpha:1).setFill();NSRect(x:0,y:0,width:W,height:H).fill()
 func label(_ s:String,_ x:Int,_ y:Int,_ size:CGFloat,_ color:NSColor){let attrs:[NSAttributedString.Key:Any]=[.font:NSFont.systemFont(ofSize:size,weight:size>30 ? .semibold : .regular),.foregroundColor:color];NSString(string:s).draw(at:NSPoint(x:x,y:H-y-Int(size*1.2)),withAttributes:attrs)}
 label("CT LIFE / REAL CHARACTER COMPARISON",35,25,48,.white)
 label("Actual downloaded 3D models. Same Blender lighting. No generated substitutes. / 05 OCT 2026",37,87,23,.lightGray)
 for (i,c) in cards.enumerated(){let x=(i%3)*600,y=140+(i/3)*815
  let im=NSImage(contentsOf:root.appendingPathComponent("renders/\(c.0)_\(kind).png"))!
  let scale=min(575/im.size.width,675/im.size.height),w=im.size.width*scale,h=im.size.height*scale
  im.draw(in:NSRect(x:CGFloat(x)+(600-w)/2,y:CGFloat(H-y)-h,width:w,height:h))
  label(c.1,x+22,y+695,31,.white);label(c.2,x+22,y+739,22,.lightGray);label(c.3,x+22,y+771,22,NSColor(calibratedWhite:0.64,alpha:1))
 }
 label("Snow/Rain: Blender Foundation. Quaternius: Quaternius. MetaPerson: itSeez3D. Sources and preview changes in README.md.",28,1771,19,.lightGray)
 NSGraphicsContext.restoreGraphicsState()
 let url=root.appendingPathComponent("comparisons/\(kind)-comparison.jpg")
 try rep.representation(using:.jpeg,properties:[.compressionFactor:0.94])!.write(to:url)
 print(url.path)
}
