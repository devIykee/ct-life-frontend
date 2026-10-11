import Foundation
import AppKit
let root=URL(fileURLWithPath:CommandLine.arguments[0]).deletingLastPathComponent().deletingLastPathComponent()
let proof=root.appendingPathComponent("snow-adult-proof")
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
let d="snow-adult-proof/renders/"
sheet("before-after.jpg","SNOW / ADULT PROPORTION EXPLORATION","Actual model renders. Head 76% scale, legs +9%, subtle orbital reduction. Source identity retained; art direction remains provisional.",[
 Card(path:"renders/snow_full.png",title:"ORIGINAL SNOW",subtitle:"Supplied proportions / source preview"),
 Card(path:d+"adult_high_reference.png",title:"ADULT / DETAIL REFERENCE",subtitle:"Same source surfaces, adapted proportions"),
 Card(path:d+"adult_lod0_relaxed.png",title:"ADULT / CITY LOD0 DRAFT",subtitle:"27 bones / four influences / atlas pending")])
sheet("face-comparison.jpg","SNOW / FACE QUALITY CHECK","Actual face geometry and textures. Detailed reference is retained; city simplification is not the final creator mesh.",[
 Card(path:"renders/snow_face.png",title:"ORIGINAL FACE",subtitle:"Supplied face and eye proportions"),
 Card(path:d+"adult_high_face.png",title:"ADULT / DETAIL REFERENCE",subtitle:"Face modelling preserved through adaptation"),
 Card(path:d+"adult_lod0_face.png",title:"ADULT / CITY LOD0 DRAFT",subtitle:"Visible contour and surface detail loss")])
sheet("pose-checks.jpg","SNOW / DEFORMATION PROOF","Three static diagnostic poses on the 27-bone draft. These are not animation clips or a complete deformation acceptance test.",[
 Card(path:d+"adult_lod0_arm_raise.png",title:"ARM RAISE",subtitle:"Shoulder / sleeve stress"),
 Card(path:d+"adult_lod0_walk_pose.png",title:"WALK CONTACT POSE",subtitle:"Hip / knee / ankle stress"),
 Card(path:d+"adult_lod0_sit_pose.png",title:"SEATED POSE",subtitle:"Hip + knee bend / garment stress")])
let metrics=try! JSONSerialization.jsonObject(with:Data(contentsOf:proof.appendingPathComponent("metrics.json"))) as! [String:Any]
let totals=metrics["lod_totals"] as! [String:Int]
sheet("lod-comparison.jpg","SNOW / CITY LOD CHECK","Same camera at enlarged review size. Far LODs need screen-size acceptance; triangle budgets do not establish visual quality.",[
 Card(path:d+"adult_lod0_relaxed.png",title:"LOD0 / \(totals["0"]!) TRIANGLES",subtitle:"Near city draft; close-up issues remain"),
 Card(path:d+"adult_lod1_relaxed.png",title:"LOD1 / \(totals["1"]!) TRIANGLES",subtitle:"Mid-distance draft; detail reduced"),
 Card(path:d+"adult_lod2_relaxed.png",title:"LOD2 / \(totals["2"]!) TRIANGLES",subtitle:"Far-distance draft; not for portraits")])
sheet("hand-defect-comparison.jpg","SNOW / HAND LOD DEFECT","The low-detail hand introduces dark patches absent in the reference. Not accepted for close interactions.",[
 Card(path:d+"adult_ref_hand.png",title:"DETAILED REFERENCE",subtitle:"Source surface + remapped game skeleton"),
 Card(path:d+"adult_lod0_hand.png",title:"CITY LOD0 / UNRESOLVED",subtitle:"Texture / surface defects require cleanup")],true)
