"""Assemble native render captures for manual grip review (Python + Pillow)."""
from pathlib import Path
import argparse
from PIL import Image, ImageDraw

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument("capture",type=Path)
parser.add_argument("--before",type=Path)
args=parser.parse_args()
files=sorted(args.capture.glob("*-quarter.png"))
if not files:parser.error("Capture folder has no quarter-view PNGs.")
canvas=Image.new("RGB",(1280,((len(files)+4)//5)*282),(20,22,26))
draw=ImageDraw.Draw(canvas)
for i,path in enumerate(files):
    picture=Image.open(path).convert("RGB");picture.thumbnail((256,256))
    x=i%5*256;y=i//5*282;canvas.paste(picture,(x,y))
    draw.text((x+4,y+258),path.stem.replace("WEAPON_","").replace("-quarter",""),fill="white")
canvas.save(args.capture/"weapon-contact-sheet.jpg",quality=92)
if args.before:
    views=["quarter","left","right"]
    canvas=Image.new("RGB",(1080,776),(20,22,26));draw=ImageDraw.Draw(canvas)
    for row,(folder,label) in enumerate([(args.before,"BEFORE"),(args.capture,"AFTER")]):
        draw.text((8,row*388+4),label,fill="white")
        for col,view in enumerate(views):
            picture=Image.open(folder/("WEAPON_Shovel-"+view+".png")).convert("RGB")
            picture.thumbnail((360,360));canvas.paste(picture,(col*360,row*388+24))
            draw.text((col*360+8,row*388+370),view,fill="white")
    canvas.save(args.capture/"saw-before-after.jpg",quality=94)
