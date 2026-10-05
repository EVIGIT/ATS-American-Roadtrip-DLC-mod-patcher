"""Generates the Truckers Tool Kit app icon and logo.

Design notes, so the next person does not have to reverse-engineer the intent:

The reference points are the two brands a player already recognises. TruckersMP publishes exact brand
values (Red #B92025, Black #040608, White #FEFEFE), uses a lorry silhouette as its motif, and ships a
deliberately simplified "badge" for small sizes because its wordmark dies below ~32px. SCS's own ATS
identity is a dark UI with a warm amber accent. This borrows the shape language from the first and the
palette temperature from the second, so it reads as belonging on the same shelf as the tools it sits
beside.

The motif is a front-on cab on a road, because a front view is what survives 16px - a side profile is
a long horizontal shape that smears at that size, while a stack of horizontal bands keeps its
silhouette. Amber #FF8A3D is the app's own accent from Theme.cs, so icon and running UI agree.

Everything is drawn at 1024 and downsampled with LANCZOS. Nothing is authored at icon size.
"""

import os

from PIL import Image, ImageDraw, ImageFilter

S = 1024
OUT = os.path.dirname(os.path.abspath(__file__))

# The app accent, from Theme.cs. Change one without the other and the icon stops matching the UI.
AMBER = (255, 138, 61, 255)
AMBER_DEEP = (198, 86, 28, 255)
AMBER_PALE = (255, 206, 150, 255)
NIGHT_TOP = (26, 30, 40, 255)
NIGHT_BOTTOM = (14, 16, 22, 255)
ROAD_SURFACE = (38, 43, 55, 255)
TYRE = (18, 20, 26, 255)


def backdrop():
    """Dark rounded square with a vertical gradient, echoing the app's dark surface."""
    plate = Image.new("RGBA", (S, S))
    draw = ImageDraw.Draw(plate)
    for y in range(S):
        t = y / S
        draw.line(
            [(0, y), (S, y)],
            fill=(
                round(NIGHT_TOP[0] + (NIGHT_BOTTOM[0] - NIGHT_TOP[0]) * t),
                round(NIGHT_TOP[1] + (NIGHT_BOTTOM[1] - NIGHT_TOP[1]) * t),
                round(NIGHT_TOP[2] + (NIGHT_BOTTOM[2] - NIGHT_TOP[2]) * t),
                255,
            ),
        )
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, S - 1, S - 1], radius=210, fill=255)
    canvas = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    canvas.paste(plate, (0, 0), mask)
    return canvas


def road_layer(cx, horizon, bottom, half_top, half_bottom):
    """The road the cab sits on, in perspective, with amber shoulders."""
    layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    d.polygon(
        [(cx - half_top, horizon), (cx + half_top, horizon),
         (cx + half_bottom, bottom), (cx - half_bottom, bottom)],
        fill=ROAD_SURFACE,
    )
    for side in (-1, 1):
        d.line(
            [(cx + side * half_top, horizon), (cx + side * half_bottom, bottom)],
            fill=AMBER,
            width=int(S * 0.013),
        )
    return layer



def truck_layer():
    """A side-profile tractor unit: boxy cab, long chassis, three big wheels.

    Side profile, not front-on. A front view is ambiguous between a car, a van and a bus, and a
    side profile is the silhouette players already read as "lorry" from TruckersMP's own mark. The
    earlier front-on attempt lost that cue entirely.

    Everything is proportioned around the wheels rather than the body, because wheel size is what
    separates a truck from a car at a glance - and it is the first thing to survive at 16px.
    """
    layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)

    ground = S * 0.760
    wheel_r = S * 0.088
    axle_y = ground - wheel_r

    # Chassis rail, running the length of the unit.
    d.rounded_rectangle(
        [S * 0.105, axle_y - S * 0.012, S * 0.895, axle_y + S * 0.030],
        radius=S * 0.012,
        fill=AMBER_DEEP,
    )

    # Trailer body: tall, square, and taller than the cab, as a real unit is.
    d.rounded_rectangle(
        [S * 0.105, S * 0.285, S * 0.560, axle_y - S * 0.010],
        radius=S * 0.030,
        fill=AMBER,
    )

    # Cab, stepped down from the trailer. One shape, not a rectangle plus a nose wedge: the wedge
    # left a visible seam at the corner and made the front read as two panels.
    d.rounded_rectangle(
        [S * 0.590, S * 0.365, S * 0.960, axle_y - S * 0.010],
        radius=S * 0.030,
        fill=AMBER,
    )

    # Glazing, knocked out so the dark plate shows through and the massing stays simple. A single
    # raked windscreen; the earlier version also cut a triangle that read as a torn corner.
    glass = Image.new("L", (S, S), 0)
    gd = ImageDraw.Draw(glass)
    gd.polygon(
        [(S * 0.755, S * 0.420), (S * 0.905, S * 0.455), (S * 0.905, S * 0.545), (S * 0.755, S * 0.545)],
        fill=255,
    )
    gd.rounded_rectangle([S * 0.630, S * 0.435, S * 0.715, S * 0.530], radius=S * 0.014, fill=255)
    layer.putalpha(Image.composite(Image.new("L", (S, S), 0), layer.getchannel("A"), glass))
    d = ImageDraw.Draw(layer)

    # No bumper, and no grille bars beyond the trailer ribs. An earlier version had a dark bumper
    # block at the nose; at 16px it was invisible clutter and at 1024 it read as a detached blob.
    # Every added detail below roughly 24px is detail the mark does not need.

    # Five-stroke bars across the trailer, the way a curtain-sider reads at distance.
    for i in range(3):
        x = S * (0.165 + i * 0.125)
        d.rectangle([x, S * 0.345, x + S * 0.016, axle_y - S * 0.075], fill=AMBER_DEEP)

    # Wheels. Dark hubs in an amber ring so they stay visible instead of vanishing into the plate.
    for cx in (S * 0.215, S * 0.430, S * 0.775):
        d.ellipse([cx - wheel_r, axle_y - wheel_r, cx + wheel_r, axle_y + wheel_r], fill=TYRE)
        d.ellipse(
            [cx - wheel_r * 0.42, axle_y - wheel_r * 0.42, cx + wheel_r * 0.42, axle_y + wheel_r * 0.42],
            fill=AMBER_DEEP,
        )
    return layer


def build():
    cx = S / 2
    canvas = backdrop()
    ground = S * 0.760

    # Warm bloom behind the unit, so it sits in the scene rather than on top of it.
    bloom = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(bloom).ellipse(
        [cx - S * 0.44, S * 0.190, cx + S * 0.44, ground + S * 0.10],
        fill=(255, 138, 61, 70),
    )
    canvas = Image.alpha_composite(canvas, bloom.filter(ImageFilter.GaussianBlur(S * 0.055)))

    # Ground shadow first, so the wheels sit on something.
    shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).ellipse(
        [S * 0.075, ground - S * 0.030, S * 0.955, ground + S * 0.070],
        fill=(0, 0, 0, 130),
    )
    canvas = Image.alpha_composite(canvas, shadow.filter(ImageFilter.GaussianBlur(S * 0.026)))

    canvas = Image.alpha_composite(canvas, truck_layer())

    # A single amber speed streak under the chassis. One line, not three: at 16px more than one
    # turns into noise and the mark loses its speed entirely.
    streak = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(streak).rounded_rectangle(
        [S * 0.055, ground + S * 0.052, S * 0.945, ground + S * 0.074],
        radius=S * 0.012,
        fill=(255, 138, 61, 150),
    )
    return Image.alpha_composite(canvas, streak.filter(ImageFilter.GaussianBlur(S * 0.010)))


if __name__ == "__main__":
    image = build()
    image.save(os.path.join(OUT, "logo_1024.png"))

    sizes = [16, 24, 32, 48, 64, 128, 256]
    resized = image.resize((256, 256), Image.LANCZOS)
    resized.save(os.path.join(OUT, "app.ico"), sizes=[(s, s) for s in sizes])
    resized.save(os.path.join(OUT, "logo.png"))
    image.resize((32, 32), Image.LANCZOS).save(os.path.join(OUT, "logo_32.png"))

    # Contact sheet: the only honest way to see whether a mark survives 16px.
    sheet = Image.new("RGBA", (sum(sizes) + 12 * (len(sizes) + 1), 300), (58, 58, 66, 255))
    x = 12
    for s in sizes:
        sheet.alpha_composite(image.resize((s, s), Image.LANCZOS), (x, 12))
        x += s + 12
    sheet.save(os.path.join(OUT, "_sizes_preview.png"))
    print("wrote logo_1024.png, logo.png, logo_32.png, app.ico, _sizes_preview.png")
