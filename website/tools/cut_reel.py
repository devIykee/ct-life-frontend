import subprocess, sys, os
E = "/Users/mac/Documents/codes/projects/ct-life-evidence"
FA = f"{E}/video-production-20261020/motion/assets"
OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)
SRC = {
  "walk": f"{E}/casino-r3-20261024/seeker-walk-20261025/rr_walk_online.mp4",
  "walkin": f"{E}/casino-r3-20261024/seeker-walk-20261025/rr_walkin_online.mp4",
  "drive": f"{E}/perf2-20261023/compare/drive-after.mp4",
  "crash": f"{E}/casino-r3-20261024/seeker/rr_crash.mp4",
  "slots": f"{E}/casino-r3-20261024/seeker/rr_slots.mp4",
  "bp": f"{E}/breakpoint-r2-20261024/final/walk.mp4",
  "van": f"{E}/home-shop-20261023/delivery-van-parcel.mp4",
  "lift": f"{FA}/island/roadstar-lift.mp4",
  "aerial": f"{FA}/island/city-aerial.mp4",
}
# crop presets -> 16:9 frame that leaves most of the HUD outside
CROP = {
  "world": "crop=1680:945:470:110",
  "ui": "crop=2133:1200:0:0",            # casino screens: the game panel and the chart
  "film": "crop=1600:900:160:20",
}
GRADE = "eq=contrast=1.07:saturation=1.18:brightness=0.01,vignette=PI/5"
def seg_filter(i, crop, w, h):
    return f"[{i}:v]{CROP[crop]},scale={w}:{h}:flags=lanczos,fps=30,setsar=1,{GRADE}[v{i}]"
def build(name, segs, w, h, crf):
    args = ["ffmpeg", "-v", "error", "-y"]
    for key, ss, dur, crop in segs:
        args += ["-ss", str(ss), "-t", str(dur), "-i", SRC[key]]
    fc = ";".join(seg_filter(i, s[3], w, h) for i, s in enumerate(segs))
    fc += ";" + "".join(f"[v{i}]" for i in range(len(segs))) + f"concat=n={len(segs)}:v=1:a=0[out]"
    args += ["-filter_complex", fc, "-map", "[out]", "-an", "-c:v", "libx264", "-preset", "slow", "-crf", str(crf),
             "-pix_fmt", "yuv420p", "-movflags", "+faststart", f"{OUT}/{name}.mp4"]
    subprocess.run(args, check=True)
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-ss", "0.3", "-i", f"{OUT}/{name}.mp4", "-frames:v", "1", "-c:v", "libwebp", "-quality", "80", f"{OUT}/{name}.webp"], check=True)
    print(name, os.path.getsize(f"{OUT}/{name}.mp4") // 1024, "KB")

HERO = [("walk", 24.0, 2.0, "world"), ("drive", 2.0, 2.4, "world"), ("lift", 1.8, 2.4, "film"),
        ("walkin", 13.2, 2.4, "world"), ("slots", 2.0, 2.0, "ui"), ("crash", 36.0, 4.4, "ui"),
        ("bp", 11.6, 2.2, "world"), ("bp", 18.0, 2.2, "world"), ("van", 7.0, 2.4, "world"), ("aerial", 0.5, 2.6, "film")]
build("hero-reel", HERO, 1920, 1080, 25)
build("hero-reel-720", HERO, 1280, 720, 27)
PANELS = {
  "p-rugroom": [("walkin", 12.6, 9.0, "world")],
  "p-rugged": [("crash", 33.0, 9.5, "ui")],
  "p-drive": [("drive", 0.0, 10.0, "world")],
  "p-breakpoint": [("bp", 15.0, 9.5, "world")],
  "p-delivery": [("van", 4.5, 8.5, "world")],
}
for n, s in PANELS.items():
    build(n, s, 1600, 900, 27)
