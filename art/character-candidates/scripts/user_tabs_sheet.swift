import Foundation
import AppKit
let root=URL(fileURLWithPath:CommandLine.arguments[0]).deletingLastPathComponent().deletingLastPathComponent().appendingPathComponent("user-tabs-audit")
let W=1800,H=1040
let rep=NSBitmapImageRep(bitmapDataPlanes:nil,pixelsWide:W,pixelsHigh:H,bitsPerSample:8,samplesPerPixel:4,hasAlpha:true,isPlanar:false,colorSpaceName:.deviceRGB,bytesPerRow:0,bitsPerPixel:0)!
NSGraphicsContext.saveGraphicsState();NSGraphicsContext.current=NSGraphicsContext(bitmapImageRep:rep);NSColor(calibratedWhite:0.06,alpha:1).setFill();NSRect(x:0,y:0,width:W,height:H).fill()
func label(_ s:String,_ x:Int,_ y:Int,_ size:CGFloat,_ color:NSColor){NSString(string:s).draw(at:NSPoint(x:x,y:H-y-Int(size*1.2)),withAttributes:[.font:NSFont.systemFont(ofSize:size,weight:size>24 ? .semibold : .regular),.foregroundColor:color])}
label("OPEN SKETCHFAB TABS / ACTUAL PUBLISHER PREVIEWS",24,15,32,.white);label("Research evidence only. No geometry acquired, game import or image generation. Verified 2026-10-05.",24,60,21,.lightGray)
let cards=[("man_coat","MAN IN COAT · Kasit Studio","Standard · no free download · 19.4k tri"),("broadway_block","358 BROADWAY · 99.Miles","Paid Fab pack · real geometry cost unknown"),("razor_crest","RAZOR CREST · Binkley-Spacetrucker","CC-BY · 333k faces · franchise rights unresolved"),("drone","DRONE V2 · iggy-design","Standard · no free download · 9.6k faces"),("wings","PRINTABLE WINGS · Rumpelstiltskin","Standard · 1.53M faces · print sculpt"),("bridge","MANHATTAN BRIDGE · Brian Trepanier","CC-BY · 1.40M faces · rough street surfaces")]
for (i,c) in cards.enumerated(){let x=(i%3)*600,y=115+(i/3)*445;let im=NSImage(contentsOf:root.appendingPathComponent("thumbs/"+c.0+".jpg"))!;im.draw(in:NSRect(x:x+10,y:H-y-326,width:580,height:326));label(c.1,x+15,y+342,21,.white);label(c.2,x+15,y+377,19,.lightGray)}
label("Sources and full licence/format/access limits: README.md + original API JSON. Images remain credited to their authors.",24,H-38,18,.lightGray)
NSGraphicsContext.restoreGraphicsState();try! rep.representation(using:.jpeg,properties:[.compressionFactor:0.93])!.write(to:root.appendingPathComponent("observed-tabs-contact-sheet.jpg"))
