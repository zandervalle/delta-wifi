// Zeichnet das App-Icon (1024 px). Aufruf: swift Tools/AppIcon.swift icon-1024.png
// Danach mit sips auf 16, 32, 64, 128, 256, 512 px verkleinern und in AppIcon.appiconset ablegen.

import AppKit
import CoreGraphics

let size: CGFloat = 1024
let space = CGColorSpace(name: CGColorSpace.sRGB)!
let ctx = CGContext(data: nil, width: Int(size), height: Int(size), bitsPerComponent: 8, bytesPerRow: 0,
                    space: space, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
// Koordinaten wie im Entwurf: Ursprung oben links.
ctx.translateBy(x: 0, y: size); ctx.scaleBy(x: 1, y: -1)

func color(_ hex: UInt32, _ alpha: CGFloat = 1) -> CGColor {
    CGColor(colorSpace: space, components: [CGFloat((hex >> 16) & 0xFF) / 255, CGFloat((hex >> 8) & 0xFF) / 255, CGFloat(hex & 0xFF) / 255, alpha])!
}

// Hintergrund: abgerundetes Quadrat im macOS-Raster (824 pt auf 1024), mit weichem Schatten.
let tile = CGRect(x: 100, y: 100, width: 824, height: 824)
let tilePath = CGPath(roundedRect: tile, cornerWidth: 186, cornerHeight: 186, transform: nil)
ctx.saveGState()
ctx.setShadow(offset: CGSize(width: 0, height: -12), blur: 28, color: color(0x000000, 0.35))
ctx.addPath(tilePath); ctx.setFillColor(color(0x101726)); ctx.fillPath()
ctx.restoreGState()
ctx.saveGState()
ctx.addPath(tilePath); ctx.clip()
let background = CGGradient(colorsSpace: space, colors: [color(0x25324A), color(0x0D1320)] as CFArray, locations: [0, 1])!
ctx.drawLinearGradient(background, start: CGPoint(x: 512, y: 100), end: CGPoint(x: 512, y: 924), options: [])
ctx.restoreGState()

// Delta: Dreieck mit runden Ecken. Die Spitze ist der Sender.
let apex = CGPoint(x: 512, y: 236), left = CGPoint(x: 204, y: 770), right = CGPoint(x: 820, y: 770)
let radius: CGFloat = 46
let triangle = CGMutablePath()
triangle.move(to: CGPoint(x: (apex.x + right.x) / 2, y: (apex.y + right.y) / 2))
triangle.addArc(tangent1End: right, tangent2End: left, radius: radius)
triangle.addArc(tangent1End: left, tangent2End: apex, radius: radius)
triangle.addArc(tangent1End: apex, tangent2End: right, radius: radius)
triangle.closeSubpath()

// Funkbögen um die Spitze, eingefärbt wie die Heatmap: nah grün, fern rot.
let bands: [(inner: CGFloat, outer: CGFloat, color: UInt32)] = [
    (0, 158, 0x34C759), (196, 300, 0xFFD60A), (338, 434, 0xFF9F0A), (472, 720, 0xFF453A),
]
ctx.saveGState()
ctx.addPath(triangle); ctx.clip()
for band in bands {
    let ring = CGMutablePath()
    ring.addEllipse(in: CGRect(x: apex.x - band.outer, y: apex.y - band.outer, width: band.outer * 2, height: band.outer * 2))
    if band.inner > 0 {
        ring.addEllipse(in: CGRect(x: apex.x - band.inner, y: apex.y - band.inner, width: band.inner * 2, height: band.inner * 2))
    }
    ctx.addPath(ring); ctx.setFillColor(color(band.color)); ctx.fillPath(using: .evenOdd)
}
ctx.restoreGState()

let image = ctx.makeImage()!
let rep = NSBitmapImageRep(cgImage: image)
try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: CommandLine.arguments[1]))
