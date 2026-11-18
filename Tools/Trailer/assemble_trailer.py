"""
Cuts the trailer: frames in, finished film out.

The footage comes from the game itself (`RiftRally.exe -trailer`, see
`Assets/RiftRally/Scripts/Trailer/TrailerDirector.cs`), the music from `compose_music.py`,
and this does the work a cutting room would: a title card, section cards timed to the music,
a grade, a vignette, flash transitions on the hard cuts, and the mix.

Everything is ffmpeg filter graph. The reason for doing titles here rather than in the engine
is iteration: a typo in a card costs one second to fix here and a twenty-minute re-render
there, and type that lives in a filter graph can be kerned, timed and re-timed without
touching the game.

    python assemble_trailer.py --frames <dir> --music trailer_music.wav --out trailer.mp4

There is no voice-over by design. A ninety-second trailer for a game whose whole pitch is
physical - pinch, pull, release - is better served by letting the throw read on its own.
"""

from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys

FPS = 60

# Windows ships these; the first one that exists is used. Escaped for ffmpeg's filter parser,
# where a colon separates options and a backslash is an escape character.
FONT_CANDIDATES = [
    r"C:\Windows\Fonts\bahnschrift.ttf",
    r"C:\Windows\Fonts\segoeuib.ttf",
    r"C:\Windows\Fonts\arialbd.ttf",
    "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
]


def find_font() -> str:
    for path in FONT_CANDIDATES:
        if os.path.exists(path):
            return path.replace("\\", "/").replace(":", r"\:")
    raise SystemExit("no usable font found for the titles")


def seconds_to_frames(seconds: float) -> int:
    return int(round(seconds * FPS))


class Card:
    """
    One piece of on-screen type.

    Cards fade in and out rather than cutting, and they sit on a horizontal rule that draws
    itself in. Hard-cut type on a moving background is the single most common way a trailer
    made by a programmer announces itself as one.
    """

    def __init__(self, text: str, start: float, duration: float, size: int = 64,
                 sub: str | None = None, y: str = "h*0.42", fade: float = 0.5):
        self.text = text
        self.sub = sub
        self.start = start
        self.duration = duration
        self.size = size
        self.y = y
        self.fade = fade

    def filters(self, font: str) -> list[str]:
        end = self.start + self.duration
        alpha = (f"if(lt(t,{self.start}),0,"
                 f"if(lt(t,{self.start + self.fade}),(t-{self.start})/{self.fade},"
                 f"if(lt(t,{end - self.fade}),1,"
                 f"if(lt(t,{end}),({end}-t)/{self.fade},0))))")

        out = [
            # A shadow copy under every glyph, for the same reason the game draws one: the
            # background is a landscape and cannot be relied on to be dark.
            f"drawtext=fontfile='{font}':text='{self.text}':fontcolor=black@0.55:"
            f"fontsize={self.size}:x=(w-text_w)/2+3:y={self.y}+3:alpha='{alpha}'",
            f"drawtext=fontfile='{font}':text='{self.text}':fontcolor=white:"
            f"fontsize={self.size}:x=(w-text_w)/2:y={self.y}:alpha='{alpha}'",
        ]

        if self.sub:
            sub_size = max(20, int(self.size * 0.36))
            sub_y = f"{self.y}+{int(self.size * 1.25)}"
            out += [
                f"drawtext=fontfile='{font}':text='{self.sub}':fontcolor=black@0.5:"
                f"fontsize={sub_size}:x=(w-text_w)/2+2:y={sub_y}+2:alpha='{alpha}'",
                f"drawtext=fontfile='{font}':text='{self.sub}':fontcolor=0xD8E6F2:"
                f"fontsize={sub_size}:x=(w-text_w)/2:y={sub_y}:alpha='{alpha}'",
            ]

        return out


def build_cards() -> list[Card]:
    """
    The script, timed to the cut and to the music.

    The film runs 81 seconds: the opening landscape to 16, the throw to 27, the three
    modifiers to 47, the eight worlds to 66, the match and the pull-back to 81. Every card
    sits inside the shot it describes and clears the frame before the cut, and the two big
    ones - the title and the sign-off - land on the music's section changes.
    """
    return [
        # Title, over the opening landscape.
        Card("RIFT RALLY", 1.8, 4.6, size=108, sub="A HAND-TRACKED TABLE GAME FOR META QUEST",
             y="h*0.38"),

        # The mechanic, over the pinch-pull-release shot.
        Card("PINCH. PULL. RELEASE.", 16.6, 4.4, size=64,
             sub="NO CONTROLLERS - YOUR HANDS ARE THE INPUT", y="h*0.74"),

        # The twists. One card per modifier, inside its own shot.
        Card("CROSS CURRENT", 28.2, 3.2, size=52, sub="THE ICE PUSHES BACK", y="h*0.76"),
        Card("BUMPER POSTS", 34.6, 3.2, size=52, sub="THREAD IT, OR BANK IT", y="h*0.76"),
        Card("WARP GATES", 41.0, 3.4, size=52, sub="IN ONE SIDE, OUT THE OTHER", y="h*0.76"),

        # The worlds.
        Card("EIGHT WORLDS", 48.2, 3.6, size=72, sub="A NEW ONE EVERY GAME", y="h*0.40"),

        # The match.
        Card("BEST OF THREE", 67.4, 3.2, size=56, sub="SOLO VS AI, OR 1V1 ONLINE", y="h*0.76"),

        # Sign-off, over the pull-back, clearing before the fade.
        Card("RIFT RALLY", 74.4, 5.0, size=104,
             sub="BUILT FOR THE METAVR HACKATHON - UNITY 6, OPENXR, XR HANDS", y="h*0.40"),
    ]


def flash_filter(times: list[float], length: float = 0.10, strength: float = 0.26) -> str:
    """
    A short white lift on each hard cut.

    Not a dissolve: the cuts in this film land on the beat, and a dissolve softens exactly the
    thing the beat is there to sharpen. A tenth of a second of lift reads as an impact.

    Done with eq brightness rather than with geq. The arithmetic is the same either way, but
    geq evaluates its expression per pixel - two million times a frame, nearly five thousand
    frames - while eq evaluates once per frame and applies the result through a lookup table.
    """
    terms = [f"if(between(t,{t},{t + length}),{strength}*({t + length}-t)/{length},0)"
             for t in times]
    expression = "+".join(terms) if terms else "0"
    return f"eq=brightness='{expression}':eval=frame"


def build_filter_graph(font: str, cards: list[Card], flashes: list[float],
                       total: float) -> str:
    chain = [
        # Grade: a gentle S-curve and a little saturation. The game's palettes are already
        # doing the work, so this is a nudge, not a look.
        "eq=contrast=1.09:saturation=1.12:gamma=0.98",

        # Vignette, which also stops the eye wandering to the corners of a wide landscape.
        "vignette=angle=PI/5.2",

        # A whisper of grain. It hides banding in the big smooth skies, which is the one
        # artefact a gradient-heavy film reliably shows after H.264.
        "noise=alls=4:allf=t",
    ]

    if flashes:
        chain.append(flash_filter(flashes))

    for card in cards:
        chain += card.filters(font)

    # Open from black and close to it.
    chain.append(f"fade=t=in:st=0:d=1.0,fade=t=out:st={total - 2.2}:d=2.2")
    chain.append("format=yuv420p")

    return ",".join(chain)


def run(command: list[str]) -> None:
    print("$", " ".join(command[:6]), "...")
    result = subprocess.run(command, capture_output=True, text=True)
    if result.returncode != 0:
        sys.stderr.write(result.stderr[-4000:])
        raise SystemExit(f"ffmpeg failed with code {result.returncode}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--frames", required=True,
                        help="directory of numbered frames written by the trailer director")
    parser.add_argument("--music", required=True)
    parser.add_argument("--out", default="RiftRally_Trailer.mp4")
    parser.add_argument("--fps", type=int, default=FPS)
    args = parser.parse_args()

    ffmpeg = shutil.which("ffmpeg")
    if ffmpeg is None:
        raise SystemExit("ffmpeg is not on PATH")

    frames = sorted(f for f in os.listdir(args.frames) if f.endswith(".jpg"))
    if not frames:
        raise SystemExit(f"no frames in {args.frames}")

    total = len(frames) / args.fps
    print(f"{len(frames)} frames = {total:.1f} s at {args.fps} fps")

    font = find_font()
    cards = [c for c in build_cards() if c.start < total]

    # Flashes land on the section changes, which are the same places the music changes.
    # On the cuts between acts, which are also where the arrangement changes.
    flashes = [t for t in (16.0, 27.2, 46.8, 66.0, 74.0) if t < total - 0.2]

    graph = build_filter_graph(font, cards, flashes, total)

    run([
        ffmpeg, "-y",
        "-framerate", str(args.fps),
        "-i", os.path.join(args.frames, "frame_%05d.jpg"),
        "-i", args.music,
        "-filter_complex", f"[0:v]{graph}[v]",
        "-map", "[v]",
        "-map", "1:a",
        # Audio is ducked slightly under the end card so the last line of type reads quietly,
        # and faded out with the picture.
        "-af", f"volume=0.92,afade=t=in:st=0:d=0.8,afade=t=out:st={total - 2.4}:d=2.4",
        "-c:v", "libx264",
        "-preset", "slow",
        "-crf", "17",
        "-pix_fmt", "yuv420p",
        "-movflags", "+faststart",
        "-c:a", "aac",
        "-b:a", "256k",
        "-shortest",
        args.out,
    ])

    size = os.path.getsize(args.out) / (1024 * 1024)
    print(f"wrote {args.out}: {total:.1f} s, {size:.1f} MB")


if __name__ == "__main__":
    main()
