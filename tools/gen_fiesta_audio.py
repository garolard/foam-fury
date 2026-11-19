"""Procedural sounds for JO's Fiesta Popper and the donkey pinata.

Standalone so regenerating these doesn't touch the other audio files.
Run: python3 tools/gen_fiesta_audio.py
"""
import wave, struct, math, random, os
SR = 22050
random.seed(23)
OUT = os.path.join(os.path.dirname(__file__), '..', 'audio')


def write(name, samples, vol=0.9):
    peak = max(1e-6, max(abs(s) for s in samples))
    k = vol / peak
    with wave.open(os.path.join(OUT, name + '.wav'), 'w') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes(b''.join(struct.pack('<h', int(max(-1, min(1, s * k)) * 32767)) for s in samples))


class LP:
    def __init__(s, c): s.c = c; s.y = 0
    def __call__(s, x): s.y += s.c * (x - s.y); return s.y


class HP:
    def __init__(s, c): s.lp = LP(c)
    def __call__(s, x): return x - s.lp(x)


def noise(): return random.uniform(-1, 1)


def rustle(t, dur):
    """Crinkly paper: sparse random clicks, denser at the start."""
    p = 0.25 * math.exp(-t / (dur * 0.4))
    return noise() if random.random() < p else 0.0


# Fiesta Popper shot: the crack of a party popper, a soft cardboard thump, then a shower of paper
def shot_confetti():
    out = []; hp = HP(0.3); lp = LP(0.08); ph = 0
    dur = 0.6
    for i in range(int(dur * SR)):
        t = i / SR
        crack = hp(noise()) * min(1, t / 0.0015) * math.exp(-t / 0.018)
        ph += 2 * math.pi * (90 + 140 * math.exp(-t / 0.03)) / SR
        thump = math.sin(ph) * math.exp(-t / 0.06) * 0.7
        paper = lp(rustle(t, dur)) * 3.5
        out.append(crack * 1.2 + thump + paper)
    return out


# Zombie -> pinata: springy "boing" with a kazoo-ish blip on top
def pinata_pop():
    out = []; ph = 0; ph2 = 0; lp = LP(0.25)
    dur = 0.55
    for i in range(int(dur * SR)):
        t = i / SR
        f = 180 + 260 * (1 - math.exp(-t / 0.08)) + 40 * math.sin(2 * math.pi * 14 * t) * math.exp(-t / 0.2)
        ph += 2 * math.pi * f / SR
        boing = math.sin(ph) * math.exp(-t / 0.22)
        ph2 += 2 * math.pi * (520 + 30 * math.sin(2 * math.pi * 6 * t)) / SR
        saw = (ph2 / (2 * math.pi)) % 1 * 2 - 1
        kazoo = lp(saw) * (min(1, max(0, (t - 0.05) / 0.02))) * math.exp(-max(0, t - 0.05) / 0.12) * 0.8
        pop = noise() * math.exp(-t / 0.006) * 0.8
        out.append(boing + kazoo + pop)
    return out


# A zombie whacks the pinata: hollow papier-mache thud
def pinata_whack():
    out = []; lp = LP(0.12); ph = 0
    dur = 0.22
    for i in range(int(dur * SR)):
        t = i / SR
        ph += 2 * math.pi * (140 + 60 * math.exp(-t / 0.02)) / SR
        out.append(math.sin(ph) * math.exp(-t / 0.05) + lp(noise()) * 2.2 * math.exp(-t / 0.03) + rustle(t, dur) * 0.15)
    return out


# Pinata bursts: big pop, a party horn blowing and a long patter of falling confetti
def pinata_boom():
    out = []; lp = LP(0.05); lp2 = LP(0.18); hp = HP(0.25); ph = 0; phh = 0
    dur = 1.6
    for i in range(int(dur * SR)):
        t = i / SR
        boom = lp(noise()) * 3.0 * min(1, t / 0.003) * math.exp(-t / 0.18)
        ph += 2 * math.pi * (55 + 90 * math.exp(-t / 0.05)) / SR
        sub = math.sin(ph) * math.exp(-t / 0.25)
        crack = hp(noise()) * math.exp(-t / 0.02)
        # party horn: buzzy saw with a rise, wobble and droop
        ht = t - 0.08
        horn = 0.0
        if 0 < ht < 0.9:
            f = 420 + 120 * min(1, ht / 0.08) - 90 * max(0, ht - 0.6) / 0.3 + 18 * math.sin(2 * math.pi * 7 * ht)
            phh += 2 * math.pi * f / SR
            saw = (phh / (2 * math.pi)) % 1 * 2 - 1
            horn = lp2(saw) * min(1, ht / 0.03) * min(1, (0.9 - ht) / 0.15) * 0.55
        paper = rustle(t, dur * 1.4) * 0.35 * min(1, t / 0.2)
        out.append(boom + sub + crack * 0.6 + horn + paper)
    return out


for name, fn, vol in (("shot_confetti", shot_confetti, 0.85), ("pinata_pop", pinata_pop, 0.8),
                      ("pinata_whack", pinata_whack, 0.75), ("pinata_boom", pinata_boom, 0.95)):
    write(name, fn(), vol)
    print("wrote", name)
