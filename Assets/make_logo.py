from PIL import Image, ImageDraw, ImageFilter
import math
S=1024
img=Image.new("RGBA",(S,S),(0,0,0,0))
# background rounded square with vertical gradient
bg=Image.new("RGBA",(S,S))
for y in range(S):
    t=y/S
    c=(int(30-12*t),int(34-14*t),int(44-18*t),255)
    ImageDraw.Draw(bg).line([(0,y),(S,y)],fill=c)
mask=Image.new("L",(S,S),0)
ImageDraw.Draw(mask).rounded_rectangle([0,0,S-1,S-1],radius=210,fill=255)
img.paste(bg,(0,0),mask)

def shield(cx,top,w,h):
    pts=[]
    # top edge: gentle upward bumps at corners, dip in middle
    n=60
    for i in range(n+1):
        x=cx-w/2+w*i/n
        u=(i/n-0.5)*2
        y=top+ h*0.06*(1-u*u)  # dip toward middle
        pts.append((x,y))
    # right side down to point
    for i in range(1,n+1):
        t=i/n
        x=cx+w/2*math.cos(t*math.pi/2)**0.9
        y=top+h*0.30+ (h*0.70)*math.sin(t*math.pi/2)**1.4
        if t<0.02: y=top
        pts.append((x,y))
    for i in range(n,-1,-1):
        t=i/n
        x=cx-w/2*math.cos(t*math.pi/2)**0.9
        y=top+h*0.30+ (h*0.70)*math.sin(t*math.pi/2)**1.4
        pts.append((x,y))
    return pts
orange=(255,145,40,255); orange2=(255,190,110,255)
cx=S/2; top=170; w=660; h=720
outer=shield(cx,top,w,h)
d=ImageDraw.Draw(img)
# glow
glow=Image.new("RGBA",(S,S),(0,0,0,0))
ImageDraw.Draw(glow).polygon(outer,fill=(255,145,40,120))
glow=glow.filter(ImageFilter.GaussianBlur(40))
img=Image.alpha_composite(img,glow)
d=ImageDraw.Draw(img)
d.polygon(outer,fill=orange)
inner=shield(cx,top+38,w-76,h-80)
# inner: sunset sky gradient + road
inn=Image.new("RGBA",(S,S),(0,0,0,0))
for y in range(S):
    t=max(0,min(1,(y-top)/(h*0.55)))
    c=(int(40+40*t),int(30+20*t),int(60-10*t),255)
    ImageDraw.Draw(inn).line([(0,y),(S,y)],fill=c)
horizon=top+h*0.46
di=ImageDraw.Draw(inn)
# sun
r=110
di.ellipse([cx-r,horizon-r,cx+r,horizon+r],fill=(255,170,60,255))
for k in range(4):
    yy=horizon-18-k*26
    di.rectangle([cx-r,yy,cx+r,yy+8-k],fill=(70,48,58,255))
# ground
di.rectangle([0,horizon,S,S],fill=(22,24,30,255))
# road (perspective)
vp=(cx,horizon)
bl=(cx-300,top+h); br=(cx+300,top+h)
di.polygon([(cx-14,horizon),(cx+14,horizon),br,bl],fill=(52,56,66,255))
# edge lines
di.line([(cx-14,horizon),bl],fill=(235,235,240,255),width=10)
di.line([(cx+14,horizon),br],fill=(235,235,240,255),width=10)
# center dashes
yb=top+h
for a,b in [(0.05,0.12),(0.2,0.32),(0.45,0.65),(0.8,1.1)]:
    y1=horizon+(yb-horizon)*a; y2=horizon+(yb-horizon)*min(b,1)
    w1=3+18*a; w2=3+18*min(b,1)
    di.polygon([(cx-w1,y1),(cx+w1,y1),(cx+w2,y2),(cx-w2,y2)],fill=(255,200,60,255))
im=Image.new("L",(S,S),0); ImageDraw.Draw(im).polygon(inner,fill=255)
img.paste(inn,(0,0),im)
# top band text-free stripe
d=ImageDraw.Draw(img)
img.save("logo_1024.png")
sizes=[16,24,32,48,64,128,256]
img.resize((256,256),Image.LANCZOS).save("app.ico",sizes=[(s,s) for s in sizes])
img.resize((256,256),Image.LANCZOS).save("logo_256.png")
img.resize((32,32),Image.LANCZOS).save("logo_32.png")
