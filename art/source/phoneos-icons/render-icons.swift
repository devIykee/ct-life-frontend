// CTOS original layered icon artwork. Native CoreGraphics; no downloaded symbols or Apple artwork.
import AppKit
import Foundation
let out = CommandLine.arguments[1]
try FileManager.default.createDirectory(atPath: out, withIntermediateDirectories: true)
var g: CGContext!
func color(_ hex: String, _ alpha: CGFloat = 1) -> CGColor {
    let v = UInt32(hex.replacingOccurrences(of: "#", with: ""), radix: 16) ?? 0
    return CGColor(red: CGFloat((v>>16)&255)/255, green: CGFloat((v>>8)&255)/255, blue: CGFloat(v&255)/255, alpha: alpha)
}
func rect(_ x: CGFloat,_ y: CGFloat,_ w: CGFloat,_ h: CGFloat,_ r: CGFloat = 0)->CGPath { CGPath(roundedRect: CGRect(x:x,y:y,width:w,height:h), cornerWidth:r,cornerHeight:r,transform:nil) }
func circle(_ x: CGFloat,_ y: CGFloat,_ r: CGFloat)->CGPath { CGPath(ellipseIn:CGRect(x:x-r,y:y-r,width:r*2,height:r*2),transform:nil) }
func path(_ str:String)->CGPath {
 let regex=try! NSRegularExpression(pattern:"[MLCQHVZmlcqhvz]|[-+]?[0-9]*\\.?[0-9]+")
 let ns=str as NSString; let t=regex.matches(in:str,range:NSRange(location:0,length:ns.length)).map{ns.substring(with:$0.range)}
 let p=CGMutablePath();var i=0;var point=CGPoint.zero;var start=CGPoint.zero
 func val()->CGFloat { let v=CGFloat(Double(t[i])!); i+=1; return v }
 while i<t.count { let cmd=t[i];i+=1; switch cmd {
 case "M": point=CGPoint(x:val(),y:val()); p.move(to:point); start=point
 case "L": point=CGPoint(x:val(),y:val()); p.addLine(to:point)
 case "H": point.x=val();p.addLine(to:point)
 case "V": point.y=val();p.addLine(to:point)
 case "C": let a=CGPoint(x:val(),y:val()), b=CGPoint(x:val(),y:val()); point=CGPoint(x:val(),y:val());p.addCurve(to:point,control1:a,control2:b)
 case "Q":let a=CGPoint(x:val(),y:val());point=CGPoint(x:val(),y:val());p.addQuadCurve(to:point,control:a)
 case "Z":p.closeSubpath();point=start
 default: fatalError("Bad path token \(cmd)")
 }}
 return p
}
func gradient(_ p:CGPath,_ a:String,_ b:String, alpha:CGFloat=1) {
 g.saveGState();g.addPath(p);g.clip()
 let bnd=p.boundingBoxOfPath
 let gr=CGGradient(colorsSpace:CGColorSpaceCreateDeviceRGB(),colors:[color(a,alpha),color(b,alpha)] as CFArray,locations:[0,1])!
 g.drawLinearGradient(gr,start:CGPoint(x:bnd.midX,y:bnd.minY),end:CGPoint(x:bnd.midX,y:bnd.maxY),options:[.drawsBeforeStartLocation,.drawsAfterEndLocation]);g.restoreGState()
}
func fill(_ p:CGPath,_ c:String,_ alpha:CGFloat=1){g.addPath(p);g.setFillColor(color(c,alpha));g.fillPath()}
func stroke(_ p:CGPath,_ c:String,_ w:CGFloat=2,_ alpha:CGFloat=1){g.addPath(p);g.setStrokeColor(color(c,alpha));g.setLineWidth(w);g.setLineCap(.round);g.setLineJoin(.round);g.strokePath()}
func glass(_ p:CGPath,_ a:String="FFFFFF",_ b:String="DCEBFA", shadow:Bool=true) {
 g.saveGState();if shadow {g.setShadow(offset:CGSize(width:0,height:3),blur:5,color:color("07162E",0.2))};fill(p,b);g.restoreGState()
 gradient(p,a,b);stroke(p,"FFFFFF",1.2,0.64)
}
func line(_ s:String,_ c:String="FFFFFF",_ w:CGFloat=7,_ alpha:CGFloat=1){stroke(path(s),c,w,alpha)}
func orb(_ x:CGFloat,_ y:CGFloat,_ r:CGFloat,_ a:String,_ b:String){glass(circle(x,y,r),a,b)}
func enclosure()->CGPath {
 let p=CGMutablePath();let n:CGFloat=4.7
 for i in 0...256 { let t=CGFloat(i)/256*CGFloat.pi*2;let x=cos(t),y=sin(t);let q=CGPoint(x:96+94*(x<0 ? -1:1)*pow(abs(x),2/n),y:96+94*(y<0 ? -1:1)*pow(abs(y),2/n));if i==0{p.move(to:q)}else{p.addLine(to:q)}};p.closeSubpath();return p
}
func base(_ a:String,_ b:String) {let p=enclosure();gradient(p,a,b);stroke(p,"FFFFFF",1.5,0.6)}
func gear(_ cx:CGFloat,_ cy:CGFloat,_ r:CGFloat,_ inner:CGFloat,_ count:Int=12)->CGPath {
 let p=CGMutablePath()
 for i in 0..<(count*4){let t=CGFloat(i)/CGFloat(count*4)*CGFloat.pi*2;let rr=(i%4==1 || i%4==2) ? r : inner;let q=CGPoint(x:cx+cos(t)*rr,y:cy+sin(t)*rr);if i==0{p.move(to:q)}else{p.addLine(to:q)}};p.closeSubpath();return p
}
func person(_ x:CGFloat,_ y:CGFloat,_ s:CGFloat,_ a:String,_ b:String){g.saveGState();g.translateBy(x:x,y:y);g.scaleBy(x:s,y:s);glass(circle(0,-17,18),a,b);glass(path("M-32 31 C-32 2 32 2 32 31 C15 42 -15 42 -32 31 Z"),a,b);g.restoreGState()}
func coin(_ x:CGFloat,_ y:CGFloat,_ r:CGFloat){orb(x,y,r,"FFF9AE","EAA41E");stroke(circle(x,y,r-5),"AD6A0A",1.5,0.38);stroke(path("M\(x-5) \(y-r+8) C\(x-20) \(y-r+16) \(x-20) \(y+r-14) \(x-5) \(y+r-8)"),"FFFFFF",2.0,0.8)}
func symbol(_ id:String) {
 switch id {
 case "map":
 base("EEF7EF","D6E6F4")
 fill(path("M2 2 H82 V66 L2 106 Z"),"A4DEA0");fill(path("M130 2 H192 V106 L130 77 Z"),"72B5F1");fill(path("M2 133 L82 97 L130 123 V192 H2 Z"),"FFC976")
 line("M-5 120 L82 78 L198 137","FFFFFF",18);line("M95 -10 L95 204","FFFFFF",18);line("M95 -10 L95 204","CDD9E1",2)
 glass(path("M131 48 C104 48 91 67 96 87 C99 102 131 132 131 132 C131 132 163 102 166 87 C171 67 158 48 131 48 Z"),"68A9FF","2163E4")
 orb(131,78,13,"FFFFFF","DCEBFF")
 case "jobs":
 base("37B6FF","1164DE");glass(rect(46,47,105,102,12),"83D3FF","3189EE");glass(path("M45 67 H74 L84 79 H155 V143 Q155 153 145 153 H44 Q34 153 34 143 V78 Q34 67 45 67 Z"),"FFFFFF","CDE9FF")
 stroke(rect(73,51,47,29,10),"FFFFFF",10);glass(rect(81,96,29,20,6),"FFDC78","F6AE32",shadow:false);line("M38 102 H80 M111 102 H151","2876BC",2,0.22)
 case "bank":
 base("FFFDF4","F2E1B5");glass(path("M32 68 L96 35 L160 68 Z"),"FDE899","D7A837");glass(rect(39,72,114,12,4),"FFFFFF","E7D2A0")
 for x:CGFloat in [49,88,127] {glass(rect(x,83,16,49,4),"FFFFF6","D8BD75")};glass(rect(32,135,128,17,5),"FFF3C6","DBAD45");coin(135,139,24)
 case "wallet":
 base("323F59","0C1428");g.saveGState();g.translateBy(x:96,y:86);g.rotate(by:-0.12);glass(rect(-63,-48,126,80,15),"B49BFF","6D5CBA");g.restoreGState()
 glass(rect(35,51,124,82,14),"6FDFC1","2DAD88");glass(rect(31,74,132,83,17),"E9BE7A","AF703B");glass(rect(32,89,130,68,16),"D5A267","8F592E",shadow:false)
 glass(rect(120,101,48,30,10),"FAD9A4","BF864C");orb(137,116,5,"FFF6D9","D9B779")
 case "richlist":
 base("FFFADE","F2D87B");glass(rect(29,110,42,45,7),"E8C657","C19326");glass(rect(76,83,42,72,7),"FFEB9B","D9A432");glass(rect(123,122,40,33,7),"D5AE42","B78820")
 glass(path("M60 40 L80 55 L97 29 L113 55 L134 40 L126 77 H69 Z"),"FFFBCC","E9B139");orb(97,54,6,"FFFFFF","FFEB96")
 case "home":
 base("FFFFFF","F6F0E6");glass(path("M40 88 L96 38 L152 88 L141 88 V151 H51 V88 Z"),"FFBB3D","F08418");glass(path("M59 91 L96 59 L134 91 V140 H59 Z"),"FFDA71","FFB13E");glass(path("M76 99 L96 82 L116 99 V129 H76 Z"),"FFF6CA","FFDF7C")
 case "profile":
 base("E6E8FF","BAC1F5");glass(rect(34,29,124,134,25),"FFFFFF","E5E9FB");glass(circle(96,96,46),"88B9F3","547FCA");person(96,97,0.88,"FFFFFF","CEDEFA")
 case "settings":
 base("E3E8ED","A4B1C0");glass(gear(96,97,66,56),"F7F9FB","8393A5");orb(96,97,45,"6F7F90","1B2637");stroke(circle(96,97,39),"FFFFFF",2,0.65);glass(gear(96,97,31,27,10),"DBE4ED","76869A");orb(96,97,16,"3C4A5C","0F1C2E");stroke(circle(96,97,12),"FFFFFF",1,0.5)
 case "pulse":
 base("FFFFFF","F7E9ED");glass(path("M96 148 C65 125 32 99 34 72 C36 39 73 32 96 59 C119 32 156 39 158 72 C160 99 127 125 96 148 Z"),"FF788F","DA3456");line("M43 98 H66 L78 78 L94 119 L109 87 L119 99 H151","FFFFFF",7)
 case "perps":
 base("29354B","081528");for y:CGFloat in [52,96,140]{line("M25 \(y) H167","7B91AE",1,0.21)}
 for (x,y,h,c) in [(45.0,103.0,35.0,"52DCAF"),(77.0,86.0,39.0,"FF858B"),(109.0,67.0,49.0,"52DCAF"),(141.0,43.0,37.0,"52DCAF")]{line("M\(x) \(y-12) V\(y+h+12)",c,3);glass(rect(x-9,y,18,h,5),c,c,shadow:false)}
 case "predict":
 base("7B76FF","393A9E");g.saveGState();g.translateBy(x:67,y:93);g.rotate(by:-0.17);glass(rect(-34,-48,69,103,15),"FCE2EE","D9B5EB");line("M-13 -9 L14 18 M14 -9 L-13 18","A55989",7);g.restoreGState()
 g.saveGState();g.translateBy(x:124,y:101);g.rotate(by:0.12);glass(rect(-34,-48,69,103,15),"BDFFEA","62D8B5");line("M-17 1 L-4 14 L19 -11","217E69",8);g.restoreGState()
 case "ore":
 base("2D2C35","100D15");glass(path("M96 27 L148 58 L158 119 L96 163 L38 126 L41 65 Z"),"FFC578","AA6022");fill(path("M96 27 L97 92 L41 65 Z"),"FFE0A5");fill(path("M148 58 L97 92 L96 163 L158 119 Z"),"DA8436");fill(path("M41 65 L97 92 L38 126 Z"),"F6B466");line("M96 29 L97 92 L153 119 M97 92 L97 160","FFF0CB",1.5,0.7)
 case "topup":
 base("E8FFF7","BFECDD");glass(rect(31,60,130,83,18),"FFFFFF","C9E9DE");glass(rect(31,60,130,24,8),"53CBA3","21A47D",shadow:false);coin(128,71,26);glass(rect(48,96,42,12,6),"65C8AA","439C84",shadow:false);line("M128 58 V85 M115 71 H141","FFFFFF",7)
 case "timeline":
 base("3DAAFF","1D63CF");glass(rect(35,31,124,132,19),"FFFFFF","D8EAFF");glass(rect(48,46,38,37,8),"FFCB91","F09260",shadow:false);glass(rect(93,48,52,8,4),"83ADDA","538BCC",shadow:false);fill(rect(94,64,36,6,3),"A8BBDC");for y:CGFloat in [99,117,135]{fill(rect(49,y,96,7,3),"8BACD3")}
 case "messages":
 base("75E875","26AA4A");glass(path("M65 40 H149 Q165 40 165 58 V109 Q165 124 149 124 H142 L142 140 L121 124 H65 Q49 124 49 109 V58 Q49 40 65 40 Z"),"8BEF9E","50C46A");glass(path("M40 63 H124 Q142 63 142 81 V128 Q142 145 124 145 H72 L47 162 L50 145 H40 Q24 145 24 128 V81 Q24 63 40 63 Z"),"FFFFFF","DFF5E0")
 case "spaces":
 base("C47DFA","783BC7");for r:CGFloat in [65,48]{stroke(circle(96,91,r),"E6B6FF",2,0.45)};glass(circle(96,91,36),"C68DF1","9F62D7");glass(rect(83,62,26,59,13),"FFFFFF","E5CDFA");line("M70 99 V105 C70 139 122 139 122 105 V99 M96 132 V149 M80 150 H112","FFFFFF",6,0.93)
 case "calls":
 base("6AE965","1DA849");glass(path("M54 39 C49 36 43 38 39 43 C25 61 34 91 57 117 C81 144 112 159 135 150 C145 145 154 131 151 125 L129 105 Q124 101 119 107 L109 119 C90 112 69 92 64 76 L76 65 Q81 60 76 55 Z"),"FFFFFF","DBF9DF")
 case "shop":
 base("FFF4E6","FFDCC4");glass(path("M44 65 H148 L159 156 H33 Z"),"FF9C71","E55943");stroke(path("M66 75 V56 C66 17 126 17 126 56 V75"),"F4AB83",10);stroke(path("M67 59 V56 C67 20 125 20 125 56"),"FFE1C9",2,0.8);line("M46 151 H146","A52F28",2,0.18)
 case "week":
 base("FFFFFF","E4EAF0");glass(rect(29,33,134,130,19),"FFFFFF","E7EEF8");glass(path("M48 33 H144 Q163 33 163 52 V70 H29 V52 Q29 33 48 33 Z"),"FF7977","E54451",shadow:false);for x:CGFloat in [58,135]{stroke(path("M\(x) 25 V48"),"E6EBF0",8)}
 for y:CGFloat in [92,122]{for x:CGFloat in [55,83,111,139]{fill(circle(x,y,5),"9CAABE")}};glass(circle(111,122,13),"78B3FF","437CE1",shadow:false);line("M104 122 L109 127 L119 117","FFFFFF",3)
 case "biz":
 base("E7EAFE","B1C1F6");glass(rect(29,83,40,76,8),"94B6F2","577EC5");glass(rect(72,35,61,124,10),"FFFFFF","C8DBF6");glass(rect(129,98,34,61,7),"A7C6F1","6086CC");for y:CGFloat in [52,73,94,115]{for x:CGFloat in [86,112]{fill(rect(x,y,9,10,2),"809DCF")}};fill(rect(93,137,15,22,3),"6581B0")
 case "ads":
 base("FFD078","F39534");glass(path("M46 76 H81 L139 43 V139 L81 109 H46 Z"),"FFFFFF","F5DAC2");glass(rect(31,76,35,36,8),"FFE5BE","DAAF6A");glass(path("M62 110 H83 L92 152 H68 Z"),"DBEEFF","A5BBD7");line("M157 63 L166 57 M158 92 H173 M157 122 L166 127","FFFFFF",5,0.78)
 case "builder":
 base("FF906C","DC4739");g.saveGState();g.translateBy(x:97,y:91);g.rotate(by:0.64)
 glass(path("M-24 26 C-34 -13 -18 -48 0 -66 C18 -48 34 -13 24 26 Z"),"FFFFFF","E8CFCB");glass(path("M-23 -9 L-43 22 V48 L-18 29 Z"),"FFE4DD","EBADA7");glass(path("M23 -9 L43 22 V48 L18 29 Z"),"FFE4DD","EBADA7");orb(0,-22,12,"DE7167","B63736");glass(path("M-13 36 C-23 59 -7 65 0 81 C11 60 22 54 13 36 Z"),"FFF4B2","FFB153");g.restoreGState()
 case "vc":
 base("383661","14192F");glass(rect(38,34,99,123,16),"DAD8FC","A7A6D8");fill(rect(54,54,58,8,4),"807CAA");fill(rect(54,72,45,7,3),"9B98BF");coin(130,128,29);line("M65 133 V98 M52 111 L65 97 L78 111","FFFFFF",7)
 case "league":
 base("75BAFF","366DC8");glass(path("M59 49 H133 V80 C133 116 111 125 96 125 C81 125 59 116 59 80 Z"),"FFF3A5","E1A537");stroke(path("M58 62 H37 V75 C37 99 50 107 68 105 M134 62 H155 V75 C155 99 142 107 124 105"),"FFED9A",10);glass(rect(89,122,14,24,4),"FFE69C","DDA437");glass(rect(66,146,60,13,5),"FFF3BB","DCA544");glass(path("M96 62 L102 77 L118 78 L106 88 L110 104 L96 95 L82 104 L86 88 L74 78 L90 77 Z"),"FFFFFF","FFE7A3",shadow:false)
 case "beef":
 base("FF817A","D73B51");glass(path("M40 40 H98 Q113 40 113 55 V92 Q113 107 98 107 H74 L52 124 V107 H40 Q25 107 25 92 V55 Q25 40 40 40 Z"),"FFE9E6","F9BEC6");glass(path("M92 83 H148 Q164 83 164 99 V134 Q164 150 148 150 H137 V164 L116 150 H92 Q78 150 78 134 V99 Q78 83 92 83 Z"),"FFE59D","FFC25F");line("M50 62 L69 73 L50 84 M134 104 L115 115 L134 126","B84250",5)
 case "farm":
 base("F9FFE4","DCEDB5");coin(96,133,30);glass(path("M95 116 C72 110 71 88 50 84 C48 53 89 63 97 87 Z"),"A5DF6B","5FAD43");glass(path("M98 99 C99 61 124 37 151 46 C153 76 135 99 98 99 Z"),"BEED8A","7EC35C");line("M95 133 C94 100 100 80 121 64","4C8C42",5)
 case "cabal":
 base("6757A9","272650");glass(circle(96,96,64),"7566AD","444576");person(59,90,0.72,"DDC4FF","A38AC9");person(131,90,0.72,"B8ECD9","67B8AD");person(95,105,0.94,"FFFFFF","DDDDFC")
 case "npcchat":
 base("7FD6E4","27A5BB");glass(path("M42 33 H148 Q165 33 165 53 V124 Q165 145 148 145 H82 L52 166 V145 H42 Q27 145 27 124 V53 Q27 33 42 33 Z"),"FFFFFF","D7F2F7");person(96,88,0.93,"79C7D9","419AAC")
 case "xlink":
 base("394658","111C2E");g.saveGState();g.translateBy(x:96,y:96);g.rotate(by:-0.59)
 stroke(rect(-65,-23,81,46,23),"071121",15,0.5);stroke(rect(-11,-23,81,46,23),"071121",15,0.5);stroke(rect(-65,-26,81,46,23),"C5D5EB",12);stroke(rect(-11,-26,81,46,23),"FFFFFF",12);stroke(path("M-41 -43 H-24 M24 14 H42"),"FFFFFF",2,0.8);g.restoreGState()
 default:base("DCEBFF","9BBDE7");for x:CGFloat in [49,104]{for y:CGFloat in [49,104]{glass(rect(x,y,39,39,11),"FFFFFF","E5EFFF")}}
 }
 // Crisp but restrained outer rim: the set shares one continuous enclosure and vertical light source.
 stroke(enclosure(),"FFFFFF",1.3,0.5)
}
let ids=["app","map","jobs","bank","wallet","richlist","home","profile","settings","pulse","perps","predict","ore","topup","timeline","messages","spaces","calls","shop","week","biz","ads","builder","vc","league","beef","farm","cabal","npcchat","xlink"]
func render(_ id:String)->CGImage {
 let ctx=CGContext(data:nil,width:384,height:384,bitsPerComponent:8,bytesPerRow:0,space:CGColorSpaceCreateDeviceRGB(),bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue)!
 g=ctx;g.scaleBy(x:2,y:2);g.translateBy(x:0,y:192);g.scaleBy(x:1,y:-1);g.setAllowsAntialiasing(true);g.setShouldAntialias(true)
 g.saveGState();g.addPath(enclosure());g.clip();symbol(id);g.restoreGState()
 let hi=ctx.makeImage()!;let down=CGContext(data:nil,width:192,height:192,bitsPerComponent:8,bytesPerRow:0,space:CGColorSpaceCreateDeviceRGB(),bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue)!
 down.interpolationQuality = .high;down.draw(hi,in:CGRect(x:0,y:0,width:192,height:192));return down.makeImage()!
}
var imgs:[CGImage]=[]
for id in ids{let im=render(id);imgs.append(im);let rep=NSBitmapImageRep(cgImage:im);try rep.representation(using:.png,properties:[:])!.write(to:URL(fileURLWithPath:out+"/"+id+".png"))}
let cols=6,cell=238,rows=5;let sheet=CGContext(data:nil,width:cols*cell,height:rows*cell+80,bitsPerComponent:8,bytesPerRow:0,space:CGColorSpaceCreateDeviceRGB(),bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue)!
sheet.setFillColor(color("E9EDF5"));sheet.fill(CGRect(x:0,y:0,width:cols*cell,height:rows*cell+80));
for (i,img) in imgs.enumerated(){let x=(i%cols)*cell+31,y=(rows-1-i/cols)*cell+54;sheet.draw(img,in:CGRect(x:x,y:y,width:176,height:176));
 NSGraphicsContext.saveGraphicsState();NSGraphicsContext.current=NSGraphicsContext(cgContext:sheet,flipped:false)
 let str=ids[i] as NSString;str.draw(at:CGPoint(x:x,y:y-28),withAttributes:[.font:NSFont.systemFont(ofSize:18,weight:.medium),.foregroundColor:NSColor(cgColor:color("25334E"))!]);NSGraphicsContext.restoreGraphicsState()}
let rep=NSBitmapImageRep(cgImage:sheet.makeImage()!);try rep.representation(using:.png,properties:[:])!.write(to:URL(fileURLWithPath:out+"/contact-sheet.png"))
print("Rendered \(ids.count) original CTOS icons at 192px and contact sheet.")
// Small-size inspection sheets are evidence, not runtime resources.
for (name,bg,ink) in [("light","EDF1F7","25334E"),("dark","101A30","EBF1FF")] {
 let c=140,r=5,w=6*c,h=r*c; let ctx=CGContext(data:nil,width:w,height:h,bitsPerComponent:8,bytesPerRow:0,space:CGColorSpaceCreateDeviceRGB(),bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue)!
 ctx.setFillColor(color(bg));ctx.fill(CGRect(x:0,y:0,width:w,height:h))
 for (i,im) in imgs.enumerated(){let x=i%6*c+38,y=(r-1-i/6)*c+46;ctx.draw(im,in:CGRect(x:x,y:y,width:64,height:64));NSGraphicsContext.saveGraphicsState();NSGraphicsContext.current=NSGraphicsContext(cgContext:ctx,flipped:false);(ids[i] as NSString).draw(at:CGPoint(x:x-8,y:y-25),withAttributes:[.font:NSFont.systemFont(ofSize:13,weight:.medium),.foregroundColor:NSColor(cgColor:color(ink))!]);NSGraphicsContext.restoreGraphicsState()}
 let rep=NSBitmapImageRep(cgImage:ctx.makeImage()!);try rep.representation(using:.png,properties:[:])!.write(to:URL(fileURLWithPath:out+"/contact-sheet-64px-"+name+".png"))
}
